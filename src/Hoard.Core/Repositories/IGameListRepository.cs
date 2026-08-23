using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

public interface IGameListRepository
{
    /// <summary>Inserts a list (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(GameList list, CancellationToken ct = default);

    Task<GameList?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<GameList>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Adds a release to a list, or moves it if already present.</summary>
    Task AddItemAsync(ListItem item, CancellationToken ct = default);

    /// <summary>Items in a list, ordered by position.</summary>
    Task<IReadOnlyList<ListItem>> GetItemsAsync(long listId, CancellationToken ct = default);

    Task RemoveItemAsync(long listId, long releaseId, CancellationToken ct = default);
}
