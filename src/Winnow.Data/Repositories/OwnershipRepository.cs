using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

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

    public async Task<long> UpsertAsync(OwnershipUpsert ownership, CancellationToken ct = default)
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
        //
        // Install state follows the same principle with a different rule, and
        // missing it is what made the "Installed" filter match nothing on a
        // library with games on disk. @Installed is three-valued: null means the
        // source cannot see the local disk at all (§4.2's GetOwnedGames), not
        // that the game is uninstalled. Writing excluded.installed
        // unconditionally let the web candidates — resolved after the local scan
        // purely because of union order — clear every flag the appmanifests had
        // just set.
        //
        //   @Installed IS NULL  → both install columns keep their stored values.
        //   @Installed non-null → both are written, false included. Uninstalling
        //                         is a real event and has to show.
        //
        // The path moves WITH the flag, never COALESCEd on its own: "installed =
        // 0 pointing at a directory that is gone" is worse than either honest
        // answer. On insert, a null answer stores 0/NULL — a fresh row needs
        // some value, and "not known to be installed" is the safe one; the next
        // scan that can see the disk corrects it.
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO ownerships (release_id, store, account_ref, acquired_at,
                                    install_path, installed)
            VALUES (@ReleaseId, @Store, @AccountRef, @AcquiredAt,
                    CASE WHEN @Installed IS NULL THEN NULL ELSE @InstallPath END,
                    COALESCE(@Installed, 0))
            ON CONFLICT (release_id, store) DO UPDATE SET
                account_ref  = COALESCE(excluded.account_ref, ownerships.account_ref),
                acquired_at  = COALESCE(excluded.acquired_at, ownerships.acquired_at),
                install_path = CASE WHEN @Installed IS NULL
                                    THEN ownerships.install_path
                                    ELSE excluded.install_path END,
                installed    = CASE WHEN @Installed IS NULL
                                    THEN ownerships.installed
                                    ELSE excluded.installed END
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

    public async Task<bool> FillAcquisitionFactsAsync(
        OwnershipAcquisitionFill fill, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fill);

        // Fill-only, and the WHERE clause is what makes it so. Every assignment
        // is COALESCE(stored, incoming), so a column that already holds a value
        // keeps it — the account pages are one source among several and the
        // newest reading is not automatically the best one.
        //
        // The WHERE clause then requires at least one column to be genuinely
        // empty AND to have something to put in it. Without it this UPDATE would
        // report a changed row on every re-run, writing each column back onto
        // itself, and "did the import do anything" would always answer yes.
        // With it, a second run over the same pages matches no rows and the
        // report honestly says nothing was filled.
        //
        // price_source moves WITH the price rather than being COALESCEd on its
        // own: a source label describing a price that came from somewhere else
        // is worse than no label.
        using var lease = _factory.Lease();
        var changed = await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE ownerships SET
                acquired_at      = COALESCE(acquired_at, @AcquiredAt),
                license_type     = COALESCE(license_type, @LicenseType),
                price_paid_cents = COALESCE(price_paid_cents, @PricePaidCents),
                price_source     = CASE WHEN price_paid_cents IS NULL AND @PricePaidCents IS NOT NULL
                                        THEN @PriceSource
                                        ELSE price_source END
            WHERE id = @OwnershipId
              AND ((acquired_at      IS NULL AND @AcquiredAt     IS NOT NULL)
                OR (license_type     IS NULL AND @LicenseType    IS NOT NULL)
                OR (price_paid_cents IS NULL AND @PricePaidCents IS NOT NULL));
            """, fill, transaction: lease.Transaction, cancellationToken: ct));

        return changed > 0;
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
