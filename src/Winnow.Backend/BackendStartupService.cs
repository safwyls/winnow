using Winnow.App.Services;
using Winnow.Plugins;
using Winnow.PluginSdk;

namespace Winnow.Backend;

/// <summary>All refresh lifetimes belong to the backend, independent of attached windows.</summary>
internal sealed record BackendRunOptions(bool BackgroundEnabled);

internal sealed class BackendStartupService(IServiceProvider services, BackendOwnership ownership, BackendRunOptions options,
    ILogger<BackendStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.BackgroundEnabled)
        {
            await DiscoverPluginsAsync(stoppingToken);
            return;
        }
        var startup = SynchronizeAsync(stoppingToken);
        var ownershipRequests = services.GetRequiredService<OwnershipRefreshRequests>();
        var igdb = services.GetRequiredService<IgdbSettingsService>();
        var pluginSettings = services.GetRequiredService<PluginSettingsBackend>();
        var ownershipRefresh = new CredentialMetadataRefresh(startup,
            async ct => { await services.GetRequiredService<IRemoteOwnershipSync>().SyncAsync(ct); },
            ReportFailure, stoppingToken);
        var metadataRefresh = new CredentialMetadataRefresh(startup,
            ct => services.GetRequiredService<LibraryRefreshPipeline>().RunAsync(ct, igdbOnly: true), ReportFailure, stoppingToken);
        var pluginStartup = DiscoverPluginsAsync(stoppingToken);
        var plugins = new PluginRefreshCoordinator(pluginStartup,
            ct => services.GetRequiredService<PluginSyncService>().ImportLibrariesAsync(ct),
            ct => services.GetRequiredService<PluginSyncService>().EnrichAsync(ct),
            ct => services.GetRequiredService<LibraryChangePublisher>().PublishAsync(ct),
            ReportFailure, stoppingToken, libraryStartup: startup,
            refreshSuggestions: ct => services.GetRequiredService<IMergeSuggestionRefresh>().RefreshAsync(ct));
        ownershipRequests.Requested += ownershipRefresh.Request;
        igdb.CredentialsChanged += metadataRefresh.Request;
        pluginSettings.RefreshRequested += plugins.Request;
        plugins.Request();
        try
        {
            await Task.WhenAll(startup, pluginStartup, ownershipRefresh.Completion, metadataRefresh.Completion, plugins.Completion);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            ownershipRequests.Requested -= ownershipRefresh.Request;
            igdb.CredentialsChanged -= metadataRefresh.Request;
            pluginSettings.RefreshRequested -= plugins.Request;
        }
    }

    private async Task SynchronizeAsync(CancellationToken ct)
    {
        using var trim = StartupMemoryTrim.Start(services, ct);
        try
        {
            LibrarySyncReport local;
            using (services.GetRequiredService<LibraryScanBaseline>().Expect())
                local = await services.GetRequiredService<ILocalLibrarySync>().SyncAsync(ct);
            if (local.Candidates > 0) await services.GetRequiredService<LibraryRefreshPipeline>().RunAsync(ct);
            var remote = services.GetRequiredService<IRemoteOwnershipSync>();
            if (local.Scan is { } scan) await remote.SyncAsync(scan, ct);
            else await remote.SyncAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private async Task DiscoverPluginsAsync(CancellationToken ct)
    {
        try
        {
            if (options.BackgroundEnabled) await services.GetRequiredService<LegacySteamGridDbPluginMigration>().RunAsync(ct);
            var catalog = services.GetRequiredService<PluginCatalog>();
            await catalog.DiscoverAsync(Path.Combine(services.GetRequiredService<BackendInstallationLease>().InstallationDirectory, "plugins"),
                Path.Combine(ownership.DataDirectory, "plugins"), ct);
            var preferences = services.GetRequiredService<ArtworkPreferences>();
            preferences.ConfigureSources(catalog.GetActive<IArtworkProviderPlugin>()
                .Select(p => new ArtworkSourceOption("plugin:" + p.Manifest.Id, p.Manifest.Name)));
            await preferences.LoadAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception exception) { ReportFailure(exception); }
    }

    private void ReportFailure(Exception exception) => logger.LogWarning(
        "Backend refresh failed ({FaultType}); persisted library data remains available.", exception.GetType().Name);
}
