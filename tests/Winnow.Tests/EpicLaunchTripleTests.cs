using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Data.Repositories;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Pins the contract between the Epic scan, the launch-triple store and the
/// launch-key reader. The 0/67 regression — every Epic row present, zero
/// actionable — is exactly what <see cref="Every_owned_base_game_the_scan_reports_also_gets_a_launch_triple"/>
/// and <see cref="A_library_with_no_stored_triple_offers_no_Epic_action_at_all"/>
/// catch.
/// </summary>
public class EpicLaunchTripleTests
{
    private const string FezId = "7a70b499513441c792b541d53505e0b2";
    private const string FezNamespace = "41f47fd0d3e248bc938a5815d6d64daa";
    private const string CelesteId = "38c07a09dc174b69b756aa51890c3dd4";
    private const string WatchDogsId = "6dc445f656de4e029834b2d32b6a2f77";
    private const string BountyOfBloodId = "0854f1cf60fd48d4a29178b211d2f133";

    private static EpicLibrarySource SourceOver(EpicFixtureTree tree)
        => new(
            installProbe: new FakeEpicThirdPartyInstallProbe(EpicInstallState.Unknown),
            dataRoot: tree.DataRoot);

    /// <summary>Every candidate the scan reports also gets a triple. The 0/67 regression is exactly what this catches.</summary>
    [Fact]
    public void Every_owned_base_game_the_scan_reports_also_gets_a_launch_triple()
    {
        using var tree = EpicFixtureTree.Create(
            manifests: [EpicFixtureTree.FezManifest, EpicFixtureTree.IncompleteManifest]);

        var scan = SourceOver(tree).ScanLibrary();

        Assert.NotEmpty(scan.Candidates);
        Assert.Equal(
            scan.Candidates.Select(c => c.ProviderId).OrderBy(id => id, StringComparer.Ordinal),
            scan.LaunchTriples.Select(t => t.CatalogItemId).OrderBy(id => id, StringComparer.Ordinal));
    }

    /// <summary>A catalog-only title yields its namespace and AppName from <c>catcache.bin</c>.</summary>
    [Fact]
    public void The_triple_carries_the_namespace_the_catalog_holds()
    {
        using var tree = EpicFixtureTree.Create(manifests: []);

        var triple = Assert.Single(
            SourceOver(tree).ScanLibrary().LaunchTriples, t => t.CatalogItemId == FezId);

        Assert.Equal(FezNamespace, triple.CatalogNamespace);
        Assert.Equal("Bluebird", triple.AppName);
    }

    /// <summary>A manifest the catalog has not caught up with still yields a triple from its own fields.</summary>
    [Fact]
    public void A_manifest_the_catalog_has_not_caught_up_with_still_yields_a_triple()
    {
        using var tree = EpicFixtureTree.Create(manifests: [EpicFixtureTree.IncompleteManifest]);

        var triple = Assert.Single(
            SourceOver(tree).ScanLibrary().LaunchTriples, t => t.CatalogItemId == CelesteId);

        Assert.Equal("b671fbc7be424e888c9346a9a6d3d9db", triple.CatalogNamespace);
        Assert.Equal("Salt", triple.AppName);
    }

    /// <summary>A title delivered by another launcher gets a triple from the third-party record.</summary>
    [Fact]
    public void A_title_delivered_by_another_launcher_gets_a_triple_too()
    {
        using var tree = EpicFixtureTree.Create(manifests: []);

        var triple = Assert.Single(
            SourceOver(tree).ScanLibrary().LaunchTriples, t => t.CatalogItemId == WatchDogsId);

        Assert.Equal("ecebf45065bc4993abfe0e84c40ff18e", triple.CatalogNamespace);
        Assert.Equal("Jasper", triple.AppName);
    }

    /// <summary>DLC is filtered at the candidate level, so no triple is produced for it either.</summary>
    [Fact]
    public void Dlc_is_not_offered_a_triple_because_it_is_never_a_candidate()
    {
        using var tree = EpicFixtureTree.Create(manifests: [EpicFixtureTree.DlcManifest]);

        var scan = SourceOver(tree).ScanLibrary();

        Assert.DoesNotContain(scan.LaunchTriples, t => t.CatalogItemId == BountyOfBloodId);
    }

    /// <summary><see cref="EpicLibrarySource.Scan()"/> and <see cref="EpicLibrarySource.ScanLibrary"/> agree on candidates.</summary>
    [Fact]
    public void Scan_returns_exactly_what_ScanLibrary_reports_as_candidates()
    {
        using var tree = EpicFixtureTree.Create(manifests: [EpicFixtureTree.FezManifest]);
        var source = SourceOver(tree);

        Assert.Equal(
            source.Scan().Select(c => c.ProviderId),
            source.ScanLibrary().Candidates.Select(c => c.ProviderId));
    }

