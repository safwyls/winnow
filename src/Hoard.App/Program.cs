using Avalonia;
using Hoard.App.Services;
using Hoard.App.ViewModels;
using Hoard.Core.Repositories;
using Hoard.Covers;
using Hoard.Data;
using Hoard.Data.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Steam;
using Hoard.Ingest.Steam;
using Hoard.Resolve;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hoard.App;

public static class Program
{
    /// <summary>
    /// The generic host backing the app. Built before Avalonia starts;
    /// <see cref="App"/> resolves view models from its service provider.
    /// </summary>
    public static IHost? AppHost { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureServices(builder.Services);

        using var host = builder.Build();
        AppHost = host;
        try
        {
            host.Start();

            // Migrations run before any UI or repository touches the db.
            host.Services.GetRequiredService<DatabaseInitializer>().Initialize();

#if DEBUG
            // --seed-sample fills an empty database with the M0 sample library
            // so the view is verifiable before real ingest is wired. After
            // migrations, before the UI reads anything.
            if (args.Contains("--seed-sample"))
            {
                Services.SampleDataSeeder.SeedAsync(host.Services).GetAwaiter().GetResult();
            }
            else
#endif
            // The M0 library comes from local Steam files only — no network, so
            // this stays fast enough to precede the window. --no-sync skips it
            // for UI work against a fixed database.
            if (!args.Contains("--no-sync"))
            {
                host.Services.GetRequiredService<Services.SteamSyncService>()
                    .SyncAsync().GetAwaiter().GetResult();

                // Names for the games the local files could only identify by
                // appid. This one DOES touch the network, so it must not gate
                // the window: §7 promises a browsable library immediately with
                // metadata filling in behind it. Fire and forget, and let the
                // failure land in the log rather than in front of the user.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await host.Services.GetRequiredService<EnrichmentSyncService>().EnrichAsync();
                    }
                    catch (Exception ex)
                    {
                        host.Services.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(typeof(Program))
                            .LogWarning(ex, "Enrichment failed; titles stay provisional until the next run.");
                    }
                });
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            host.StopAsync().GetAwaiter().GetResult();
            AppHost = null;
        }
    }

    // Avalonia configuration; also used by the previewer. Do not remove.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void ConfigureServices(IServiceCollection services)
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hoard",
            "hoard.db");

        services.AddSingleton<ISqliteConnectionFactory>(new SqliteConnectionFactory(dbPath));
        // Same instance under both contracts: the unit-of-work scope repositories
        // enlist in is owned by the connection factory, so resolving
        // IUnitOfWorkFactory separately would hand out a second, unrelated scope.
        services.AddSingleton<IUnitOfWorkFactory>(
            sp => sp.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<DatabaseInitializer>();

        services.AddSingleton<IWorkRepository, WorkRepository>();
        services.AddSingleton<IReleaseRepository, ReleaseRepository>();
        services.AddSingleton<IOwnershipRepository, OwnershipRepository>();
        services.AddSingleton<IPlayRecordRepository, PlayRecordRepository>();
        services.AddSingleton<IPlaytimeSnapshotRepository, PlaytimeSnapshotRepository>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<IUpdateEventRepository, UpdateEventRepository>();
        services.AddSingleton<IGameListRepository, GameListRepository>();
        services.AddSingleton<IMergeCandidateRepository, MergeCandidateRepository>();
        services.AddSingleton<ILibraryQueryRepository, LibraryQueryRepository>();

        // Ingest → Resolve → sync (§5.1: the UI reads the database; it never
        // calls these directly. Program composes them, the view models don't).
        services.AddSingleton<LibraryFoldersReader>();
        services.AddSingleton<AppManifestReader>();
        services.AddSingleton<LocalConfigReader>();
        services.AddSingleton<SteamAccountEnumerator>();
        services.AddSingleton<SteamLibrarySource>();
        services.AddSingleton<ExternalIdResolver>();
        services.AddSingleton<SteamSyncService>();

        // Cover art (§5.4). Steam's portrait capsule needs no credentials, so
        // the grid has real art regardless of IGDB configuration; an IGDB cover
        // source registered later fills the gaps. Without this the tile's
        // optional ICoverCache is null and every tile silently falls back to
        // procedural placeholder art — the grid still works, which is exactly
        // what makes the omission easy to miss.
        services.AddCoverCache();

        // Enrichment. IGDB is the designed backbone (§4.4) and wins conflicts;
        // the keyless Steam store endpoint is the fallback that keeps titles
        // resolving with no credentials set. Both soft-fail to "no data", and
        // neither may block a user-facing path (§5.1, pitfall 3).
        services.AddIgdbEnrichment();
        services.AddSteamStoreEnrichment();
        services.AddSingleton<EnrichmentSyncService>();

        services.AddSingleton<LibraryViewModel>();

        // MainWindowViewModel takes MergeQueueViewModel as a required
        // dependency, so omitting this throws at startup rather than at build.
        services.AddMergeQueue();

        services.AddSingleton<MainWindowViewModel>();
    }
}
