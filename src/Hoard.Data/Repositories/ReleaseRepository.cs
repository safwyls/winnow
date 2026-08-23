using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class ReleaseRepository : IReleaseRepository
{
    private const string Columns = """
        id              AS Id,
        work_id         AS WorkId,
        igdb_version_id AS IgdbVersionId,
        name            AS Name,
        platform        AS Platform,
        edition_note    AS EditionNote
        """;

    private readonly ISqliteConnectionFactory _factory;

    public ReleaseRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Release release, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO releases (work_id, igdb_version_id, name, platform, edition_note)
            VALUES (@WorkId, @IgdbVersionId, @Name, @Platform, @EditionNote)
            RETURNING id;
            """, release, cancellationToken: ct));
    }

    public async Task<Release?> GetAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<Release>(new CommandDefinition(
            $"SELECT {Columns} FROM releases WHERE id = @id;",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Release>(new CommandDefinition(
            $"SELECT {Columns} FROM releases WHERE work_id = @workId ORDER BY id;",
            new { workId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO external_ids (release_id, provider, provider_id)
            VALUES (@ReleaseId, @Provider, @ProviderId);
            """, externalId, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<ExternalId>(new CommandDefinition("""
            SELECT release_id AS ReleaseId, provider AS Provider, provider_id AS ProviderId
            FROM external_ids
            WHERE release_id = @releaseId
            ORDER BY provider;
            """, new { releaseId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Release?> FindByExternalIdAsync(string provider, string providerId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<Release>(new CommandDefinition($"""
            SELECT {Columns}
            FROM releases
            WHERE id = (SELECT release_id
                        FROM external_ids
                        WHERE provider = @provider AND provider_id = @providerId);
            """, new { provider, providerId }, cancellationToken: ct));
    }
}
