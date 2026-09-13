using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Steam;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Winnow.Tests.Igdb;
using Winnow.Tests.SteamStore;
using Xunit;

namespace Winnow.Tests;

public sealed class ReleaseYearEvidenceTests
{
    [Fact]
    public async Task Existing_work_year_is_a_fallback_never_backfilled_as_edition_evidence()
    {
        using var db = new TempDatabase();
        var (work, release) = await Seed(db, "440", 2011);
        var identity = Assert.Single(await new ReleaseRepository(db.Factory).GetIdentitiesAsync());
        Assert.Equal(2011, identity.MatchYear);
        Assert.Equal("work_first_release_year", identity.MatchYearSource);
        Assert.Null(identity.EditionReleaseYear);
        Assert.Null(identity.FirstReleaseYearSource);

        var evidence = new ReleaseYearEvidenceRepository(db.Factory);
        Assert.True(await evidence.ObserveSteamAsync(release, "440", 2016));
        Assert.False(await evidence.ObserveSteamAsync(release, "440", 2016));
        identity = Assert.Single(await new ReleaseRepository(db.Factory).GetIdentitiesAsync());
        Assert.Equal(2016, identity.MatchYear);
        Assert.Equal("steam_original_release_date", identity.MatchYearSource);
        Assert.Equal(2011, (await new WorkRepository(db.Factory).GetAsync(work))!.FirstReleaseYear);
        Assert.Empty(await new WorkFieldSourceRepository(db.Factory).GetSourcesAsync(work));
    }

