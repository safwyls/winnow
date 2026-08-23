namespace Hoard.Core.Repositories;

/// <summary>
/// The little bit of durable state the resolver keeps about itself, over the
/// §6 <c>settings</c> table.
///
/// <para><b>Why it exists.</b> An empty merge queue has two entirely different
/// meanings — "the matcher compared your library and found nothing ambiguous"
/// and "the matcher has not run" — and the queue screen cannot tell them apart
/// from <c>merge_candidates</c> alone, because both look like zero rows. The
/// first is a fact about the user's library; the second is a fact about
/// Hoard's own plumbing, and stating the first when the second is true is the
/// interface lying about the data (design-system §7: empty states are
/// directions, not moods, and they have to be true).</para>
///
/// <para>Only completed sweeps are recorded. A sweep that threw, or that was
/// cancelled by the window closing, leaves the previous value alone — so the
/// worst this can be is stale, never optimistic.</para>
/// </summary>
public interface IResolveStateRepository
{
    /// <summary>
    /// When a soft-match sweep last ran to completion, or null when none ever
    /// has on this database.
    /// </summary>
    Task<DateTimeOffset?> GetLastSoftMatchSweepAsync(CancellationToken ct = default);

    /// <summary>Records a completed sweep. Called only after the queue writes commit.</summary>
    Task SetLastSoftMatchSweepAsync(DateTimeOffset completedAt, CancellationToken ct = default);
}
