using System.Globalization;
using Dapper;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

/// <summary>
/// <see cref="IResolveStateRepository"/> over the §6 <c>settings</c> table. No
/// migration: <c>settings(key, value)</c> has existed since 0001 precisely so
/// small scalars like this need no schema of their own.
/// </summary>
public sealed class ResolveStateRepository : IResolveStateRepository
{
    /// <summary>
    /// Namespaced by module. <c>settings</c> is shared with IGDB credentials
    /// and the cached Twitch token, so an unprefixed key like
    /// <c>last_sweep_at</c> would be a collision waiting for the second module
    /// that wants one.
    /// </summary>
    public const string LastSoftMatchSweepKey = "resolve.soft_match.last_sweep_at";

    private readonly ISqliteConnectionFactory _factory;

    public ResolveStateRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<DateTimeOffset?> GetLastSoftMatchSweepAsync(CancellationToken ct = default)
    {
        using var lease = _factory.Lease();
        var raw = await lease.Connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @key;",
            new { key = LastSoftMatchSweepKey },
            transaction: lease.Transaction,
            cancellationToken: ct));

        // A value that will not parse is treated as absent rather than as a
        // crash: the caller's fallback is "we cannot say the sweep has run",
        // which is the honest answer to an unreadable timestamp too.
        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    public async Task SetLastSoftMatchSweepAsync(
        DateTimeOffset completedAt, CancellationToken ct = default)
    {
        using var lease = _factory.Lease();

        // Round-trip format ("O"): unambiguous, sorts lexicographically, and
        // survives a machine whose locale is not the one that wrote it.
        await lease.Connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            new
            {
                key = LastSoftMatchSweepKey,
                value = completedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            },
            transaction: lease.Transaction,
            cancellationToken: ct));
    }
}
