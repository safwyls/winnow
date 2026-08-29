using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// The <c>lists</c> and <c>list_items</c> tables. Manual lists store items with
/// explicit ordering; live lists store a <see cref="LibraryFilter"/> and compute
/// membership at read time. Deleting a list never deletes a game.
/// </summary>
public interface IGameListRepository
{
    /// <summary>Inserts a list (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(GameList list, CancellationToken ct = default);

    Task<GameList?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<GameList>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Renames a list and replaces its description (null clears it).</summary>
    /// <returns>False when no such list exists.</returns>
    Task<bool> RenameAsync(long id, string name, string? description, CancellationToken ct = default);

    /// <summary>Replaces a live list's filter rule. Also sets <c>is_smart = 1</c>.</summary>
    /// <returns>False when no such list exists.</returns>
    Task<bool> SetFilterAsync(long id, LibraryFilter filter, CancellationToken ct = default);

    /// <summary>Deletes a list and its membership rows. Never touches releases or ownerships.</summary>
    /// <returns>False when no such list exists.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>Adds a release to a list, or moves it if already present.</summary>
    Task AddItemAsync(ListItem item, CancellationToken ct = default);

    /// <summary>Adds a release at the end of a list, or no-ops if already present.</summary>
    /// <returns>The item's position in the list.</returns>
    Task<int> AppendItemAsync(long listId, long releaseId, CancellationToken ct = default);

    /// <summary>Items in a list, ordered by position.</summary>
    Task<IReadOnlyList<ListItem>> GetItemsAsync(long listId, CancellationToken ct = default);

    Task RemoveItemAsync(long listId, long releaseId, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the whole order of a manual list. Unknown ids are ignored;
    /// omitted members are appended in their previous relative order.
    /// </summary>
    Task ReorderAsync(long listId, IReadOnlyList<long> releaseIdsInOrder, CancellationToken ct = default);
}
