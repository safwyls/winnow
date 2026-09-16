using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class SteamPlaytimeObservationRepository(ISqliteConnectionFactory factory)
    : ISteamPlaytimeObservationRepository
{
    private const string Columns = """
        id AS Id, ownership_id AS OwnershipId, account_ref AS AccountRef, source AS Source,
        playtime_minutes AS PlaytimeMinutes, last_played_at AS LastPlayedAt, observed_at AS ObservedAt
        """;

    public async Task ObserveAsync(SteamPlaytimeObservation observation, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.AccountRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Source);
        if (observation.Source is not ("steam_local" or "steam_web_api"))
            throw new ArgumentException("Only live Steam local and Web API observations are supported.", nameof(observation));
        if (observation.PlaytimeMinutes < 0) throw new ArgumentOutOfRangeException(nameof(observation));
        if (observation.ObservedAt.Kind == DateTimeKind.Unspecified
            || observation.LastPlayedAt?.Kind == DateTimeKind.Unspecified)
            throw new ArgumentException("Steam observation timestamps require an explicit timezone.", nameof(observation));
        observation = observation with
        {
            AccountRef = NormalizeAccount(observation.AccountRef),
            ObservedAt = observation.ObservedAt.ToUniversalTime(),
            LastPlayedAt = observation.LastPlayedAt?.ToUniversalTime(),
        };
        using var batch = new RepositoryWriteBatch(factory);
        var lease = batch.Lease;
        var previous = await lease.Connection.QuerySingleOrDefaultAsync<SteamPlaytimeObservation>(new CommandDefinition($"""
            SELECT {Columns} FROM steam_playtime_observations
            WHERE ownership_id = @OwnershipId AND account_ref = @AccountRef AND source = @Source
            ORDER BY observed_at DESC, id DESC LIMIT 1;
            """, observation, transaction: lease.Transaction, cancellationToken: ct));
        // Preserve change points, including decreases. Repeated polls do not grow
        // history or move the original increase's settling clock; bounds stay broad.
        if (previous is null || observation.ObservedAt < previous.ObservedAt
            || observation.PlaytimeMinutes != previous.PlaytimeMinutes || observation.LastPlayedAt != previous.LastPlayedAt)
        {
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO steam_playtime_observations
                    (ownership_id, account_ref, source, playtime_minutes, last_played_at, observed_at)
                VALUES (@OwnershipId, @AccountRef, @Source, @PlaytimeMinutes, @LastPlayedAt, @ObservedAt)
                ON CONFLICT DO NOTHING;
                """, observation, transaction: lease.Transaction, cancellationToken: ct));
        }
        batch.Commit();
    }

    public async Task<IReadOnlyList<SteamPlaytimeObservation>> GetByOwnershipAsync(
        long ownershipId, CancellationToken ct = default)
    {
        using var lease = factory.Lease();
        return (await lease.Connection.QueryAsync<SteamPlaytimeObservation>(new CommandDefinition($"""
            SELECT {Columns} FROM steam_playtime_observations
            WHERE ownership_id = @ownershipId ORDER BY observed_at, id;
            """, new { ownershipId }, transaction: lease.Transaction, cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<SteamReportedActivity>> GetActivityAsync(
        IReadOnlyCollection<long> ownershipIds, DateTime asOfUtc, string? accountRef = null,
        CancellationToken ct = default)
    {
        if (asOfUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Activity reads require UTC.", nameof(asOfUtc));
        if (accountRef is not null) accountRef = NormalizeAccount(accountRef);
        if (ownershipIds.Count == 0) return [];
        var ids = JsonSerializer.Serialize(ownershipIds.Distinct().ToArray(), SteamObservationJsonContext.Default.Int64Array);
        using var lease = factory.Lease();
        using var transaction = lease.Transaction is null ? lease.Connection.BeginTransaction(deferred: true) : null;
        using var results = await lease.Connection.QueryMultipleAsync(new CommandDefinition($"""
            SELECT {Columns} FROM steam_playtime_observations
            WHERE ownership_id IN (SELECT value FROM json_each(@ids)) AND observed_at <= @asOfUtc
            ORDER BY observed_at, id;
            SELECT id AS Id, ownership_id AS OwnershipId, started_at AS StartedAt,
                ended_at AS EndedAt, duration_s AS DurationSeconds, detection_method AS DetectionMethod,
                attributed_by AS AttributedBy, monitor_key AS MonitorKey
            FROM sessions WHERE ownership_id IN (SELECT value FROM json_each(@ids));
            SELECT ownership_id AS OwnershipId, account_ref AS AccountRef
            FROM ownership_accounts
            WHERE ownership_id IN (SELECT value FROM json_each(@ids)) AND first_seen_at <= @asOfUtc;
            """, new { ids, asOfUtc }, transaction: lease.Transaction ?? transaction, cancellationToken: ct));
        var observations = (await results.ReadAsync<SteamPlaytimeObservation>()).AsList();
        var sessions = (await results.ReadAsync<Session>()).AsList();
        var knownAccounts = (await results.ReadAsync<KnownAccount>()).AsList();
        var ambiguous = knownAccounts.Select(a => (a.OwnershipId, AccountRef: NormalizeKnownAccount(a.AccountRef)))
            .Concat(observations.Select(o => (o.OwnershipId, o.AccountRef)))
            .GroupBy(a => a.OwnershipId)
            .Where(g => g.Select(a => a.AccountRef).Distinct(StringComparer.Ordinal).Skip(1).Any())
            .Select(g => g.Key).ToHashSet();
        ct.ThrowIfCancellationRequested();
        return SteamPlaytimeReconciler.Reconcile(observations, sessions, asOfUtc, accountRef, ambiguous);
    }

    private static string NormalizeAccount(string accountRef)
    {
        if (!uint.TryParse(accountRef, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new ArgumentException("A Steam observation requires a positive account ID.", nameof(accountRef));
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeKnownAccount(string accountRef)
        => uint.TryParse(accountRef, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id.ToString(CultureInfo.InvariantCulture) : accountRef;

    private sealed record KnownAccount
    {
        public long OwnershipId { get; init; }
        public string AccountRef { get; init; } = "";
    }
}

[JsonSerializable(typeof(long[]))]
internal sealed partial class SteamObservationJsonContext : JsonSerializerContext;
