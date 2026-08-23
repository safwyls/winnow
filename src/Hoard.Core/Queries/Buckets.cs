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
public sealed record BucketThresholds(
    long BouncedCeilingMinutes,
    long RetiredFloorMinutes,
    int StaleWindowMonths)
{
    /// <summary>Conservative defaults; per-genre configuration comes later (§6.1).</summary>
    public static BucketThresholds Default { get; } = new(
        BouncedCeilingMinutes: 120,
        RetiredFloorMinutes: 6_000,
        StaleWindowMonths: 6);
}

/// <summary>One row of the derived-bucket query: the bucket for a single ownership.</summary>
public sealed record OwnershipBucket
{
    public required long OwnershipId { get; init; }
    public required long ReleaseId { get; init; }
    public required long PlaytimeMinutes { get; init; }
    public DateTime? LastPlayedAt { get; init; }
    public required string Bucket { get; init; }
}
