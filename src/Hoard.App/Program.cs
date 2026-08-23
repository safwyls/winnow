using Avalonia;
using Hoard.App.Services;
using Hoard.App.ViewModels;
using Hoard.Core.Repositories;
using Hoard.Data;
using Hoard.Data.Repositories;
using Hoard.Ingest.Steam;
using Hoard.Resolve;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<MainWindowViewModel>();
    }
}
