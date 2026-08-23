using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class UpdateEventRepository : IUpdateEventRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public UpdateEventRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(UpdateEvent updateEvent, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO update_events (release_id, kind, build_id, occurred_at, title, raw_json)
            VALUES (@ReleaseId, @Kind, @BuildId, @OccurredAt, @Title, @RawJson)
            RETURNING id;
            """, updateEvent, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<UpdateEvent>> GetByReleaseAsync(long releaseId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<UpdateEvent>(new CommandDefinition("""
            SELECT id          AS Id,
                   release_id  AS ReleaseId,
                   kind        AS Kind,
                   build_id    AS BuildId,
                   occurred_at AS OccurredAt,
                   title       AS Title,
                   raw_json    AS RawJson
            FROM update_events
            WHERE release_id = @releaseId
            ORDER BY occurred_at, id;
            """, new { releaseId }, cancellationToken: ct));
        return rows.AsList();
    }
}
