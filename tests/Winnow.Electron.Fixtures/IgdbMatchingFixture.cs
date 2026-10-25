using System.Net;
using Dapper;
using Microsoft.Extensions.Http;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Stores;

namespace Winnow.Electron.Fixtures;

// Only external IGDB responses and the source test's deliberate link refusal are substituted.
// Assignment, mapping isolation, pin precedence, API snapshots and publication remain production code.
internal sealed class IgdbMatchingFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher,
    MatchingIgdbClient igdb)
{
    internal const string WrongCover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg";
    internal const string RightCover = "https://images.igdb.com/igdb/image/upload/t_cover_big/co2abc.jpg";
    internal const string CatalogId = "7a70b499513441c792b541d53505e0b2";
    internal const string OfferId = "442f123b4d884d8ca85236aa30b99a79";
    internal const string PageId = "78e2d1ca-9ff2-4179-95d4-f67c1acf3b76";
    internal const string Namespace = "41f47fd0d3e248bc938a5815d6d64daa";
    private static readonly DateTime Observed = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task<object> SeedAsync(string kind)
    {
        using (var check = database.Open())
            if (check.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Matching fixture requires an empty library.");
        igdb.NoCover = kind == "no-cover";
        igdb.Geometry = kind == "geometry";
        long selected;
        if (kind == "pipeline")
        {
            await SeedPipelineAsync();
            selected = 1;
        }
        else if (kind == "mapping")
        {
            var entry = await new ManualEntryRepository(database).CreateAsync(new() { Title = "Old game", IgdbId = 111 });
            selected = entry.WorkId;
            await new FacetRepository(database).SetWorkFacetsAsync(selected, [new(FacetKinds.Genre, "Old genre")]);
            await new WorkMaturityRepository(database).UpsertAsync(new() { WorkId = selected, Source = "igdb", Ratings = "esrb:ao", ObservedAt = Observed });
            await new WorkImageRepository(database).UpsertAsync(new() { WorkId = selected, Source = "igdb", Kind = ImageKinds.Screenshot, ImageIds = "oldshot", ObservedAt = Observed });
            await new WorkRatingRepository(database).UpsertAsync(new() { WorkId = selected, Source = RatingSources.IgdbUsers, Score = 95, RatingCount = 20, ObservedAt = Observed });
            using var seed = database.Open();
            seed.Execute("UPDATE works SET background_url='https://example.test/old.jpg' WHERE id=@selected; INSERT INTO work_field_sources(work_id,field,source,set_at) VALUES(@selected,'background_url','igdb','2026-09-11');", new { selected });
        }
        else
        {
            if (kind == "claim") await AddPreyAsync(5678, 2017, RightCover, false);
            selected = await AddPreyAsync(1234, 2006, WrongCover, kind is "steam" or "no-cover" or "claim");
        }
        await publisher.PublishAsync(default);
        return new { WorkId = selected };
    }

    private async Task<long> AddPreyAsync(long id, int year, string cover, bool steam)
    {
        var work = await new WorkRepository(database).InsertAsync(new Work
        {
            Name = "Prey", IgdbId = id, FirstReleaseYear = year, Publisher = "2K Games",
            Summary = "A Cherokee garage mechanic is abducted.", CoverUrl = cover,
        });
        var releases = new ReleaseRepository(database);
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Prey", Platform = "windows" });
        if (steam) await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "3900" });
        var store = steam ? "steam" : "gog";
        var ownership = await new OwnershipRepository(database).InsertAsync(new Ownership { ReleaseId = release, Store = store });
        await new PlayRecordRepository(database).InsertAsync(new PlayRecord
        {
            OwnershipId = ownership, PlaytimeMinutes = 120, LastPlayedAt = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Source = store, ObservedAt = Observed,
        });
        return work;
    }

    private async Task SeedPipelineAsync()
    {
        // Exact LibraryReadFixtures.Seed(2), followed by the source composition test's identity edits.
        using (var seed = database.Open()) seed.Execute("""
            INSERT INTO works(id,name,sort_name) VALUES(1,'Shared game','Shared game'),(2,'Shared game','Shared game');
            INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Shared game','windows'),(2,2,'Shared game','windows');
            INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'epic',0),(2,2,'steam',0);
            INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'epic',@CatalogId),(2,'steam','2');
            INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
            INSERT INTO list_items(list_id,release_id,position) VALUES(1,2,0),(1,1,1);
            INSERT INTO merge_candidates(left_release_id,right_release_id,score,status) VALUES(1,2,0.9,'pending');
            """, new { CatalogId });
        var now = DateTime.UtcNow;
        var storefront = new StorefrontCache(database);
        await storefront.SaveAsync("epic", "{\"" + Namespace + "\":\"fez\"}", now);
        await storefront.SaveAsync("epic-edition-v1:fez", """
            {"pages":[{"_id":"78e2d1ca-9ff2-4179-95d4-f67c1acf3b76","namespace":"41f47fd0d3e248bc938a5815d6d64daa",
            "item":{"catalogId":"7a70b499513441c792b541d53505e0b2","appName":"Bluebird","namespace":"41f47fd0d3e248bc938a5815d6d64daa","hasItem":true},
            "offer":{"id":"442f123b4d884d8ca85236aa30b99a79","namespace":"41f47fd0d3e248bc938a5815d6d64daa","hasOffer":true}}]}
            """, now);
        var metadata = new SqliteMetadataCache(database);
        await metadata.SetAsync(SqliteEpicLaunchKeyStore.Provider, CatalogId,
            "{\"Namespace\":\"" + Namespace + "\",\"AppName\":\"Bluebird\"}", now);
        foreach (var (source, uid, edition) in new[] { (1, "2", true), (26, OfferId, true), (26, PageId, false) })
        {
            var fields = edition ? "\"status\":3,\"game_id\":42,\"parent_id\":1,\"title\":\"Gold Edition\""
                : "\"status\":0,\"game_id\":null,\"parent_id\":null,\"title\":null";
            await metadata.SetAsync(IgdbClient.EditionCacheProvider, IgdbClient.EditionCacheKey(source, uid),
                "{\"version\":1,\"source_id\":" + source + ",\"uid\":\"" + uid + "\"," + fields + "}", now);
        }
    }

    public async Task PublishAsync() => await publisher.PublishAsync(default);
    public object Snapshot()
    {
        using var read = database.Open();
        var tables = new[] { "works", "releases", "ownerships", "external_ids", "work_igdb_pins", "identity_links", "work_facets", "work_maturity", "work_images", "work_ratings", "release_edition_evidence" };
        return new { Rows = tables.ToDictionary(table => table, table => read.Query("SELECT * FROM " + table + " ORDER BY rowid").ToArray()),
            Pending = read.ExecuteScalar<int>("SELECT COUNT(*) FROM merge_candidates WHERE status='pending'"), Searches = igdb.Searches.ToArray() };
    }
}

