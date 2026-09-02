using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>lists</c> and <c>list_items</c>.
///
/// <para>A live list's membership is not here, on purpose — see
/// <see cref="IGameListRepository"/>. This type stores a live list's rule and
/// hands it back; who it currently contains is a question about the library, and
/// <see cref="LibraryFilter.Apply"/> answers it over rows the caller already
/// holds.</para>
///
/// <para>Identity links are deliberately NOT resolved here. Adding a game to a
/// list is an explicit user act on one store entry, and the user picked that
/// entry. De-duplicating a list by resolved work would remove a row the user
/// put there by hand. If the grid ever becomes work-grained (TASK-70.6),
/// display may de-duplicate what it draws; the stored membership still stays
/// exactly what was added.</para>
/// </summary>
public sealed class GameListRepository : IGameListRepository
{
    private const string Columns = """
        id          AS Id,
        name        AS Name,
        description AS Description,
        is_smart    AS IsSmart,
        filter_json AS FilterJson
        """;

    private readonly ISqliteConnectionFactory _factory;

    public GameListRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(GameList list, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lists (name, description, is_smart, filter_json)
            VALUES (@Name, @Description, @IsSmart, @FilterJson)
            RETURNING id;
            """, list, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<GameList?> GetAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<GameList>(new CommandDefinition(
            $"SELECT {Columns} FROM lists WHERE id = @id;",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<GameList>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<GameList>(new CommandDefinition(
            $"SELECT {Columns} FROM lists ORDER BY name;",
            transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> RenameAsync(
        long id, string name, string? description, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE lists
            SET name = @name, description = @description
            WHERE id = @id;
            """, new { id, name, description }, transaction: lease.Transaction, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> SetFilterAsync(
        long id, LibraryFilter filter, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE lists
            SET filter_json = @filterJson, is_smart = 1
            WHERE id = @id;
            """,
            new { id, filterJson = filter.ToJson() },
            transaction: lease.Transaction, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // One statement, and the cascade does the rest. list_items has an inbound
        // FK from lists ON DELETE CASCADE, so its rows go; releases has no FK
        // pointing INTO list_items, so no game, ownership, play record or
        // achievement is reachable from here. Verified by a test rather than by
        // reading the schema, because "which way does this cascade run" is
        // exactly the question people get wrong from memory.
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lists WHERE id = @id;",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));
        return rows > 0;
    }

    public async Task AddItemAsync(ListItem item, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO list_items (list_id, release_id, position)
            VALUES (@ListId, @ReleaseId, @Position)
            ON CONFLICT (list_id, release_id) DO UPDATE SET position = excluded.position;
            """, item, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<int> AppendItemAsync(long listId, long releaseId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // DO NOTHING, not DO UPDATE: re-adding a game the list already holds
        // leaves it where the user put it. The RETURNING clause fires only on a
        // real insert, hence the follow-up read for the already-a-member case.
        //
        // COALESCE(MAX(position) + 1, 0) makes the first item position 0 and
        // every later one one past the end, which is what ReorderAsync also
        // produces — so the two cannot leave a list numbered two different ways.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO list_items (list_id, release_id, position)
            SELECT @listId, @releaseId, COALESCE(MAX(position) + 1, 0)
            FROM list_items
            WHERE list_id = @listId
            ON CONFLICT (list_id, release_id) DO NOTHING;
            """, new { listId, releaseId }, transaction: lease.Transaction, cancellationToken: ct));

        return await lease.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT position FROM list_items WHERE list_id = @listId AND release_id = @releaseId;",
            new { listId, releaseId }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ListItem>> GetItemsAsync(long listId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<ListItem>(new CommandDefinition("""
            SELECT list_id AS ListId, release_id AS ReleaseId, position AS Position
            FROM list_items
            WHERE list_id = @listId
            ORDER BY position;
            """, new { listId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task RemoveItemAsync(long listId, long releaseId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM list_items WHERE list_id = @listId AND release_id = @releaseId;",
            new { listId, releaseId }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task ReorderAsync(
        long listId, IReadOnlyList<long> releaseIdsInOrder, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var current = (await lease.Connection.QueryAsync<long>(new CommandDefinition(
            "SELECT release_id FROM list_items WHERE list_id = @listId ORDER BY position;",
            new { listId }, transaction: lease.Transaction, cancellationToken: ct))).AsList();

        if (current.Count == 0)
        {
            return;
        }

        var member = current.ToHashSet();

        // The caller's order first, filtered to actual members and de-duplicated,
        // then everything it did not mention in its existing relative order. A
        // reorder must not be able to drop a game (see IGameListRepository).
        var ordered = new List<long>(current.Count);
        var placed = new HashSet<long>();
        foreach (var releaseId in releaseIdsInOrder)
        {
            if (member.Contains(releaseId) && placed.Add(releaseId))
            {
                ordered.Add(releaseId);
            }
        }

        foreach (var releaseId in current)
        {
            if (placed.Add(releaseId))
            {
                ordered.Add(releaseId);
            }
        }

        // Positions are re-dealt 0..n-1 rather than nudged, so they stay dense
        // however many drags a list has survived. UPDATE per row: the PK is
        // (list_id, release_id), so position is free to move without ever
        // colliding — no shuffle through a temporary range is needed.
        for (var position = 0; position < ordered.Count; position++)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                UPDATE list_items
                SET position = @position
                WHERE list_id = @listId AND release_id = @releaseId;
                """,
                new { listId, releaseId = ordered[position], position },
                transaction: lease.Transaction, cancellationToken: ct));
        }
    }
}