    /// <summary>A machine with no Epic install yields empty results, not an exception.</summary>
    [Fact]
    public void A_missing_launcher_yields_no_triples_and_does_not_throw()
    {
        var missing = Path.Combine(Path.GetTempPath(), "winnow-epic-absent-" + Guid.NewGuid().ToString("N"));

        var scan = new EpicLibrarySource().ScanLibrary(missing);

        Assert.Empty(scan.Candidates);
        Assert.Empty(scan.LaunchTriples);
    }

    /// <summary>A saved triple round-trips through the store and reader into a complete launch key.</summary>
    [Fact]
    public async Task A_stored_triple_reads_back_as_a_complete_launch_key()
    {
        using var db = new TempDatabase();
        var store = new SqliteEpicLaunchKeyStore(db.Factory);

        await store.SaveAsync([new EpicLaunchTriple(FezId, FezNamespace, "Bluebird")]);

        var key = Assert.Contains(FezId, await new SqliteEpicLaunchKeys(db.Factory).GetAllAsync());
        Assert.Equal(FezNamespace, key.Namespace);
        Assert.Equal(FezId, key.CatalogItemId);
        Assert.Equal("Bluebird", key.ArtifactId);
        Assert.Equal($"{FezNamespace}%3A{FezId}%3ABluebird", key.PathSegment);
    }

    /// <summary>Saving twice upserts one row rather than inserting a duplicate or throwing.</summary>
    [Fact]
    public async Task Saving_the_same_triple_twice_updates_one_row_rather_than_failing()
    {
        using var db = new TempDatabase();
        var store = new SqliteEpicLaunchKeyStore(db.Factory);

        await store.SaveAsync([new EpicLaunchTriple(FezId, FezNamespace, "Bluebird")]);
        await store.SaveAsync([new EpicLaunchTriple(FezId, FezNamespace, "Starling")]);

        Assert.Equal(1, RowCount(db, SqliteEpicLaunchKeyStore.Provider));
        var key = Assert.Contains(FezId, await new SqliteEpicLaunchKeys(db.Factory).GetAllAsync());
        Assert.Equal("Starling", key.ArtifactId);
    }

    /// <summary>The local provider writes its own row; the remote catalog cache row stays intact.</summary>
    [Fact]
    public async Task The_local_provider_never_overwrites_the_remote_catalog_cache()
    {
        using var db = new TempDatabase();
        await new SqliteEpicCatalogCache(db.Factory).SetAsync(
            FezId,
            """{"Namespace":"41f47fd0d3e248bc938a5815d6d64daa","AppName":"Bluebird","Title":"Fez"}""",
            DateTime.UtcNow);

        await new SqliteEpicLaunchKeyStore(db.Factory)
            .SaveAsync([new EpicLaunchTriple(FezId, FezNamespace, "Bluebird")]);

        var cached = await new SqliteEpicCatalogCache(db.Factory).GetAsync(FezId);
        Assert.NotNull(cached);
        Assert.Contains("\"Title\":\"Fez\"", cached.Value.PayloadJson);
        Assert.Equal(1, RowCount(db, SqliteEpicCatalogCache.Provider));
        Assert.Equal(1, RowCount(db, SqliteEpicLaunchKeyStore.Provider));
    }

    /// <summary>The reader unions both providers; where both hold the same item, the local row wins.</summary>
    [Fact]
    public async Task The_reader_unions_both_providers_and_the_local_row_wins()
    {
        using var db = new TempDatabase();
        await new SqliteEpicCatalogCache(db.Factory).SetAsync(
            FezId, """{"Namespace":"stale","AppName":"Stale"}""", DateTime.UtcNow);
        await new SqliteEpicCatalogCache(db.Factory).SetAsync(
            WatchDogsId,
            """{"Namespace":"ecebf45065bc4993abfe0e84c40ff18e","AppName":"Jasper"}""",
            DateTime.UtcNow);

        await new SqliteEpicLaunchKeyStore(db.Factory)
            .SaveAsync([new EpicLaunchTriple(FezId, FezNamespace, "Bluebird")]);

        var keys = await new SqliteEpicLaunchKeys(db.Factory).GetAllAsync();

        Assert.Equal(FezNamespace, keys[FezId].Namespace);
        Assert.Equal("Bluebird", keys[FezId].ArtifactId);
        Assert.Equal("Jasper", keys[WatchDogsId].ArtifactId);
    }

    /// <summary>An empty collection of triples is a no-op — no rows written.</summary>
    [Fact]
    public async Task An_empty_scan_writes_nothing()
    {
        using var db = new TempDatabase();

        await new SqliteEpicLaunchKeyStore(db.Factory).SaveAsync([]);

        Assert.Equal(0, RowCount(db, SqliteEpicLaunchKeyStore.Provider));
    }

