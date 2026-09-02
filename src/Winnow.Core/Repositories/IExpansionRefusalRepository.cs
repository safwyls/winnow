using Winnow.Core.Identity;

namespace Winnow.Core.Repositories;

/// <summary>
/// Reads and writes expansion refusals (migration 0020).
///
/// <para>Only the refusals. The affirmative answer lives in
/// <c>identity_links</c> at kind <c>expansion_of</c>, because a proposal is
/// answered affirmatively if and only if such a link is live — one home for
/// one answer.</para>
/// </summary>
public interface IExpansionRefusalRepository
{
    /// <summary>
    /// Every refusal, ordered by id. The scan reads the whole table once per
    /// pass and filters in memory; the table holds one row per question a
    /// person has answered, so it stays small.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<ExpansionRefusal>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Records that these pairs are not a base game and its expansion.
    /// Idempotent: refusing the same pair twice writes one row, so a second
    /// answer is a no-op rather than a constraint violation.
    /// </summary>
    /// <param name="pairs">The directional pairs to refuse. Empty writes nothing.</param>
    /// <param name="note">Free text stored on every row written by this call.</param>
    /// <param name="ct">Cancellation.</param>
    Task RefuseAsync(
        IReadOnlyList<ExpansionRefusalRequest> pairs,
        string? note = null,
        CancellationToken ct = default);
}
