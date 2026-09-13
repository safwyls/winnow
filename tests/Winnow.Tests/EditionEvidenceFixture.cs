using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Stores;
using Winnow.Enrich.GamesDb;
using Winnow.Enrich.GamesDb.Model;

namespace Winnow.Tests;

public sealed class EditionEvidenceFixture : IDisposable
{
    public const string OfferId = "442f123b4d884d8ca85236aa30b99a79";
    public const string PageId = "78e2d1ca-9ff2-4179-95d4-f67c1acf3b76";
    public const string CatalogId = "7a70b499513441c792b541d53505e0b2";
    public const string Namespace = "41f47fd0d3e248bc938a5815d6d64daa";
    public const string Cms = """
        {"pages":[{"_id":"78e2d1ca-9ff2-4179-95d4-f67c1acf3b76","namespace":"41f47fd0d3e248bc938a5815d6d64daa",
        "item":{"catalogId":"7a70b499513441c792b541d53505e0b2","appName":"Bluebird","namespace":"41f47fd0d3e248bc938a5815d6d64daa","hasItem":true},
        "offer":{"id":"442f123b4d884d8ca85236aa30b99a79","namespace":"41f47fd0d3e248bc938a5815d6d64daa","hasOffer":true}}]}
        """;
    public TempDatabase Db { get; } = new();
    public SqliteMetadataCache Cache { get; }
    public WorkRepository Works { get; }
    public ReleaseRepository Releases { get; }
    public IdentityLinkRepository Links { get; }
    public OwnershipRepository Ownerships { get; }
    public MergeCandidateRepository Candidates { get; }
    public LibraryQueryRepository Queries { get; }
    public ReleaseEditionEvidenceRepository Evidence { get; }
    public EditionIgdb Igdb { get; }
    public StorefrontCache StoreCache { get; }
    public ReleaseEditionEvidenceAcquirer Acquirer { get; }
    public EnrichmentTarget Epic { get; private set; } = null!;
    public EnrichmentTarget Steam { get; private set; } = null!;
    public DateTime Now { get; } = DateTime.UtcNow;
    private readonly HttpClient _http;

    public EditionEvidenceFixture()
    {
        Cache = new(Db.Factory); Works = new(Db.Factory); Releases = new(Db.Factory); Links = new(Db.Factory);
        Ownerships = new(Db.Factory); Candidates = new(Db.Factory); Queries = new(Db.Factory); Evidence = new(Db.Factory);
        StoreCache = new(Db.Factory); Igdb = new(Cache, Now);
        _http = new(new RejectNetwork());
        Acquirer = new(Igdb, new IgdbOptions(), Cache, new(_http, StoreCache, TimeProvider.System), Evidence);
    }

    public static async Task<EditionEvidenceFixture> CreateAsync()
    {
        var fixture = new EditionEvidenceFixture();
        fixture.Epic = await fixture.AddAsync("epic", CatalogId, "Epic edition");
        fixture.Steam = await fixture.AddAsync("steam", "620", "Steam edition");
        await fixture.StoreCache.SaveAsync("epic", "{\"" + Namespace + "\":\"fez\"}", fixture.Now);
        await fixture.StoreCache.SaveAsync("epic-edition-v1:fez", Cms, fixture.Now);
        await fixture.Cache.SetAsync(SqliteEpicLaunchKeyStore.Provider, CatalogId,
            "{\"Namespace\":\"" + Namespace + "\",\"AppName\":\"Bluebird\"}", fixture.Now);
        fixture.Igdb.Answers[(1, "620")] = (IgdbEditionMatchStatus.Edition, 42);
        fixture.Igdb.Answers[(26, OfferId)] = (IgdbEditionMatchStatus.Edition, 42);
        return fixture;
    }

    public async Task<EnrichmentTarget> AddAsync(string provider, string providerId, string name)
    {
        var work = await Works.InsertAsync(new Work { Name = name, FirstReleaseYear = 2020 });
        var release = await Releases.InsertAsync(new Release { WorkId = work, Name = name });
        await Releases.AddExternalIdAsync(new() { ReleaseId = release, Provider = provider, ProviderId = providerId });
        await Ownerships.InsertAsync(new Ownership { ReleaseId = release, Store = provider });
        return new() { WorkId = work, ReleaseId = release, Provider = provider, ProviderId = providerId };
    }

    public Task<IReadOnlyDictionary<TargetKey, EditionAcquisition>> AcquireAsync()
        => Acquirer.AcquireAsync([Epic, Steam]);

    public GamesDbIdentitySyncService SyncService() => new(Releases, Links, Candidates, new WorkIgdbPinRepository(Db.Factory),
        new EnrichmentLookupPlanner(new IgdbOptions(), [new Aliases()], new Graph()),
        NullLogger<GamesDbIdentitySyncService>.Instance, Acquirer);

    public IdentityLinkRequest Request(IReadOnlyDictionary<TargetKey, EditionAcquisition> answers)
        => new() { ParentWorkId = Steam.WorkId, ChildWorkIds = [Epic.WorkId], Source = IdentityLinkSources.HardId,
            ExpectedEditionEvidence = [answers[new("epic", CatalogId)].Evidence!, answers[new("steam", "620")].Evidence!] };

    public void Dispose() { _http.Dispose(); Db.Dispose(); }

    public sealed class EditionIgdb(SqliteMetadataCache cache, DateTime now) : IIgdbClient
    {
        public Dictionary<(int Source, string Uid), (IgdbEditionMatchStatus Status, long? Game)> Answers { get; } = new();
        public List<(int Source, string Uid)> Requested { get; } = [];
        public HashSet<(int Source, string Uid)> Unavailable { get; } = [];
        public async Task<IReadOnlyDictionary<string, IgdbEditionMatch>> ResolveEditionsByExternalIdsAsync(
            int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var result = new Dictionary<string, IgdbEditionMatch>();
            foreach (var uid in uids.Distinct())
            {
                Requested.Add((externalGameSourceId, uid));
                if (Unavailable.Contains((externalGameSourceId, uid))) continue;
                var (status, game) = Answers.GetValueOrDefault((externalGameSourceId, uid), (IgdbEditionMatchStatus.Missing, null));
                var key = externalGameSourceId + ":" + uid;
                var payload = "{\"status\":\"" + status + "\",\"game\":" + (game?.ToString() ?? "null") + "}";
                await cache.SetAsync("edition-fixture", key, payload, now, ct);
                result[uid] = new(uid, status, game, status == IgdbEditionMatchStatus.Edition ? 1 : null,
                    status == IgdbEditionMatchStatus.Edition ? "Gold Edition" : null,
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
}
