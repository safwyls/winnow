using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// The <c>hidden_games</c> table (migration 0023). Append-and-stamp: hiding
/// inserts a row, unhiding stamps <c>unhidden_at</c> rather than deleting,
/// so the row is the history and "is this game hidden" stays a query.
/// </summary>
public interface IHiddenGameRepository
{
    /// <summary>
    /// Hides a work. Returns false when the work does not exist or is already
    /// hidden (idempotent against the partial unique index).
    /// </summary>
    Task<bool> HideAsync(long workId, CancellationToken ct = default);

    /// <summary>
    /// Unhides a work by stamping the live row's <c>unhidden_at</c>. Returns
    /// false when nothing was live to unhide. Re-hiding after an unhide is a
    /// fresh row, so there is no terminal state.
    /// </summary>
    Task<bool> UnhideAsync(long workId, CancellationToken ct = default);

    /// <summary>True when at least one row for this work has <c>unhidden_at IS NULL</c>.</summary>
    Task<bool> IsHiddenAsync(long workId, CancellationToken ct = default);

    /// <summary>Every currently-hidden work id, for the bucket query's exclusion.</summary>
    Task<IReadOnlyList<long>> GetHiddenWorkIdsAsync(CancellationToken ct = default);

    /// <summary>
    /// Every currently-hidden game with its title and store-entry count, for
    /// the unhide screen.
    /// </summary>
    Task<IReadOnlyList<HiddenGame>> GetHiddenGamesAsync(CancellationToken ct = default);
}
