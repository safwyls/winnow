using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IWorkRepository
{
    /// <summary>Inserts a work (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(Work work, CancellationToken ct = default);

    Task<Work?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<Work>> GetAllAsync(CancellationToken ct = default);
}
