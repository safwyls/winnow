using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>hidden_games</c> (migration 0023). Append-and-stamp: hiding
/// inserts, unhiding stamps <c>unhidden_at</c>, and the history is the table.
/// </summary>
public sealed class HiddenGameRepository : IHiddenGameRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    public HiddenGameRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> HideAsync(long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // The SELECT ... WHERE NOT EXISTS is what makes hiding idempotent
        // against ux_hidden_games_live: a second hide of a game already hidden
        // inserts nothing rather than violating the partial unique index.
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO hidden_games (work_id, hidden_at, unhidden_at)
            SELECT @workId, @now, NULL
            WHERE EXISTS (SELECT 1 FROM works WHERE id = @workId)
              AND NOT EXISTS (
                  SELECT 1 FROM hidden_games
                  WHERE work_id = @workId AND unhidden_at IS NULL);
            """,
            new { workId, now = _clock.GetUtcNow().UtcDateTime },
            transaction: lease.Transaction, cancellationToken: ct));

        return rows > 0;
    }

    public async Task<bool> UnhideAsync(long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Stamped, never deleted: the row stays the history of what was hidden
        // and when, and re-hiding after an unhide is a fresh row.
        var rows = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE hidden_games
            SET unhidden_at = @now
            WHERE work_id = @workId AND unhidden_at IS NULL;
            """,
            new { workId, now = _clock.GetUtcNow().UtcDateTime },
            transaction: lease.Transaction, cancellationToken: ct));

        return rows > 0;
    }

    public async Task<bool> IsHiddenAsync(long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var count = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT COUNT(*)
            FROM hidden_games
            WHERE work_id = @workId AND unhidden_at IS NULL;
            """, new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        return count > 0;
    }

    public async Task<IReadOnlyList<long>> GetHiddenWorkIdsAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<long>(new CommandDefinition("""
            SELECT work_id
            FROM hidden_games
            WHERE unhidden_at IS NULL
            ORDER BY work_id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<IReadOnlyList<HiddenGame>> GetHiddenGamesAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // The unhide screen needs a title and a sense of what it is offering
        // back, so the count of store entries behind the game rides along.
        var rows = await lease.Connection.QueryAsync<HiddenGame>(new CommandDefinition("""
            SELECT h.work_id  AS WorkId,
                   w.name     AS Title,
                   h.hidden_at AS HiddenAt,
                   (SELECT COUNT(*)
                    FROM ownerships o
                    JOIN releases r ON r.id = o.release_id
                    WHERE r.work_id = w.id) AS StoreEntryCount
            FROM hidden_games h
            JOIN works w ON w.id = h.work_id
            WHERE h.unhidden_at IS NULL
            ORDER BY w.name, h.work_id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }
}
