using Dapper;
using Hoard.Core.Repositories;

namespace Hoard.Data.Repositories;

/// <summary>
/// <see cref="ISettingsRepository"/> over the §6 <c>settings</c> table. No
/// migration: <c>settings(key, value)</c> has existed since 0001 precisely so
/// small scalars need no schema of their own.
///
/// <para>Leases rather than opening its own connection, so a preference written
/// inside an ambient unit of work commits or rolls back with everything else in
/// it (<see cref="ISqliteConnectionFactory.Lease"/>).</para>
/// </summary>
public sealed class SettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public SettingsRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // ExecuteScalar over a missing row yields null, which is exactly the
        // "never written" the contract promises — no separate EXISTS probe.
        return await lease.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @key;",
            new { key },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Upsert, not DELETE-then-INSERT: one statement, so it cannot leave the
        // key absent if the process dies between the two halves.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            new { key, value },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }
}
