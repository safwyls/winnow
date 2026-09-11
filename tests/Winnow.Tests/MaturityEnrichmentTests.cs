using System.Globalization;
using System.Net;
using System.Text.Json;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Igdb.Storage;
using Winnow.Enrich.Steam;
using Winnow.Enrich.Steam.Model;
using Winnow.Enrich.Steam.Storage;
using Winnow.Tests.Igdb;
using Winnow.Tests.SteamStore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Maps real IGDB age-rating payloads and real Steam
/// <c>content_descriptorids</c> arrays into the token vocabulary migration 0024
/// defines, and writes one <c>work_maturity</c> row per (work, source) through
/// <see cref="IWorkMaturityRepository"/>.
///
/// <para>Every id asserted here comes from evidence, not memory. The Steam
/// descriptor ids are in the pinned fixture captured 2026-08-23
/// (<c>tests/fixtures/steam-store/getitems-v1.json</c>), cross-checked
/// against Valve's own descriptor names on the store page. The IGDB rating
/// values are IGDB's published <c>age_ratings.rating</c> enum. No test
/// touches a live API.</para>
/// </summary>
public sealed class MaturityEnrichmentTests
{
    // ── Steam content descriptors ─────────────────────────────────────────

    /// <summary>
    /// The fixture was captured months before anything read
    /// <c>content_descriptorids</c>. The field arrives with the query
    /// <c>BuildGetItemsQuery</c> has always sent, so every body already in
    /// <c>metadata_cache</c> carries it and the Steam maturity pass is a
    /// re-parse of bytes already on disk, not a new HTTP request.
    /// </summary>
    [Fact]
    public void Pinned_store_fixture_already_carries_content_descriptor_ids()
    {
        var items = ParseFixtureItems();

        // ELDEN RING: [2, 5]. Descriptor 2 is "Frequent Violence or Gore",
        // 5 is "General Mature Content" — the two lines the store page prints
        // for this app, tying the numbering to the names.
        Assert.Equal([2, 5], items[StoreFixtures.EldenRingAppId].ContentDescriptorIds);

        // Dota 2: descriptor 5 alone.
        Assert.Equal([5], items[StoreFixtures.DotaAppId].ContentDescriptorIds);

        // Team Fortress 2 has no descriptor array. Absent, not empty — and
        // absent must produce no maturity row.
        Assert.Empty(items[StoreFixtures.TeamFortressAppId].ContentDescriptorIds);
    }

    [Fact]
    public void Store_descriptor_ids_map_onto_the_vocabulary()
    {
        Assert.Equal(
            MaturityDescriptors.NudityOrSexualContent,
            SteamContentDescriptors.TokenFor(SteamContentDescriptors.SomeNudityOrSexualContent));

        Assert.Equal(
            MaturityDescriptors.ViolenceOrGore,
            SteamContentDescriptors.TokenFor(SteamContentDescriptors.FrequentViolenceOrGore));

        Assert.Equal(
            MaturityDescriptors.AdultOnlySexualContent,
            SteamContentDescriptors.TokenFor(SteamContentDescriptors.AdultOnlySexualContent));

        Assert.Equal(
            MaturityDescriptors.GeneralMatureContent,
            SteamContentDescriptors.TokenFor(SteamContentDescriptors.GeneralMatureContent));
    }

    /// <summary>
    /// Descriptor 3 (<c>adult_only_sexual_content</c>) is the only one that
    /// trips the gate. Descriptor 5 is the one the fixture's two real games
    /// carry, and it must not.
    /// </summary>
    [Fact]
    public void Only_adult_only_sexual_content_makes_a_steam_row_explicit()
    {
        Assert.True(MaturityRules.IsExplicit(
            null,
            MaturityRules.Join(SteamContentDescriptors.TokensFor([3]))));

        Assert.False(MaturityRules.IsExplicit(
            null,
            MaturityRules.Join(SteamContentDescriptors.TokensFor([2, 5]))));

        // Descriptor 4 has no tier in MaturityTiers' descriptor table.
        // Carried verbatim so the distinction between "some" and "frequent"
        // survives a later retune. It does not change today's verdict.
        Assert.Equal(
            [SteamContentDescriptors.FrequentNudityOrSexualContentToken],
            SteamContentDescriptors.TokensFor([4]));
        Assert.False(MaturityRules.IsExplicit(
            null, MaturityRules.Join(SteamContentDescriptors.TokensFor([4]))));
    }

