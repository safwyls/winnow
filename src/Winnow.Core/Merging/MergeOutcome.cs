namespace Winnow.Core.Merging;

/// <summary>
/// The dependent-table inventory for a merge. Every table with a foreign key to
/// <c>works</c>, <c>releases</c> or <c>ownerships</c> has a field here. A table
/// that gained a foreign key without gaining a field is a table the merge forgot,
/// and the cascade tripwire in the repository will throw rather than let
/// ON DELETE CASCADE silently destroy its rows.
/// </summary>
public sealed record MergeRepointCounts
{
    // ── Work layer ───────────────────────────────────────────────────────────
    public int Releases { get; init; }

    public int WorkFacets { get; init; }

    // ── Release layer ────────────────────────────────────────────────────────
    public int ExternalIds { get; init; }

    public int Ownerships { get; init; }

    public int Achievements { get; init; }

    public int AchievementUnlocks { get; init; }

    public int UpdateEvents { get; init; }

    public int UpdateAcknowledgements { get; init; }

    public int ListItems { get; init; }

    public int ReleaseFacets { get; init; }

    public int FeedVerdicts { get; init; }

    public int FeedSurfacings { get; init; }

    public int MergeCandidates { get; init; }

    // ── Ownership layer (folded same-store ownerships) ───────────────────────
    public int OwnershipsFolded { get; init; }

    public int PlayRecords { get; init; }

    public int PlaytimeSnapshots { get; init; }

    public int Sessions { get; init; }

    public int OwnershipAccounts { get; init; }

    /// <summary>
    /// Rows removed because an identical row already existed on the surviving
    /// side. For <c>play_records</c> and <c>playtime_snapshots</c> the unique key
    /// covers every column except the id, so a collision after repointing is a
    /// byte-identical observation already present on the survivor. Dropping it is
    /// the same deduplication migration 0013 established, not a lost fact.
    /// </summary>
    public int DuplicateRowsDropped { get; init; }
}

/// <summary>
/// What happened when a merge was attempted. <see cref="Applied"/> is false when
/// the plan came back <see cref="MergeMode.NothingToDo"/>; the database is
/// untouched in that case. When true, <see cref="ApplicationId"/> is the
/// <c>merge_applications</c> row and <see cref="Repointed"/> is the full
/// dependent-table inventory of what moved.
/// </summary>
public sealed record MergeOutcome
{
    public required MergePlan Plan { get; init; }

    public bool Applied { get; init; }

    public long? ApplicationId { get; init; }

    public MergeRepointCounts Repointed { get; init; } = new();

    public static MergeOutcome NotApplied(MergePlan plan) => new() { Plan = plan, Applied = false };
}
