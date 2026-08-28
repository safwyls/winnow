namespace Winnow.Core.Queries;

/// <summary>
/// Everything the soft matcher (§5.3 step 2) needs to know about one release,
/// in one row — the release, the work above it, and the metadata a title match
/// is corroborated against.
///
/// <para><b>Why a projection rather than <c>Release</c> + <c>Work</c>.</b> The
/// sweep reads every release in the library and needs the work's year and
/// provisional flag for each one. Fetching a <c>Work</c> per <c>Release</c> is
/// the N+1 that turns a 600-game pass into 1,200 round trips; the join costs
/// one.</para>
///
/// <para><b>Publisher.</b> §5.3 names publisher as a soft-match signal and the
/// matcher has always scored it, but until migration 0005 there was no column
/// to read it from, so on every library-internal pair the signal reported
/// "publisher unknown on at least one side" and contributed nothing — leaving
/// title similarity as the only evidence in the score. It now joins here, from
/// <c>works.publisher</c>, alongside the year. Null is still normal and still
/// correct: absent evidence contributes nothing and is never renormalised away
/// (SoftMatchThresholds), so an unenriched work scores exactly as it did.</para>
///
/// <para><b>Properties, not positional parameters.</b> SQLite reports every
/// INTEGER column as <c>Int64</c>, so Dapper cannot match a constructor taking
/// <c>int?</c> and <c>bool</c> and refuses to materialise a positional record.
/// Init-only properties go through setters, where Dapper does convert — the
/// same reason <c>Work</c> is shaped this way.</para>
/// </summary>
public sealed record ReleaseIdentity
{
    /// <summary>
    /// <c>releases.id</c> — the identity <c>merge_candidates</c> rows are keyed by.
    /// </summary>
    public required long ReleaseId { get; init; }

    /// <summary>
    /// Its work. Two releases of the SAME work are never compared: they are the
    /// Skyrim / Skyrim Special Edition case, already correctly modelled as
    /// separate releases, and asking the user to merge them is asking to
    /// corrupt the four-layer model (§9 pitfall 5).
    /// </summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// <c>releases.name</c>. This, not the work name, is what gets matched: the
    /// release is the layer carrying the edition, and the edition is what the
    /// rebuild-edition veto reads.
    /// </summary>
    public required string ReleaseName { get; init; }

    /// <summary>The work's title, used when the release row has nothing usable.</summary>
    public required string WorkName { get; init; }

    /// <summary><c>works.first_release_year</c>, or null. Feeds the ±1-year signal.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary>
    /// <c>works.publisher</c>, or null. Feeds the publisher signal: one
    /// deterministic name, normalised by the matcher before comparison (see
    /// migration 0005 for why it is a single string rather than a list).
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// True when the name is a machine-minted placeholder (<c>App 1203620</c>).
    /// These are excluded from matching outright — a placeholder is evidence
    /// about nothing, and comparing two of them compares two appids.
    /// </summary>
    public bool NameIsProvisional { get; init; }

    /// <summary>
    /// The title to match on: the release name, falling back to the work name
    /// when the release row is blank.
    /// </summary>
    public string MatchTitle =>
        string.IsNullOrWhiteSpace(ReleaseName) ? WorkName : ReleaseName;
}
