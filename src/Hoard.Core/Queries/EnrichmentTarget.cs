namespace Hoard.Core.Queries;

/// <summary>
/// A work enrichment still has something to do for, paired with the external id
/// it can be looked up by.
///
/// <para><b>Wider than <see cref="ProvisionalNameTarget"/>, and deliberately
/// so.</b> Enrichment used to ask only "which works still carry a placeholder
/// name?". On a library that has been named once — which, after the first run,
/// is every library — that set is empty, so a pass that had learned how to
/// store the year, summary, cover and publisher would still have had nothing to
/// store them for. A work whose title is real but whose metadata columns are
/// empty is exactly as unfinished as one with no title, and the matcher feels
/// it far more sharply: a missing year and publisher are two of §5.3's four
/// signals silently not firing.</para>
///
/// <para><b>Why the row carries what it already knows.</b> The columns already
/// filled decide what the writer offers, so a source answering with less than
/// the database holds cannot be mistaken for an instruction to erase. It also
/// makes the "already complete" case observable from the query alone.</para>
///
/// <para><b>Properties, not positional parameters</b> — SQLite reports every
/// INTEGER as <c>Int64</c>, so Dapper cannot bind a constructor taking
/// <c>int?</c>/<c>bool</c>, the same reason <see cref="ReleaseIdentity"/> is
/// shaped this way.</para>
/// </summary>
public sealed record EnrichmentTarget
{
    /// <summary>The work to fill in.</summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// Its release. M0/M1 are 1:1, and <c>releases.name</c> is NOT NULL and
    /// carries the same placeholder, so a name promotion moves both together —
    /// otherwise the release keeps <c>App 1203620</c> forever with nothing to
    /// find it by.
    /// </summary>
    public required long ReleaseId { get; init; }

    /// <summary>External id provider, e.g. <c>steam</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's id, e.g. the Steam appid.</summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// True when the stored name is a machine-minted placeholder. Only these
    /// may be renamed, and only these justify falling back to the Steam store —
    /// which exists to supply titles, not metadata.
    /// </summary>
    public bool NameIsProvisional { get; init; }

    /// <summary>Whether <c>works.igdb_id</c> is already set.</summary>
    public bool HasIgdbId { get; init; }

    /// <summary>Whether <c>works.first_release_year</c> is already set.</summary>
    public bool HasFirstReleaseYear { get; init; }

    /// <summary>Whether <c>works.summary</c> is already set.</summary>
    public bool HasSummary { get; init; }

    /// <summary>Whether <c>works.cover_url</c> is already set.</summary>
    public bool HasCoverUrl { get; init; }

    /// <summary>Whether <c>works.publisher</c> is already set (migration 0005).</summary>
    public bool HasPublisher { get; init; }

    /// <summary>
    /// True when only metadata is outstanding — the backfill case for a library
    /// named by an earlier build. Reported so a run can say how much of its work
    /// was catching up rather than naming.
    /// </summary>
    public bool IsMetadataOnly => !NameIsProvisional;
}
