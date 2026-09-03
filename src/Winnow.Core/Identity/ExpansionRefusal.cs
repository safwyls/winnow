namespace Winnow.Core.Identity;

/// <summary>
/// One stored refusal: the user said these two titles are not a base game and
/// its expansion. Directional, so it answers only the direction that was asked.
///
/// <para>Refusals are stored and proposals are not. A proposal is re-derived on
/// every scan because the detector's guards get tuned; a refusal is a decision
/// a person made about two specific titles and cannot be re-derived.</para>
/// </summary>
public sealed record ExpansionRefusal
{
    /// <summary>The <c>expansion_refusals.id</c> row key.</summary>
    public long Id { get; init; }

    /// <summary>The work the proposal named as the base.</summary>
    public required long BaseWorkId { get; init; }

    /// <summary>The work the proposal named as the expansion.</summary>
    public required long ChildWorkId { get; init; }

    /// <summary>
    /// When the user said no, in UTC. The FIRST time: refusing the same pair
    /// again keeps this stamp, because that is when the answer was given.
    /// </summary>
    public required DateTime RefusedAt { get; init; }

    /// <summary>Free text about the refusal. Nothing writes one today.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// One pair to refuse. Directional: refusing "child extends base" says nothing
/// about the reverse claim.
/// </summary>
/// <param name="BaseWorkId">The work the proposal named as the base.</param>
/// <param name="ChildWorkId">The work the proposal named as the expansion.</param>
public readonly record struct ExpansionRefusalRequest(long BaseWorkId, long ChildWorkId);