    /// <summary>Every triple the fixture scan produces resolves to a complete launch key after storage.</summary>
    [Fact]
    public async Task Every_triple_the_fixture_scan_produces_resolves_to_a_launch_key()
    {
        using var tree = EpicFixtureTree.Create(
            manifests: [EpicFixtureTree.FezManifest, EpicFixtureTree.IncompleteManifest]);
        using var db = new TempDatabase();

        var scan = SourceOver(tree).ScanLibrary();
        await new SqliteEpicLaunchKeyStore(db.Factory).SaveAsync(scan.LaunchTriples);

        var keys = await new SqliteEpicLaunchKeys(db.Factory).GetAllAsync();

        Assert.Equal(scan.Candidates.Count, keys.Count);
        Assert.All(scan.Candidates, c => Assert.True(keys.ContainsKey(c.ProviderId)));
    }

    /// <summary>With no stored triple, no Epic launch key exists and no action is offered. This is the state the user was actually in.</summary>
    [Fact]
    public async Task A_library_with_no_stored_triple_offers_no_Epic_action_at_all()
    {
        using var db = new TempDatabase();

        var keys = await new SqliteEpicLaunchKeys(db.Factory).GetAllAsync();

        Assert.Empty(keys);
        Assert.Null(StoreActions.PrimaryFor(
            Winnow.Core.Domain.ExternalIdProviders.Epic, false, null, null, null));
        Assert.Null(StoreActions.PrimaryFor(
            Winnow.Core.Domain.ExternalIdProviders.Epic, true, null, null, null));
    }

    /// <summary>One full local sync pass persists the triples and every Epic ownership row gains a launch key.</summary>
    [Fact]
    public async Task One_local_sync_pass_leaves_every_Epic_row_with_a_launch_key()
    {
        using var tree = EpicFixtureTree.Create(
            manifests: [EpicFixtureTree.FezManifest, EpicFixtureTree.IncompleteManifest]);
        using var db = new TempDatabase();

        var resolver = new ExternalIdResolver(
            new WorkRepository(db.Factory),
            new ReleaseRepository(db.Factory),
            new OwnershipRepository(db.Factory),
            new PlayRecordRepository(db.Factory),
            new PlaytimeSnapshotRepository(db.Factory),
            db.Factory,
            new OwnershipAccountRepository(db.Factory));

        var sync = new LocalLibrarySyncService(
            new SteamLibrarySource(steamRoot: Path.Combine(
                Path.GetTempPath(), "winnow-tests-no-launcher-here")),
            SourceOver(tree),
            SilentStores.Gog(),
            resolver,
            new LibrarySyncGate(),
            NullLogger<LocalLibrarySyncService>.Instance,
            new SqliteEpicLaunchKeyStore(db.Factory));

        var report = await sync.SyncAsync();

        var keys = await new SqliteEpicLaunchKeys(db.Factory).GetAllAsync();

        Assert.NotNull(report.Scan);
        Assert.NotEmpty(report.Scan.Value.Epic);
        Assert.All(report.Scan.Value.Epic, c => Assert.True(keys.ContainsKey(c.ProviderId)));
    }

    /// <summary>When no <see cref="IEpicLaunchKeyStore"/> is registered, the sync still resolves the library and does not throw.</summary>
    [Fact]
    public async Task A_sync_with_no_store_wired_up_still_resolves_the_library()
    {
        using var tree = EpicFixtureTree.Create(manifests: [EpicFixtureTree.FezManifest]);
        using var db = new TempDatabase();

        var resolver = new ExternalIdResolver(
            new WorkRepository(db.Factory),
            new ReleaseRepository(db.Factory),
            new OwnershipRepository(db.Factory),
            new PlayRecordRepository(db.Factory),
            new PlaytimeSnapshotRepository(db.Factory),
            db.Factory,
            new OwnershipAccountRepository(db.Factory));

        var sync = new LocalLibrarySyncService(
            new SteamLibrarySource(steamRoot: Path.Combine(
                Path.GetTempPath(), "winnow-tests-no-launcher-here")),
            SourceOver(tree),
            SilentStores.Gog(),
            resolver,
            new LibrarySyncGate(),
            NullLogger<LocalLibrarySyncService>.Instance);

        var report = await sync.SyncAsync();

        Assert.NotNull(report.Scan);
        Assert.NotEmpty(report.Scan.Value.Epic);
        Assert.NotEmpty(report.Scan.Value.EpicLaunchTriples);
        Assert.Equal(0, RowCount(db, SqliteEpicLaunchKeyStore.Provider));
    }

    private static int RowCount(TempDatabase db, string provider)
    {
        using var lease = db.Factory.Lease();
        return lease.Connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM metadata_cache WHERE provider = @provider;",
            new { provider },
            lease.Transaction);
    }
}
