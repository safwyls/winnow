using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.App.Services;

public sealed class SteamAchievementSyncService(
    IAchievementRepository repository, ISettingsRepository settings, ISteamApiKeyProvider keys,
    ISteamAchievementClient client, TimeProvider clock)
{
    /// <summary>At most sixty requests per pass; twenty games spreads large libraries across the day.</summary>
    public const int BatchSize = 20;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<int> SyncAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var key = await keys.GetAsync(ct);
            if (key is null || !SteamId.TryParse(await settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct), out var account)
                || await settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, ct) != SteamCredentialFingerprint.OfApiKey(key.Value))
                return 0;
            var candidates = await repository.GetDueSteamAsync(account.AccountRef, clock.GetUtcNow().UtcDateTime, BatchSize, ct);
            var count = 0;
            foreach (var candidate in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (!uint.TryParse(candidate.AppId, out var appId) || appId == 0) continue;
                // Account changes do not repurpose an in-flight answer. Stop before sending more requests.
                if (await settings.GetAsync(SteamOwnedAccount.RefSettingKey, ct) != account.AccountRef
                    || await settings.GetAsync(SteamOwnedAccount.KeyFingerprintSettingKey, ct) != SteamCredentialFingerprint.OfApiKey(key.Value)) break;
                var result = await client.FetchAsync(account, appId, key, ct);
                await repository.SaveAsync(candidate.ReleaseId, account.AccountRef, result, ct);
                count++;
            }
            return count;
        }
        finally { _gate.Release(); }
    }
}

public sealed class SteamAchievementSchedulerService(SteamAchievementSyncService sync,
    LibraryChangePublisher publisher, IOptions<RemoteOwnershipSchedulerOptions> options,
    TimeProvider clock, ILogger<SteamAchievementSchedulerService> log) : BackgroundService
{
    /// <summary>Twenty games per quarter hour permits a full daily sweep of about 1,900 games.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(Interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { if (await sync.SyncAsync(stoppingToken) > 0) await publisher.PublishAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { log.LogWarning("Achievement refresh failed ({FaultType}); retained earlier evidence.", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
