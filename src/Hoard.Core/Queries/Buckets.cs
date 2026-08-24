namespace Hoard.Core.Queries;

/// <summary>
/// Derived bucket names (§6.1). Buckets are QUERIES, never stored columns:
/// thresholds get tuned, and stored values rot.
/// </summary>
public static class LibraryBuckets
{
    /// <summary>Zero recorded playtime.</summary>
    public const string NeverTouched = "never_touched";

    /// <summary>0 &lt; playtime &lt; bounced ceiling — the highest-value pile.</summary>
    public const string Bounced = "bounced";

    /// <summary>Played meaningfully, then a release update landed more than the stale window after last play.</summary>
    public const string StaleButPatched = "stale_but_patched";

    /// <summary>High playtime; excluded from surfacing.</summary>
    public const string Retired = "retired";

    /// <summary>Everything else: played past the bounce threshold and not stale.</summary>
    public const string Active = "active";
}

/// <summary>
/// Tunable thresholds for the derived-bucket query. Deliberately parameters,
/// not schema: §6.1 requires retuning without migration.
/// </summary>
/// <param name="BouncedCeilingMinutes">Playtime strictly below this (and above zero) is Bounced.</param>
/// <param name="RetiredFloorMinutes">Playtime at or above this is Retired.</param>
/// <param name="StaleWindowMonths">An update more than this many months after last play marks Stale-but-patched.</param>
/// <param name="UpdateCorrelationWindowDays">
/// How far apart a build push and an announcement may be and still count as one
/// "major update" (§4.5). Neither signal means anything alone: a depot push
/// fires on DRM bumps, localization files and one-line hotfixes, and
/// announcements are pure marketing half the time. Only the pair counts.
/// <para>Default 7 days. Studios do not ship the build and the patch notes
/// simultaneously — the announcement commonly lands a day or two either side of
/// the push (a teaser before, a write-up after), and content patches often
/// trickle out as several depot pushes across a release week. A week absorbs
/// that without reaching far enough to pair a patch with the *next* month's
/// unrelated announcement. Tunable, like every other threshold here: both raw
/// signals are stored, so retuning never re-fetches (§4.5).</para>
/// </param>
public sealed record BucketThresholds(
    long BouncedCeilingMinutes,
    long RetiredFloorMinutes,
    int StaleWindowMonths,
    int UpdateCorrelationWindowDays = 7)
{
    /// <summary>Conservative defaults; per-genre configuration comes later (§6.1).</summary>
    public static BucketThresholds Default { get; } = new(
        BouncedCeilingMinutes: 120,
        RetiredFloorMinutes: 6_000,
        StaleWindowMonths: 6,
        UpdateCorrelationWindowDays: 7);
}

/// <summary>One row of the derived-bucket query: the bucket for a single ownership.</summary>
public sealed record OwnershipBucket
{
    public required long OwnershipId { get; init; }
    public required long ReleaseId { get; init; }
    public required long PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required string Bucket { get; init; }

    /// <summary>
    /// How many owned demo releases this row supersedes
    /// (<see cref="DemoConsolidation"/>) — 0 for almost every row.
    ///
    /// <para>The demos themselves are absent from the result: owning both
    /// <c>Bastion</c> and <c>Bastion Demo</c> yields one row, this one, and the
    /// demo's tile disappears. A solitary demo is a normal row with a normal
    /// count of 0.</para>
    ///
    /// <para><b>A count, deliberately, and never a total.</b> The demo's
    /// minutes belong to the demo's own ownership and are still stored,
    /// unchanged and queryable, there. Adding them to
    /// <see cref="PlaytimeMinutes"/> would be §6.2's forbidden blend — two
    /// appids, two achievement sets, two facts — so this row reports only that
    /// something was folded in, never a merged number.</para>
    /// </summary>
    public int ConsolidatedDemoCount { get; init; }
}
