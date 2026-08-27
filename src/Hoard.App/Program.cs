using Avalonia;
using Avalonia.Threading;
using Hoard.App.Services;
using Hoard.App.ViewModels;
using Hoard.Auth.WebView;
using Hoard.Core.Auth;
using Hoard.Core.Repositories;
using Hoard.Covers;
using Hoard.Covers.Igdb;
using Hoard.Data;
using Hoard.Data.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Steam;
using Hoard.Enrich.SteamWeb;
using Hoard.Enrich.Updates;
using Hoard.Ingest.Epic;
using Hoard.Ingest.Epic.Web;
using Hoard.Ingest.Gog;
using Hoard.Ingest.Steam;
using Hoard.Monitor;
using Hoard.Resolve;
using Microsoft.Extensions.Configuration;
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

        // Credentials that must not go in the repo, for people who would rather
        // not fight environment variables.
        //
        // CreateApplicationBuilder loads appsettings.json and
        // appsettings.{Environment}.json and stops there, so this file -- which
        // EpicLoginConsole has been telling users to create -- was read by
        // nothing at all until this line existed.
        //
        // Anchored to BaseDirectory rather than the content root so that "beside
        // the executable" is literally true. The content root is the CURRENT
        // directory, so a config keyed to it would load or not load depending on
        // where the user happened to be standing when they typed dotnet run --
        // which is the same class of invisible, environment-dependent failure
        // this file exists to get away from.
        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"),
            optional: true,
            reloadOnChange: false);
        ConfigureServices(builder.Services);

        // Both flags mean "leave this database alone", so the scheduler has to
        // honour them too — otherwise rows appear fifteen minutes into UI work
        // against a fixed or seeded library.
        var writesSuppressed = args.Contains("--no-sync") || args.Contains("--seed-sample");
        builder.Services.Configure<SnapshotSchedulerOptions>(o => o.Enabled = !writesSuppressed);
        builder.Services.Configure<SessionWatcherOptions>(o => o.Enabled = !writesSuppressed);

        using var host = builder.Build();
        AppHost = host;
        Task enrichment = Task.CompletedTask;
        try
        {
            // Migrations run before ANY reader or writer touches the db —
            // including hosted services, which host.Start() launches. The
            // scheduler's first tick is an interval away so today's ordering
            // would survive either way, but "start the workers, then create
            // their tables" is a trap waiting for the first person who sets
            // RunOnStartup.
            host.Services.GetRequiredService<DatabaseInitializer>().Initialize();

            // The one-time interactive Epic sign-in. Deliberately BEFORE
            // host.Start() and before Avalonia: it is a terminal flow that ends
            // in an exit code, not a UI mode, and starting the scheduler or the
            // window underneath it would be noise. Needs the database only
            // because the encrypted session is stored in the settings table,
            // which is why it sits after Initialize().
            if (args.Contains(Services.EpicLoginConsole.Argument))
            {
                Environment.ExitCode = Services.EpicLoginConsole
                    .RunAsync(
                        host.Services,
                        Services.EpicLoginConsole.CodeFrom(args),
                        Shutdown.Token)
                    .GetAwaiter().GetResult();
                return;
            }

            // M4.6, and the minimal trigger for the embedded-browser sign-in.
            // Unlike --epic-login this NEEDS Avalonia, because the browser lives
            // in a window — so EpicSignInLauncher starts the window system by
            // hand rather than starting the app, and never opens the main window,
            // the sync, the scheduler or the session watcher. Same placement
            // reasoning as above: a terminal flow that ends in an exit code.
            if (args.Contains(Services.EpicSignInLauncher.Argument))
            {
                Environment.ExitCode = Services.EpicSignInLauncher
                    .Run(host.Services, BuildAvaloniaApp, Shutdown.Token);
                return;
            }

            host.Start();

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
                        // Genres, themes, game modes, store tags and Steam
                        // categories, for the filter panel. After enrichment
                        // because it reads the caches enrichment warms — on a
                        // warm library this is a pure database pass and touches
                        // the network not at all.
                        var facets = await services.GetRequiredService<FacetSyncService>()
                            .SyncAsync(Shutdown.Token);

                        await services.GetRequiredService<LibrarySoftMatchSweep>()
                            .SweepAsync(Shutdown.Token);

                        // §4.5's two signals. Staggered so a day costs tens of
                        // requests rather than the naive 1,232, and background
                        // only — never an onboarding path (§5.1, pitfall 3).
                        var poll = await services.GetRequiredService<UpdateSignalPoller>()
                            .PollDueBatchAsync(Shutdown.Token);

                        // Titles were rewritten underneath a library the UI has
                        // already loaded; without this they only appear on the
                        // next launch, which is not what §7's copy promises.
                        // New update events move bucket membership, so they need
                        // the same refresh — that is the unread badge appearing.
                        // MetadataFilled, not just Promoted: after the first run
                        // every title is already real, so a pass that back-fills
                        // years, publishers and summaries for hundreds of works
                        // promotes nothing — and the detail view would keep
                        // showing the gaps until the next launch.
                        if (report.Promoted > 0
                            || report.MetadataFilled > 0
                            || facets.RowsWritten > 0
                            || poll.AnnouncementsRecorded > 0
                            || poll.BuildPushesRecorded > 0)
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
        services.AddSingleton<IFacetRepository, FacetRepository>();
        services.AddSingleton<IResolveStateRepository, ResolveStateRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        // Ingest → Resolve → sync (§5.1: the UI reads the database; it never
        // calls these directly. Program composes them, the view models don't).
        services.AddSingleton<LibraryFoldersReader>();
        services.AddSingleton<AppManifestReader>();
        services.AddSingleton<LocalConfigReader>();
        services.AddSingleton<SteamAccountEnumerator>();
        services.AddSingleton<SteamLibrarySource>();

        // M4: the other two stores. Both are filesystem-only and answer
        // empty on a machine without that launcher, so neither adds a
        // failure mode to startup.
        services.AddEpicIngest();
        services.AddGogIngest();
        services.AddSingleton<ExternalIdResolver>();
        services.AddSingleton<SteamSyncService>();

        // M2 (§5 "Snapshot Scheduler", §8): keeps the longitudinal series
        // growing while the app sits in the tray, instead of recording one
        // point per launch. Same instance under both contracts — a separately
        // constructed SteamSyncService would be a second scanner.
        services.AddSingleton<ISteamSync>(sp => sp.GetRequiredService<SteamSyncService>());
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<SnapshotSchedulerService>();

        // M3 (§5.2 mechanism A): the process watcher — the first writer the
        // `sessions` table has ever had. Polls for game starts, takes an OS
        // exit callback for the ends, records one row per sitting. Nothing here
        // is on a user-facing path, and a machine with no games installed
        // resolves an empty executable index and opens no handles.
        services.AddSessionWatching();

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

        // MUST come after AddCoverCache(): CoverPipeline takes the first source
        // that answers, in registration order. Steam's 600x900 portrait capsule
        // is the design system's specified art (§11) and stays first; IGDB only
        // sees keys Steam declined. Registered before it, IGDB would silently
        // replace the art on ~520 tiles that were never in question.
        services.AddIgdbCoverSource();

        // Enrichment. IGDB is the designed backbone (§4.4) and wins conflicts;
        // the keyless Steam store endpoint is the fallback that keeps titles
        // resolving with no credentials set. Both soft-fail to "no data", and
        // neither may block a user-facing path (§5.1, pitfall 3).
        services.AddIgdbEnrichment();
        services.AddSteamStoreEnrichment();

        // §4.2. A second INGEST source, not a name fallback: localconfig.vdf
        // only records games that have been played, so the never-launched
        // library — 330 games on this machine — is invisible without it. Needs a
        // user-supplied key; unconfigured is a clean no-op.
        services.AddSteamWebApi();

        // The same idea for Epic, and the same clean no-op when unconfigured.
        // catcache.bin already gives the owned library locally, so this is NOT
        // how Epic ownership is discovered — it is the only route to two facts
        // Epic writes nowhere on disk: when a title was acquired, and per-game
        // playtime. It needs a user-supplied OAuth client pair AND a one-time
        // interactive sign-in, so the overwhelmingly common state is "registered
        // and idle". See docs/spikes/epic-oauth.md, including the risks the user
        // is accepting by turning it on.
        services.AddEpicWebApi();

        // M4.6 — the interactive sign-in, registration ONLY. Order here IS the
        // fallback order: prompts are consulted in registration order, so the
        // embedded browser goes first and the console is the peer that runs
        // where a window cannot (headless, no WebView2 runtime, or Epic having
        // broken the embedded page). Neither is a fallback in the sense of being
        // second-class — see ConsoleAuthPrompt.
        //
        // The Chromium profile lives beside the database rather than beside the
        // executable, because WebView2's default is the executable's own folder
        // and that is read-only for an installed app.
        services.AddWebViewAuthPrompt(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hoard",
            "WebView2"));
        services.AddSingleton<IInteractiveAuthPrompt, ConsoleAuthPrompt>();

        // The seam a "Sign in to Epic" command binds to. A view model must not
        // resolve EpicInteractiveSignIn or IEpicTokenProvider directly — that
        // would put Ingest in the view model's constructor and delete the §5.1
        // boundary.
        services.AddSingleton<EpicSignInService>();

        services.AddSingleton<EnrichmentSyncService>();
        services.AddSingleton<FacetSyncService>();

        // M2 (§4.5): the two update signals behind "Patched since". Both
        // endpoints are keyless, so there is no unconfigured state to handle.
        services.AddUpdateSignals();

        services.AddSingleton<LibraryViewModel>();

        // MainWindowViewModel takes MergeQueueViewModel as a required
        // dependency, so omitting this throws at startup rather than at build.
        services.AddMergeQueue();

        services.AddSingleton<MainWindowViewModel>();
    }
}
