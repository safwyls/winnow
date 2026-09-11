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
        attributed_by    AS AttributedBy,
        monitor_key      AS MonitorKey
        """;

    private readonly ISqliteConnectionFactory _factory;

    public SessionRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Session session, CancellationToken ct = default)
    {
        if (session.MonitorKey is not null)
        {
            throw new ArgumentException("Monitored sessions require SaveMonitoredAsync.", nameof(session));
        }

        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO sessions (
                ownership_id, started_at, ended_at, duration_s, detection_method, attributed_by)
            VALUES (
                @OwnershipId, @StartedAt, @EndedAt, @DurationSeconds, @DetectionMethod, @AttributedBy)
            RETURNING id;
            """, session, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<Session?> FindOpenMonitoredAsync(long ownershipId,
        IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await FindOpenMonitoredAsync(lease, ownershipId, processes, ct);
    }

    private static async Task<Session?> FindOpenMonitoredAsync(DbLease lease, long ownershipId,
        IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct)
    {
        var matches = new HashSet<long>();
        foreach (var process in processes.Distinct())
        {
            var ids = await lease.Connection.QueryAsync<long>(new CommandDefinition("""
                SELECT s.id
                FROM sessions s
                INNER JOIN monitored_session_processes p ON p.session_id = s.id
                WHERE s.ownership_id = @ownershipId
                  AND s.monitor_key IS NOT NULL AND s.ended_at IS NULL
                  AND p.process_id = @ProcessId AND p.started_at = @StartedAt
                  AND p.process_name = @ProcessName;
                """, new { ownershipId, process.ProcessId, process.StartedAt, process.ProcessName },
                transaction: lease.Transaction, cancellationToken: ct));
            matches.UnionWith(ids);
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException("Process identities match multiple open sittings; automatic recovery is unsafe.");
        }

        return matches.Count == 0 ? null
            : await lease.Connection.QuerySingleAsync<Session>(new CommandDefinition(
                $"SELECT {Columns} FROM sessions WHERE id = @id;", new { id = matches.Single() },
                transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<Session> SaveMonitoredAsync(Session session,
        IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(session.MonitorKey);
        if (processes.Count == 0 || session.DetectionMethod != DetectionMethods.ProcessWatch)
        {
            throw new ArgumentException("A monitored sitting requires process observations and process-watch detection.", nameof(session));
        }

        using var batch = new RepositoryWriteBatch(_factory);
        var lease = batch.Lease;
        var existing = await lease.Connection.QuerySingleOrDefaultAsync<Session>(new CommandDefinition(
            $"""
            SELECT {Columns} FROM sessions
            WHERE monitor_key = @MonitorKey
               OR id = (SELECT session_id FROM monitored_session_keys WHERE monitor_key = @MonitorKey);
            """, session,
            transaction: lease.Transaction, cancellationToken: ct));
        existing ??= await FindOpenMonitoredAsync(lease, session.OwnershipId, processes, ct);
        if (existing is not null && existing.OwnershipId != session.OwnershipId)
        {
            throw new InvalidOperationException("The monitored sitting belongs to another ownership.");
        }

        var saved = existing is null ? session : existing with
        {
            EndedAt = existing.EndedAt ?? session.EndedAt,
        };
        if (existing?.EndedAt is null)
        {
            if (saved.EndedAt < saved.StartedAt)
            {
                throw new ArgumentException("A session cannot end before it starts.", nameof(session));
            }

            saved = saved with
            {
                DurationSeconds = saved.EndedAt is { } end ? (long)(end - saved.StartedAt).TotalSeconds : null,
            };
            if (existing is null)
            {
                var id = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
                    INSERT INTO sessions (ownership_id, started_at, ended_at, duration_s,
                        detection_method, attributed_by, monitor_key)
                    VALUES (@OwnershipId, @StartedAt, @EndedAt, @DurationSeconds,
                        @DetectionMethod, @AttributedBy, @MonitorKey)
                    RETURNING id;
                    """, saved, transaction: lease.Transaction, cancellationToken: ct));
                saved = saved with { Id = id };
            }
            else
            {
                await lease.Connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE sessions SET started_at = @StartedAt, ended_at = @EndedAt, duration_s = @DurationSeconds
                    WHERE id = @Id;
                    """, saved, transaction: lease.Transaction, cancellationToken: ct));
            }
        }
        else
        {
            saved = existing;
        }

        // A caller can discover an existing sitting during this save. Remember
        // its submitted key too: a lost completion response must still retry
        // the same row after the open-process ledger has been retired.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT OR IGNORE INTO monitored_session_keys (monitor_key, session_id)
            VALUES (@MonitorKey, @sessionId);
            """, new { session.MonitorKey, sessionId = saved.Id },
            transaction: lease.Transaction, cancellationToken: ct));

        if (saved.EndedAt is null)
        {
            foreach (var process in processes.Distinct())
            {
                await lease.Connection.ExecuteAsync(new CommandDefinition("""
                    INSERT OR IGNORE INTO monitored_session_processes (session_id, process_id, started_at, process_name)
                    VALUES (@sessionId, @ProcessId, @StartedAt, @ProcessName);
                    """, new { sessionId = saved.Id, process.ProcessId, process.StartedAt, process.ProcessName },
                    transaction: lease.Transaction, cancellationToken: ct));
            }
        }
        else
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM monitored_session_processes WHERE session_id = @Id;", saved,
                transaction: lease.Transaction, cancellationToken: ct));
        }

        ct.ThrowIfCancellationRequested();
        batch.Commit();
        return saved;
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
