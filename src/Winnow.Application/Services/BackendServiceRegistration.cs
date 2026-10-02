using Winnow.App.Services;
using Winnow.Core.Auth;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.Igdb;
using Winnow.Plugins;
using Winnow.PluginSdk;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Stores;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.Updates;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Epic.Web;
using Winnow.Ingest.Gog;
using Winnow.Ingest.Steam;
using Winnow.Monitor;
using Winnow.Recommend;
using Winnow.Resolve;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Winnow.Application;

/// <summary>Composes domain workers without creating a frontend or its dispatcher.</summary>
public static class BackendServiceRegistration
{
    public static IServiceCollection AddWinnowRuntime(this IServiceCollection services, string dataRoot, bool backgroundEnabled = true, string? databasePath = null)
    {
        var data = new DataLocation(dataRoot, databasePath ?? Path.Combine(dataRoot, "winnow.db"), DataMigrationOutcome.None);
        services.AddSingleton(new SteamPlaytimeBackfillOptions { Enabled = backgroundEnabled });
        services.Configure<SnapshotSchedulerOptions>(o => o.Enabled = backgroundEnabled);
        services.Configure<RemoteOwnershipSchedulerOptions>(o => o.Enabled = backgroundEnabled);
        services.Configure<SessionWatcherOptions>(o => o.Enabled = backgroundEnabled);
        services.AddSingleton(sp => new LibraryChangePublisher(_ =>
        {
            // Cancellation after a commit must not hide that change from connected clients.
            sp.GetRequiredService<IApplicationChangePublisher>().Publish("library.changed");
            return Task.CompletedTask;
        }));
        services.AddSingleton(data);
        services.TryAddSingleton<ISqliteConnectionFactory>(
            _ => new SqliteConnectionFactory(data.DatabasePath));
        services.TryAddSingleton<IUnitOfWorkFactory>(
            sp => sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IWorkRepository, WorkRepository>();
        services.AddSingleton<IArtworkChoiceRepository, ArtworkChoiceRepository>();
        services.AddSingleton<ArtworkSelectionService>();
        services.AddSingleton<ArtworkBrowserService>();
        services.AddSingleton<IArtworkBrowserService>(sp => sp.GetRequiredService<ArtworkBrowserService>());
        services.AddSingleton<ArtworkApplication>();
        services.AddHostedService<LaunchObservationBridge>();
        services.AddSingleton<ICoverSource, SteamBrowserArtworkSource>();
        services.AddSingleton<IIgdbObservationWriter, IgdbObservationWriter>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IReleaseYearEvidenceRepository, ReleaseYearEvidenceRepository>();
        services.AddSingleton<IReleaseEditionEvidenceRepository, ReleaseEditionEvidenceRepository>();
        services.AddSingleton<IGroupHeaderPreferenceRepository, GroupHeaderPreferenceRepository>();
        services.AddSingleton<IOwnershipRepository, OwnershipRepository>();
        services.AddSingleton<ISteamInstallStateRepository, SteamInstallStateRepository>();
        services.AddSingleton<IGogInstallStateRepository, GogInstallStateRepository>();
        services.AddSingleton<IOwnershipAccountRepository, OwnershipAccountRepository>();
        services.AddSingleton<IOwnershipInventoryRepository, OwnershipInventoryRepository>();
        services.AddSingleton<IPlayRecordRepository, PlayRecordRepository>();
        services.AddSingleton<IPlaytimeSnapshotRepository, PlaytimeSnapshotRepository>();
        services.AddSingleton<ISteamPlaytimeObservationRepository, SteamPlaytimeObservationRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IActivityRepository, ActivityRepository>();
        services.AddSingleton<IUpdateEventRepository, UpdateEventRepository>();
        services.AddSingleton<ILifecycleRepository, LifecycleRepository>();
        services.AddSingleton<IGameListRepository, GameListRepository>();
        services.AddSingleton<IMergeCandidateRepository, MergeCandidateRepository>();
        services.AddSingleton<IIdentityLinkRepository, IdentityLinkRepository>();
        services.AddSingleton<IExpansionRefusalRepository, ExpansionRefusalRepository>();
        services.AddSingleton<IAchievementQueryRepository, AchievementQueryRepository>();
        services.AddSingleton<IAchievementRepository, AchievementRepository>();
        services.AddSingleton<SteamAchievementSyncService>();
        services.AddSingleton<IHiddenGameRepository, HiddenGameRepository>();
        services.AddSingleton<IWorkMaturityRepository, WorkMaturityRepository>();
        services.AddSingleton<IWorkIgdbPinRepository, WorkIgdbPinRepository>();
        services.AddSingleton<IWorkImageRepository, WorkImageRepository>();
        services.AddSingleton<IWorkRatingRepository, WorkRatingRepository>();
        services.AddSingleton<IWorkFieldSourceRepository, WorkFieldSourceRepository>();
        services.AddSingleton<IWorkMetadataEditService, WorkMetadataEditService>();
        services.AddSingleton<IManualEntryRepository, ManualEntryRepository>();
        services.AddSingleton<ILibraryQueryRepository, LibraryQueryRepository>();
        services.AddSingleton<ILibraryHistoryStatsRepository, LibraryHistoryStatsRepository>();
        services.AddSingleton<IFacetRepository, FacetRepository>();
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IAccountFactRepository, AccountFactRepository>();
        services.AddSingleton<IAccountStatsRepository, AccountStatsRepository>();
        services.AddSingleton<IGameplayStatsRepository, GameplayStatsRepository>();
        services.AddSingleton<IFeedFeedbackRepository, FeedFeedbackRepository>();
        services.AddSingleton<IUpdateAcknowledgementRepository, UpdateAcknowledgementRepository>();
        services.AddSingleton<LibraryFoldersReader>();
        services.AddSingleton<AppManifestReader>();
        services.AddSingleton<LocalConfigReader>();
        services.AddSingleton<SteamAccountEnumerator>();
        services.AddSingleton<SteamLibrarySource>();
        services.AddEpicIngest();
        services.AddGogIngest();
        services.AddSingleton<ExternalIdResolver>();
        services.AddSingleton<LibrarySyncGate>();
        services.AddSingleton<IEpicLaunchKeyStore, SqliteEpicLaunchKeyStore>();
        services.AddSingleton<EpicManifestStateReader>();
        services.AddSingleton(sp => new LibraryScanBaseline(
            sp.GetRequiredService<SteamLibrarySource>().ReadInstallFingerprint,
            sp.GetRequiredService<EpicManifestStateReader>().ReadFingerprint));
        services.AddSingleton<LocalLibrarySyncService>();
        services.AddSingleton<RemoteOwnershipSyncService>();
        services.AddSingleton<ILocalLibrarySync>(sp => sp.GetRequiredService<LocalLibrarySyncService>());
        services.AddSingleton<OwnershipRefreshRequests>();
        services.AddSingleton<IMergeSuggestionRefresh, MergeSuggestionRefresh>();
        services.AddSingleton(sp => new LibraryRefreshPipeline(
        [
            new("Steam playtime history", async ct => { await sp.GetRequiredService<ISteamPlaytimeBackfill>().BackfillAsync(ct); }, PublishAfter: true),
            new("Steam achievements", async ct => { await sp.GetRequiredService<SteamAchievementSyncService>().SyncAsync(ct); }, PublishAfter: true),
            new("GamesDB identity links", async ct => { await sp.GetRequiredService<GamesDbIdentitySyncService>().SyncAsync(ct); }, PublishAfter: true),
            new("Titles and metadata", async ct => { await sp.GetRequiredService<EnrichmentSyncService>().EnrichAsync(ct); }, IgdbRelevant: true),
            new("Filter facets", async ct => { await sp.GetRequiredService<FacetSyncService>().SyncAsync(ct); }, IgdbRelevant: true),
            new("Steam maturity", async ct => { await sp.GetRequiredService<SteamStoreMaturitySync>().SyncAsync(ct); }),
            new("IGDB maturity", async ct => { await sp.GetRequiredService<IgdbMaturitySync>().SyncAsync(ct); }, IgdbRelevant: true),
            new("Reception and images", async ct => { await sp.GetRequiredService<ReceptionSyncService>().SyncAsync(ct); }, IgdbRelevant: true),
            new("Lifecycle evidence", async ct => { await sp.GetRequiredService<LifecycleSyncService>().SyncAsync(ct); }, IgdbRelevant: true),
            new("Identity proposals", async ct => { await sp.GetRequiredService<IMergeSuggestionRefresh>().RefreshAsync(ct); }),
            new("Update signals", async ct => { await sp.GetRequiredService<UpdateSignalPoller>().PollDueBatchAsync(ct); }, PublishAfter: true),
            new("Storefront links", ct => sp.GetRequiredService<StorefrontSyncService>().SyncAsync(ct), PublishAfter: true),
        ], ct => sp.GetRequiredService<LibraryChangePublisher>().PublishAsync(ct), sp.GetRequiredService<ILogger<LibraryRefreshPipeline>>()));
        services.AddSingleton(sp => new OwnershipRefreshCoordinator(
            sp.GetRequiredService<RemoteOwnershipSyncService>(), sp.GetRequiredService<LibraryRefreshPipeline>(),
            sp.GetRequiredService<ILogger<OwnershipRefreshCoordinator>>()));
        services.AddSingleton<IRemoteOwnershipSync>(sp => sp.GetRequiredService<OwnershipRefreshCoordinator>());
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService(sp => new SnapshotSchedulerService(
            sp.GetRequiredService<ILocalLibrarySync>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SnapshotSchedulerOptions>>(),
            sp.GetRequiredService<ILogger<SnapshotSchedulerService>>(),
            sp.GetRequiredService<TimeProvider>(),
            refresh: ct => sp.GetRequiredService<LibraryChangePublisher>().PublishAsync(ct)));
        services.AddHostedService<RemoteOwnershipSchedulerService>();
        services.AddHostedService<SteamAchievementSchedulerService>();
        services.AddHostedService(sp => new LifecycleSchedulerService(
            sp.GetRequiredService<LifecycleSyncService>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RemoteOwnershipSchedulerOptions>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<LifecycleSchedulerService>>(),
            refresh: () => sp.GetRequiredService<LibraryChangePublisher>().PublishAsync(CancellationToken.None)));
        services.AddHostedService(sp => new SteamInstallRefreshService(
            sp.GetRequiredService<SteamLibrarySource>().ReadInstallFingerprint,
            ct => sp.GetRequiredService<LocalLibrarySyncService>().SyncAsync(ct),
            ct => sp.GetRequiredService<LibraryChangePublisher>().PublishAsync(ct),
            sp.GetRequiredService<ILogger<SteamInstallRefreshService>>(),
            sp.GetRequiredService<TimeProvider>(),
            enabled: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SnapshotSchedulerOptions>>().Value.Enabled,
            baseline: sp.GetRequiredService<LibraryScanBaseline>()));
        services.AddHostedService(sp => new EpicInstallRefreshService(
            sp.GetRequiredService<EpicManifestStateReader>().ReadFingerprint,
            ct => sp.GetRequiredService<LocalLibrarySyncService>().SyncEpicAsync(ct),
            ct => sp.GetRequiredService<LibraryChangePublisher>().PublishAsync(ct),
            sp.GetRequiredService<ILogger<EpicInstallRefreshService>>(),
            sp.GetRequiredService<TimeProvider>(),
            enabled: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SnapshotSchedulerOptions>>().Value.Enabled,
            baseline: sp.GetRequiredService<LibraryScanBaseline>()));
        services.AddSessionWatching();
        services.AddSoftMatching();
        services.AddCoverPipeline(o => o.CacheDirectory = Path.Combine(data.Root, "covers"));
        services.AddSingleton<ISteamLibraryAssetLookup, SteamLibraryAssetLookup>();
        services.AddIgdbCoverSource();
        services.AddIgdbEnrichment();
        services.AddPluginHttp();
        services.AddSingleton<PluginStorage>();
        services.AddSingleton<IPluginStateStore>(sp => sp.GetRequiredService<PluginStorage>());
        services.AddSingleton<IPluginContextFactory, PluginContextFactory>();
        services.AddSingleton<PluginCatalog>();
        services.AddSingleton<LegacySteamGridDbPluginMigration>();
        services.AddSingleton<PluginSettingsBackend>(sp => new(sp.GetRequiredService<PluginCatalog>(),
            sp.GetRequiredService<PluginStorage>(), Path.Combine(data.Root, "plugins")));
        services.AddSingleton<IPluginSettingsBackend>(sp => sp.GetRequiredService<PluginSettingsBackend>());
        services.AddSingleton<IOfficialPluginInstaller>(sp => new OfficialPluginInstaller(
            OfficialPluginInstaller.CreateHttpClient(), sp.GetRequiredService<PluginCatalog>(),
            sp.GetRequiredService<IPluginSettingsBackend>(), sp.GetRequiredService<ArtworkPreferences>(), sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        services.AddSingleton<IPluginFacetRepository, PluginFacetRepository>();
        services.AddSingleton<Winnow.Covers.ICoverSource, PluginArtworkSource>();
        services.AddSteamStoreEnrichment();
        services.AddGamesDbIdentityGraph();
        services.AddSingleton<EnrichmentLookupPlanner>();
        services.AddSingleton<GamesDbIdentitySyncService>();
        services.AddSingleton<IReleaseEditionEvidenceAcquirer, ReleaseEditionEvidenceAcquirer>();
        services.AddSteamWebApi();
        services.AddSteamPlaytimeBackfill();
        services.AddSingleton<Winnow.Ingest.Epic.Web.IEpicCatalogCache, SqliteEpicCatalogCache>();
        services.AddSingleton<Winnow.Ingest.Epic.Web.IEpicLibraryCache, SqliteEpicLibraryCache>();
        services.AddEpicWebApi();
        services.AddSingleton<IAccountVisibility, AccountVisibilityService>();
        services.TryAddSingleton<ISteamAccountConfirmation, SteamAccountConfirmation>();
        services.AddSteamAccountPageImport();
        services.AddSingleton<IgdbSettingsService>();
        services.AddSingleton<IIgdbSettingsService>(sp => sp.GetRequiredService<IgdbSettingsService>());
        services.AddSingleton<IManualMetadataSyncService, ManualMetadataSyncService>();
        services.AddSingleton<ArtworkPreferences>();
        services.AddSingleton<FirstRunSetupService>();
        services.AddSingleton<PresentationPreferencesService>();
        services.AddSingleton<EnrichmentSyncService>();
        services.AddSingleton<FacetSyncService>();
        services.AddSingleton<WorkReceptionWriter>();
        services.AddSingleton<ReceptionSyncService>();
        services.AddSingleton<PluginSyncService>();
        services.AddSingleton<PluginGameActionService>();
        services.AddSingleton<IUriDispatcher, SystemUriDispatcher>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<GameActionApplication>();
        services.AddSingleton<PluginFeedService>();
        services.AddSingleton<LifecycleSyncService>();
        services.AddSingleton<GameRefetchService>();
        services.AddSingleton<IGameRefetch>(sp => sp.GetRequiredService<GameRefetchService>());
        services.AddUpdateSignals();
        services.AddSingleton<IEpicLaunchKeys, SqliteEpicLaunchKeys>();
        services.AddStorefrontEnrichment();
        services.AddSingleton<StorefrontSyncService>();
        services.AddSingleton<SessionJournalService>();
        services.AddSingleton<IIgdbAssignmentService, IgdbAssignmentService>();
        services.AddSingleton<IRecommendationEngine, RecommendationEngine>();
        services.AddSingleton<FeedService>();
        services.AddSingleton<IFeedService>(sp => sp.GetRequiredService<FeedService>());
        return services;
    }
}