    [Fact]
    public async Task Evidence_requires_current_exact_id_and_conflicting_listing_dates_do_not_pick_a_winner()
    {
        using var db = new TempDatabase();
        var (_, release) = await Seed(db, "440", null);
        var repository = new ReleaseRepository(db.Factory);
        var evidence = new ReleaseYearEvidenceRepository(db.Factory);
        Assert.False(await evidence.ObserveSteamAsync(release, "570", 2013));
        await evidence.ObserveSteamAsync(release, "440", 2007);
        await repository.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = "570" });
        await evidence.ObserveSteamAsync(release, "570", 2013);
        Assert.Null(Assert.Single(await repository.GetIdentitiesAsync()).EditionReleaseYear);
        using (var lease = db.Factory.Lease())
            await lease.Connection.ExecuteAsync("DELETE FROM external_ids WHERE provider = 'steam' AND provider_id = '570'");
        Assert.Equal(2007, Assert.Single(await repository.GetIdentitiesAsync()).EditionReleaseYear);
        using (var lease = db.Factory.Lease())
            await lease.Connection.ExecuteAsync("DELETE FROM external_ids WHERE provider = 'steam' AND provider_id = '440'");
        Assert.Null(Assert.Single(await repository.GetIdentitiesAsync()).MatchYear);
        Assert.False(await evidence.ObserveSteamAsync(release, "440", 2020));
    }

    [Fact]
    public async Task User_correction_and_cleared_year_win_without_changing_the_igdb_pin()
    {
        using var db = new TempDatabase();
        var (work, release) = await Seed(db, "440", 2011);
        var pins = new WorkIgdbPinRepository(db.Factory);
        await pins.PinAsync(new() { WorkId = work, IgdbId = 42, Name = "Pinned", FirstReleaseYear = 2012 });
        await new ReleaseYearEvidenceRepository(db.Factory).ObserveSteamAsync(release, "440", 2016);
        var fields = new WorkFieldSourceRepository(db.Factory);
        await fields.SetFieldAsync(work, WorkFields.FirstReleaseYear, "2020");
        var identity = Assert.Single(await new ReleaseRepository(db.Factory).GetIdentitiesAsync());
        Assert.Equal(2020, identity.MatchYear);
        Assert.Equal(FieldSources.User, identity.MatchYearSource);
        await fields.SetFieldAsync(work, WorkFields.FirstReleaseYear, null);
        identity = Assert.Single(await new ReleaseRepository(db.Factory).GetIdentitiesAsync());
        Assert.Null(identity.MatchYear);
        Assert.Equal(2016, identity.EditionReleaseYear);
        Assert.Equal(42, (await new WorkRepository(db.Factory).GetAsync(work))!.IgdbId);
        using var lease = db.Factory.Lease();
        Assert.Equal(1, await lease.Connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM work_igdb_pins WHERE cleared_at IS NULL"));

        var subject = new MatchSubject { ReleaseId = release, Title = "Prey (2017)", ReleaseYear = identity.MatchYear,
            ReleaseYearSource = identity.MatchYearSource, SuppressTitleYearFallback = identity.YearIsUserOwned };
        var score = new SoftMatcher().Score(subject, subject with { ReleaseId = release + 1 });
        Assert.Null(score.YearDelta);
        var snapshot = SoftMatchSignalsJson.ToPayload(score);
        Assert.Null(snapshot.Left.Year);
        Assert.Equal(FieldSources.User, snapshot.Left.YearSource);
    }

    [Fact]
    public async Task Sweep_compares_edition_dates_and_records_provenance_for_the_shared_review_queue()
    {
        using var db = new TempDatabase();
        var (_, left) = await Seed(db, "440", 2011);
        var (_, right) = await Seed(db, "570", 2011);
        var evidence = new ReleaseYearEvidenceRepository(db.Factory);
        await evidence.ObserveSteamAsync(left, "440", 2016);
        await evidence.ObserveSteamAsync(right, "570", 2017);
        var candidates = new MergeCandidateRepository(db.Factory);
        var sweep = new LibrarySoftMatchSweep(new ReleaseRepository(db.Factory),
            new SoftMatchResolver(new SoftMatcher(), candidates, db.Factory), new ResolveStateRepository(db.Factory));
        await sweep.SweepAsync();
        var queued = Assert.Single(await candidates.GetPendingAsync());
        var snapshot = SoftMatchSignalsJson.Deserialize(queued.SignalsJson)!;
        Assert.Equal(2016, snapshot.Left.Year);
        Assert.Equal(2017, snapshot.Right.Year);
        Assert.Equal(1, snapshot.YearDelta);
        Assert.Equal("steam_original_release_date", snapshot.Left.YearSource);
    }

    [Theory]
    [InlineData(StoreFixtures.TeamFortressAppId, 2007)]
    [InlineData(StoreFixtures.DotaAppId, null)]
    [InlineData(StoreFixtures.EldenRingAppId, null)]
    public async Task Cached_exact_listing_projects_only_explicit_original_date_and_refresh_persists_it(string appId, int? expected)
    {
        using var db = new TempDatabase();
        await Seed(db, appId, 1999);
        using var steam = new SteamStoreTestHost((_, _) => throw new InvalidOperationException("Cache only"));
        await steam.Cache.SetAsync(SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey(appId),
            StoreFixtures.CapturedItemJson(appId), steam.Clock.GetUtcNow().UtcDateTime);
        using var igdb = new IgdbTestHost((_, _) => throw new InvalidOperationException("No IGDB mapping"));
        var service = new ReceptionSyncService(new LibraryQueryRepository(db.Factory),
            new WorkReceptionWriter(new WorkImageRepository(db.Factory), new WorkRatingRepository(db.Factory),
                new IgdbObservationWriter(db.Factory)),
            igdb.Client, steam.Client, NullLogger<ReceptionSyncService>.Instance, new ReleaseYearEvidenceRepository(db.Factory));
        await service.SyncAsync();
        await service.SyncAsync();
        var identity = Assert.Single(await new ReleaseRepository(db.Factory).GetIdentitiesAsync());
        Assert.Equal(expected, identity.EditionReleaseYear);
        Assert.Equal(1999, identity.FirstReleaseYear);
        Assert.Empty(steam.Handler.Requests);
        Assert.Empty(igdb.Handler.Requests);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"original_release_date\":-1}")]
    [InlineData("{\"original_release_date\":0}")]
    [InlineData("{\"original_release_date\":\"2007\"}")]
    [InlineData("{\"original_release_date\":253402300800}")]
    [InlineData("{\"steam_release_date\":1191999600}")]
    public async Task Missing_and_malformed_original_dates_do_not_become_edition_years(string releaseJson)
    {
        using var steam = new SteamStoreTestHost((_, _) => throw new InvalidOperationException("Cache only"));
        await steam.Cache.SetAsync(SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey("440"),
            "{\"id\":440,\"success\":1,\"name\":\"Example\",\"release\":" + releaseJson + "}",
            steam.Clock.GetUtcNow().UtcDateTime);
        Assert.Null((await steam.Client.GetItemsAsync(["440"]))["440"].OriginalReleaseYear);
        Assert.Empty(steam.Handler.Requests);
    }

    private static async Task<(long Work, long Release)> Seed(TempDatabase db, string appId, int? year)
    {
        var work = await new WorkRepository(db.Factory).InsertAsync(new Work { Name = "Example Edition", FirstReleaseYear = year });
        var releases = new ReleaseRepository(db.Factory);
        var release = await releases.InsertAsync(new Release { WorkId = work, Name = "Example Edition" });
        await releases.AddExternalIdAsync(new ExternalId { ReleaseId = release, Provider = "steam", ProviderId = appId });
        return (work, release);
    }
}
