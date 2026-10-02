using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Winnow.Core.Lifecycle;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Steam;

namespace Winnow.App.Services;

/// <summary>Bounded, persisted daily polling, independent of metadata completeness and play history.</summary>
public sealed class LifecycleSyncService(ILibraryQueryRepository library, ILifecycleRepository observations,
    ISettingsRepository settings, IIgdbLifecycleClient igdb, ISteamLifecycleClient steam,
    IIgdbObservationWriter writes,
    TimeProvider clock, ILogger<LifecycleSyncService> logger)
{
    public const string ScheduleKey = "lifecycle.poll-attempts.v2";
    public const int BatchSize = 50;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var targets = await library.GetFacetTargetsAsync(ct);
            var rawSchedule = await settings.GetAsync(ScheduleKey, ct);
            Dictionary<string, DateTime> attempts;
            try { attempts = rawSchedule is null ? [] : JsonSerializer.Deserialize<Dictionary<string, DateTime>>(rawSchedule) ?? []; }
            catch (JsonException) { attempts = []; }
            var currentKeys = targets.Select(AttemptKey).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in attempts.Keys.Where(key => !currentKeys.Contains(key)).ToArray()) attempts.Remove(stale);
            var now = clock.GetUtcNow().UtcDateTime;
            var due = targets.Where(t => t.IgdbId.HasValue || t.SteamAppId is not null)
                .Where(t => !attempts.TryGetValue(AttemptKey(t), out var last) || last <= now.AddDays(-1))
                .OrderBy(t => attempts.GetValueOrDefault(AttemptKey(t))).ThenBy(t => t.ReleaseId)
                .DistinctBy(t => t.ReleaseId).Take(BatchSize).ToArray();
            var inserted = 0;
            foreach (var target in due)
            {
                ct.ThrowIfCancellationRequested();
                var existing = await observations.GetForReleaseAsync(target.ReleaseId, ct);
                async Task Append(string source, string sourceId, DateTime at, LifecycleSignals signals, string raw)
                {
                    if (existing.Any(e => e.Source == source && e.SourceId == sourceId && e.ObservedAt == at)) return;
                    await observations.AppendAsync(new LifecycleObservation { ReleaseId = target.ReleaseId,
                        Source = source, SourceId = sourceId, ObservedAt = at, Signals = signals, RawJson = raw }, ct);
                    inserted++;
                }
                try
                {
                    if (target.IgdbId is { } id && await igdb.GetAsync(id, ct) is { } result)
                        await writes.TryWriteAsync(target.IgdbMapping, _ =>
                            Append("igdb", id.ToString(System.Globalization.CultureInfo.InvariantCulture), result.ObservedAt, result.Signals, result.RawJson), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { logger.LogWarning("IGDB lifecycle collection failed for release {ReleaseId}.", target.ReleaseId); }
                try
                {
                    if (target.SteamAppId is { } appId)
                        foreach (var result in await steam.GetAsync(appId, ct))
                            await Append(result.Source, appId, result.ObservedAt, result.Signals, result.RawJson);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { logger.LogWarning("Steam lifecycle collection failed for release {ReleaseId}.", target.ReleaseId); }
                // Failed requests advance only the attempt schedule, never the evidence clock.
                attempts[AttemptKey(target)] = clock.GetUtcNow().UtcDateTime;
                await settings.SetAsync(ScheduleKey, JsonSerializer.Serialize(attempts), ct);
            }
            return inserted;
        }
        finally { _gate.Release(); }
    }

    private static string AttemptKey(Winnow.Core.Queries.FacetTarget target)
        => FormattableString.Invariant($"{target.ReleaseId}:{target.IgdbMappingRevision}");
}

public sealed class LifecycleSchedulerService(LifecycleSyncService sync,
    IOptions<RemoteOwnershipSchedulerOptions> options, TimeProvider clock,
    ILogger<LifecycleSchedulerService> logger, Func<Task>? refresh = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (await sync.SyncAsync(stoppingToken) > 0 && refresh is not null) await refresh();
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { logger.LogWarning("Lifecycle sync failed; it resumes on the next hourly tick."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
