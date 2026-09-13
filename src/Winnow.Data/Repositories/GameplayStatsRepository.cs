using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Data.Repositories;

public sealed class GameplayStatsRepository(ISqliteConnectionFactory factory) : IGameplayStatsRepository
{
    // Familiar duration bands keep the histogram stable when its population changes.
    private const long HalfHourSeconds = 1800;
    private const long HourSeconds = 3600;
    private const long TwoHoursSeconds = 7200;
    // Scope is a JSON table so a large library cannot exceed SQLite's parameter limit.
    // The current library owns resolution and visibility; raw ownerships cannot supply either.
    private const string SessionCte = """
        WITH scope AS (
            SELECT json_extract(value, '$.OwnershipId') AS ownership_id,
                   json_extract(value, '$.ResolvedWorkId') AS work_id
            FROM json_each(@scope)
        ), candidates AS (
            SELECT s.ownership_id, v.work_id, o.store,
                   s.started_at, s.ended_at, s.duration_s,
                   unixepoch(s.started_at, 'subsec') AS start_s,
                   unixepoch(s.ended_at, 'subsec') AS end_s
            FROM scope v
            JOIN ownerships o ON o.id = v.ownership_id
            JOIN sessions s ON s.ownership_id = o.id
            WHERE (@store IS NULL OR o.store = @store)
              AND s.started_at < @untilUtc
              AND (s.ended_at > @fromUtc OR s.started_at >= @fromUtc)
        ), assessed AS (
            SELECT *, CASE WHEN start_s IS NOT NULL AND end_s IS NOT NULL
                                AND start_s > -62135596800 AND ended_at <= @asOfUtc
                                AND end_s > start_s AND duration_s > 0
                                AND duration_s <= end_s - start_s + 1
                           THEN 1 ELSE 0 END AS valid
            FROM candidates
        ), valid_sessions AS (
            SELECT ownership_id, work_id, store, start_s, end_s, duration_s
            FROM assessed
            WHERE valid = 1 AND end_s > @fromSecond AND start_s < @untilSecond
            GROUP BY ownership_id, work_id, store, started_at, ended_at, duration_s
        ), clipped AS (
            SELECT *, 1.0 * duration_s
                * (MIN(end_s, @untilSecond) - MAX(start_s, @fromSecond))
                / (end_s - start_s) AS seconds
            FROM valid_sessions
        ), started AS (
            SELECT * FROM valid_sessions WHERE start_s >= @fromSecond
        )
        """;

