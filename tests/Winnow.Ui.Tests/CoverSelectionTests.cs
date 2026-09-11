using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class CoverSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false, "plugin")]
    [InlineData(true, "plugin")]
    [InlineData(false, "unavailable")]
    [InlineData(true, "unavailable")]
    [InlineData(false, "user")]
    [InlineData(true, "user")]
    [InlineData(false, "igdb")]
    [InlineData(true, "igdb")]
    [InlineData(false, "steam")]
    [InlineData(true, "steam")]
    public async Task Production_library_and_merge_composition_select_the_same_art_on_both_surfaces(bool fullscreen, string kind)
    {
        using var database = new TempDatabase();
        var pluginKey = PluginArtRef.Key("fixture", "https://images.example.test/cover.jpg")!.Value;
        var cover = kind switch
        {
            "user" => UserArtRef.Format("fixtureuser"),
            "igdb" or "steam" => "https://images.igdb.com/igdb/image/upload/t_cover_big/co42.jpg",
            _ => PluginArtRef.Reference(pluginKey),
        };
        CoverKey? expected = kind switch
        {
            "unavailable" => null,
            "user" => CoverKey.User("fixtureuser"),
            "igdb" => CoverKey.Igdb("co42"),
            "steam" => CoverKey.Steam("42"),
            _ => pluginKey,
        };
        using (var connection = database.Factory.Open())
        {
            connection.Execute("""
                INSERT INTO works(id,name,sort_name,cover_url) VALUES (1,'Game','Game',@cover),(2,'Game edition','Game edition',NULL);
                INSERT INTO releases(id,work_id,name,platform) VALUES (1,1,'Game','windows'),(2,2,'Game edition','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES (1,1,'epic',0),(2,2,'epic',0);
                INSERT INTO merge_candidates(left_release_id,right_release_id,score,status) VALUES (1,2,0.95,'pending');
                """, new { cover });
            if (kind is "steam" or "user")
                connection.Execute("INSERT INTO external_ids(release_id,provider,provider_id) VALUES (1,'steam','42');");
        }
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(registrations, new(database.DatabasePath + "-data", database.DatabasePath, DataMigrationOutcome.Overridden));
        registrations.AddSingleton<ISqliteConnectionFactory>(database.Factory);
        var leases = new Leases();
        registrations.AddSingleton<ICoverLeases>(leases);
        await using var services = registrations.BuildServiceProvider();
        services.GetRequiredService<ArtworkPreferences>().ConfigureSources(kind == "unavailable" ? [] : [new("plugin:fixture", "Fixture")]);
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        var library = fullscreen ? context.Library : services.GetRequiredService<LibraryViewModel>();
        using var merge = fullscreen ? ActivatorUtilities.CreateInstance<MergeQueueViewModel>(services)
            : services.GetRequiredService<MergeQueueViewModel>();
        await library.LoadCommand.ExecuteAsync(null);
        await merge.LoadCommand.ExecuteAsync(null);
        var tile = library.TileForRelease(1)!;
        Assert.NotNull(tile);
        Assert.Equal(expected, tile.CoverKey);
        var row = Assert.Single(merge.Sections.SelectMany(section => section.Cards).SelectMany(card => card.Rows), row => row.WorkId == 1);
        Assert.Equal(expected, row.Side.CoverKey);
        Control view = fullscreen ? new FullscreenCover(tile) : new GameTileView { DataContext = tile };
        view.Width = 160; view.Height = 240;
        var window = new Window { Content = view, Width = 400, Height = 400 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (expected is { } key) Assert.Contains(key, leases.Requested);
            else Assert.Empty(leases.Requested);
        }
        finally
        {
            window.Close();
            var desktopFeed = services.GetRequiredService<FeedViewModel>();
            desktopFeed.Dispose(); context.Feed.Dispose();
            await (desktopFeed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await (context.Feed.LoadCommand.ExecutionTask ?? Task.CompletedTask);
            await desktopFeed.AdditionalShelvesLoading;
            await context.Feed.AdditionalShelvesLoading;
        }
    }

    private sealed class Leases : ICoverLeases
    {
        public List<CoverKey> Requested { get; } = [];
        public ICoverLease Acquire(CoverKey key, double width, CoverLayers layers = CoverLayers.VividAndFloor)
        { Requested.Add(key); return new Lease(key, CoverImaging.SnapWidth(width), layers); }
    }
    private sealed class Lease(CoverKey key, int width, CoverLayers layers) : ICoverLease
    {
        public CoverKey Key => key;
        public int Width => width;
        public CoverLayers Layers => layers;
        public bool TryGetArt(out CoverArt art) { art = null!; return false; }
        public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult<CoverArt?>(null);
        public void Dispose() { }
    }
}
