using Avalonia;
using Avalonia.Threading;
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

    /// <summary>
    /// Cancelled when the window closes, so background enrichment stops before
    /// the host — and the SQLite connection factory with it — is disposed.
    /// </summary>
    private static readonly CancellationTokenSource Shutdown = new();

    [STAThread]
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ConfigureServices(builder.Services);

        using var host = builder.Build();
        AppHost = host;
        Task enrichment = Task.CompletedTask;
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
                // metadata filling in behind it.
                //
                // Held rather than dropped: the finally block cancels it and
                // waits, because `using var host` disposes the service provider
                // — and the SQLite connection factory with it — and closing the
                // window two seconds into the first run is a normal thing to do.
                enrichment = Task.Run(async () =>
                {
                    var services = host.Services;
                    try
                    {
                        var report = await services.GetRequiredService<EnrichmentSyncService>()
                            .EnrichAsync(Shutdown.Token);

                        // §5.3 step 2, after enrichment so it compares real
                        // titles rather than the "App 620" placeholders it
                        // skips. Unconditional: a pass that promoted nothing can
                        // still be the first sweep this library has ever had.
                        await services.GetRequiredService<LibrarySoftMatchSweep>()
                            .SweepAsync(Shutdown.Token);

                        // Titles were rewritten underneath a library the UI has
                        // already loaded; without this they only appear on the
                        // next launch, which is not what §7's copy promises.
                        if (report.Promoted > 0)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                                services.GetRequiredService<LibraryViewModel>()
                                    .LoadCommand.ExecuteAsync(null));
                        }

                        // MainWindow loads the queue on open, before the sweep
                        // has run, so the rail's REVIEW count and the empty
                        // state are both stale until this reload.
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            services.GetRequiredService<MergeQueueViewModel>()
                                .LoadCommand.ExecuteAsync(null));
                    }
                    catch (OperationCanceledException)
                    {
                        // Window closed mid-run. Each promotion commits on its
                        // own, so the next launch resumes from what is left.
                    }
                    catch (Exception ex)
                    {
                        services.GetRequiredService<ILoggerFactory>()
                            .CreateLogger(typeof(Program))
                            .LogWarning(ex, "Enrichment failed; titles stay provisional until the next run.");
                    }
                }, Shutdown.Token);
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Stop enrichment and let it unwind BEFORE the host disposes the
            // connection factory it is writing through.
            Shutdown.Cancel();
            try
            {
                enrichment.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Already logged inside the task; shutdown must not throw.
            }

            host.StopAsync().GetAwaiter().GetResult();
            Shutdown.Dispose();
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
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();

        // Ingest → Resolve → sync (§5.1: the UI reads the database; it never
        // calls these directly. Program composes them, the view models don't).
        services.AddSingleton<LibraryFoldersReader>();
        services.AddSingleton<AppManifestReader>();
        services.AddSingleton<LocalConfigReader>();
        services.AddSingleton<SteamAccountEnumerator>();
        services.AddSingleton<SteamLibrarySource>();
        services.AddSingleton<ExternalIdResolver>();
        services.AddSingleton<SteamSyncService>();

        // §5.3 step 2. Without this the soft matcher exists, is tested, and
        // never runs — merge_candidates stays empty and the queue's empty state
        // becomes a false claim about the user's library rather than a
        // description of a feature that was never wired.
        services.AddSoftMatching();

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
