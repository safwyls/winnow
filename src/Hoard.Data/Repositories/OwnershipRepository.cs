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
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO ownerships (release_id, store, account_ref, acquired_at, license_type,
                                    price_paid_cents, price_source, install_path, installed)
            VALUES (@ReleaseId, @Store, @AccountRef, @AcquiredAt, @LicenseType,
                    @PricePaidCents, @PriceSource, @InstallPath, @Installed)
            RETURNING id;
            """, ownership, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<long> UpsertAsync(Ownership ownership, CancellationToken ct = default)
    {
        // A real upsert against the UNIQUE (release_id, store) index added in
        // 0003, matching how session_notes and list_items already work. The
        // read-then-insert it replaces was two round trips and a race.
        //
        // account_ref is REFRESHED, not written once: when a different account
        // becomes the playtime winner, the play record carries the new
        // account's minutes and last-played, and attribution has to move with
        // them or the row claims one account's hours under another's name.
        // COALESCE keeps the last known account when this scan cannot name one
        // (no readable userdata, no playtime record) — refresh, never erase.
        // acquired_at follows the same rule: sources that know it are rare.
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO ownerships (release_id, store, account_ref, acquired_at, license_type,
                                    price_paid_cents, price_source, install_path, installed)
            VALUES (@ReleaseId, @Store, @AccountRef, @AcquiredAt, @LicenseType,
                    @PricePaidCents, @PriceSource, @InstallPath, @Installed)
            ON CONFLICT (release_id, store) DO UPDATE SET
                install_path = excluded.install_path,
                installed    = excluded.installed,
                account_ref  = COALESCE(excluded.account_ref, ownerships.account_ref),
                acquired_at  = COALESCE(excluded.acquired_at, ownerships.acquired_at)
            RETURNING id;
            """, ownership, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<Ownership?> GetAsync(long id, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.QuerySingleOrDefaultAsync<Ownership>(new CommandDefinition(
            $"SELECT {Columns} FROM ownerships WHERE id = @id;",
            new { id }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Ownership>> GetByReleaseAsync(long releaseId, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Ownership>(new CommandDefinition(
            $"SELECT {Columns} FROM ownerships WHERE release_id = @releaseId ORDER BY id;",
            new { releaseId }, transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task UpdateInstallStateAsync(long id, string? installPath, bool installed, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ownerships
            SET install_path = @installPath, installed = @installed
            WHERE id = @id;
            """, new { id, installPath, installed }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Ownership>> GetAllAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var rows = await lease.Connection.QueryAsync<Ownership>(new CommandDefinition(
            $"SELECT {Columns} FROM ownerships ORDER BY id;",
            transaction: lease.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
