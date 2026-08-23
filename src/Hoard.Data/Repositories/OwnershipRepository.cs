using Dapper;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

public sealed class OwnershipRepository : IOwnershipRepository
{
    private const string Columns = """
        id               AS Id,
        release_id       AS ReleaseId,
        store            AS Store,
        account_ref      AS AccountRef,
        acquired_at      AS AcquiredAt,
        license_type     AS LicenseType,
        price_paid_cents AS PricePaidCents,
        price_source     AS PriceSource,
        install_path     AS InstallPath,
        installed        AS Installed
        """;

    private readonly ISqliteConnectionFactory _factory;

    public OwnershipRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(Ownership ownership, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO ownerships (release_id, store, account_ref, acquired_at, license_type,
                                    price_paid_cents, price_source, install_path, installed)
            VALUES (@ReleaseId, @Store, @AccountRef, @AcquiredAt, @LicenseType,
                    @PricePaidCents, @PriceSource, @InstallPath, @Installed)
            RETURNING id;
            """, ownership, cancellationToken: ct));
    }

    public async Task<Ownership?> GetAsync(long id, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        return await conn.QuerySingleOrDefaultAsync<Ownership>(new CommandDefinition(
            $"SELECT {Columns} FROM ownerships WHERE id = @id;",
            new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long releaseId, CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Ownership>(new CommandDefinition(
            $"SELECT {Columns} FROM ownerships WHERE release_id = @releaseId ORDER BY id;",
            new { releaseId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default)
    {
        using var conn = _factory.Open();
        var rows = await conn.QueryAsync<Ownership>(new CommandDefinition(
            $"SELECT {Columns} FROM ownerships ORDER BY id;",
            cancellationToken: ct));
        return rows.AsList();
    }
}
