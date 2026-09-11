using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Tests for <see cref="GameRefetchService"/>: pinned-vs-unpinned id
/// selection, the cache bypass (<c>TimeSpan.Zero</c>), the per-work cooldown,
/// every <see cref="GameRefetchOutcome"/> word, and the metadata-fill guard
/// that leaves present columns alone.
/// </summary>
public sealed class GameRefetchServiceTests : IDisposable
{
    private readonly TempDatabase _db = new();
    private readonly WorkRepository _works;
    private readonly ReleaseRepository _releases;
    private readonly WorkIgdbPinRepository _pins;
    private readonly WorkImageRepository _images;
    private readonly WorkRatingRepository _ratings;
    private readonly RefetchIgdbClient _igdb = new();
    private readonly RefetchStoreClient _store = new();
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));

    public GameRefetchServiceTests()
    {
        _works = new WorkRepository(_db.Factory);
        _releases = new ReleaseRepository(_db.Factory);
        _pins = new WorkIgdbPinRepository(_db.Factory);
        _images = new WorkImageRepository(_db.Factory);
        _ratings = new WorkRatingRepository(_db.Factory);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_pinned_work_refetches_against_its_pinned_id_and_never_re_resolves()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });

        _igdb.Games[103_298] = Game(103_298, screenshots: ["sc1"]);
        _igdb.Games[1020] = Game(1020, screenshots: ["scWRONG"]);

        var result = await Service().RefetchAsync(workId);

        Assert.True(result.Succeeded);
        Assert.True(result.UsedPin);
        Assert.Equal([103_298], _igdb.GameIdsAsked);
        Assert.Empty(_igdb.ExternalIdsAsked);

        var screenshots = Assert.Single(await _images.GetForWorkAsync(workId));
        Assert.Equal(["sc1"], screenshots.Ids);
    }

    [Fact]
    public async Task The_pin_survives_a_refetch()
    {
        var workId = await SeedAsync("620");
        await _pins.PinAsync(new WorkIgdbPinAssignment
        {
            WorkId = workId, IgdbId = 103_298, Name = "Prey (2017)",
        });

        _igdb.Games[103_298] = Game(103_298, screenshots: ["sc1"]);

        await Service().RefetchAsync(workId);

        var pin = await _pins.GetAsync(workId);
        Assert.NotNull(pin);
        Assert.Equal(103_298, pin.IgdbId);

        var work = await _works.GetAsync(workId);
        Assert.Equal("Prey (2017)", work!.Name);
    }

    [Fact]
    public async Task An_unpinned_work_refetches_against_the_id_it_already_resolved_to()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Games[1020] = Game(1020, screenshots: ["sc1"]);

        var result = await Service().RefetchAsync(workId);

        Assert.True(result.Succeeded);
        Assert.False(result.UsedPin);
        Assert.Equal([1020], _igdb.GameIdsAsked);
    }

    [Fact]
    public async Task The_refetch_asks_past_the_cache_rather_than_reading_it()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Games[1020] = Game(1020, screenshots: ["sc1"]);

        await Service().RefetchAsync(workId);

        // TimeSpan.Zero is how both clients are told "nothing cached counts":
        // their Cutoff reads a non-positive TTL as DateTime.MaxValue.
        Assert.Equal(TimeSpan.Zero, Assert.Single(_igdb.TtlsAsked));
        Assert.Equal(TimeSpan.Zero, Assert.Single(_store.TtlsAsked));
    }

    [Fact]
    public async Task A_new_mapping_can_refetch_immediately_and_fill_its_pinned_metadata()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Games[1020] = Game(1020, screenshots: ["old"]);
        _igdb.Games[1021] = new IgdbGame(1021, "Corrected", null, 2017, "Correct summary", [], [], []);
        var service = Service();
        await service.RefetchAsync(workId);
        await _pins.PinAsync(new() { WorkId = workId, IgdbId = 1021, Name = "Corrected" });
        var result = await service.RefetchAsync(workId);
        Assert.Equal(GameRefetchOutcome.Updated, result.Outcome);
        Assert.True(result.MetadataFilled);
        Assert.Equal("Correct summary", (await _works.GetAsync(workId))!.Summary);
        Assert.Equal([1020L, 1021L], _igdb.GameIdsAsked);
    }

    [Fact]
    public async Task A_second_refetch_inside_the_cooldown_is_refused_without_a_request()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Games[1020] = Game(1020, screenshots: ["sc1"]);

        var service = Service();
        Assert.True((await service.RefetchAsync(workId)).Succeeded);

        var second = await service.RefetchAsync(workId);

        Assert.Equal(GameRefetchOutcome.TooSoon, second.Outcome);
        Assert.True(second.RetryAfter > TimeSpan.Zero);
        Assert.Single(_igdb.GameIdsAsked);
        Assert.Single(_store.AppIdsAsked);
    }

    [Fact]
    public async Task The_cooldown_lets_go_once_it_has_elapsed()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Games[1020] = Game(1020, screenshots: ["sc1"]);

        var service = Service();
        await service.RefetchAsync(workId);

        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.NotEqual(GameRefetchOutcome.TooSoon, (await service.RefetchAsync(workId)).Outcome);
        Assert.Equal(2, _igdb.GameIdsAsked.Count);
    }

    [Fact]
    public async Task The_cooldown_is_per_work_not_per_library()
    {
        var first = await SeedAsync("620", igdbId: 1020);
        var second = await SeedAsync("730", igdbId: 1021);
        _igdb.Games[1020] = Game(1020, screenshots: ["sc1"]);
        _igdb.Games[1021] = Game(1021, screenshots: ["sc2"]);

        var service = Service();
        await service.RefetchAsync(first);

        Assert.NotEqual(GameRefetchOutcome.TooSoon, (await service.RefetchAsync(second)).Outcome);
    }

    [Fact]
    public async Task A_refetch_that_changes_nothing_says_so()
    {
        var workId = await SeedAsync("620", igdbId: 1020, name: "Prey", provisional: false);
        _igdb.Games[1020] = Game(1020, screenshots: ["sc1"]);

        var service = Service();
        Assert.Equal(GameRefetchOutcome.Updated, (await service.RefetchAsync(workId)).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(GameRefetchOutcome.NothingNew, (await service.RefetchAsync(workId)).Outcome);
    }

    [Fact]
    public async Task A_source_that_cannot_be_reached_is_reported_rather_than_thrown()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Throw = new HttpRequestException("offline");

        var result = await Service().RefetchAsync(workId);

        Assert.Equal(GameRefetchOutcome.Unreachable, result.Outcome);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task An_unconfigured_igdb_is_reported_as_its_own_outcome()
    {
        var workId = await SeedAsync(appId: null, igdbId: 1020);
        _igdb.Configured = false;

        var result = await Service().RefetchAsync(workId);

        Assert.Equal(GameRefetchOutcome.NotConfigured, result.Outcome);
        Assert.Empty(_igdb.GameIdsAsked);
    }

    [Fact]
    public async Task A_work_no_source_can_be_asked_about_is_reported_without_a_request()
    {
        var workId = await SeedAsync(appId: null);

        var result = await Service().RefetchAsync(workId);

        Assert.Equal(GameRefetchOutcome.NoSourceToAsk, result.Outcome);
        Assert.Empty(_igdb.GameIdsAsked);
        Assert.Empty(_store.AppIdsAsked);
    }

    [Fact]
    public async Task A_work_that_is_not_there_is_reported_rather_than_thrown()
        => Assert.Equal(
            GameRefetchOutcome.WorkNotFound, (await Service().RefetchAsync(9_999)).Outcome);

    [Fact]
    public async Task Steam_review_figures_land_on_the_work_the_appid_belongs_to()
    {
        var workId = await SeedAsync("620", igdbId: 1020);
        _igdb.Games[1020] = Game(1020);
        _store.Items["620"] = new SteamStoreItem("620", "Portal 2", SteamStoreItem.NoTags)
        {
            Reviews = new SteamStoreReviewSummary(41_203, 91) { Label = "Very Positive" },
        };

        var result = await Service().RefetchAsync(workId);

        Assert.True(result.AskedSteam);

        var steam = Assert.Single(await _ratings.GetForWorkAsync(workId));
        Assert.Equal(RatingSources.Steam, steam.Source);
        Assert.Equal("Very Positive", steam.Label);
        Assert.Equal(41_203, steam.RatingCount);
    }

    [Fact]
    public async Task A_missing_metadata_column_is_filled_and_a_present_one_is_left_alone()
    {
        var workId = await SeedAsync("620", igdbId: 1020, name: "Portal 2", provisional: false);
        _igdb.Games[1020] = new IgdbGame(
            1020, "Portal 2 (IGDB)", "https://images.igdb.com/igdb/image/upload/t_cover_big/co1.jpg",
            2011, "A canned summary.", [], [], ["Valve"]);

        var result = await Service().RefetchAsync(workId);

        Assert.True(result.MetadataFilled);

        var work = await _works.GetAsync(workId);
        Assert.Equal("Portal 2", work!.Name);
        Assert.Equal(2011, work.FirstReleaseYear);
        Assert.Equal("A canned summary.", work.Summary);
        Assert.Equal("Valve", work.Publisher);
    }

    private GameRefetchService Service()
        => new(
            _works,
            _releases,
            _pins,
            _igdb,
            _store,
            new WorkReceptionWriter(_images, _ratings, new IgdbObservationWriter(_db.Factory), _clock),
            _db.Factory,
            NullLogger<GameRefetchService>.Instance,
            new GameRefetchOptions { Cooldown = TimeSpan.FromMinutes(5) },
            _clock);

    private static IgdbGame Game(long igdbId, IReadOnlyList<string>? screenshots = null)
        => new(igdbId, "Game " + igdbId, null, null, null, [], [], [])
        {
            ScreenshotImageIds = screenshots ?? [],
        };

    private async Task<long> SeedAsync(
        string? appId = null, long? igdbId = null, string name = "App 620", bool provisional = true)
    {
        var workId = await _works.InsertAsync(new Work
        {
            Name = name,
            NameIsProvisional = provisional,
            IgdbId = igdbId,
        });

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

        return workId;
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now;

        public TestClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RefetchIgdbClient : IIgdbClient
    {
        public bool Configured { get; set; } = true;

        public Exception? Throw { get; set; }

        public Dictionary<long, IgdbGame> Games { get; } = [];

        public List<long> GameIdsAsked { get; } = [];

        public List<(int Source, string Uid)> ExternalIdsAsked { get; } = [];

        public List<TimeSpan?> TtlsAsked { get; } = [];

        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(Configured);

        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveBySteamAppIdsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => ResolveByExternalIdsAsync(1, appIds, cacheTtl, ct);

        public Task<IReadOnlyDictionary<string, IgdbExternalMatch>> ResolveByExternalIdsAsync(
            int externalGameSourceId,
            IEnumerable<string> uids,
            TimeSpan? cacheTtl = null,
            CancellationToken ct = default)
        {
            foreach (var uid in uids)
            {
                ExternalIdsAsked.Add((externalGameSourceId, uid));
            }

            return Task.FromResult<IReadOnlyDictionary<string, IgdbExternalMatch>>(
                new Dictionary<string, IgdbExternalMatch>(StringComparer.Ordinal));
        }

        public Task<IReadOnlyList<IgdbGame>> GetGamesAsync(
            IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            TtlsAsked.Add(cacheTtl);

            if (Throw is not null)
            {
                throw Throw;
            }

            var found = new List<IgdbGame>();
            foreach (var id in igdbIds)
            {
                GameIdsAsked.Add(id);
                if (Games.TryGetValue(id, out var game))
                {
                    found.Add(game);
                }
            }

            return Task.FromResult<IReadOnlyList<IgdbGame>>(found);
        }

        public Task<IReadOnlyDictionary<long, IgdbAgeRatings>> GetAgeRatingsAsync(
            IEnumerable<long> igdbIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<long, IgdbAgeRatings>>(
                new Dictionary<long, IgdbAgeRatings>());

        public Task<IReadOnlyList<IgdbSearchResult>> SearchGamesAsync(
            string title, int limit = 0, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IgdbSearchResult>>([]);
    }

    private sealed class RefetchStoreClient : ISteamStoreClient
    {
        public Dictionary<string, SteamStoreItem> Items { get; } = new(StringComparer.Ordinal);

        public List<string> AppIdsAsked { get; } = [];

        public List<TimeSpan?> TtlsAsked { get; } = [];

        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetItemsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            TtlsAsked.Add(cacheTtl);

            var found = new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
            foreach (var appId in appIds)
            {
                AppIdsAsked.Add(appId);
                if (Items.TryGetValue(appId, out var item))
                {
                    found[appId] = item;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, SteamStoreItem>>(found);
        }

        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetCachedItemsAsync(
            IEnumerable<string> appIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, SteamStoreItem>>(
                new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal));

        public Task<SteamTagVocabulary> GetTagListAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(SteamTagVocabulary.Empty);

        public Task<SteamStoreCategoryVocabulary> GetStoreCategoriesAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(SteamStoreCategoryVocabulary.Empty);
    }
}
