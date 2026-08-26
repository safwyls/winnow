using Hoard.App.Services;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Data.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Storage;
using Hoard.Enrich.Steam;
using Hoard.Enrich.Steam.Storage;
using Hoard.Tests.Igdb;
using Hoard.Tests.SteamStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The pass that turns metadata Hoard already fetched into rows a filter can
/// query.
///
/// <para><b>Every test here runs against real clients with no working
/// network.</b> Both hosts are built by the production DI extensions and given a
/// transport that fails every request the way a disconnected machine does — so
/// what the pass reads is <c>metadata_cache</c>, seeded with the exact payload
/// shapes the two clients write: the projected <c>IgdbGame</c> record, and the
/// verbatim store item body captured in tests/fixtures/steam-store/.</para>
///
/// <para>The transport fails rather than throws on purpose. A test double that
/// threw something the client does not catch would pass for the wrong reason —
/// it would prove the client crashes, not that the pass stayed off the wire. So
/// the requests are counted instead, and
/// <see cref="SyncHost.AssertNoRequestsMade"/> is the actual assertion.</para>
///
/// <para>That is the claim migration 0005 made and 0007 collects on — the data
/// was kept so a later feature could build its table "without spending a single
/// request".</para>
/// </summary>
public sealed class FacetSyncServiceTests : IDisposable
{
    /// <summary>The clock both hosts start at; cached entries are stamped with it so nothing looks stale.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly FacetRepository _facets;
    private readonly LibraryQueryRepository _libraryQueries;

    public FacetSyncServiceTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _facets = new FacetRepository(_db.Factory);
        _libraryQueries = new LibraryQueryRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Reads_genres_and_themes_out_of_the_cache_with_no_network()
    {
        await SeedAsync(igdbId: 1, appId: null, name: "Thief II: The Metal Age");

        using var host = new SyncHost(_db, Now);
        host.CacheIgdbGame(1, """
            {
              "igdb_id": 1,
              "name": "Thief II: The Metal Age",
              "genres": ["Shooter", "Simulator", "Adventure"],
              "themes": ["Action", "Fantasy", "Stealth"]
            }
            """);

        var report = await host.Service(_libraryQueries, _facets).SyncAsync();

        host.AssertNoRequestsMade();
        Assert.Equal(1, report.IgdbGamesRead);
        Assert.Equal(1, report.WorksWritten);

        var snapshot = await _facets.GetSnapshotAsync();
        Assert.Equal(
            ["Adventure", "Shooter", "Simulator"],
            Names(snapshot, FacetKinds.Genre));
        Assert.Equal(
            ["Action", "Fantasy", "Stealth"],
            Names(snapshot, FacetKinds.Theme));
    }

    /// <summary>
    /// The 865 IGDB entries already on the author's disk were written before
    /// <c>game_modes</c> was requested, so they carry none. They must still
    /// deserialize and still yield their genres — the alternative was a cache
    /// shape change that would have made every one of them unreadable on a
    /// machine with no Twitch credentials.
    /// </summary>
    [Fact]
    public async Task An_older_cached_payload_without_game_modes_still_yields_its_genres()
    {
        await SeedAsync(igdbId: 1, appId: null, name: "Thief II");

        using var host = new SyncHost(_db, Now);
        host.CacheIgdbGame(1, """{"igdb_id": 1, "name": "Thief II", "genres": ["Adventure"]}""");

        await host.Service(_libraryQueries, _facets).SyncAsync();

        var snapshot = await _facets.GetSnapshotAsync();
        Assert.Equal(["Adventure"], Names(snapshot, FacetKinds.Genre));
        Assert.Empty(snapshot.Releases.Single().GameModes);
    }

    [Fact]
    public async Task Reads_game_modes_and_perspectives_from_a_newer_cached_payload()
    {
        await SeedAsync(igdbId: 1, appId: null, name: "Portal 2");

        using var host = new SyncHost(_db, Now);
        host.CacheIgdbGame(1, """
            {
              "igdb_id": 1,
              "name": "Portal 2",
              "game_modes": ["Single player", "Co-operative", "Multiplayer"],
              "player_perspectives": ["First person"]
            }
            """);

        await host.Service(_libraryQueries, _facets).SyncAsync();

        var snapshot = await _facets.GetSnapshotAsync();
        Assert.Equal(
            [GameModes.CoOperative, GameModes.Multiplayer, GameModes.SinglePlayer],
            snapshot.Releases.Single().GameModes.Order(StringComparer.Ordinal));
        Assert.Equal(["First person"], Names(snapshot, FacetKinds.PlayerPerspective));
    }

