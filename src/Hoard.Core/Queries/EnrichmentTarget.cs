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

    /// <summary>Whether <c>works.steam_app_type</c> is already set (migration 0006).</summary>
    public bool HasSteamAppType { get; init; }

    /// <summary>
    /// Whether <c>works.epic_categories</c> is already set (migration 0009).
    ///
    /// <para>Carried for the same reason <see cref="HasSteamAppType"/> is: so the
    /// pass can decide whether an Epic catalog item is worth one request to
    /// Epic's catalog service without a second query. Once set it is never asked
    /// again — the classification of a catalog item does not change, and asking
    /// would spend an authenticated request to relearn <c>public,games,applications</c>
    /// on every launch.</para>
    /// </summary>
    public bool HasEpicCategories { get; init; }

    /// <summary>
    /// The stored title — <c>releases.name</c>, falling back to the work name,
    /// the same COALESCE the bucket query makes.
    ///
    /// <para>Carried so the pass can decide whether an appid is worth asking
    /// steamcmd.net about for its <c>common.type</c>, WITHOUT a second query.
    /// The type only changes an outcome for entries
    /// <see cref="DemoConsolidation"/> is going to reason about, and asking for
    /// the other few hundred would spend a volunteer service's bandwidth to
    /// learn <c>Game</c> six hundred times.</para>
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// True when only metadata is outstanding — the backfill case for a library
    /// named by an earlier build. Reported so a run can say how much of its work
    /// was catching up rather than naming.
    /// </summary>
    public bool IsMetadataOnly => !NameIsProvisional;

    /// <summary>
    /// How many of the five metadata columns this work is still missing. 5 is
    /// "nothing at all" — a tile showing placeholder art and no year.
    ///
    /// <para><b>This is the query's primary sort key, recomputed here.</b>
    /// <see cref="Repositories.IWorkRepository.GetEnrichmentTargetsAsync"/>
    /// orders emptiest-first so that a run cut short by the window closing spends
    /// its time on the works a user can see are unfinished, rather than
    /// perfecting rows that already have four fields out of five. The C# copy
    /// exists so the caller can log and reason about the same tiering without a
    /// second round trip, and because a test can assert on it without asserting
    /// on an ORDER BY string. The two definitions must stay in step — if a
    /// sixth column joins the pass, it joins both.</para>
    ///
    /// <para><c>steam_app_type</c> is excluded from the count on purpose. It is
    /// a demo-detection detail with no visible effect on the library, and
    /// counting it would rank a fully illustrated game beside one still showing
    /// an appid.</para>
    /// </summary>
    public int MissingColumns
        => (HasIgdbId ? 0 : 1)
         + (HasFirstReleaseYear ? 0 : 1)
         + (HasSummary ? 0 : 1)
         + (HasCoverUrl ? 0 : 1)
         + (HasPublisher ? 0 : 1);
}
