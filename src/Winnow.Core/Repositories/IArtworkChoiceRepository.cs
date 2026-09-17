using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

public interface IArtworkChoiceRepository
{
    Task<IReadOnlyList<ArtworkChoice>> GetAllAsync(CancellationToken ct = default);

    Task<ArtworkChoice?> GetEffectiveAsync(
        IReadOnlyCollection<long> workIds, ArtworkSlot slot, CancellationToken ct = default);

    /// <summary>Replace one kind on the original work, preserving the other kind. Returns its new revision.</summary>
    Task<long> SetAsync(ArtworkChoice choice, CancellationToken ct = default);

    /// <summary>Remove both kinds for one slot across the current confirmed group, restoring automatic selection.</summary>
    Task ResetAsync(IReadOnlyCollection<long> workIds, ArtworkSlot slot, CancellationToken ct = default);

    /// <summary>
    /// Restore a preceding value only if the same revision still occupies the slot and kind.
    /// A null preceding value removes it. A restoration receives a new revision.
    /// </summary>
    Task<bool> TryRestoreAsync(ArtworkChoice written, ArtworkChoice? preceding, CancellationToken ct = default);
}
