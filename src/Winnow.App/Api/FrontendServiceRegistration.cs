using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Auth.WebView;
using Winnow.Core.Auth;
using Winnow.Core.Ingest;
using Winnow.Ingest.Steam.AccountPages;

namespace Winnow.App.Api;

/// <summary>Composes presentation clients only. The independent backend owns storage and workers.</summary>
internal static class FrontendServiceRegistration
{
    internal static IServiceCollection AddWinnowFrontend(IServiceCollection services, DataLocation data, WinnowApiClient api,
        IReadOnlyList<string>? backendArguments = null)
    {
        services.AddSingleton(data);
        services.AddSingleton(api);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ApiLaunchObservations>();
        services.AddSingleton<Winnow.Monitor.ILaunchObservations>(sp => sp.GetRequiredService<ApiLaunchObservations>());
        services.AddHostedService(sp => sp.GetRequiredService<ApiLaunchObservations>());
        services.AddSingleton<ConnectionSessionWatcherHealth>();
        services.AddSingleton<Winnow.Monitor.ISessionWatcherHealth>(sp => sp.GetRequiredService<ConnectionSessionWatcherHealth>());
        services.AddSingleton<ApiPresentationSettings>();
        services.AddSingleton<ISettingsRepository>(sp => sp.GetRequiredService<ApiPresentationSettings>());
        services.AddSingleton<Winnow.Enrich.Igdb.Storage.ISettingsStore, ArtworkPreferenceStore>();
        services.AddSingleton<ArtworkPreferences>();
        services.AddSingleton<IFirstRunSetup, ApiSetupProgress>();
        services.AddSingleton<IIgdbSettingsService, ConnectionIgdbSettingsService>();
        services.AddSingleton<Winnow.Api.Contracts.Companion.ICompanionSettingsService, ApiCompanionSettings>();
        services.AddSingleton<IPluginSettingsBackend, ConnectionPluginSettingsBackend>();
        services.AddSingleton<IOfficialPluginInstaller, ConnectionOfficialPluginInstaller>();
        services.AddSingleton<IManualMetadataSyncService, ConnectionManualMetadataSyncService>();
        services.AddSingleton<IAccountVisibility, ApiAccountVisibility>();
        services.AddSingleton<IGameRefetch, ApiGameRefetch>();
        services.AddSingleton<IMergeSuggestionRefresh, ApiMergeSuggestionRefresh>();
        services.AddSingleton<IIgdbAssignmentService, ApiIgdbAssignmentService>();
        services.AddTransient<IWorkMetadataEditService, ApiWorkMetadataEditService>();
        services.AddSingleton<IUpdateFlagService, ApiUpdateFlagService>();
        services.AddTransient<IArtworkBrowserService, ApiArtworkBrowserService>();
        services.AddSingleton<IGameLaunchService, ApiGameLaunchService>();
        services.AddSingleton<IPluginActionDispatcher, ApiPluginActionDispatcher>();
        services.AddSingleton<ISessionJournalService, ApiSessionJournalService>();
        services.AddSingleton<IFeedService, ApiFeedService>();
        services.AddSingleton<Winnow.Api.Contracts.Connections.IStoreConnectionApi, ConnectionStoreApi>();
        services.AddSingleton<IWebViewInputSupport, FullscreenWebViewInputSupport>();
        services.AddWebViewAuthPrompt(Path.Combine(data.Root, "frontend-cache", "WebView2"));
        services.AddWebViewPatchNotesReader(Path.Combine(data.Root, "frontend-cache", "WebView2"));
        services.AddSteamWebViewSignIn();
        services.AddSteamAccountPageHarvester();
        services.AddSingleton<ApiSteamAccountPages>();
        services.AddSingleton<ISteamAccountPageImport>(sp => sp.GetRequiredService<ApiSteamAccountPages>());
        services.AddSingleton<ISteamAccountPageFileLoader>(sp => sp.GetRequiredService<ApiSteamAccountPages>());
        services.AddSingleton<ISteamAccountPageFilePicker, TopLevelSteamAccountPageFilePicker>();
        services.AddSingleton<SteamAccountImportViewModel>();
        services.AddSingleton<IAcquisitionExport, ApiAcquisitionExport>();
        services.AddSingleton(sp => new SteamSignInService(sp.GetRequiredService<ISteamSignInSession>(), sp.GetRequiredService<Winnow.Api.Contracts.Connections.IStoreConnectionApi>()));
        services.AddSingleton<ISteamSignInService>(sp => sp.GetRequiredService<SteamSignInService>());
        services.AddSingleton(sp => new EpicSignInService(sp.GetRequiredService<Winnow.Api.Contracts.Connections.IStoreConnectionApi>(), sp.GetServices<IInteractiveAuthPrompt>()));
        services.AddSingleton<IStoreConnections>(sp => new StoreConnections(sp.GetRequiredService<Winnow.Api.Contracts.Connections.IStoreConnectionApi>(), sp.GetRequiredService<EpicSignInService>()));

        services.AddCoverCache(options =>
        {
            options.CacheDirectory = Path.Combine(data.Root, "frontend-cache", "avalonia");
            options.NegativeTtl = TimeSpan.FromSeconds(30);
        });
        services.RemoveAll<ICoverSource>();
        services.AddSingleton<ICoverSource, ApiCoverSource>();

        services.AddSingleton(_ => new UserThemeStore(Path.Combine(data.Root, "themes")));
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AppearanceViewModel>();
        services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
        services.AddSingleton<IUriDispatcher, TopLevelUriDispatcher>();
        services.AddSingleton<IStoreClientAvailability, StoreClientAvailability>();
        services.AddSingleton<IGameLinkRouter, GameLinkRouter>();
        services.AddSingleton<IExecutableFilePicker, TopLevelExecutableFilePicker>();
        services.AddSingleton<IExecutableInspector, FileVersionInfoExecutableInspector>();
        services.AddSingleton<IImageFilePicker, TopLevelImageFilePicker>();
        services.AddSingleton<IAcquisitionExportDestination, TopLevelAcquisitionExportDestination>();
        services.AddSingleton<WindowsJournalNotification>();
        services.AddSingleton<IJournalNotification>(sp => sp.GetRequiredService<WindowsJournalNotification>());
        services.AddSingleton<JournalPromptViewModel>();
        services.AddSingleton<LaunchStatusViewModel>();
        services.AddSingleton<DormancyRamp>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<IStoreTitleCounts>(sp => sp.GetRequiredService<LibraryViewModel>());
        services.AddSingleton<IGameTileSource>(sp => sp.GetRequiredService<LibraryViewModel>());
        services.AddSingleton(sp => sp.GetRequiredService<LibraryViewModel>().Lists);
        services.AddSingleton<LibrarySettingsViewModel>();
        services.AddSingleton<MergeQueueViewModel>();
        services.AddSingleton<FeedViewModel>();
        services.AddSingleton<FetchStatusViewModel>();
        services.AddSingleton<IgdbSettingsViewModel>();
        services.AddSingleton<PluginSettingsViewModel>();
        services.AddSingleton<ArtworkOrderViewModel>();
        services.AddSingleton<MetadataSyncViewModel>();
        services.AddSingleton<EnrichmentSettingsViewModel>();
        services.AddSingleton(sp => new DiagnosticsViewModel(sp.GetRequiredService<Winnow.Monitor.ISessionWatcherHealth>(), dataDirectory: data.Root));
        services.AddSingleton<PhoneSyncViewModel>();
        services.AddSingleton<ApplicationSettingsViewModel>();
        services.AddSingleton<FirstRunSetupViewModel>();
        services.AddHttpClient<GitHubReleaseClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<IUpdateInstaller>(_ =>
        {
            var installed = new WindowsUpdateInstaller();
            return installed.IsSupported ? installed
                : new PortableUpdateInstaller(AppContext.BaseDirectory, data.Root, Program.PortableLeaseAvailable);
        });
        services.AddSingleton(_ => new BackendUpdateLifecycle(data.Root, backendArguments));
        services.AddSingleton<ILibraryServiceLifecycle>(sp => sp.GetRequiredService<BackendUpdateLifecycle>());
        services.AddSingleton(sp => new ApplicationUpdater(
            sp.GetRequiredService<GitHubReleaseClient>(), sp.GetRequiredService<ISettingsRepository>(),
            sp.GetRequiredService<IUpdateInstaller>(), Path.Combine(data.Root, "updates", "downloads"),
            ApplicationBuildInfo.Current.Version,
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64 ? ""
                : OperatingSystem.IsWindows() ? sp.GetRequiredService<IUpdateInstaller>() is WindowsUpdateInstaller ? "win-x64-setup.exe" : "win-x64.zip"
                : OperatingSystem.IsLinux() ? UpdateInstallation.IsManagedLinux(AppContext.BaseDirectory) ? "linux-x64.deb" : "linux-x64.tar.gz" : "",
            () => Avalonia.Threading.Dispatcher.UIThread.Post(() => (Avalonia.Application.Current as App)?.ExitForUpdate()),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ApplicationUpdater>>(),
            sp.GetRequiredService<BackendUpdateLifecycle>().StopAsync,
            sp.GetRequiredService<BackendUpdateLifecycle>().RecoverAsync));
        services.AddSingleton<IApplicationUpdater>(sp => sp.GetRequiredService<ApplicationUpdater>());
        services.AddHostedService(sp => sp.GetRequiredService<ApplicationUpdater>());
        services.AddSingleton<StoresViewModel>();
        services.AddSingleton(sp => new AccountStatsViewModel(api));
        services.AddSingleton(sp => new GameplayStatsViewModel(api, sp.GetRequiredService<LibraryViewModel>()));
        services.AddSingleton<StatsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddHostedService<BackendLiveUpdates>();
        return services;
    }
}