    public async Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);
        ct.ThrowIfCancellationRequested();
        var scope = request.Ownerships.Distinct().ToArray();
        if (scope.GroupBy(s => s.OwnershipId).Any(g => g.Count() != 1))
            throw new ArgumentException("An ownership must resolve to exactly one game.", nameof(request));
        var bins = request.TimeBins.ToArray();
        var parameters = new
        {
            scope = JsonSerializer.Serialize(scope, GameplayStatsJsonContext.Default.GameplayOwnershipScopeArray),
            bins = JsonSerializer.Serialize(bins, GameplayStatsJsonContext.Default.GameplayTimeBinArray),
            fromUtc = request.FromUtc,
            untilUtc = request.UntilUtc,
            fromSecond = new DateTimeOffset(request.FromUtc).ToUnixTimeSeconds(),
            untilSecond = new DateTimeOffset(request.UntilUtc).ToUnixTimeSeconds(),
            asOfUtc = request.AsOfUtc,
            store = request.Store,
            topLimit = GameplayStats.MaximumTopGames,
            halfHour = HalfHourSeconds,
            hour = HourSeconds,
            twoHours = TwoHoursSeconds,
        };
        using var lease = factory.Lease();
        using var snapshot = lease.Transaction is null ? lease.Connection.BeginTransaction(deferred: true) : null;
        using var result = await lease.Connection.QueryMultipleAsync(new CommandDefinition($"""
            {SessionCte}
            SELECT COALESCE(SUM(seconds), 0.0) AS RecordedSeconds,
                   COUNT(*) AS OverlappingSessionCount,
                   COUNT(DISTINCT work_id) AS GamesPlayedCount,
                   (SELECT COUNT(*) FROM started) AS StartedSessionCount,
                   (SELECT COUNT(*) FROM assessed
                    WHERE valid = 0 AND start_s >= @fromSecond) AS ExcludedSessionCount
            FROM clipped;

            {SessionCte}, ranked AS (
                SELECT duration_s, ROW_NUMBER() OVER (ORDER BY duration_s) AS position,
                       COUNT(*) OVER () AS total FROM started
            )
            SELECT AVG(1.0 * duration_s) FROM ranked
            WHERE position IN ((total + 1) / 2, (total + 2) / 2);

            {SessionCte}, bins AS (
                SELECT CAST(key AS INTEGER) AS position,
                       unixepoch(json_extract(value, '$.FromUtc')) AS start_s,
                       unixepoch(json_extract(value, '$.UntilUtc')) AS end_s
                FROM json_each(@bins)
            )
            SELECT b.position AS Position,
                   COALESCE(SUM(1.0 * s.duration_s
                     * (MIN(s.end_s, b.end_s) - MAX(s.start_s, b.start_s))
                     / (s.end_s - s.start_s)), 0.0) AS RecordedSeconds
            FROM bins b
            LEFT JOIN valid_sessions s ON s.start_s < b.end_s AND s.end_s > b.start_s
            GROUP BY b.position ORDER BY b.position;

            {SessionCte}
            SELECT work_id AS ResolvedWorkId, SUM(seconds) AS RecordedSeconds
            FROM clipped GROUP BY work_id
            ORDER BY RecordedSeconds DESC, work_id LIMIT @topLimit;

            {SessionCte}
            SELECT CASE WHEN duration_s < @halfHour THEN 0 WHEN duration_s < @hour THEN 1
                        WHEN duration_s < @twoHours THEN 2 ELSE 3 END AS Band,
                   COUNT(*) AS Count
            FROM started GROUP BY Band ORDER BY Band;

            {SessionCte}
            SELECT store AS Store, SUM(seconds) AS RecordedSeconds
            FROM clipped GROUP BY store ORDER BY RecordedSeconds DESC, store;
            """, parameters, transaction: lease.Transaction ?? snapshot, cancellationToken: ct)).ConfigureAwait(false);
        var totals = await result.ReadSingleAsync<TotalsRow>().ConfigureAwait(false);
        var median = await result.ReadSingleAsync<double?>().ConfigureAwait(false);
        var periods = (await result.ReadAsync<PeriodRow>().ConfigureAwait(false)).ToArray();
        var games = (await result.ReadAsync<GameRow>().ConfigureAwait(false)).ToArray();
        var lengths = (await result.ReadAsync<LengthRow>().ConfigureAwait(false)).ToDictionary(r => r.Band);
        var stores = (await result.ReadAsync<StoreRow>().ConfigureAwait(false)).ToArray();
        ct.ThrowIfCancellationRequested();
        long[] boundaries = [0, HalfHourSeconds, HourSeconds, TwoHoursSeconds];
        return new GameplayStats
        {
            RecordedSeconds = totals.RecordedSeconds,
            OverlappingSessionCount = checked((int)totals.OverlappingSessionCount),
            GamesPlayedCount = checked((int)totals.GamesPlayedCount),
            StartedSessionCount = checked((int)totals.StartedSessionCount),
            ExcludedSessionCount = checked((int)totals.ExcludedSessionCount),
            MedianSessionSeconds = median,
            Periods = periods.Select(p => new GameplayPeriodTotal(bins[p.Position].FromUtc,
                bins[p.Position].UntilUtc, p.RecordedSeconds)).ToArray(),
            TopGames = games.Select(g => new GameplayGameTotal(g.ResolvedWorkId, g.RecordedSeconds)).ToArray(),
            SessionLengths = boundaries.Select((lower, band) => new GameplaySessionLength(lower,
                band + 1 < boundaries.Length ? boundaries[band + 1] : null,
                checked((int)(lengths.GetValueOrDefault(band)?.Count ?? 0)))).ToArray(),
            Stores = stores.Select(s => new GameplayStoreTotal(s.Store, s.RecordedSeconds)).ToArray(),
        };
    }

    private static void Validate(GameplayStatsRequest request)
    {
        static bool WholeUtc(DateTime value) => value.Kind == DateTimeKind.Utc
            && value.Ticks % TimeSpan.TicksPerSecond == 0;
        if (!WholeUtc(request.FromUtc) || !WholeUtc(request.UntilUtc) || request.AsOfUtc.Kind != DateTimeKind.Utc
            || request.FromUtc >= request.UntilUtc)
            throw new ArgumentException("Gameplay bounds must be whole UTC seconds with a positive duration.", nameof(request));
        if (request.TimeBins.Count is < 1 or > GameplayStatsRequest.MaximumTimeBins)
            throw new ArgumentException("Provide between one and 512 time bins.", nameof(request));
        var cursor = request.FromUtc;
        foreach (var bin in request.TimeBins)
        {
            if (!WholeUtc(bin.FromUtc) || !WholeUtc(bin.UntilUtc)
                || bin.FromUtc != cursor || bin.UntilUtc <= bin.FromUtc || bin.UntilUtc > request.UntilUtc)
                throw new ArgumentException("Time bins must cover the period contiguously in UTC.", nameof(request));
            cursor = bin.UntilUtc;
        }
        if (cursor != request.UntilUtc || request.Ownerships.Any(o => o.OwnershipId <= 0 || o.ResolvedWorkId <= 0))
            throw new ArgumentException("Provide the full period and positive ownership/game IDs.", nameof(request));
    }

    private sealed class TotalsRow
    {
        public double RecordedSeconds { get; init; }
        public long GamesPlayedCount { get; init; }
        public long OverlappingSessionCount { get; init; }
        public long StartedSessionCount { get; init; }
        public long ExcludedSessionCount { get; init; }
    }
    private sealed class PeriodRow
    {
        public int Position { get; init; }
        public double RecordedSeconds { get; init; }
    }
    private sealed class GameRow
    {
        public long ResolvedWorkId { get; init; }
        public double RecordedSeconds { get; init; }
    }
    private sealed class LengthRow
    {
        public int Band { get; init; }
        public long Count { get; init; }
    }
    private sealed class StoreRow
    {
        public string Store { get; init; } = "";
        public double RecordedSeconds { get; init; }
    }
}

[JsonSerializable(typeof(GameplayOwnershipScope[]))]
[JsonSerializable(typeof(GameplayTimeBin[]))]
internal sealed partial class GameplayStatsJsonContext : JsonSerializerContext;
