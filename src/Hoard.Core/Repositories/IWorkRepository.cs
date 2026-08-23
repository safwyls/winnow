using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IWorkRepository
{
    /// <summary>Inserts a work (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Work work, CancellationToken ct = default);

    /// <summary>
    /// Renames a work and sets <see cref="Work.NameIsProvisional"/>. Used to
    /// promote a placeholder name to the real title once a source supplies one;
    /// never to demote a real title back to a placeholder.
    /// </summary>
    Task UpdateNameAsync(long id, string name, bool nameIsProvisional, CancellationToken ct = default);

    Task<Work?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default);
}
