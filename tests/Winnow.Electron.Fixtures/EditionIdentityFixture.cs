using System.Net;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Stores;

namespace Winnow.Electron.Fixtures;

// The frozen EditionEvidenceFixture's external substitutes feed the production acquirer and sync service.
// The database, evidence validation, identity history and frontend API remain production implementations.
internal sealed class EditionIdentityFixture : IDisposable
{
    private const string OfferId = "442f123b4d884d8ca85236aa30b99a79";
    private const string CatalogId = "7a70b499513441c792b541d53505e0b2";
    private const string Namespace = "41f47fd0d3e248bc938a5815d6d64daa";
    private const string Cms = """
        {"pages":[{"_id":"78e2d1ca-9ff2-4179-95d4-f67c1acf3b76","namespace":"41f47fd0d3e248bc938a5815d6d64daa",
        "item":{"catalogId":"7a70b499513441c792b541d53505e0b2","appName":"Bluebird","namespace":"41f47fd0d3e248bc938a5815d6d64daa","hasItem":true},
        "offer":{"id":"442f123b4d884d8ca85236aa30b99a79","namespace":"41f47fd0d3e248bc938a5815d6d64daa","hasOffer":true}}]}
        """;
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;
    private readonly MergeCandidateRepository _candidates;
    private readonly IdentityLinkRepository _links;
    private readonly SqliteMetadataCache _cache;
    private readonly StorefrontCache _storeCache;
    private readonly FixtureIgdb _igdb;
    private readonly GamesDbIdentitySyncService _sync;
    private readonly LibraryChangePublisher _publisher;
    private readonly HttpClient _http = new(new RejectNetwork());
    private readonly DateTime _now = DateTime.UtcNow;

    public EditionIdentityFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher,
        ILogger<GamesDbIdentitySyncService> logger)
    {
        _works = new(database); _releases = new(database); _ownerships = new(database);
        _candidates = new(database); _links = new(database); _cache = new(database); _storeCache = new(database);
        _igdb = new(_cache, _now); _publisher = publisher;
        var acquirer = new ReleaseEditionEvidenceAcquirer(_igdb, new IgdbOptions(), _cache,
            new(_http, _storeCache, TimeProvider.System), new ReleaseEditionEvidenceRepository(database));
        _sync = new(_releases, _links, _candidates, new WorkIgdbPinRepository(database),
            new EnrichmentLookupPlanner(new IgdbOptions(), [new Aliases()], new Graph()), logger, acquirer);
    }

    public async Task SeedAsync()
    {
        if ((await _releases.GetAllExternalIdsAsync()).Count != 0)
            throw new InvalidOperationException("Edition evidence requires an empty fixture library.");
        var epic = await AddAsync("epic", CatalogId, "Epic edition");
        var steam = await AddAsync("steam", "620", "Steam edition");
        await _candidates.InsertAsync(new MergeCandidate { LeftReleaseId = epic, RightReleaseId = steam, Score = .9 });
        await _storeCache.SaveAsync("epic", "{\"" + Namespace + "\":\"fez\"}", _now);
        await _storeCache.SaveAsync("epic-edition-v1:fez", Cms, _now);
        await _cache.SetAsync(SqliteEpicLaunchKeyStore.Provider, CatalogId,
            "{\"Namespace\":\"" + Namespace + "\",\"AppName\":\"Bluebird\"}", _now);
        await _publisher.PublishAsync(default);
    }

    private async Task<long> AddAsync(string store, string providerId, string name)
    {
        var work = await _works.InsertAsync(new Work { Name = name, FirstReleaseYear = 2020 });
        var release = await _releases.InsertAsync(new Release { WorkId = work, Name = name });
        await _releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = store, ProviderId = providerId });
        await _ownerships.InsertAsync(new Ownership { ReleaseId = release, Store = store });
        return release;
    }

    public async Task<object> SyncAsync()
    {
        var linked = await _sync.SyncAsync();
        await _publisher.PublishAsync(default);
        return new { Linked = linked, Result = _sync.LastResult, State = await SnapshotAsync() };
    }

    public async Task<object> SnapshotAsync() => new
    {
        Pending = await _candidates.CountPendingAsync(),
        ExternalIds = await _releases.GetAllExternalIdsAsync(),
        History = await _links.GetHistoryAsync(),
        Acts = await _links.GetActsAsync(),
        Requested = _igdb.Requested.Select(request => new { request.Source, request.Uid }).ToArray(),
    };

    public void Dispose() => _http.Dispose();

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class Aliases : IStoreArtifactAliasSource
    {
        public ValueTask<IReadOnlyDictionary<string, string>> GetAliasesAsync(string provider, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string> { [CatalogId] = "Bluebird" });
    }

    private sealed class Graph : IGameIdentityGraph
    {
        public Task<GamesDbGame?> ResolveAsync(string platform, string externalId, CancellationToken ct = default)
            => Task.FromResult<GamesDbGame?>(new(platform, externalId, "fixture-game", [new("steam", "620")]));
    }

    private sealed class FixtureIgdb(SqliteMetadataCache cache, DateTime now) : IIgdbClient
    {
        public List<(int Source, string Uid)> Requested { get; } = [];
        public async Task<IReadOnlyDictionary<string, IgdbEditionMatch>> ResolveEditionsByExternalIdsAsync(
            int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var result = new Dictionary<string, IgdbEditionMatch>();
            foreach (var uid in uids.Distinct())
            {
                Requested.Add((externalGameSourceId, uid));
                var knownEdition = (externalGameSourceId, uid) is (1, "620") or (26, OfferId);
                var status = knownEdition ? IgdbEditionMatchStatus.Edition : IgdbEditionMatchStatus.Missing;
                var key = externalGameSourceId + ":" + uid;
                var payload = "{\"status\":\"" + status + "\",\"game\":" + (knownEdition ? "42" : "null") + "}";
                await cache.SetAsync("edition-fixture", key, payload, now, ct);
                result[uid] = new(uid, status, knownEdition ? 42 : null, knownEdition ? 1 : null, knownEdition ? "Gold Edition" : null,
                    CachedEvidenceSource.FromPayload("edition-fixture", key, payload), now.AddDays(30));
            }
            return result;
        }
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
