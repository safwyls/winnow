using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class SessionRepository : ISessionRepository
{
    private const string Columns = """
        id               AS Id,
        ownership_id     AS OwnershipId,
        started_at       AS StartedAt,
        ended_at         AS EndedAt,
        duration_s       AS DurationSeconds,
        detection_method AS DetectionMethod
        """;

    private readonly ISqliteConnectionFactory _factory;

    public SessionRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Session session, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO sessions (ownership_id, started_at, ended_at, duration_s, detection_method)
            VALUES (@OwnershipId, @StartedAt, @EndedAt, @DurationSeconds, @DetectionMethod)
            RETURNING id;
            """, session, cancellationToken: ct));
    }

    public async Task<Session?> GetAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<Session>(new CommandDefinition(
            $"SELECT {Columns} FROM sessions WHERE id = @id;",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Session>(new CommandDefinition(
            $"SELECT {Columns} FROM sessions WHERE ownership_id = @ownershipId ORDER BY started_at, id;",
            new { ownershipId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO session_notes (session_id, note, rating)
            VALUES (@SessionId, @Note, @Rating)
            ON CONFLICT (session_id) DO UPDATE SET note = excluded.note, rating = excluded.rating;
            """, note, cancellationToken: ct));
    }

    public async Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<SessionNote>(new CommandDefinition("""
            SELECT session_id AS SessionId, note AS Note, rating AS Rating
            FROM session_notes
            WHERE session_id = @sessionId;
            """, new { sessionId }, cancellationToken: ct));
    }
}
