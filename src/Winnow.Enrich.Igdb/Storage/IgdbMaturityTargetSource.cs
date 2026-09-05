using Dapper;
using Winnow.Data;

namespace Winnow.Enrich.Igdb.Storage;

/// <summary>One work that needs IGDB maturity evidence: the work id to write and the IGDB id to query.</summary>
public sealed record IgdbMaturityTarget(long WorkId, long IgdbId);

/// <summary>Lists the works whose IGDB age ratings should be fetched.</summary>
public interface IIgdbMaturityTargetSource
{
    /// <summary>Returns every (work, igdb_id) pair eligible for the IGDB maturity pass.</summary>
    Task<IReadOnlyList<IgdbMaturityTarget>> GetTargetsAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads maturity targets from <c>works.igdb_id</c>, which enrichment has
/// already resolved, so this pass adds no identity work of its own.
/// </summary>
public sealed class SqliteIgdbMaturityTargetSource : IIgdbMaturityTargetSource
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteIgdbMaturityTargetSource(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<IgdbMaturityTarget>> GetTargetsAsync(CancellationToken ct = default)
    {
        // works.igdb_id was resolved by the enrichment pass that runs
        // before this one. No identity work here.
        const string sql = """
            SELECT id      AS WorkId,
                   igdb_id AS IgdbId
            FROM works
            WHERE igdb_id IS NOT NULL
              AND igdb_id > 0
            ORDER BY id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<IgdbMaturityTarget>(new CommandDefinition(
            sql,
            transaction: lease.Transaction,
            cancellationToken: ct));

        return rows.ToList();
    }
}
