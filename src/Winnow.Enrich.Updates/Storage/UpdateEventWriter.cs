using Dapper;
using Winnow.Core.Domain;
using Winnow.Data;

namespace Winnow.Enrich.Updates.Storage;

/// <summary>
/// Idempotent writes to <c>update_events</c>.
///
/// <para>Separate from <c>IUpdateEventRepository</c> on purpose. That repository
/// offers a plain INSERT, which is the right contract for a caller asserting
/// "this is a new event" — <c>--seed-sample</c>, say — and would now throw on a
/// constraint violation, correctly. A poller has the opposite contract: it
/// re-reads the same feeds every sweep and sees the same newest item until the
/// next patch lands, so writing the same event twice is its <i>normal</i>
/// behaviour, not an error. The two need different SQL, so they get different
/// types rather than an <c>ignoreConflicts</c> flag on a shared one.</para>
/// </summary>
public interface IUpdateEventWriter
{
    /// <summary>
    /// Writes an event unless one with the same identity already exists.
    /// Returns true when a row was created.
    /// </summary>
    Task<bool> UpsertAsync(UpdateEvent updateEvent, CancellationToken ct = default);
}

/// <summary><see cref="IUpdateEventWriter"/> over the <c>update_events</c> table.</summary>
public sealed class SqliteUpdateEventWriter : IUpdateEventWriter
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteUpdateEventWriter(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<bool> UpsertAsync(UpdateEvent updateEvent, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Asked before the write rather than inferred from it. SQLite's upsert
        // reports the same `changes()` for an insert and an update, and
        // `last_insert_rowid()` is a connection-wide value that a write to any
        // other table can make coincide with this row's — so the only honest way
        // to distinguish "created" from "already knew" is to look first. Both
        // statements share this lease, and therefore the ambient transaction
        // when one is open.
        var existed = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            SELECT EXISTS(
                SELECT 1 FROM update_events
                WHERE release_id  = @ReleaseId
                  AND kind        = @Kind
                  AND occurred_at = @OccurredAt);
            """, updateEvent, transaction: lease.Transaction, cancellationToken: ct));

        // The conflict target is migration 0004's ux_update_events_identity:
        // (release_id, kind, occurred_at) — "this release changed in this way at
        // this instant". Re-polling therefore writes no duplicate rows, which is
        // what keeps the table bounded while §6.1's EXISTS-based correlation goes
        // on answering correctly either way.
        //
        // DO UPDATE with COALESCE rather than DO NOTHING: still exactly one row
        // per event, but a field that was null when the event was first seen —
        // a url a build push later gains a title for, a raw body a shape change
        // once failed to project — gets filled in on a later pass instead of
        // being lost forever. Non-null stored values are never overwritten, so
        // the operation stays convergent no matter how often it runs.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO update_events (release_id, kind, build_id, occurred_at, title, url, raw_json)
            VALUES (@ReleaseId, @Kind, @BuildId, @OccurredAt, @Title, @Url, @RawJson)
            ON CONFLICT(release_id, kind, occurred_at) DO UPDATE SET
                build_id = COALESCE(update_events.build_id, excluded.build_id),
                title    = COALESCE(update_events.title,    excluded.title),
                url      = COALESCE(update_events.url,      excluded.url),
                raw_json = COALESCE(update_events.raw_json, excluded.raw_json);
            """, updateEvent, transaction: lease.Transaction, cancellationToken: ct));

        return existed == 0;
    }
}
