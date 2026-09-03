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

/// <summary>
/// One game as the library shows it (TASK-70.6) — the fold of every visible
/// store entry that resolves to one work. Held by reference by every member
/// row, so two entries of one game cannot disagree about its bucket, its
/// playtime or its last-played: there is one object and they both point at it.
///
/// <para>The figures come from <see cref="Winnow.Core.Identity.CoveragePlaytime.Across"/>
/// over the same entries, which is the same function the details modal's
/// TOTAL uses, so the grid can never stand behind a sum the modal refuses to
/// report. The fold runs over the rows that survived consolidation, so a
/// folded demo, a hidden non-game and an account-filtered row are excluded
/// here exactly as they are excluded from the grid.</para>
/// </summary>
public sealed class GameGrouping
{
    private GameGrouping(
        long resolvedWorkId,
        string bucket,
        long playtimeMinutes,
        DateTime? lastPlayedAt,
        DateTime? majorUpdateAt,
        int entryCount)
    {
        ResolvedWorkId = resolvedWorkId;
        Bucket = bucket;
        PlaytimeMinutes = playtimeMinutes;
        LastPlayedAt = lastPlayedAt;
        MajorUpdateAt = majorUpdateAt;
        EntryCount = entryCount;
    }

    /// <summary>The work the group is filed under — the primary.</summary>
    public long ResolvedWorkId { get; }

    /// <summary>
    /// The bucket of the game, from
    /// <see cref="LibraryBucketRules.Classify"/> over the summed minutes and the
    /// group's own last-played. Not the highest-precedence member bucket: two
    /// entries at sixty minutes each are two Active rows and one Bounced game,
    /// so the thresholds are re-applied to the sum.
    /// </summary>
    public string Bucket { get; }

    /// <summary>Minutes summed across every visible entry of this game.</summary>
    public long PlaytimeMinutes { get; }

    /// <summary>
    /// The latest last-played across the same entries the minutes were summed
    /// over. Derived in one pass with the sum, so the F10 pairing — one
    /// store's minutes beside another store's date — is not expressible.
    /// </summary>
    public DateTime? LastPlayedAt { get; }

    /// <summary>
    /// The correlated build push, latest across the group's releases. Already
    /// filtered by the acknowledgement watermark.
    /// </summary>
    public DateTime? MajorUpdateAt { get; }

    /// <summary>How many visible store entries this game has. One is the ordinary case.</summary>
    public int EntryCount { get; }

    /// <summary>True when this game is owned on more than one entry.</summary>
    public bool IsCollapsed => EntryCount > 1;

    /// <summary>
    /// The only way to make one, for the same reason
    /// <see cref="Winnow.Core.Identity.CoveragePlaytime"/> has only one: the
    /// minutes and the date must come out of the same pass over the same
    /// entries, and the bucket must be the shared rules applied to exactly
    /// those figures. There is no constructor that would let a caller pair a
    /// sum with a date it did not derive, or file a game under a bucket its
    /// own playtime does not put it in.
    /// </summary>
    public static GameGrouping Of(
        long resolvedWorkId,
        IEnumerable<Winnow.Core.Identity.IPlayedEntry> entries,
        DateTime? majorUpdateAt,
        BucketThresholds thresholds)
    {
        var total = Winnow.Core.Identity.CoveragePlaytime.Across(entries);

        return new GameGrouping(
            resolvedWorkId,
            LibraryBucketRules.Classify(
                total.PlaytimeMinutes, total.LastPlayedAt, majorUpdateAt, thresholds),
            total.PlaytimeMinutes,
            total.LastPlayedAt,
            majorUpdateAt,
            total.EntryCount);
    }
}

/// <summary>One row of the derived-bucket query: the bucket for a single ownership.</summary>
public sealed record OwnershipBucket : Winnow.Core.Identity.IPlayedEntry
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

    /// <summary>
    /// The correlated build push for this release, already filtered by the
    /// acknowledgement watermark, or null. Carried on the row because the
    /// bucket rules are applied in C# now, at two grains, and the group
    /// grain needs the latest across the game's releases.
    /// </summary>
    public DateTime? MajorUpdateAt { get; init; }

    /// <summary>
    /// The bucket of THIS ownership, on its own minutes and its own date. What
    /// the query has always returned.
    /// </summary>
    public required string Bucket { get; init; }

    /// <summary>
    /// The game this row is one store entry of (TASK-70.6). Shared by
    /// reference with every other entry of the same game. The grid draws one
    /// tile per distinct instance; the rail counts, All Games, the filter
    /// options, the list counts and the recommender all read the game's
    /// bucket from here rather than folding their own.
    /// </summary>
    public required GameGrouping Game { get; init; }

    /// <summary>How many owned demo releases this row supersedes via <see cref="DemoConsolidation"/>. Usually 0.</summary>
    public int ConsolidatedDemoCount { get; init; }
}
