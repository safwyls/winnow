namespace Winnow.Core.Queries;

/// <summary>
/// Derived bucket names (§6.1). Buckets are QUERIES, never stored columns:
/// thresholds get tuned, and stored values rot.
/// </summary>
public static class LibraryBuckets
{
    /// <summary>Zero minutes and no last-played date: the game was never opened.</summary>
    public const string NeverPlayed = "never_played";

    /// <summary>
    /// Refund line (inclusive) up to the retired floor — the highest-value pile.
    /// Past the point of no return and abandoned anyway.
    /// </summary>
    public const string Bounced = "bounced";

    /// <summary>
    /// A release update landed more than the stale window after last play.
    /// Outranks Bounced; outranked by Retired and by the never-opened case.
    /// </summary>
    public const string StaleButPatched = "stale_but_patched";

    /// <summary>High playtime; excluded from surfacing.</summary>
    public const string Retired = "retired";

    /// <summary>Residual bucket: nonzero playtime under the refund line, or a last-played date beside zero (unknown) minutes.</summary>
    public const string Active = "active";
}

/// <summary>
/// Tunable thresholds for the derived-bucket query. Deliberately parameters,
/// not schema: §6.1 requires retuning without migration.
/// </summary>
/// <param name="BouncedFloorMinutes">Playtime at or above this is Bounced. Default 120 (Steam refund line).</param>
/// <param name="RetiredFloorMinutes">Playtime at or above this is Retired.</param>
/// <param name="StaleWindowMonths">Months after last play before an update marks Stale-but-patched.</param>
/// <param name="UpdateCorrelationWindowDays">Max days between a build push and announcement to count as one update. Default 7.</param>
/// <param name="ShowNonGameEntries">Whether to include non-game entries (<see cref="NonGameEntries"/>). Default false.</param>
public sealed record BucketThresholds(
    long BouncedFloorMinutes,
    long RetiredFloorMinutes,
    int StaleWindowMonths,
    int UpdateCorrelationWindowDays = 7,
    bool ShowNonGameEntries = false)
{
    /// <summary>Settings key for the "show non-game entries" preference.</summary>
    public const string ShowNonGameEntriesSettingKey = "library.show_non_game_entries";

    /// <summary>Conservative defaults; per-genre configuration comes later (§6.1).</summary>
    public static BucketThresholds Default { get; } = new(
        BouncedFloorMinutes: 120,
        RetiredFloorMinutes: 6_000,
        StaleWindowMonths: 6,
        UpdateCorrelationWindowDays: 7,
        ShowNonGameEntries: false);

    /// <summary>Parses stored preference text. Non-<c>true</c> values default to hidden.</summary>
    public static bool ParseShowNonGameEntries(string? stored)
        => bool.TryParse(stored?.Trim(), out var show) && show;

    /// <summary>Formats the preference for storage. Round-trips with <see cref="ParseShowNonGameEntries"/>.</summary>
    public static string FormatShowNonGameEntries(bool show) => show ? "true" : "false";
}

/// <summary>One row of the derived-bucket query: the bucket for a single ownership.</summary>
public sealed record OwnershipBucket
{
    public required long OwnershipId { get; init; }
    public required long ReleaseId { get; init; }

    /// <summary>
    /// The work this ownership's release belongs to, unresolved. Kept beside
    /// <see cref="ResolvedWorkId"/> because enrichment targets the row's own
    /// work while display targets the resolved one.
    /// </summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// The same-game parent of <see cref="WorkId"/>, or <see cref="WorkId"/>
    /// itself. Total, never null. Computed by the bucket query in the same
    /// pass as demo consolidation (migration 0018 <c>identity_links</c>,
    /// kind <c>same_game</c> only — expansion links are excluded because an
    /// expansion's playtime does not roll up). With no links this equals
    /// <see cref="WorkId"/> on every row.
    /// </summary>
    public required long ResolvedWorkId { get; init; }

    /// <summary>True when this row's work is linked under another.</summary>
    public bool IsLinkedChild => ResolvedWorkId != WorkId;

    public required long PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required string Bucket { get; init; }

    /// <summary>How many owned demo releases this row supersedes via <see cref="DemoConsolidation"/>. Usually 0.</summary>
    public int ConsolidatedDemoCount { get; init; }
}
