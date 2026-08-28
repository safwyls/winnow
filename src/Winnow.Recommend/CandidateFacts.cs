namespace Winnow.Recommend;

/// <summary>
/// How a candidate's game modes sit against the way this user actually plays.
/// Classified by <see cref="TasteProfile.ClassifyModes"/>, only when the
/// evidence clears the tuning's floors — a library that plays a bit of
/// everything, or one with too few mode-carrying committed games, classifies
/// everything as <see cref="None"/>.
/// </summary>
public enum ModeMismatch
{
    /// <summary>No mismatch, or not enough evidence to claim one.</summary>
    None = 0,

    /// <summary>The candidate is online-multiplayer-only and the user's committed games are overwhelmingly single-player.</summary>
    OnlineOnlyForSoloPlayer = 1,

    /// <summary>The candidate is strictly single-player and the user's committed games are overwhelmingly online.</summary>
    SoloOnlyForOnlinePlayer = 2,
}

/// <summary>
/// Everything the scorer may know about one candidate, already read and
/// flattened. The split matters: <see cref="RecommendationScorer"/> is pure
/// functions over this record, so every curve and penalty is unit-testable
/// without a database, and the engine's only job is honest assembly.
/// </summary>
public sealed record CandidateFacts
{
    public required long OwnershipId { get; init; }
    public required long ReleaseId { get; init; }
    public required long WorkId { get; init; }
    public required string Title { get; init; }
    public required string Store { get; init; }

    /// <summary>The §6.1 bucket (LibraryBuckets vocabulary). Retired never reaches the scorer.</summary>
    public required string Bucket { get; init; }

    /// <summary>Latest cumulative minutes. Zero can mean zero OR unmeasured — the bucket disambiguates.</summary>
    public required long PlaytimeMinutes { get; init; }

    /// <summary>
    /// Null means one of two very different things, and the pairing with
    /// minutes tells them apart, same as the bucket query: null beside zero
    /// minutes is "never opened" (no time evidence at all); null beside REAL
    /// minutes is Steam's pre-timestamp sentinel — played, unknown when,
    /// certainly ancient.
    /// </summary>
    public DateTime? LastPlayedAt { get; init; }

    public bool Installed { get; init; }

    /// <summary>Distinct stores this candidate's WORK is owned on. 2+ is a purchase made twice.</summary>
    public int StoreCount { get; init; } = 1;

    /// <summary>
    /// Taste-affinity value in [0,1], computed by the engine from the facet
    /// snapshot (playtime-weighted profile), plus the descriptor's display
    /// name for the explanation. Null when the library has no facet evidence
    /// or the candidate carries none — absent evidence, not a zero match.
    /// </summary>
    public double? TasteAffinity { get; init; }
    public string? TasteFacetName { get; init; }

    /// <summary>True when the caller's feed showed this release recently.</summary>
    public bool RecentlySurfaced { get; init; }

    /// <summary>
    /// The engine's verdict on game-mode fit (see <see cref="Recommend.ModeMismatch"/>).
    /// Computed against the taste profile's mode tally, because the scorer is
    /// pure over these facts and cannot see the profile itself.
    /// </summary>
    public ModeMismatch ModeMismatch { get; init; }

    /// <summary>
    /// Genre-kind facet ids this candidate carries, for the shelf-level
    /// diversity cap. Empty when nothing is known — a game with no metadata
    /// can never be "too much of one genre".
    /// </summary>
    public IReadOnlyList<long> GenreFacetIds { get; init; } = [];

    /// <summary>
    /// Distinct play episodes the history shows (snapshot rises, or sessions
    /// when those exist) — 2 or more means the user came back after the first
    /// taste. Null when history was not probed for this row (only the
    /// shortlist is). Null and 0 score the same today; they are kept apart so
    /// "no evidence" never gets reported as "never returned".
    /// </summary>
    public int? ReturnEpisodes { get; init; }

    /// <summary>
    /// Major updates observed after <see cref="LastPlayedAt"/> and the latest
    /// one's title, when the shortlist probe fetched them. Reason decoration
    /// only — the SCORING input for the patch signal is bucket membership,
    /// which the §6.1 query computed with the correlation heuristic; a second
    /// count here disagreeing with it would be two definitions of one fact.
    /// </summary>
    public int? UpdatesSinceLastPlayed { get; init; }
    public string? LatestUpdateTitle { get; init; }
}