    /// <summary>
    /// The Steam half, against the bytes captured live on 2026-08-23 — two days
    /// before anything read <c>categories</c>. Tags keep their rank; player
    /// categories become game modes; feature and controller categories become
    /// their own kinds.
    /// </summary>
    [Fact]
    public async Task Reads_tags_and_categories_out_of_the_captured_store_body()
    {
        await SeedAsync(igdbId: null, appId: StoreFixtures.EldenRingAppId, name: "ELDEN RING");

        using var host = new SyncHost(_db, Now);
        host.CacheStoreItem(StoreFixtures.EldenRingAppId);
        host.CacheStoreVocabularies();

        var report = await host.Service(_libraryQueries, _facets).SyncAsync();

        host.AssertNoRequestsMade();
        Assert.Equal(1, report.SteamItemsRead);
        Assert.Equal(1, report.ReleasesWritten);

        var snapshot = await _facets.GetSnapshotAsync();
        var tags = snapshot.Releases.Single().FacetIds
            .Select(id => snapshot.ById[id])
            .Where(f => f.Kind == FacetKinds.Tag)
            .Select(f => f.Name)
            .ToArray();

        Assert.Equal(20, tags.Length);
        Assert.Equal("Souls-like", tags[0]);

        Assert.Contains("Steam Achievements", Names(snapshot, FacetKinds.Feature));
        Assert.Contains("Full controller support", Names(snapshot, FacetKinds.Controller));

        // supported_player_categoryids [2, 1, 49, 36, 9, 38]:
        // single-player, multi-player, PvP, online PvP, co-op, online co-op.
        Assert.Equal(
            [GameModes.CoOperative, GameModes.Multiplayer, GameModes.SinglePlayer],
            snapshot.Releases.Single().GameModes.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Both halves at once, on one release: IGDB writes the work layer, Steam
    /// writes the release layer, and the reader unions them — §6's identity model
    /// preserved in storage and collapsed only on read.
    /// </summary>
    [Fact]
    public async Task Both_sources_land_at_their_own_layer()
    {
        var (workId, releaseId) = await SeedAsync(
            igdbId: 1, appId: StoreFixtures.EldenRingAppId, name: "ELDEN RING");

        using var host = new SyncHost(_db, Now);
        host.CacheIgdbGame(1, """{"igdb_id": 1, "name": "ELDEN RING", "genres": ["Role-playing (RPG)"]}""");
        host.CacheStoreItem(StoreFixtures.EldenRingAppId);
        host.CacheStoreVocabularies();

        await host.Service(_libraryQueries, _facets).SyncAsync();

        var snapshot = await _facets.GetSnapshotAsync();
        var facets = snapshot.ByRelease[releaseId].FacetIds.Select(id => snapshot.ById[id]).ToArray();

        Assert.Contains(facets, f => f is { Kind: FacetKinds.Genre, Name: "Role-playing (RPG)" });
        Assert.Contains(facets, f => f is { Kind: FacetKinds.Tag, Name: "Souls-like" });

        // The genre is stored against the work, not the release.
        using var lease = _db.Factory.Lease();
        Assert.Equal(1, await Dapper.SqlMapper.ExecuteScalarAsync<int>(
            lease.Connection,
            "SELECT COUNT(*) FROM work_facets WHERE work_id = @workId;",
            new { workId },
            lease.Transaction));
    }

    /// <summary>
    /// The idempotence claim, stated the way the service reports it: a second
    /// run writes zero rows. Nothing is re-fetched because the clients read the
    /// cache first, and nothing is re-stored because the repository compares
    /// before it writes.
    /// </summary>
    [Fact]
    public async Task A_second_run_writes_nothing()
    {
        await SeedAsync(igdbId: 1, appId: StoreFixtures.EldenRingAppId, name: "ELDEN RING");

        using var host = new SyncHost(_db, Now);
        host.CacheIgdbGame(1, """{"igdb_id": 1, "name": "ELDEN RING", "genres": ["Role-playing (RPG)"]}""");
        host.CacheStoreItem(StoreFixtures.EldenRingAppId);
        host.CacheStoreVocabularies();

        var service = host.Service(_libraryQueries, _facets);

        var first = await service.SyncAsync();
        Assert.True(first.RowsWritten > 0);

        var second = await service.SyncAsync();
        host.AssertNoRequestsMade();
        Assert.Equal(0, second.RowsWritten);
        Assert.Equal(0, second.WorksWritten);
        Assert.Equal(0, second.ReleasesWritten);

        // And the data is still there — "wrote nothing" is not "cleared it".
        Assert.NotEmpty((await _facets.GetSnapshotAsync()).Releases);
    }

    /// <summary>
    /// A work nothing has ever been cached for contributes no facets, and — the
    /// part that matters — is not disturbed in any way. Facets describe the
    /// library; they never decide what is in it.
    /// </summary>
    [Fact]
    public async Task A_work_with_no_cached_metadata_contributes_nothing_and_is_not_lost()
    {
        var (_, described) = await SeedAsync(igdbId: 1, appId: null, name: "Thief II");
        var (_, undescribed) = await SeedAsync(igdbId: 2, appId: "999999", name: "App 999999");

        using var host = new SyncHost(_db, Now);
        host.CacheIgdbGame(1, """{"igdb_id": 1, "name": "Thief II", "genres": ["Adventure"]}""");
        host.CacheStoreVocabularies();

        await host.Service(_libraryQueries, _facets).SyncAsync();

        var snapshot = await _facets.GetSnapshotAsync();
        Assert.Contains(described, snapshot.ByRelease.Keys);
        Assert.DoesNotContain(undescribed, snapshot.ByRelease.Keys);

        // The one appid nothing is cached for IS asked about — "genuinely
        // absent" is the only case allowed to reach the network — and the
        // request failing changes nothing about the outcome. (More than one
        // attempt: the resilience handler retries a dead socket, which is its
        // job. What matters is that no OTHER appid was asked about.)
        Assert.NotEmpty(host.SteamRequests);
        Assert.All(host.SteamRequests, r => Assert.Equal(["999999"], r.RequestedAppIds));

        // Still in the library, still named, still a release.
        Assert.Equal(2, (await _works.GetAllAsync()).Count);
        Assert.NotNull(await _releases.GetAsync(undescribed));
        Assert.Equal(2, (await _libraryQueries.GetFacetTargetsAsync()).Count);
    }

    /// <summary>
    /// Without a vocabulary the ids cannot be named, and a release write replaces
    /// that release's whole set — so writing anyway would DELETE the tags a
    /// previous run stored. The pass leaves the Steam half alone instead.
    /// </summary>
    [Fact]
    public async Task Without_a_store_vocabulary_the_steam_half_is_left_untouched()
    {
        var (_, releaseId) = await SeedAsync(
            igdbId: null, appId: StoreFixtures.EldenRingAppId, name: "ELDEN RING");

        using var warm = new SyncHost(_db, Now);
        warm.CacheStoreItem(StoreFixtures.EldenRingAppId);
        warm.CacheStoreVocabularies();
        await warm.Service(_libraryQueries, _facets).SyncAsync();

        var before = (await _facets.GetSnapshotAsync()).ByRelease[releaseId].FacetIds.Count;
        Assert.True(before > 0);

        // A second host with the item cached but no vocabulary at all — a machine
        // that has never reached the store.
        using var cold = new SyncHost(_db, Now);
        cold.CacheStoreItem(StoreFixtures.EldenRingAppId);

        var report = await cold.Service(_libraryQueries, _facets).SyncAsync();

        Assert.Equal(0, report.ReleasesWritten);
        Assert.Equal(before, (await _facets.GetSnapshotAsync()).ByRelease[releaseId].FacetIds.Count);
    }

    [Fact]
    public async Task An_empty_library_is_a_no_op()
    {
        using var host = new SyncHost(_db, Now);

        Assert.Equal(FacetSyncReport.Empty, await host.Service(_libraryQueries, _facets).SyncAsync());
    }

    private static string[] Names(FacetSnapshot snapshot, string kind)
        => snapshot.Facets
            .Where(f => f.Kind == kind)
            .Select(f => f.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private async Task<(long WorkId, long ReleaseId)> SeedAsync(long? igdbId, string? appId, string name)
    {
        var workId = await _works.InsertAsync(new Work { Name = name, IgdbId = igdbId });
        var releaseId = await _releases.InsertAsync(new Release { WorkId = workId, Name = name });

        if (appId is not null)
        {
            await _releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = appId,
            });
        }

        return (workId, releaseId);
    }

    /// <summary>
    /// Real clients, real DI wiring, real SQLite caches — and a transport that
    /// throws. Anything this host produces came off disk.
    /// </summary>
    private sealed class SyncHost : IDisposable
    {
        private readonly IgdbTestHost _igdb;
        private readonly SteamStoreTestHost _steam;
        private readonly IMetadataCache _igdbCache;
        private readonly IStoreMetadataCache _storeCache;
        private readonly DateTime _fetchedAt;

        public SyncHost(TempDatabase db, DateTimeOffset now)
        {
            _fetchedAt = now.UtcDateTime;
            _igdbCache = new SqliteMetadataCache(db.Factory);
            _storeCache = new SqliteStoreMetadataCache(db.Factory);

            _igdb = new IgdbTestHost(Unreachable, cache: _igdbCache, now: now);
            _steam = new SteamStoreTestHost(Unreachable, cache: _storeCache, now: now);
        }

        public FacetSyncService Service(
            LibraryQueryRepository libraryQueries, FacetRepository facets)
            => new(
                libraryQueries,
                facets,
                _igdb.Client,
                _steam.Client,
                NullLogger<FacetSyncService>.Instance);

        /// <summary>Writes the payload shape <see cref="IgdbClient"/> stores for one game.</summary>
        public void CacheIgdbGame(long igdbId, string payloadJson)
            => _igdbCache.SetAsync(
                IgdbClient.CacheProvider,
                IgdbClient.GameCacheKey(igdbId),
                payloadJson,
                _fetchedAt).GetAwaiter().GetResult();

        /// <summary>Writes the verbatim captured store body for one appid.</summary>
        public void CacheStoreItem(string appId)
            => _storeCache.SetAsync(
                SteamStoreClient.CacheProvider,
                SteamStoreClient.AppCacheKey(appId),
                StoreFixtures.CapturedItemJson(appId),
                _fetchedAt).GetAwaiter().GetResult();

        /// <summary>Writes both captured vocabularies, so tag and category ids resolve.</summary>
        public void CacheStoreVocabularies()
        {
            _storeCache.SetAsync(
                SteamStoreClient.CacheProvider,
                SteamStoreClient.TagListCacheKey("english"),
                StoreFixtures.TagListResponse(),
                _fetchedAt).GetAwaiter().GetResult();

            _storeCache.SetAsync(
                SteamStoreClient.CacheProvider,
                SteamStoreClient.StoreCategoriesCacheKey("english"),
                StoreFixtures.StoreCategoriesResponse(),
                _fetchedAt).GetAwaiter().GetResult();
        }

        /// <summary>Store requests that were attempted, all of which failed.</summary>
        public IReadOnlyList<Hoard.Tests.SteamStore.RecordedStoreRequest> SteamRequests
            => _steam.Handler.Requests;

        /// <summary>
        /// The claim the whole file is about: a warm run reads the caches and
        /// never reaches for the wire. Asserted by counting rather than by
        /// exploding, so a client that swallowed the failure could not pass by
        /// accident.
        /// </summary>
        public void AssertNoRequestsMade()
        {
            Assert.Empty(_igdb.Handler.Requests);
            Assert.Empty(_steam.Handler.Requests);
        }

        public void Dispose()
        {
            _igdb.Dispose();
            _steam.Dispose();
        }

        /// <summary>
        /// Fails every request the way an offline machine does. Deliberately an
        /// <see cref="HttpRequestException"/> rather than something the clients
        /// do not catch: this must exercise their real degrade path, not their
        /// crash path.
        /// </summary>
        private static HttpResponseMessage Unreachable<TRequest>(TRequest request, int attempt)
            => throw new HttpRequestException("No network in this test.");
    }
}
