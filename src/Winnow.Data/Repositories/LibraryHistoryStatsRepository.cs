using Dapper;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// One statement, four figures, computed on read. Nothing is stored, so no
/// threshold retune can rot it, the same rule the derived buckets follow
/// (section 6.1).
///
/// <para>The recommender's maturity tier is a claim about the whole library.
/// The interface's own doc says any sample small enough to be cheap is drawn from
/// rows chosen for some other reason and is biased by that choice. Without this
/// repository the caller falls back to a scaled sample, which is right about the
/// tier and wrong about the count. With it the tier is counted, which is why
/// <see cref="LibraryHistoryStats.IsEstimate"/> is false on every path here.</para>
///
/// <para>A "snapshot rise" is any later observation of one ownership reporting
/// more minutes than an earlier one. Three readings of the same number is one
/// fact observed three times, not a history; only a series that moved is evidence
/// the series is a series. That is what the EXISTS clause tests.</para>
/// </summary>
public sealed class LibraryHistoryStatsRepository : ILibraryHistoryStatsRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public LibraryHistoryStatsRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<LibraryHistoryStats> GetAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        var stats = await lease.Connection.QueryFirstAsync<LibraryHistoryStats>(new CommandDefinition("""
            SELECT (SELECT COUNT(*)        FROM sessions) AS SessionCount,
                   (SELECT MIN(started_at) FROM sessions) AS FirstSessionAt,
                   (SELECT MAX(started_at) FROM sessions) AS LastSessionAt,
                   (SELECT COUNT(*)
                    FROM ownerships o
                    WHERE EXISTS (
                        SELECT 1
                        FROM playtime_snapshots earlier
                        JOIN playtime_snapshots later
                          ON later.ownership_id      = earlier.ownership_id
                         AND later.observed_at       > earlier.observed_at
                         AND later.playtime_minutes  > earlier.playtime_minutes
                        WHERE earlier.ownership_id = o.id)) AS OwnershipsWithSnapshotRises;
            """, transaction: lease.Transaction, cancellationToken: ct));

        // IsEstimate stays false: every figure above is an exact aggregate over
        // the whole table, which is the entire reason this repository exists.
        return stats;
    }
}
