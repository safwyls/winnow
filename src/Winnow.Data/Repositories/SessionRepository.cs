using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

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

    public async Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(
        long ownershipId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<SessionJournalEntry>(new CommandDefinition("""
            SELECT s.id AS SessionId,
                   s.ownership_id AS OwnershipId,
                   COALESCE(s.ended_at, s.started_at) AS SessionAt,
                   n.note AS Note,
                   n.rating AS Rating
            FROM sessions AS s
            INNER JOIN session_notes AS n ON n.session_id = s.id
            WHERE s.ownership_id = @ownershipId
              AND (NULLIF(TRIM(n.note), '') IS NOT NULL OR n.rating IS NOT NULL)
            ORDER BY COALESCE(s.ended_at, s.started_at) DESC, s.id DESC;
            """, new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task DeleteNoteAsync(long sessionId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM session_notes WHERE session_id = @sessionId;
            """, new { sessionId }, transaction: lease.Transaction, cancellationToken: ct));
    }
}