    [Fact]
    public void Unknown_descriptor_id_is_kept_verbatim_and_ignored()
    {
        var tokens = SteamContentDescriptors.TokensFor([99]);

        Assert.Equal(["steam:99"], tokens);
        Assert.False(MaturityRules.IsExplicit(null, MaturityRules.Join(tokens)));
    }

    [Fact]
    public void Duplicate_descriptor_ids_collapse()
        => Assert.Equal(
            [MaturityDescriptors.ViolenceOrGore],
            SteamContentDescriptors.TokensFor([2, 2, 2]));

    // ── IGDB age ratings ─────────────────────────────────────────────────

    /// <summary>
    /// IGDB's published <c>age_ratings.rating</c> enum, values 1-39. The
    /// board is implied by the value, so the deprecated <c>category</c> field
    /// is not needed to read it.
    /// </summary>
    [Theory]
    [InlineData(5, "pegi:18")]
    [InlineData(4, "pegi:16")]
    [InlineData(11, "esrb:m")]
    [InlineData(12, "esrb:ao")]
    [InlineData(17, "cero:z")]
    [InlineData(22, "usk:18")]
    [InlineData(26, "grac:18")]
    [InlineData(33, "classind:18")]
    [InlineData(38, "acb:r18")]
    public void Legacy_rating_enum_maps_to_a_board_tier_token(int rating, string expected)
        => Assert.Equal(expected, IgdbAgeRatingTokens.FromLegacyRating(rating));

    [Fact]
    public void Legacy_rating_enum_covers_the_whole_published_range_and_nothing_else()
    {
        for (var rating = 1; rating <= 39; rating++)
        {
            Assert.NotNull(IgdbAgeRatingTokens.FromLegacyRating(rating));
        }

        // Outside the published table: no answer, no token, no row.
        Assert.Null(IgdbAgeRatingTokens.FromLegacyRating(0));
        Assert.Null(IgdbAgeRatingTokens.FromLegacyRating(40));
        Assert.Null(IgdbAgeRatingTokens.FromLegacyRating(null));
    }

