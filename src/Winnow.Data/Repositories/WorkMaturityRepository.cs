using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// Dapper over <c>work_maturity</c> (migration 0024). One row per (work,
/// source); a re-read from one source replaces its own row and never
/// clobbers the other's.
/// </summary>
public sealed class WorkMaturityRepository : IWorkMaturityRepository
{
    private const string Columns = """
        work_id     AS WorkId,
        source      AS Source,
        ratings     AS Ratings,
        descriptors AS Descriptors,
        observed_at AS ObservedAt
        """;

    private readonly ISqliteConnectionFactory _factory;

    public WorkMaturityRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task UpsertAsync(WorkMaturity maturity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(maturity);

        // One row per (work, source), so a later reading from the same source
        // replaces its own row and never the other source's. Both columns are
        // written together: they are one source's coherent answer, and a stale
        // descriptor beside a fresh rating is a reading no source ever reported.
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO work_maturity (work_id, source, ratings, descriptors, observed_at)
            VALUES (@WorkId, @Source, @Ratings, @Descriptors, @ObservedAt)
            ON CONFLICT (work_id, source) DO UPDATE SET
                ratings     = excluded.ratings,
                descriptors = excluded.descriptors,
                observed_at = excluded.observed_at;
            """, maturity, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<WorkMaturity>> GetForWorkAsync(
        long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<WorkMaturity>(new CommandDefinition(
            $"SELECT {Columns} FROM work_maturity WHERE work_id = @workId ORDER BY source;",
            new { workId }, transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<bool> DeleteAsync(long workId, string source, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM work_maturity WHERE work_id = @workId AND source = @source;",
            new { workId, source }, lease.Transaction, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyList<WorkMaturity>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<WorkMaturity>(new CommandDefinition(
            $"SELECT {Columns} FROM work_maturity ORDER BY work_id, source;",
            transaction: lease.Transaction, cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<bool> IsExplicitAsync(long workId, CancellationToken ct = default)
    {
        // The verdict is never read from SQL. The rows carry the evidence and
        // MaturityRules decides — adults-only ratings and the
        // adult_only_sexual_content descriptor, not the broad 18+ tier.
        // The same arrangement NonGameEntries has with steam_app_type:
        // retuning the rule is a code change, not a migration, and no
        // stored answer can rot.
        var rows = await GetForWorkAsync(workId, ct);
        foreach (var row in rows)
        {
            if (MaturityRules.IsExplicit(row.Ratings, row.Descriptors))
            {
                return true;
            }
        }

        return false;
    }
}
