using Hoard.Core.Matching;

namespace Hoard.Resolve.Matching;

/// <summary>Where a scored pair lands. Note that none of these means "merge it".</summary>
public enum SoftMatchBand
{
    /// <summary>Below the queue floor, or vetoed. Not written to <c>merge_candidates</c> at all.</summary>
    Discarded = 0,

    /// <summary>Queued as <c>status='pending'</c> for human confirmation.</summary>
    Review = 1,

    /// <summary>
    /// Queued, and shown first. <b>Still not auto-merged</b> — in M1 only the
    /// external-id hard join may merge without asking (§5.3).
    /// </summary>
    Priority = 2,
}

/// <summary>Stable identifiers for the signals, used as JSON keys and UI labels.</summary>
public static class SoftMatchSignalNames
{
    public const string Title = "title_similarity";
    public const string ReleaseYear = "release_year";
    public const string Publisher = "publisher";
    public const string CoverHash = "cover_hash";
    public const string BundleEdition = "bundle_edition";
}

/// <summary>Stable identifiers for the veto reasons.</summary>
public static class SoftMatchVetoReasons
{
    /// <summary>Sequel numbers differ: Portal / Portal 2, Dark Souls II / Dark Souls III.</summary>
    public const string SequelOrdinal = "sequel_ordinal";

    /// <summary>Separate builds: Skyrim / Skyrim Special Edition / Skyrim Anniversary Edition (§9 pitfall 5).</summary>
    public const string RebuildEdition = "rebuild_edition";

    /// <summary>Normalised titles are too far apart to be the same game under any other evidence.</summary>
    public const string TitleBelowFloor = "title_below_floor";

    /// <summary>One or both titles normalised to nothing comparable.</summary>
    public const string EmptyTitle = "empty_title";

    /// <summary>A release cannot be a merge candidate with itself.</summary>
    public const string SameRelease = "same_release";
}

/// <summary>
/// One signal's verdict, as the merge-confirm UI needs it (design-system §6:
/// "signal diff between them: title distance, year delta, publisher").
/// </summary>
/// <param name="Name">Stable key from <see cref="SoftMatchSignalNames"/>.</param>
/// <param name="Fired">
/// False when the signal could not be evaluated because one or both sides
/// lacked the data. A signal that did not fire contributes exactly zero — it is
/// never renormalised into agreement.
/// </param>
/// <param name="Agreement">
/// How much the two sides agreed, in [0,1], or null when
/// <paramref name="Fired"/> is false. 1.0 is perfect agreement.
/// </param>
/// <param name="Contribution">Signed points this signal added to the final score.</param>
/// <param name="Detail">Short human phrase for the UI, e.g. "years 2006 vs 2017 (Δ11)".</param>
public sealed record SoftMatchSignal(
    string Name,
    bool Fired,
    double? Agreement,
    double Contribution,
    string Detail);

/// <summary>
/// The full verdict on one candidate pair: a confidence in [0,1], the band it
/// falls in, and the itemised breakdown of which signals fired and what each
/// one was worth.
///
/// <para>The breakdown is not diagnostics — it is the product. The merge-confirm
/// UI has to show the user <i>why</i> two things might be the same game before
/// asking "Same game / Different games", and a bare number cannot answer
/// that.</para>
/// </summary>
public sealed record SoftMatchScore
{
    public required MatchSubject Left { get; init; }
    public required MatchSubject Right { get; init; }

    /// <summary>Normalised form of <see cref="Left"/>'s title, for the UI's title diff.</summary>
    public required NormalizedTitle LeftTitle { get; init; }

    public required NormalizedTitle RightTitle { get; init; }

    /// <summary>Confidence in [0,1]. Zero whenever <see cref="VetoReason"/> is set.</summary>
    public required double Score { get; init; }

    public required SoftMatchBand Band { get; init; }

    /// <summary>
    /// Non-null when a structural check rejected the pair before scoring — a
    /// value from <see cref="SoftMatchVetoReasons"/>. A veto is not "low
    /// confidence"; it is "these are provably different things", and the pair
    /// is discarded rather than queued.
    /// </summary>
    public string? VetoReason { get; init; }

    public required IReadOnlyList<SoftMatchSignal> Signals { get; init; }

    // ── Fields the merge-confirm UI reads directly (design-system §6) ────────

    /// <summary>Normalised title similarity in [0,1]; 1.0 - this is the "title distance" the UI shows.</summary>
    public required double TitleSimilarity { get; init; }

    /// <summary>Absolute year delta, or null when either year is unknown.</summary>
    public int? YearDelta { get; init; }

    /// <summary>True/false when both publishers are known, null otherwise.</summary>
    public bool? PublisherMatch { get; init; }

    /// <summary>Hamming distance between the two cover hashes (0–64), or null when either is missing.</summary>
    public int? CoverHashDistance { get; init; }

    /// <summary>
    /// <b>Always false.</b> §5.3's non-negotiable: never auto-merge on fuzzy
    /// title similarity. It is a property rather than an absent concept so that
    /// call sites reading it, and tests asserting it, both have something
    /// concrete to point at — and so nobody re-derives "well, 0.97 is basically
    /// certain" at a call site six months from now.
    /// </summary>
    public bool AutoMergeAllowed => false;

    /// <summary>True when this pair should be written to <c>merge_candidates</c>.</summary>
    public bool ShouldQueue => Band is SoftMatchBand.Review or SoftMatchBand.Priority;
}

/// <summary>A <see cref="SoftMatchScore"/> in a ranked result list.</summary>
public sealed record RankedMatch(MatchSubject Possibility, SoftMatchScore Score);