internal sealed class MatchingIgdbClient : IIgdbClient
{
    public bool NoCover { get; set; }
    public bool Geometry { get; set; }
    public List<string> Searches { get; } = [];
    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
    public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        Searches.Add(title);
        IReadOnlyList<IgdbSearchResult> results = Geometry
            ? [new(5678, "Prey", IgdbMatchingFixture.RightCover, 2017, ["PC (Microsoft Windows)", "PlayStation 4", "Xbox One", "Nintendo Switch", "Macintosh", "Linux", "PlayStation 5", "Xbox Series X|S"]), new(5679, "Prey", IgdbMatchingFixture.WrongCover, 2006, ["Xbox 360"])]
            : [new(5678, "Prey", NoCover ? null : IgdbMatchingFixture.RightCover, 2017, ["PC (Microsoft Windows)", "PlayStation 4"])];
        return Task.FromResult(results);
    }
    public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IgdbGame>>(igdbIds.Select(id => id == 222
            ? new IgdbGame(222, "Corrected game", null, 2020, "Corrected summary", [], [], [])
            : new IgdbGame(id, "Prey", NoCover ? null : IgdbMatchingFixture.RightCover, 2017, "Morgan Yu wakes on Talos I.", [], [], ["Bethesda Softworks"]) { Platforms = ["PC (Microsoft Windows)", "PlayStation 4"] }).ToArray());
    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class MatchingAliases : IStoreArtifactAliasSource
{
    public ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(string provider, CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { [IgdbMatchingFixture.CatalogId] = "Bluebird" });
}
internal sealed class MatchingGraph : IGameIdentityGraph
{
    public Task<GamesDbGame?> ResolveAsync(string platform, string externalId, CancellationToken ct = default)
        => Task.FromResult<GamesDbGame?>(new(platform, externalId, "game", [new(GamesDbPlatforms.Steam, "2")]));
}
internal sealed class MatchingOfflineHttp : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        builder.PrimaryHandler = new OfflineHandler();
    };
    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
    }
}

internal sealed class MatchingRefusingLinks(ISqliteConnectionFactory database) : IIdentityLinkRepository
{
    private readonly IdentityLinkRepository _inner = new(database);
    public Task<IdentityResolution> GetResolutionAsync(CancellationToken ct = default) => _inner.GetResolutionAsync(ct);
    public Task<long> LinkAsync(IdentityLinkRequest request, CancellationToken ct = default)
        => throw new IdentityLinkRefusedException(IdentityLinkRefusal.ParentIsAlreadyAChild, "The chosen parent is already a child.");
    public Task<bool> RetractActAsync(long actId, string? note = null, CancellationToken ct = default) => _inner.RetractActAsync(actId, note, ct);
    public Task<bool> RetractLinkAsync(long childWorkId, string? note = null, CancellationToken ct = default) => _inner.RetractLinkAsync(childWorkId, note, ct);
    public Task<IReadOnlyList<IdentityLink>> GetHistoryAsync(long? workId = null, CancellationToken ct = default) => _inner.GetHistoryAsync(workId, ct);
    public Task<IReadOnlyList<IdentityAct>> GetActsAsync(CancellationToken ct = default) => _inner.GetActsAsync(ct);
}
