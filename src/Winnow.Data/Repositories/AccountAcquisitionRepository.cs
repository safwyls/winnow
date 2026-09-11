using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class AccountAcquisitionRepository(ISqliteConnectionFactory factory) : IAccountAcquisitionRepository
{
    public async Task<bool> TryAppendAsync(OwnershipAcquisitionObservation observation, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO ownership_acquisition_observations
              (ownership_id, account_ref, acquired_at, license_type, price_paid_cents, price_source, source, captured_at)
            VALUES (@OwnershipId, @AccountRef, @AcquiredAt, @LicenseType, @PricePaidCents, @PriceSource, @Source, @CapturedAt)
            ON CONFLICT DO NOTHING;
            """, observation, transaction: lease.Transaction, cancellationToken: ct)).ConfigureAwait(false) > 0;
    }

    public async Task<IReadOnlyList<OwnershipAcquisitionObservation>> GetAsync(
        IReadOnlyList<long> ownershipIds, CancellationToken ct = default)
    {
        if (ownershipIds.Count == 0) return [];
        using var lease = factory.Lease();
        var rows = await lease.Connection.QueryAsync<OwnershipAcquisitionObservation>(new CommandDefinition("""
            SELECT id AS Id, ownership_id AS OwnershipId, account_ref AS AccountRef,
                   acquired_at AS AcquiredAt, license_type AS LicenseType,
                   price_paid_cents AS PricePaidCents, price_source AS PriceSource,
                   source AS Source, captured_at AS CapturedAt
            FROM ownership_acquisition_observations WHERE ownership_id IN @ownershipIds
            ORDER BY captured_at, id;
            """, new { ownershipIds }, transaction: lease.Transaction, cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<long>> GetSteamOwnershipIdsAsync(string accountRef, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountRef);
        using var lease = factory.Lease();
        return (await lease.Connection.QueryAsync<long>(new CommandDefinition("""
            SELECT a.ownership_id FROM ownership_accounts a
            JOIN ownerships o ON o.id=a.ownership_id
            WHERE o.store='steam' AND a.account_ref=@accountRef;
            """, new { accountRef }, transaction: lease.Transaction, cancellationToken: ct)).ConfigureAwait(false)).AsList();
    }
}
