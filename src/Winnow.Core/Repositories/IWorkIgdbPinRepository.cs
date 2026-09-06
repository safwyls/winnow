using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// Reads and writes <c>work_igdb_pins</c> (migration 0026). Append-and-stamp:
/// pinning inserts, clearing stamps <c>cleared_at</c>, and the history is the
/// table.
/// </summary>
public interface IWorkIgdbPinRepository
{
    /// <summary>
    /// Records a user-chosen IGDB mapping and rewrites the work's metadata to
    /// match. Returns an outcome rather than throwing for the two refusal cases:
    /// the work does not exist, and another work already holds that
    /// <c>igdb_id</c>. Re-pinning to a different IGDB game stamps the previous
    /// row and inserts a fresh one.
    /// </summary>
    Task<WorkIgdbPinOutcome> PinAsync(WorkIgdbPinAssignment assignment, CancellationToken ct = default);

    /// <summary>
    /// Stamps the live pin as cleared, returning the work to automatic
    /// resolution. The metadata the pin wrote stays in place; the next
    /// automatic pass fills what is empty around it.
    /// </summary>
    Task<bool> ClearAsync(long workId, CancellationToken ct = default);

    /// <summary>
    /// Returns the live pin for a work, or null when none is active.
    /// </summary>
    Task<WorkIgdbPin?> GetAsync(long workId, CancellationToken ct = default);

    /// <summary>
    /// Bulk companion to <see cref="GetAsync"/>: one query returning the work
    /// ids that currently carry a live (uncleared) pin. Callers that need the
    /// whole set — the library load's cover-key precedence — use this instead
    /// of calling <c>GetAsync</c> once per work.
    ///
    /// <para>Returns an empty set, never null, when nothing is pinned.
    /// Cleared pins are excluded, and the partial unique index
    /// <c>ux_work_igdb_pins_live</c> guarantees a re-pinned work appears
    /// exactly once.</para>
    /// </summary>
    Task<IReadOnlySet<long>> GetLivePinnedWorkIdsAsync(CancellationToken ct = default);
}
