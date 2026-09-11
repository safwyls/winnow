using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class OwnershipInventoryRepository(ISqliteConnectionFactory factory, TimeProvider? clock = null)
    : IOwnershipInventoryRepository
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<OwnershipInventoryAttempt> BeginAttemptAsync(string store, string accountRef, string source,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        store = store.Trim(); accountRef = accountRef.Trim(); source = source.Trim();
        using var lease = factory.Lease();
        var revision = await lease.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO account_inventory_observations
                (store, account_ref, source, revision, attempted_at, is_complete)
            VALUES (@store, @accountRef, @source, 1, @now, 0)
            ON CONFLICT (store, account_ref, source) DO UPDATE SET
                revision = account_inventory_observations.revision + 1,
                attempted_at = excluded.attempted_at,
                is_complete = 0, observed_at = NULL, item_count = NULL
            RETURNING revision;
            """, new { store, accountRef, source, now = _clock.GetUtcNow().UtcDateTime }, lease.Transaction, cancellationToken: ct));
        return new(store, accountRef, source, revision);
    }

    public async Task<bool> CompleteAsync(OwnershipInventoryAttempt attempt, DateTime observedAt, int itemCount,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        using var lease = factory.Lease();
        return await lease.Connection.ExecuteAsync(new CommandDefinition("""
            UPDATE account_inventory_observations
            SET is_complete = 1, observed_at = @observedAt, item_count = @itemCount
            WHERE store = @Store AND account_ref = @AccountRef AND source = @Source AND revision = @Revision;
            """, new { attempt.Store, attempt.AccountRef, attempt.Source, attempt.Revision, observedAt, itemCount },
            lease.Transaction, cancellationToken: ct)) > 0;
    }
}
