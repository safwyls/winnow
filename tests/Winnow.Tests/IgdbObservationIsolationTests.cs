using System.Net;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Winnow.Tests.Igdb;
using Winnow.Tests.SteamStore;
using Xunit;

namespace Winnow.Tests;

public sealed class IgdbObservationIsolationTests
{
    public static TheoryData<string, bool> DelayedPaths
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (var path in new[] { "facets", "reception", "maturity", "lifecycle", "refetch", "enrichment", "assignment" })
            {
                cases.Add(path, false);
                cases.Add(path, true);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(DelayedPaths))]
    public async Task An_old_response_cannot_write_after_reassignment_even_when_the_same_id_returns(string path, bool returnToOriginal)
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var workId = await works.InsertAsync(new() { Name = "Original", IgdbId = 111 });
        var releaseId = await releases.InsertAsync(new() { WorkId = workId, Name = "Original" });
        await releases.AddExternalIdAsync(new() { ReleaseId = releaseId, Provider = "steam", ProviderId = "123" });
        var guard = new IgdbObservationWriter(db.Factory);
        var pins = new WorkIgdbPinRepository(db.Factory);
        var queries = new LibraryQueryRepository(db.Factory);
        var facets = new FacetRepository(db.Factory);
        var images = new WorkImageRepository(db.Factory);
        var ratings = new WorkRatingRepository(db.Factory);
        var maturity = new WorkMaturityRepository(db.Factory);
        var lifecycle = new LifecycleRepository(db.Factory);
        var reception = new WorkReceptionWriter(images, ratings, guard);
        var provider = new DelayedIgdb();
        using var steam = new SteamStoreTestHost((_, _) => FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "[]"));

        Task pending = path switch
        {
            "facets" => new FacetSyncService(queries, facets, guard, provider, steam.Client, NullLogger<FacetSyncService>.Instance).SyncAsync(),
            "reception" => new ReceptionSyncService(queries, reception, provider, steam.Client, NullLogger<ReceptionSyncService>.Instance).SyncAsync(),
            "maturity" => new IgdbMaturitySync(provider, new SqliteIgdbMaturityTargetSource(db.Factory), maturity, guard,
                TimeProvider.System, NullLogger<IgdbMaturitySync>.Instance).SyncAsync(),
            "lifecycle" => new LifecycleSyncService(queries, lifecycle, new SettingsRepository(db.Factory), provider, new NoSteamLifecycle(),
                guard, TimeProvider.System, NullLogger<LifecycleSyncService>.Instance).SyncAsync(),
            "refetch" => new GameRefetchService(works, releases, pins, provider, steam.Client, reception, db.Factory,
                NullLogger<GameRefetchService>.Instance).RefetchAsync(workId),
            "enrichment" => new EnrichmentSyncService(works, releases, provider, steam.Client, new NoBuildInfo(),
                new EnrichmentLookupPlanner(new IgdbOptions()), db.Factory, NullLogger<EnrichmentSyncService>.Instance).EnrichAsync(),
            _ => new IgdbManualAssignment(provider, pins, guard, NullLogger<IgdbManualAssignment>.Instance).AssignAsync(workId, 444),
        };
        await provider.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await pins.PinAsync(new() { WorkId = workId, IgdbId = 222, Name = "New choice" });
        if (returnToOriginal) await pins.PinAsync(new() { WorkId = workId, IgdbId = 111, Name = "New choice" });
        // Absence of a pin is insufficient: this old request predates both
        // the reassignment and the decision to return to automatic enrichment.
        await pins.ClearAsync(workId);
        provider.Release.TrySetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));

        var saved = (await works.GetAsync(workId))!;
        Assert.Equal(returnToOriginal ? 111 : 222, saved.IgdbId);
        Assert.Equal("New choice", saved.Name);
        Assert.Null(saved.Summary);
        Assert.Null(saved.FirstReleaseYear);
        Assert.Null(await pins.GetAsync(workId));
        Assert.Empty(await images.GetForWorkAsync(workId));
        Assert.Empty(await ratings.GetForWorkAsync(workId));
        Assert.Empty(await maturity.GetForWorkAsync(workId));
        Assert.Empty(await lifecycle.GetForReleaseAsync(releaseId));
        using var connection = db.Factory.Open();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM work_facets;"));
        if (path == "assignment")
            Assert.Equal(IgdbAssignmentStatus.MappingChanged, (await (Task<IgdbAssignmentResult>)pending).Status);
        if (path == "enrichment")
            Assert.Equal(0, (await (Task<EnrichmentReport>)pending).MetadataFilled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_explicit_empty_rating_response_retires_igdb_evidence_but_an_unavailable_response_preserves_it(bool unavailable)
    {
        using var db = new TempDatabase();
        var workId = await new WorkRepository(db.Factory).InsertAsync(new() { Name = "Game", IgdbId = 111 });
        var maturity = new WorkMaturityRepository(db.Factory);
        await maturity.UpsertAsync(new() { WorkId = workId, Source = "igdb", Ratings = "esrb:ao", ObservedAt = DateTime.UtcNow });
        await maturity.UpsertAsync(new() { WorkId = workId, Source = "steam_store", Descriptors = "violence_or_gore", ObservedAt = DateTime.UtcNow });
        var provider = new DelayedIgdb { EmptyRatings = true, UnavailableRatings = unavailable };
        provider.Release.SetResult();
        var sync = new IgdbMaturitySync(provider, new SqliteIgdbMaturityTargetSource(db.Factory), maturity,
            new IgdbObservationWriter(db.Factory), TimeProvider.System, NullLogger<IgdbMaturitySync>.Instance);

        Assert.Equal(unavailable ? 0 : 1, await sync.SyncAsync());
        var rows = await maturity.GetForWorkAsync(workId);
        Assert.Equal(unavailable, rows.Any(row => row.Source == "igdb"));
        Assert.Contains(rows, row => row.Source == "steam_store");
    }

    [Fact]
    public async Task A_mapping_transition_retires_every_igdb_projection_and_keeps_independent_observations()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var workId = await works.InsertAsync(new() { Name = "Game", IgdbId = 111, BackgroundUrl = "https://example.test/old.jpg" });
        var releaseId = await new ReleaseRepository(db.Factory).InsertAsync(new() { WorkId = workId, Name = "Game" });
        var facets = new FacetRepository(db.Factory);
        await facets.SetWorkFacetsAsync(workId, [new(FacetKinds.Genre, "Old genre")]);
        await facets.SetReleaseFacetsAsync(releaseId, [new(FacetKinds.Tag, "Store tag")]);
        await new PluginFacetRepository(db.Factory).SetAsync(workId, "plugin:test", [new(FacetKinds.Genre, "Plugin genre")]);
        var maturity = new WorkMaturityRepository(db.Factory);
        foreach (var source in new[] { "igdb", "steam_store" })
            await maturity.UpsertAsync(new() { WorkId = workId, Source = source, Ratings = "esrb:ao", ObservedAt = DateTime.UtcNow });
        var images = new WorkImageRepository(db.Factory);
        foreach (var source in new[] { "igdb", "plugin:test" })
            await images.UpsertAsync(new() { WorkId = workId, Source = source, Kind = "artwork", ImageIds = "image", ObservedAt = DateTime.UtcNow });
        var ratings = new WorkRatingRepository(db.Factory);
        foreach (var source in new[] { "igdb_users", "igdb_critics", "steam", "plugin:test" })
            await ratings.UpsertAsync(new() { WorkId = workId, Source = source, Score = 80, RatingCount = 30, ObservedAt = DateTime.UtcNow });
        var lifecycle = new LifecycleRepository(db.Factory);
        await lifecycle.AppendAsync(new() { ReleaseId = releaseId, Source = "igdb", SourceId = "111", ObservedAt = DateTime.UtcNow, Signals = new() { IgdbStatus = "offline" } });
        await lifecycle.AppendAsync(new() { ReleaseId = releaseId, Source = "steam", SourceId = "123", ObservedAt = DateTime.UtcNow });
        using (var connection = db.Factory.Open()) connection.Execute("INSERT INTO work_field_sources(work_id,field,source,set_at) VALUES(@workId,'background_url','igdb','2026-09-11');", new { workId });
        await new WorkFieldSourceRepository(db.Factory).SetFieldAsync(workId, WorkFields.Summary, "My summary");

        var manual = new ManualEntryRepository(db.Factory);
        // Full pin intentionally replaces scalar fields; use the mapping
        // transition through a manual draft to prove user scalar preservation.
        using (var connection = db.Factory.Open())
        {
            connection.Execute("INSERT INTO ownerships(release_id,store) VALUES(@releaseId,'manual'); INSERT INTO manual_entries(ownership_id,added_at,updated_at) VALUES(last_insert_rowid(),'2026-09-11','2026-09-11');", new { releaseId });
        }
        var entry = Assert.Single(await manual.GetAllAsync());
        await manual.UpdateAsync(entry.OwnershipId, new() { Title = entry.Title, IgdbId = 222, ExpectedIgdbMappingRevision = entry.IgdbMappingRevision });

        Assert.Null((await works.GetAsync(workId))!.BackgroundUrl);
        Assert.Equal("My summary", (await works.GetAsync(workId))!.Summary);
        Assert.Equal("steam_store", Assert.Single(await maturity.GetForWorkAsync(workId)).Source);
        Assert.Equal("plugin:test", Assert.Single(await images.GetForWorkAsync(workId)).Source);
        Assert.Equal(["plugin:test", "steam"], (await ratings.GetForWorkAsync(workId)).Select(row => row.Source));
        Assert.Equal("steam", Assert.Single(await lifecycle.GetForReleaseAsync(releaseId)).Source);
        using var check = db.Factory.Open();
        Assert.Equal(2, check.ExecuteScalar<int>("SELECT COUNT(*) FROM lifecycle_observations;"));
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM work_facets;"));
        Assert.Equal(1, check.ExecuteScalar<int>("SELECT COUNT(*) FROM release_facets;"));
        Assert.Equal(1, check.ExecuteScalar<int>("SELECT COUNT(*) FROM plugin_work_facets;"));
    }

    private sealed class DelayedIgdb : IIgdbClient, IIgdbLifecycleClient
    {
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool EmptyRatings { get; init; }
        public bool UnavailableRatings { get; init; }
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public async Task<IReadOnlyList<IgdbGame>> GetGamesAsync(IEnumerable<long> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var captured = ids.ToArray();
            Requested.TrySetResult(); await Release.Task.WaitAsync(ct);
            return captured.Select(id => new IgdbGame(id, "Old response", "https://example.test/old-cover.jpg", 1990, "Old summary", ["Old genre"], [], [])
            { ArtworkImageIds = ["old-art"], ScreenshotImageIds = ["old-shot"], UserRating = 70, UserRatingCount = 100, CriticRating = 80, CriticRatingCount = 10 }).ToArray();
        }
        public async Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(IEnumerable<long> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            var captured = ids.ToArray();
            Requested.TrySetResult(); await Release.Task.WaitAsync(ct);
            return UnavailableRatings ? new Dictionary<long, IgdbAgeRatings>() : captured.ToDictionary(id => id, id => new IgdbAgeRatings(id, EmptyRatings ? [] : ["esrb:ao"]));
        }
        public async Task<IgdbLifecycleSnapshot?> GetAsync(long gameId, CancellationToken ct = default)
        {
            Requested.TrySetResult(); await Release.Task.WaitAsync(ct);
            return new(DateTime.UtcNow, new() { IgdbStatus = "offline" }, "{}");
        }
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(IEnumerable<string> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => ResolveByExternalIdsAsync(1, ids, cacheTtl, ct);
        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(int source, IEnumerable<string> ids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(new Dictionary<string, IgdbExternalMatch>());
        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IgdbSearchResult>>([]);
    }

    private sealed class NoSteamLifecycle : ISteamLifecycleClient
    {
        public Task<IReadOnlyList<SteamLifecycleSnapshot>> GetAsync(string appId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SteamLifecycleSnapshot>>([]);
    }

    private sealed class NoBuildInfo : IBuildInfoClient
    {
        public Task<BuildInfoFetch> GetPublicBranchAsync(string appId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(BuildInfoFetch.Unavailable);
        public Task<AppInfoFetch> GetAppInfoAsync(string appId, TimeSpan? cacheTtl = null, bool cachedOnly = false, CancellationToken ct = default)
            => Task.FromResult(AppInfoFetch.Unavailable);
    }
}
