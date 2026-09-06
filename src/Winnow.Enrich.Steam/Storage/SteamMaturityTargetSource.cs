using Dapper;
using Winnow.Core.Domain;
using Winnow.Data;

namespace Winnow.Enrich.Steam.Storage;

/// <summary>One work that needs Steam maturity evidence: the work id to write and the appid to read.</summary>
public sealed record SteamMaturityTarget(long WorkId, string AppId);

/// <summary>Lists the works whose Steam store bodies should be read for content descriptors.</summary>
public interface ISteamMaturityTargetSource
{
    /// <summary>Returns every (work, appid) pair eligible for the Steam maturity pass.</summary>
    Task<IReadOnlyList<SteamMaturityTarget>> GetTargetsAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads maturity targets from <c>external_ids</c> joined to <c>releases</c>.
/// The join reaches the work id through the release, DISTINCT because the same
/// appid owned twice (two accounts, one library) is one work and one answer.
/// </summary>
public sealed class SqliteSteamMaturityTargetSource : ISteamMaturityTargetSource
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteSteamMaturityTargetSource(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<SteamMaturityTarget>> GetTargetsAsync(CancellationToken ct = default)
    {
        // DISTINCT: the same appid owned on two accounts is one work and
        // one answer. work_maturity is keyed at the work grain because the
        // grid draws one tile per resolved work.
        const string sql = """
            SELECT DISTINCT r.work_id AS WorkId,
                            e.provider_id AS AppId
            FROM external_ids e
            JOIN releases r ON r.id = e.release_id
            WHERE e.provider = @provider
              AND e.provider_id IS NOT NULL
              AND e.provider_id <> ''
            ORDER BY r.work_id, e.provider_id;
            """;

        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Row>(new CommandDefinition(
            sql,
            new { provider = ExternalIdProviders.Steam },
            transaction: lease.Transaction,
            cancellationToken: ct));

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.AppId))
            .Select(r => new SteamMaturityTarget(r.WorkId, r.AppId!))
            .ToList();
    }

    private sealed class Row
    {
        public long WorkId { get; init; }

        public string? AppId { get; init; }
    }
}