    [Fact]
    public void The_explicit_rating_codes_are_all_reachable_from_a_source()
    {
        // Every code MaturityRules calls explicit must be producible by at
        // least one mapping path, otherwise the filter can never fire.
        // acb:x18 has no value in IGDB's legacy enum and is reachable only
        // through the rating-category label path.
        var reachable = Enumerable.Range(1, 39)
            .Select(rating => IgdbAgeRatingTokens.FromLegacyRating(rating))
            .Where(t => t is not null)
            .Concat([IgdbAgeRatingTokens.FromLabels("ACB", "X18+")])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var code in MaturityRules.ExplicitRatingCodes)
        {
            Assert.Contains(code, reachable);
        }
    }

    [Theory]
    [InlineData("ESRB", "AO", "esrb:ao")]
    [InlineData("ESRB", "Adults Only 18+", "esrb:ao")]
    [InlineData("ESRB", "M", "esrb:m")]
    [InlineData("PEGI", "Eighteen", "pegi:18")]
    [InlineData("PEGI", "18", "pegi:18")]
    [InlineData("CERO", "Z", "cero:z")]
    [InlineData("CERO", "CERO_Z", "cero:z")]
    [InlineData("USK", "USK_18", "usk:18")]
    [InlineData("GRAC", "Eighteen", "grac:18")]
    [InlineData("CLASS_IND", "Eighteen", "classind:18")]
    [InlineData("ACB", "R18+", "acb:r18")]
    [InlineData("ACB", "X18+", "acb:x18")]
    public void Organization_and_rating_labels_map_to_the_same_tokens(
        string organization, string rating, string expected)
        => Assert.Equal(expected, IgdbAgeRatingTokens.FromLabels(organization, rating));

    [Fact]
    public void An_unrecognised_label_pair_yields_nothing()
    {
        Assert.Null(IgdbAgeRatingTokens.FromLabels("Ministry of Vibes", "Eighteen"));
        Assert.Null(IgdbAgeRatingTokens.FromLabels("PEGI", "Somewhat Spicy"));
        Assert.Null(IgdbAgeRatingTokens.FromLabels(null, null));
    }

    /// <summary>
    /// Third reading: the published <c>category</c> enum names the board, the
    /// <c>rating_category.rating</c> label names the tier. This path survives
    /// IGDB dropping the deprecated <c>rating</c> value while keeping
    /// <c>category</c>.
    /// </summary>
    [Fact]
    public void Legacy_category_plus_a_label_names_a_token()
    {
        Assert.Equal(
            "acb:x18",
            IgdbAgeRatingTokens.FromLegacyOrganization(IgdbAgeRatingTokens.LegacyCategoryAcb, "X18+"));

        Assert.Equal(
            "esrb:ao",
            IgdbAgeRatingTokens.FromLegacyOrganization(IgdbAgeRatingTokens.LegacyCategoryEsrb, "AO"));

        Assert.Null(IgdbAgeRatingTokens.FromLegacyOrganization(99, "AO"));
    }

    // ── IGDB client integration ───────────────────────────────────────────

    [Fact]
    public async Task Age_ratings_ride_their_own_query_not_the_games_query()
    {
        using var host = new IgdbTestHost(AgeRatingResponder());

        await host.Client.GetAgeRatingsAsync([700L]);

        var query = Assert.Single(host.Handler.Requests, r => r.Endpoint == "games");
        Assert.Contains("fields age_ratings.category,age_ratings.rating,", query.Body, StringComparison.Ordinal);

        // The shared metadata query must not mention age_ratings: a
        // deprecated field IGDB removes may cost maturity, never name,
        // cover or genres.
        Assert.DoesNotContain("age_ratings", Apicalypse.Games([1L], 500, 0), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rated_game_yields_tokens_and_an_unrated_one_is_an_explicit_empty_answer()
    {
        using var host = new IgdbTestHost(AgeRatingResponder());

        var ratings = await host.Client.GetAgeRatingsAsync([AdultsOnlyIgdbId, UnratedIgdbId]);

        Assert.Equal(
            ["esrb:ao", "pegi:18"],
            ratings[AdultsOnlyIgdbId].RatingTokens.Order(StringComparer.Ordinal));

        // Empty confirms that old evidence can be removed. Absent means the
        // request was unavailable and stored evidence must survive.
        Assert.Empty(ratings[UnratedIgdbId].RatingTokens);
    }

    [Fact]
    public async Task A_second_call_is_served_from_cache_including_the_miss()
    {
        using var host = new IgdbTestHost(AgeRatingResponder());

        await host.Client.GetAgeRatingsAsync([AdultsOnlyIgdbId, UnratedIgdbId]);
        var before = host.Handler.CountFor("games");

        var again = await host.Client.GetAgeRatingsAsync([AdultsOnlyIgdbId, UnratedIgdbId]);

        Assert.Equal(before, host.Handler.CountFor("games"));
        Assert.True(again.ContainsKey(AdultsOnlyIgdbId));
        Assert.Empty(again[UnratedIgdbId].RatingTokens);
    }

    /// <summary>
    /// When the query naming the deprecated <c>category</c> and <c>rating</c>
    /// fields is rejected, the client re-asks with only the current reference
    /// fields rather than losing the batch.
    /// </summary>
    [Fact]
    public async Task A_rejected_deprecated_field_falls_back_to_the_label_query()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.TokenResponse("token-maturity")),
            "games" when request.Body.Contains("age_ratings.rating,", StringComparison.Ordinal)
                => FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, "[]"),
            "games" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, LabelOnlyAgeRatingsJson(request.Body)),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
        });

        var ratings = await host.Client.GetAgeRatingsAsync([AdultsOnlyIgdbId]);

        Assert.Equal(["esrb:ao"], ratings[AdultsOnlyIgdbId].RatingTokens);
    }

    [Fact]
    public async Task A_failed_lookup_returns_nothing_rather_than_throwing()
    {
        using var host = new IgdbTestHost((request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.TokenResponse("token-maturity")),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.ServiceUnavailable, "[]"),
        });

        Assert.Empty(await host.Client.GetAgeRatingsAsync([AdultsOnlyIgdbId]));
    }

    // ── Steam and IGDB sync passes ────────────────────────────────────────

    [Fact]
    public async Task Steam_pass_writes_one_row_per_work_and_none_without_descriptors()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var releases = new ReleaseRepository(db.Factory);
        var maturity = new WorkMaturityRepository(db.Factory);

        var ratedWorkId = await works.InsertAsync(new Work { Name = "ELDEN RING" });
        var ratedReleaseId = await releases.InsertAsync(
            new Release { WorkId = ratedWorkId, Name = "ELDEN RING" });
        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = ratedReleaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = StoreFixtures.EldenRingAppId,
        });

        var plainWorkId = await works.InsertAsync(new Work { Name = "Team Fortress 2" });
        var plainReleaseId = await releases.InsertAsync(
            new Release { WorkId = plainWorkId, Name = "Team Fortress 2" });
        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = plainReleaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = StoreFixtures.TeamFortressAppId,
        });

        var sync = new SteamStoreMaturitySync(
            new FakeStoreItems(ParseFixtureItems()),
            new SqliteSteamMaturityTargetSource(db.Factory),
            maturity,
            TimeProvider.System,
            NullLogger<SteamStoreMaturitySync>.Instance);

        Assert.Equal(1, await sync.SyncAsync());

        var row = Assert.Single(await maturity.GetForWorkAsync(ratedWorkId));
        Assert.Equal(MaturitySources.SteamStore, row.Source);
        Assert.Null(row.Ratings);
        Assert.Equal(
            [MaturityDescriptors.ViolenceOrGore, MaturityDescriptors.GeneralMatureContent],
            MaturityRules.Split(row.Descriptors));
        Assert.False(row.IsExplicit);

        // Team Fortress 2 had no descriptor array. No row — absence is what
        // makes a work not explicit.
        Assert.Empty(await maturity.GetForWorkAsync(plainWorkId));
    }

    [Fact]
    public async Task Igdb_pass_writes_its_own_row_beside_the_steam_one()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var maturity = new WorkMaturityRepository(db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Nameless Game", IgdbId = AdultsOnlyIgdbId });
        await maturity.UpsertAsync(new WorkMaturity
        {
            WorkId = workId,
            Source = MaturitySources.SteamStore,
            Descriptors = MaturityDescriptors.GeneralMatureContent,
            ObservedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        using var host = new IgdbTestHost(AgeRatingResponder());
        var sync = new IgdbMaturitySync(
            host.Client,
            new SqliteIgdbMaturityTargetSource(db.Factory),
            maturity,
            new IgdbObservationWriter(db.Factory),
            TimeProvider.System,
            NullLogger<IgdbMaturitySync>.Instance);

        Assert.Equal(1, await sync.SyncAsync());

        var rows = await maturity.GetForWorkAsync(workId);
        Assert.Equal(2, rows.Count);

        var igdb = rows.Single(r => r.Source == MaturitySources.Igdb);
        Assert.Equal(["esrb:ao", "pegi:18"], MaturityRules.Split(igdb.Ratings).Order(StringComparer.Ordinal));
        Assert.True(igdb.IsExplicit);

        // The Steam row is untouched.
        var steam = rows.Single(r => r.Source == MaturitySources.SteamStore);
        Assert.Equal(MaturityDescriptors.GeneralMatureContent, steam.Descriptors);
        Assert.False(steam.IsExplicit);

        Assert.True(await maturity.IsExplicitAsync(workId));
    }

    [Fact]
    public async Task An_unrated_work_gets_no_igdb_row()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var maturity = new WorkMaturityRepository(db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Outer Wilds", IgdbId = UnratedIgdbId });

        using var host = new IgdbTestHost(AgeRatingResponder());
        var sync = new IgdbMaturitySync(
            host.Client,
            new SqliteIgdbMaturityTargetSource(db.Factory),
            maturity,
            new IgdbObservationWriter(db.Factory),
            TimeProvider.System,
            NullLogger<IgdbMaturitySync>.Instance);

        Assert.Equal(0, await sync.SyncAsync());
        Assert.Empty(await maturity.GetForWorkAsync(workId));
        Assert.False(await maturity.IsExplicitAsync(workId));
    }

    /// <summary>
    /// Section 5.1: enrichment degrades, never breaks its caller. A maturity
    /// pass whose lookup throws reports nothing written rather than propagating.
    /// </summary>
    [Fact]
    public async Task A_throwing_lookup_does_not_fail_the_pass()
    {
        using var db = new TempDatabase();
        var sync = new SteamStoreMaturitySync(
            new ThrowingStoreItems(),
            new ThrowingSteamTargets(),
            new WorkMaturityRepository(db.Factory),
            TimeProvider.System,
            NullLogger<SteamStoreMaturitySync>.Instance);

        Assert.Equal(0, await sync.SyncAsync());
    }

    // ── Fixture plumbing ────────────────────────────────────────────────────

    private const long AdultsOnlyIgdbId = 700;

    private const long UnratedIgdbId = 701;

    /// <summary>
    /// The three fixture appids. Projected by the real client out of a cache
    /// seeded with the pinned fixture's bytes. Nothing reaches the network
    /// — the handler throws if anything tries — which is the same claim the
    /// Steam maturity pass makes: the descriptors are already on disk.
    /// </summary>
    private static readonly string[] FixtureAppIds =
    [
        StoreFixtures.EldenRingAppId,
        StoreFixtures.DotaAppId,
        StoreFixtures.TeamFortressAppId,
    ];

    private static IReadOnlyDictionary<string, SteamStoreItem> ParseFixtureItems()
        => ParseFixtureItemsAsync().GetAwaiter().GetResult();

    private static async Task<IReadOnlyDictionary<string, SteamStoreItem>> ParseFixtureItemsAsync()
    {
        var cache = new InMemoryStoreMetadataCache();
        foreach (var appId in FixtureAppIds)
        {
            await cache.SetAsync(
                SteamStoreClient.CacheProvider,
                SteamStoreClient.AppCacheKey(appId),
                StoreFixtures.ItemJson(appId),
                new DateTime(2026, 8, 23, 0, 0, 0, DateTimeKind.Utc));
        }

        using var host = new SteamStoreTestHost(
            (_, _) => throw new InvalidOperationException(
                "The descriptors are supposed to be in the cache already."),
            cache: cache);

        return await host.Client.GetCachedItemsAsync(FixtureAppIds);
    }

    /// <summary>
    /// An IGDB responder whose <c>games</c> answer carries both the deprecated
    /// numeric pair and the expanded reference-field labels, which is how a
    /// real age-rating row arrives today.
    /// </summary>
    private static Func<RecordedRequest, int, HttpResponseMessage> AgeRatingResponder()
        => (request, _) => request.Endpoint switch
        {
            "token" => FakeHttpMessageHandler.Json(
                HttpStatusCode.OK, IgdbFixtures.TokenResponse("token-maturity")),
            "games" => FakeHttpMessageHandler.Json(HttpStatusCode.OK, AgeRatingsJson(request.Body)),
            _ => FakeHttpMessageHandler.Json(HttpStatusCode.NotFound, "[]"),
        };

    private static string AgeRatingsJson(string body)
    {
        var rows = IgdbFixtures.RequestedIds(body).Select(id => id == AdultsOnlyIgdbId
            ? (object)new
            {
                id,
                age_ratings = new object[]
                {
                    new
                    {
                        id = 1,
                        category = 1,
                        rating = 12,
                        organization = new { id = 1, name = "ESRB" },
                        rating_category = new { id = 12, rating = "AO" },
                    },
                    new
                    {
                        id = 2,
                        category = 2,
                        rating = 5,
                        organization = new { id = 2, name = "PEGI" },
                        rating_category = new { id = 5, rating = "Eighteen" },
                    },
                },
            }
            : new { id });

        return JsonSerializer.Serialize(rows);
    }

    private static string LabelOnlyAgeRatingsJson(string body)
    {
        var rows = IgdbFixtures.RequestedIds(body).Select(id => id == AdultsOnlyIgdbId
            ? (object)new
            {
                id,
                age_ratings = new object[]
                {
                    new
                    {
                        id = 1,
                        organization = new { id = 1, name = "ESRB" },
                        rating_category = new { id = 12, rating = "AO" },
                    },
                },
            }
            : new { id });

        return JsonSerializer.Serialize(rows);
    }

    private sealed class FakeStoreItems : ISteamStoreClient
    {
        private readonly IReadOnlyDictionary<string, SteamStoreItem> _items;

        public FakeStoreItems(IReadOnlyDictionary<string, SteamStoreItem> items) => _items = items;

        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetItemsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => GetCachedItemsAsync(appIds, ct);

        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetCachedItemsAsync(
            IEnumerable<string> appIds, CancellationToken ct = default)
        {
            var wanted = new Dictionary<string, SteamStoreItem>(StringComparer.Ordinal);
            foreach (var appId in appIds)
            {
                if (_items.TryGetValue(appId, out var item))
                {
                    wanted[appId] = item;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, SteamStoreItem>>(wanted);
        }

        public Task<SteamTagVocabulary> GetTagListAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(SteamTagVocabulary.Empty);

        public Task<SteamStoreCategoryVocabulary> GetStoreCategoriesAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(SteamStoreCategoryVocabulary.Empty);
    }

    private sealed class ThrowingStoreItems : ISteamStoreClient
    {
        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetItemsAsync(
            IEnumerable<string> appIds, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => throw new InvalidOperationException("store unreachable");

        public Task<IReadOnlyDictionary<string, SteamStoreItem>> GetCachedItemsAsync(
            IEnumerable<string> appIds, CancellationToken ct = default)
            => throw new InvalidOperationException("store unreachable");

        public Task<SteamTagVocabulary> GetTagListAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => throw new InvalidOperationException("store unreachable");

        public Task<SteamStoreCategoryVocabulary> GetStoreCategoriesAsync(
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => throw new InvalidOperationException("store unreachable");
    }

    private sealed class ThrowingSteamTargets : ISteamMaturityTargetSource
    {
        public Task<IReadOnlyList<SteamMaturityTarget>> GetTargetsAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("database unavailable");
    }
}
