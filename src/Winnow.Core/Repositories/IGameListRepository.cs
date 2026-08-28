using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Core.Repositories;

/// <summary>
/// The <c>lists</c> and <c>list_items</c> tables (§6).
///
/// <para><b>Two kinds of list, and only one of them has items.</b> A
/// <b>list</b> is a fixed, ordered set the user assembled by hand, stored in
/// <c>list_items</c>. A <b>live list</b> stores a <see cref="LibraryFilter"/>
/// and no items at all; its membership is computed when it is read, by
/// <see cref="LibraryFilter.Apply"/> over the library the caller already has in
/// memory. There is deliberately no method here that returns a live list's
/// members — materialising them would turn it into a manual list, and a method
/// that did it once would eventually be called by something that wrote the result
/// back.</para>
///
/// <para><b>Deleting a list never deletes a game.</b> <c>list_items</c> cascades
/// from <c>lists</c> and from <c>releases</c>, both inbound; nothing cascades
/// outward from <c>list_items</c> to either. Removing a list removes its
/// membership rows and stops there.</para>
/// </summary>
public interface IGameListRepository
{
    /// <summary>Inserts a list (Id ignored) and returns the assigned id.</summary>
    Task<long> InsertAsync(GameList list, CancellationToken ct = default);

    Task<GameList?> GetAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<GameList>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Renames a list and replaces its description.
    ///
    /// <para>The description is set to exactly what is passed, null included —
    /// this is an edit of a form the user was looking at, not a patch, so
    /// clearing the field has to be expressible.</para>
    /// </summary>
    /// <returns>False when no such list exists.</returns>
    Task<bool> RenameAsync(long id, string name, string? description, CancellationToken ct = default);

    /// <summary>
    /// Replaces a live list's rule.
    ///
    /// <para>Also flips <c>is_smart</c> to 1: a list given a rule is a live list
    /// from that moment, and leaving the flag behind would produce a row with a
    /// filter nobody reads. Converting a manual list this way abandons its
    /// <c>list_items</c> rather than deleting them, so converting back restores
    /// the hand-made ordering — the reversible choice, and the cost is a few rows
    /// nothing reads meanwhile.</para>
    /// </summary>
    /// <returns>False when no such list exists.</returns>
    Task<bool> SetFilterAsync(long id, LibraryFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Deletes a list and its membership rows. <b>Never touches a release, an
    /// ownership or a play record</b> — see the interface remarks.
    /// </summary>
    /// <returns>False when no such list exists.</returns>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>Adds a release to a list, or moves it if already present.</summary>
    Task AddItemAsync(ListItem item, CancellationToken ct = default);

    /// <summary>
    /// Adds a release at the end of a list, or leaves it where it is if it is
    /// already a member.
    ///
    /// <para>The position-free form, which is what "add to list" from a game's
    /// context menu actually means. Re-adding is a no-op rather than a move: a
    /// user who adds a game twice did not mean to send it to the bottom.</para>
    /// </summary>
    /// <returns>The item's position in the list.</returns>
    Task<int> AppendItemAsync(long listId, long releaseId, CancellationToken ct = default);

    /// <summary>Items in a list, ordered by position.</summary>
    Task<IReadOnlyList<ListItem>> GetItemsAsync(long listId, CancellationToken ct = default);

    Task RemoveItemAsync(long listId, long releaseId, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the whole order of a manual list from a sequence of release ids.
    ///
    /// <para>Whole-list rather than move-one-item: after a drag the caller knows
    /// the final order, and an incremental "insert at index 4" API would have to
    /// renumber everything after it anyway — with a window in which two items
    /// share a position. Positions are re-dealt 0..n-1, so they stay dense
    /// however many drags have happened.</para>
    ///
    /// <para>Ids not currently in the list are ignored; members the caller left
    /// out keep their membership and are appended after the given order, in their
    /// previous relative order. A reorder is not a way to remove things.</para>
    /// </summary>
    Task ReorderAsync(long listId, IReadOnlyList<long> releaseIdsInOrder, CancellationToken ct = default);
}
