using Dapper;
using Hoard.Data;

namespace Hoard.Enrich.Igdb.Storage;

/// <summary>Dapper-backed <see cref="ISettingsStore"/> over the <c>settings</c> table.</summary>
public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteSettingsStore(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        return await lease.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @key;",
            new { key }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """, new { key, value }, transaction: lease.Transaction, cancellationToken: ct));
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        await lease.Connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM settings WHERE key = @key;",
            new { key }, transaction: lease.Transaction, cancellationToken: ct));
    }
}
