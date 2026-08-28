namespace Winnow.Recommend;

/// <summary>
/// How much longitudinal evidence the library has accrued — the cold-start
/// tiers the charter names, detected from data rather than from install age.
/// Rides on the feed so a UI can calibrate its confidence copy ("early days —
/// these picks will sharpen as Winnow watches you play") instead of either
/// going blank or overclaiming.
/// </summary>
public enum DataTier
{
    /// <summary>
    /// One sync, one snapshot per game, no sessions. Only retroactive facts
    /// exist — playtime totals, last-played, patch history, facets — and the
    /// feed runs entirely on them. The real library sat here until the first
    /// sessions landed in 2026-08 (955 of 960 snapshot-bearing ownerships
    /// held exactly one reading).
    /// </summary>
    ColdStart = 0,

    /// <summary>
    /// Weeks in: at least one real snapshot delta or detected session. The
    /// tried-to-like-it signal starts contributing.
    /// </summary>
    Settling = 1,

    /// <summary>
    /// Months in: enough sessions over enough weeks that cadence and
    /// return-latency claims stop being anecdotes. Nothing scores on them yet
    /// (docs/recommendation-engine.md §7) — this tier only labels the feed.
    /// </summary>
    Established = 2,
}

/// <summary>
/// One signal's contribution to one recommendation's score, kept because
/// explainability is the contract, not a nice-to-have: a recommendation nobody
/// can interrogate cannot be debugged, cannot be tuned, and will not be
/// trusted. The <see cref="Recommendation.Reason"/> string is composed from
/// these same rows, so the prose can never drift from the arithmetic.
/// </summary>
/// <param name="Signal">Stable machine name (one of <see cref="SignalNames"/>).</param>
/// <param name="Weight">The tuning weight applied. Negative for penalties.</param>
/// <param name="Value">The signal's raw value in [0, 1].</param>
/// <param name="Contribution">Weight × value — what actually moved the score.</param>
/// <param name="Explanation">The one sentence this signal is allowed to ship if it cannot be written.</param>
public sealed record SignalContribution(
    string Signal,
    double Weight,
    double Value,
    double Contribution,
    string Explanation);

/// <summary>Stable signal names, so callers and tests never match on prose.</summary>
public static class SignalNames
{
    public const string PatchAfterDormancy = "patch_after_dormancy";
    public const string Commitment = "commitment";
    public const string Dormancy = "dormancy";
    public const string TasteAffinity = "taste_affinity";
    public const string TriedToLikeIt = "tried_to_like_it";
    public const string Installed = "installed";
    public const string BoughtTwice = "bought_twice";
    public const string RecentlyPlayed = "recently_played";
    public const string ModeMismatch = "mode_mismatch";
    public const string ProbablyDone = "probably_done";
    public const string RecentlySurfaced = "recently_surfaced";
    public const string Jitter = "jitter";
}

/// <summary>
/// One surfaced game: an owned release, its score, and why. Identity fields
/// are carried at every layer (ownership / release / work) because the caller
/// will need a different one depending on what it does next — launch, exclude,
/// or render.
/// </summary>
public sealed record Recommendation
{
    public required long OwnershipId { get; init; }
    public required long ReleaseId { get; init; }
    public required long WorkId { get; init; }
    public required string Title { get; init; }
    public required string Store { get; init; }

    /// <summary>The §6.1 bucket the row was in when scored (LibraryBuckets vocabulary).</summary>
    public required string Bucket { get; init; }

    /// <summary>
    /// The weighted sum of <see cref="Signals"/>. Comparable WITHIN this feed
    /// only — absolute magnitude shrinks at cold start because missing evidence
    /// contributes zero rather than being renormalised away, and that
    /// shrinkage is honest. Never store this anywhere.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// The human-readable why. Always non-empty: an item that cannot explain
    /// itself does not ship (charter hard constraint).
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>Every signal that moved the score, penalties included, for interrogation and tuning.</summary>
    public required IReadOnlyList<SignalContribution> Signals { get; init; }
}

/// <summary>The engine's answer: a ranked feed plus the evidence tier it was computed at.</summary>
public sealed record RecommendationFeed
{
    public required IReadOnlyList<Recommendation> Items { get; init; }

    /// <summary>See <see cref="DataTier"/> — how much history backed this feed.</summary>
    public required DataTier Tier { get; init; }

    /// <summary>
    /// How many ownerships survived the hard exclusions and were actually
    /// scored. Lets a caller distinguish "quiet feed" from "empty library".
    /// </summary>
    public required int CandidateCount { get; init; }
}
