namespace Winnow.Core.Identity;

/// <summary>
/// One store entry of one game, as the coverage section shows it. Minutes
/// and last-played on this record belong to the SAME store entry and are
/// never crossed — that pairing is the F10 hazard, and the way it is
/// avoided here is that no type in this file ever holds one entry's minutes
/// beside another entry's date.
/// </summary>
public sealed record CoverageEntry
{
    /// <summary>The ownership row this entry is.</summary>
    public required long OwnershipId { get; init; }

    /// <summary>
    /// The release the ownership is of. Achievements are read by this id
    /// and by nothing coarser (§6.2).
    /// </summary>
    public required long ReleaseId { get; init; }

    /// <summary>
    /// The work the release belongs to, UNRESOLVED. This is what decides
    /// whether the entry is the game itself or one of the titles it covers.
    /// </summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// The title as this entry's own work names it, so a covered title reads
    /// under its own name rather than under the primary's.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>The store this entry belongs to — steam, epic, gog.</summary>
    public required string Store { get; init; }

    /// <summary>This entry's own minutes.</summary>
    public required long PlaytimeMinutes { get; init; }

    /// <summary>This entry's own last-played, or null.</summary>
    public DateTime? LastPlayedAt { get; init; }
}

/// <summary>
/// The composite playtime figure, and the whole of the F10 discipline. The
/// user's decision of 2026-08-31 is that headline playtime SUMS across
/// stores, and no single source reports the sum, so it is Winnow's own
/// composite. A sum must never be displayed beside a last-played date
/// belonging to one store, so this type has no public constructor and no
/// settable members: the ONLY way to obtain one is <see cref="Across"/>,
/// which derives the minutes and the date from the same set of entries in
/// one pass. The date it derives is the latest last-played anywhere in the
/// group — a coherent statement about the composite rather than a fact
/// borrowed from one store.
/// </summary>
public sealed class CoveragePlaytime
{
    private CoveragePlaytime(long minutes, DateTime? lastPlayedAt, int entryCount)
    {
        PlaytimeMinutes = minutes;
        LastPlayedAt = lastPlayedAt;
        EntryCount = entryCount;
    }

    /// <summary>Nothing owned, nothing played.</summary>
    public static CoveragePlaytime Empty { get; } = new(0, null, 0);

    /// <summary>Minutes summed across every entry in the group.</summary>
    public long PlaytimeMinutes { get; }

    /// <summary>
    /// The latest last-played across the SAME entries the minutes were
    /// summed over. Null only when no entry has a date at all.
    /// </summary>
    public DateTime? LastPlayedAt { get; }

    /// <summary>
    /// How many store entries the sum is over. One means the composite is
    /// just that entry, so a caller can decline to draw it.
    /// </summary>
    public int EntryCount { get; }

    /// <summary>True when more than one entry contributed.</summary>
    public bool IsComposite => EntryCount > 1;

    /// <summary>
    /// The only constructor. Both figures come out of one pass over one set,
    /// so a sum cannot be paired with a date that was not derived from the
    /// same entries.
    /// </summary>
    public static CoveragePlaytime Across(IEnumerable<CoverageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var minutes = 0L;
        DateTime? latest = null;
        var count = 0;

        foreach (var entry in entries)
        {
            count++;
            minutes += entry.PlaytimeMinutes;

            if (entry.LastPlayedAt is { } played && (latest is null || played > latest))
            {
                latest = played;
            }
        }

        return count == 0 ? Empty : new CoveragePlaytime(minutes, latest, count);
    }
}

/// <summary>
/// What one game covers, for the details modal. Built from a
/// <see cref="SameGameResolution"/> and from nothing else —
/// <see cref="ExpansionGrouping"/> has no <c>Resolve</c> method, so an
/// expansion link cannot be passed here and cannot move the sum.
/// Expansions are titles; their playtime does not roll up.
///
/// <para>Every covered entry keeps its OWN store, its OWN minutes and its
/// OWN last-played, and those three always describe the same entry. The
/// composite is a <see cref="CoveragePlaytime"/>, which cannot exist
/// without its own coherent date.</para>
///
/// <para>Derived from the rows the library already read, so a link
/// retracted a moment ago stops covering anything on the very next read,
/// exactly like demo consolidation. Nothing here is stored.</para>
/// </summary>
public sealed class IdentityCoverage
{
    private IdentityCoverage(
        long primaryWorkId,
        IReadOnlyList<CoverageEntry> ownEntries,
        IReadOnlyList<CoverageEntry> coveredEntries,
        CoveragePlaytime total)
    {
        PrimaryWorkId = primaryWorkId;
        OwnEntries = ownEntries;
        CoveredEntries = coveredEntries;
        Total = total;
    }

    /// <summary>Coverage for a game that covers nothing.</summary>
    public static IdentityCoverage Empty { get; } = new(0, [], [], CoveragePlaytime.Empty);

    /// <summary>The work the group is filed under.</summary>
    public long PrimaryWorkId { get; }

    /// <summary>
    /// The primary work's own store entries, ordered by ownership id.
    /// Present even when nothing is covered, because the per-store breakdown
    /// is what lets the user check the composite.
    /// </summary>
    public IReadOnlyList<CoverageEntry> OwnEntries { get; }

    /// <summary>
    /// The entries belonging to the titles this game covers, ordered by work
    /// id then ownership id so the list does not shuffle between reads.
    /// </summary>
    public IReadOnlyList<CoverageEntry> CoveredEntries { get; }

    /// <summary>The composite across own and covered entries together.</summary>
    public CoveragePlaytime Total { get; }

    /// <summary>True when this game covers at least one other title.</summary>
    public bool HasCoverage => CoveredEntries.Count > 0;

    /// <summary>
    /// Builds coverage for the group <paramref name="workId"/> belongs to.
    /// <paramref name="entries"/> is every entry the library read — the
    /// caller passes the rows it is already showing, so an entry the bucket
    /// query filtered out (a consolidated demo, a non-game, a row hidden by
    /// the account filter) is not counted here either, and the modal cannot
    /// report a figure the grid does not stand behind.
    /// </summary>
    public static IdentityCoverage For(
        long workId,
        SameGameResolution resolution,
        IEnumerable<CoverageEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(entries);

        var primary = resolution.Resolve(workId);

        var own = new List<CoverageEntry>();
        var covered = new List<CoverageEntry>();
        foreach (var entry in entries)
        {
            if (resolution.Resolve(entry.WorkId) != primary)
            {
                continue;
            }

            if (entry.WorkId == primary)
            {
                own.Add(entry);
            }
            else
            {
                covered.Add(entry);
            }
        }

        own.Sort(static (a, b) => a.OwnershipId.CompareTo(b.OwnershipId));
        covered.Sort(static (a, b) => a.WorkId == b.WorkId
            ? a.OwnershipId.CompareTo(b.OwnershipId)
            : a.WorkId.CompareTo(b.WorkId));

        // One pass over own AND covered together: the sum and its date are
        // derived from the same set, which is the F10 guarantee.
        return new IdentityCoverage(
            primary, own, covered, CoveragePlaytime.Across(own.Concat(covered)));
    }
}
