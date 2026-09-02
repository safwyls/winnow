using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

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
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO releases (work_id, igdb_version_id, name, platform, edition_note)
            VALUES (@WorkId, @IgdbVersionId, @Name, @Platform, @EditionNote)
            RETURNING id;
            """, release, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task UpdateNameAsync(long id, string name, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "UPDATE releases SET name = @name WHERE id = @id;",
            new { id, name }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<Release?> GetAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<Release>(new CommandDefinition(
            $"SELECT {Columns} FROM releases WHERE id = @id;",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Release>> GetByWorkAsync(long workId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Release>(new CommandDefinition(
            $"SELECT {Columns} FROM releases WHERE work_id = @workId ORDER BY id;",
            new { workId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task AddExternalIdAsync(ExternalId externalId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO external_ids (release_id, provider, provider_id)
            VALUES (@ReleaseId, @Provider, @ProviderId);
            """, externalId, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ExternalId>> GetExternalIdsAsync(long releaseId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<ExternalId>(new CommandDefinition("""
            SELECT release_id AS ReleaseId, provider AS Provider, provider_id AS ProviderId
            FROM external_ids
            WHERE release_id = @releaseId
            ORDER BY provider;
            """, new { releaseId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<Release?> FindByExternalIdAsync(string provider, string providerId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<Release>(new CommandDefinition($"""
            SELECT {Columns}
            FROM releases
            WHERE id = (SELECT release_id
                        FROM external_ids
                        WHERE provider = @provider AND provider_id = @providerId);
            """, new { provider, providerId }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ReleaseIdentity>> GetIdentitiesAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // One join, one pass, ix_releases_work_id on the inner side. The
        // alternative — read releases, then a work per release — is the N+1
        // that would make the soft-match sweep cost 1,200 round trips on a
        // 600-game library instead of one.
        var rows = await lease.Connection.QueryAsync<ReleaseIdentity>(new CommandDefinition("""
            SELECT r.id                  AS ReleaseId,
                   r.work_id             AS WorkId,
                   r.name                AS ReleaseName,
                   w.name                AS WorkName,
                   w.first_release_year  AS FirstReleaseYear,
                   w.publisher           AS Publisher,
                   w.name_is_provisional AS NameIsProvisional,
                   w.steam_app_type      AS SteamAppType,
                   w.epic_categories     AS EpicCategories,
                   w.igdb_id             AS IgdbId,

                   -- Migration 0022. The storefront's own answer to what this
                   -- app is and what it is part of, carried on the same row the
                   -- title arrives on so the relation scan needs no second read.
                   w.steam_store_type       AS SteamStoreType,
                   w.steam_parent_app_id    AS SteamParentAppId,
                   w.igdb_game_type         AS IgdbGameType,
                   w.igdb_parent_id         AS IgdbParentId,
                   w.igdb_version_parent_id AS IgdbVersionParentId,

                   -- The Steam appid this release is known by, so a parent
                   -- appid can be turned into a work without a second query.
                   (SELECT e.provider_id FROM external_ids e
                     WHERE e.release_id = r.id AND e.provider = 'steam'
                     LIMIT 1)                AS SteamAppId,

                   -- EXISTS, not a join: a release owned on two stores must
                   -- still be one row here, and a join would double it.
                   EXISTS (SELECT 1 FROM ownerships o WHERE o.release_id = r.id) AS IsOwned
            FROM releases r
            JOIN works w ON w.id = r.work_id
            ORDER BY r.id;
            """, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
