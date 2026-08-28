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
        detection_method AS DetectionMethod,
        attributed_by    AS AttributedBy
        """;

    private readonly ISqliteConnectionFactory _factory;

    public SessionRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Session session, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO sessions (
                ownership_id, started_at, ended_at, duration_s, detection_method, attributed_by)
            VALUES (
                @OwnershipId, @StartedAt, @EndedAt, @DurationSeconds, @DetectionMethod, @AttributedBy)
            RETURNING id;
            """, session, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<Session?> GetAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<Session>(new CommandDefinition(
            $"SELECT {Columns} FROM sessions WHERE id = @id;",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Session>(new CommandDefinition(
            $"SELECT {Columns} FROM sessions WHERE ownership_id = @ownershipId ORDER BY started_at, id;",
            new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO session_notes (session_id, note, rating)
            VALUES (@SessionId, @Note, @Rating)
            ON CONFLICT (session_id) DO UPDATE SET note = excluded.note, rating = excluded.rating;
            """, note, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<SessionNote>(new CommandDefinition("""
            SELECT session_id AS SessionId, note AS Note, rating AS Rating
            FROM session_notes
            WHERE session_id = @sessionId;
            """, new { sessionId }, transaction: lease.Transaction, cancellationToken: ct));
    }
}
