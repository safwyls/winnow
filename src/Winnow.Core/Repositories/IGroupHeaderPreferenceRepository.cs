namespace Winnow.Core.Repositories;

/// <summary>Presentation choices for same-game groups, independent of their identity roots.</summary>
public interface IGroupHeaderPreferenceRepository
{
    /// <summary>Newest explicit choice per current root. Null is an explicit Automatic choice.</summary>
    Task<IReadOnlyDictionary<long, string?>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Changes a current group's preference; refuses stale roots and unavailable stores.</summary>
    Task<bool> SetAsync(long rootWorkId, string? store, CancellationToken ct = default);
}
