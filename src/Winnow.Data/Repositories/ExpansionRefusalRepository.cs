using Dapper;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Expansion refusals over migration 0020's one table.
///
/// <para>Nothing here reads <c>works</c> or <c>ownerships</c>, deliberately:
/// a refusal is a fact about two work IDS and nothing else, so this
/// repository has no opinion about identity resolution and never needs
/// one.</para>
/// </summary>
public sealed class ExpansionRefusalRepository : IExpansionRefusalRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly TimeProvider _clock;

    /// <summary>Creates the repository.</summary>
    /// <param name="factory">Connection and transaction source.</param>
    /// <param name="clock">Stamps <c>refused_at</c>. Injected so a test can pin the date.</param>
    public ExpansionRefusalRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    {
        _factory = factory;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpansionRefusal>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var rows = await lease.Connection.QueryAsync<ExpansionRefusal>(new CommandDefinition("""
            SELECT id            AS Id,
                   base_work_id  AS BaseWorkId,
                   child_work_id AS ChildWorkId,
                   refused_at    AS RefusedAt,
                   note          AS Note
            FROM expansion_refusals
            ORDER BY id;
            """, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task RefuseAsync(
        IReadOnlyList<ExpansionRefusalRequest> pairs,
        string? note = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
        {
            return;
        }

        using var scope = _factory.Begin();
        using var lease = _factory.Lease();

        var refusedAt = _clock.GetUtcNow().UtcDateTime;

        foreach (var pair in pairs)
        {
            // OR IGNORE against ux_expansion_refusals_pair: answering the same
            // question twice is a no-op, which is the same idempotence the
            // link path has. The row keeps its FIRST refused_at, because that
            // is when the user actually said no.
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT OR IGNORE INTO expansion_refusals (
                    base_work_id, child_work_id, refused_at, note)
                VALUES (@baseWorkId, @childWorkId, @refusedAt, @note);
                """,
                new
                {
                    baseWorkId = pair.BaseWorkId,
                    childWorkId = pair.ChildWorkId,
                    refusedAt,
                    note,
                },
                lease.Transaction,
                cancellationToken: ct));
        }

        scope.Commit();
    }
}
