using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

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
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lists (name, description, is_smart, filter_json)
            VALUES (@Name, @Description, @IsSmart, @FilterJson)
            RETURNING id;
            """, list, cancellationToken: ct));
    }

    public async Task<GameList?> GetAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<GameList>(new CommandDefinition(
            $"SELECT {Columns} FROM lists WHERE id = @id;",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<GameList>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<GameList>(new CommandDefinition(
            $"SELECT {Columns} FROM lists ORDER BY name;",
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task AddItemAsync(ListItem item, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO list_items (list_id, release_id, position)
            VALUES (@ListId, @ReleaseId, @Position)
            ON CONFLICT (list_id, release_id) DO UPDATE SET position = excluded.position;
            """, item, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ListItem>> GetItemsAsync(long listId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<ListItem>(new CommandDefinition("""
            SELECT list_id AS ListId, release_id AS ReleaseId, position AS Position
            FROM list_items
            WHERE list_id = @listId
            ORDER BY position;
            """, new { listId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task RemoveItemAsync(long listId, long releaseId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM list_items WHERE list_id = @listId AND release_id = @releaseId;",
            new { listId, releaseId }, cancellationToken: ct));
    }
}
