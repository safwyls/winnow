using System.Net;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Winnow.App;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Stores;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GamesDbRefreshCompositionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_refresh_pipeline_publishes_qualified_identity_links_on_both_surfaces(bool fullscreen)
    {
        await using var fixture = new DatabaseFixture();
        var db = fixture.Database;
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open())
        {
            connection.Execute("""
                UPDATE works SET name='Shared game',sort_name='Shared game';
                UPDATE releases SET name='Shared game';
                UPDATE external_ids SET provider='epic',provider_id=@catalogId WHERE release_id=1;
                UPDATE ownerships SET store='epic' WHERE release_id=1;
                INSERT INTO merge_candidates(left_release_id,right_release_id,score,status) VALUES(1,2,0.9,'pending');
                """, new { catalogId = EditionEvidenceFixture.CatalogId });
        }
        var observed = DateTime.UtcNow;
        var storefront = new StorefrontCache(db.Factory);
        await storefront.SaveAsync("epic", "{\"" + EditionEvidenceFixture.Namespace + "\":\"fez\"}", observed);
        await storefront.SaveAsync("epic-edition-v1:fez", EditionEvidenceFixture.Cms, observed);
        var metadata = new SqliteMetadataCache(db.Factory);
        await metadata.SetAsync(SqliteEpicLaunchKeyStore.Provider, EditionEvidenceFixture.CatalogId,
            "{\"Namespace\":\"" + EditionEvidenceFixture.Namespace + "\",\"AppName\":\"Bluebird\"}", observed);
        await SeedEdition(1, "2", edition: true);
        await SeedEdition(26, EditionEvidenceFixture.OfferId, edition: true);
        await SeedEdition(26, EditionEvidenceFixture.PageId, edition: false);
        async Task SeedEdition(int source, string uid, bool edition)
        {
            var fields = edition ? "\"status\":3,\"game_id\":42,\"parent_id\":1,\"title\":\"Gold Edition\""
                : "\"status\":0,\"game_id\":null,\"parent_id\":null,\"title\":null";
            await metadata.SetAsync(IgdbClient.EditionCacheProvider, IgdbClient.EditionCacheKey(source, uid),
                "{\"version\":1,\"source_id\":" + source + ",\"uid\":\"" + uid + "\"," + fields + "}", observed);
        }
        var registrations = new ServiceCollection();
        registrations.AddLogging();
        registrations.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Program.ConfigureServices(registrations, new(db.DatabasePath + "-data", db.DatabasePath, DataMigrationOutcome.Overridden));
        registrations.AddSingleton<ISqliteConnectionFactory>(db.Factory);
        registrations.RemoveAll<IStoreArtifactAliasSource>();
        registrations.AddSingleton<IStoreArtifactAliasSource>(new Aliases());
        registrations.AddSingleton<IGameIdentityGraph>(new Graph());
        registrations.AddSingleton<IHttpMessageHandlerBuilderFilter>(new OfflineHttp());
        await using var services = registrations.BuildServiceProvider();
        var desktop = services.GetRequiredService<LibraryViewModel>();
        using var context = FullscreenContext.Create(services, services.GetRequiredService<MainWindowViewModel>());
        context.SetActive(fullscreen);
        await desktop.LoadCommand.ExecuteAsync(null);
        await context.LoadAsync();
        Assert.Equal(2, desktop.AllTiles.Count);
        Assert.IsType<OwnershipRefreshCoordinator>(services.GetRequiredService<IRemoteOwnershipSync>());

        await services.GetRequiredService<LibraryRefreshPipeline>().RunAsync();

        Assert.Single(desktop.AllTiles);
        if (!fullscreen) context.SetActive(true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (context.Library.AllTiles.Count != 1)
        {
            await Task.Delay(10, timeout.Token);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Single(await services.GetRequiredService<IIdentityLinkRepository>().GetHistoryAsync(), link => link.IsLive);
        using (var connection = db.Factory.Open())
        {
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ownerships"));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM external_ids"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM merge_candidates WHERE status='pending'"));
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM release_edition_evidence"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM releases WHERE igdb_version_id IS NOT NULL"));
        }
    }

    private sealed class Aliases : IStoreArtifactAliasSource
    {
        public ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(string provider, CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { [EditionEvidenceFixture.CatalogId] = "Bluebird" });
    }

    private sealed class Graph : IGameIdentityGraph
    {
        public Task<GamesDbGame?> ResolveAsync(string platform, string externalId, CancellationToken ct = default) =>
            Task.FromResult<GamesDbGame?>(new(platform, externalId, "game", [new(GamesDbPlatforms.Steam, "2")]));
    }

    private sealed class OfflineHttp : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = new NotFoundHandler();
        };
    }

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        public TempDatabase Database { get; } = new();
        public async ValueTask DisposeAsync()
        {
            for (var attempt = 0; ; attempt++)
            {
                try { Database.Dispose(); return; }
                catch (IOException) when (attempt < 50) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            }
        }
    }
}
