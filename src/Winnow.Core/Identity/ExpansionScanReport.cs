using Winnow.Core.Identity;

namespace Winnow.Resolve;

/// <summary>
/// One work the scan is willing to talk about, with the store entries hanging
/// off it so a card can draw covers and entry numbers without a second read.
/// </summary>
/// <param name="WorkId">The resolved work id, so a same-game group appears once.</param>
/// <param name="Title">The title the library shows for it.</param>
/// <param name="ReleaseIds">Every store entry under this work, ascending.</param>
public sealed record ExpansionCandidateWork(
    long WorkId, string Title, IReadOnlyList<long> ReleaseIds);

/// <summary>One proposed expansion, with the evidence for it.</summary>
/// <param name="Work">The proposed expansion.</param>
/// <param name="Evidence">What the detector observed about this pair.</param>
public sealed record ExpansionProposalMember(
    ExpansionCandidateWork Work, ExpansionEvidence Evidence)
{
    /// <summary>
    /// The kind the affirmative answer writes, one of
    /// <see cref="IdentityLinkKinds"/>. <c>expansion_of</c> counts as a title
    /// and does not roll up playtime (the user's decision of 2026-08-31).
    /// <c>variant_of</c> does not count as a title while its parent is owned,
    /// counts when it is the only thing owned, and never rolls up playtime,
    /// though the variant's own hours stay visible on the parent's modal.
    /// </summary>
    public string Kind { get; init; } = IdentityLinkKinds.ExpansionOf;

    /// <summary>
    /// The source's own word for the relation, one of
    /// <see cref="RelationLabels"/>, or null when nothing named it. A card
    /// showing "Demo" or "Remaster" or "Standalone expansion" reads this, not
    /// <see cref="Kind"/>. Three kinds exist (each defined by the numbers it
    /// changes, costing a table rebuild); labels are vocabulary and cost
    /// nothing. IGDB has fifteen type names today and will add more.
    /// </summary>
    public string? RelationLabel { get; init; }

    /// <summary>
    /// True when a storefront proposed this pair rather than the title
    /// heuristic. The distinction matters for confidence, not for authority:
    /// it is still a proposal the user may refuse.
    /// </summary>
    public bool FromMetadata { get; init; }
}

/// <summary>
/// One base game and every expansion proposed under it: one card, one act. The
/// one-to-many relation presented once, rather than six pairwise questions each
/// invalidating the next.
/// </summary>
/// <param name="Base">The base game the members extend.</param>
/// <param name="Members">The proposed expansions, in the order the detector produced them.</param>
public sealed record ExpansionProposalGroup(
    ExpansionCandidateWork Base, IReadOnlyList<ExpansionProposalMember> Members);

/// <summary>What one scan looked at and produced.</summary>
/// <param name="Works">How many works were compared.</param>
/// <param name="Excluded">How many rows were dropped before comparing: non-games, placeholder names, works with no usable row.</param>
/// <param name="Groups">One entry per base game with at least one proposal, base work id ascending.</param>
/// <param name="Elapsed">How long the pass took. Logged, so a slow library shows up as a number.</param>
public sealed record ExpansionScanReport(
    int Works, int Excluded, IReadOnlyList<ExpansionProposalGroup> Groups, TimeSpan Elapsed)
{
    /// <summary>The report for a library with nothing in it.</summary>
    public static ExpansionScanReport Empty { get; } = new(0, 0, [], TimeSpan.Zero);
}

public interface IExpansionReviewScan
{
 Task<ExpansionScanReport> ScanAsync(CancellationToken ct = default);
}
