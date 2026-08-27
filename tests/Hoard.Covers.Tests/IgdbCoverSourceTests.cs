using Hoard.Covers.Igdb;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Covers.Tests;

/// <summary>
/// The gap-filler. Steam's capsule answers most of a library and 404s for the
/// rest — 96 games on the library this was measured against, 64 of which IGDB
/// has art for. These tests fix the three things that decide whether that fix is
/// visible: who wins, what is requested, and how much it costs.
/// </summary>
public class IgdbCoverSourceTests
{
    private static IgdbCoverOptions FastBatching() => new()
    {
        BatchLinger = TimeSpan.FromMilliseconds(30),
        PrewarmFromNegativeMarkers = false,
    };

    private static IgdbCoverSource Source(
        FakeIgdbClient igdb,
        FakeCoverCdn cdn,
        CoverCacheOptions coverOptions,
        IgdbCoverOptions? options = null)
        => new(igdb, cdn, coverOptions, options ?? FastBatching(), NullLogger<IgdbCoverSource>.Instance);

    [Fact]
    public async Task Declines_cleanly_when_igdb_is_not_configured()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient { Configured = false };
        igdb.AddCover("223710", "co6m51");
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        var source = Source(igdb, cdn, dir.Options());

        Assert.Null(await source.TryFetchAsync(CoverKey.Steam("223710")));

        // Not a lookup, not a request, not an exception: exactly the behaviour
        // of an app with no credentials before this source existed.
        Assert.Equal(0, igdb.BatchCount);
        Assert.Equal(0, cdn.RequestCount);
    }

    [Fact]
    public async Task An_unconfigured_source_is_a_no_op_for_the_whole_pipeline()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient { Configured = false };

        using var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            Source(igdb, cdn, options));

        Assert.Null(await pipeline.GetAsync(CoverKey.Steam("228980"), 320));

        // Two Steam capsule paths and nothing else.
        Assert.Equal(2, cdn.RequestCount);
    }

    [Fact]
    public async Task Fills_the_gap_only_after_steam_declines()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        // 223710 is Cry of Fear: delisted, no Steam portrait capsule, IGDB art.
        igdb.AddCover("223710", "co6m51");
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        using var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            Source(igdb, cdn, options));

        using var art = await pipeline.GetAsync(CoverKey.Steam("223710"), 320);
        Assert.NotNull(art);

        // Both Steam capsule shapes were tried first and 404'd, then IGDB.
        Assert.Contains("/steam/apps/223710/library_600x900_2x.jpg", cdn.Requests);
        Assert.Contains("/steam/apps/223710/library_600x900.jpg", cdn.Requests);
        Assert.Equal("/igdb/image/upload/t_cover_big_2x/co6m51.jpg", cdn.Requests[^1]);

        // And no negative marker: a source answered.
        Assert.False(File.Exists(new CoverDiskCache(options).NegativePath(CoverKey.Steam("223710"))));
    }

    [Fact]
    public async Task Steam_wins_when_both_have_art()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        cdn.AddCapsule("220", TestArt.Capsule());
        igdb.AddCover("220", "cohl2");
        cdn.AddIgdbCover("cohl2", TestArt.Capsule(528, 704));

        using var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            Source(igdb, cdn, options));

        using var art = await pipeline.GetAsync(CoverKey.Steam("220"), 320);
        Assert.NotNull(art);

        // The 600x900 portrait §5 is drawn around, and IGDB was never consulted:
        // registration order is the policy.
        Assert.Equal(["/steam/apps/220/library_600x900_2x.jpg"], cdn.Requests);
        Assert.Equal(0, igdb.BatchCount);
    }

    [Fact]
    public async Task Requests_the_2x_cover_rendition_not_the_soft_one()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        // IIgdbClient hands back t_cover_big — 264x352, verified live, and too
        // soft for a 200-300px tile on a high-DPI display. Only the 2x rendition
        // is registered on the fake CDN, so asking for anything else 404s.
        igdb.AddCover("223710", "co6m51");
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        var source = Source(igdb, cdn, dir.Options());
        var bytes = await source.TryFetchAsync(CoverKey.Steam("223710"));

        Assert.NotNull(bytes);
        Assert.Equal(["/igdb/image/upload/t_cover_big_2x/co6m51.jpg"], cdn.Requests);
    }

    [Theory]
    [InlineData(
        "https://images.igdb.com/igdb/image/upload/t_cover_big/co6m51.jpg",
        "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co6m51.jpg")]
    [InlineData(
        "//images.igdb.com/igdb/image/upload/t_thumb/co6m51.jpg",
        "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co6m51.jpg")]
    [InlineData(
        "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co6m51.jpg",
        "https://images.igdb.com/igdb/image/upload/t_cover_big_2x/co6m51.jpg")]
    public void The_size_token_is_rewritten_in_place(string given, string expected)
        => Assert.Equal(expected, IgdbImageUrl.WithSize(given, "t_cover_big_2x"));

    [Fact]
    public void A_url_shape_we_do_not_recognise_is_declined_rather_than_guessed_at()
    {
        Assert.Null(IgdbImageUrl.WithSize(null, "t_cover_big_2x"));
        Assert.Null(IgdbImageUrl.WithSize("   ", "t_cover_big_2x"));
        Assert.Null(IgdbImageUrl.WithSize("images.igdb.com/no/scheme.jpg", "t_cover_big_2x"));

        // A path with no size segment is left exactly as it is.
        Assert.Equal(
            "https://images.igdb.com/some/other/shape.jpg",
            IgdbImageUrl.WithSize("https://images.igdb.com/some/other/shape.jpg", "t_cover_big_2x"));
    }

    [Fact]
    public async Task Lookups_are_batched_rather_than_one_per_key()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        var appIds = Enumerable.Range(9000, 40).Select(i => i.ToString()).ToArray();
        foreach (var appId in appIds)
        {
            igdb.AddCover(appId, "co" + appId);
            cdn.AddIgdbCover("co" + appId, TestArt.Capsule(264, 352));
        }

        // A generous linger, so the assertion is about batching and not about
        // how fast the test machine schedules 40 tasks.
        var source = Source(igdb, cdn, dir.Options(), new IgdbCoverOptions
        {
            BatchLinger = TimeSpan.FromMilliseconds(400),
            PrewarmFromNegativeMarkers = false,
        });

        var bytes = await Task.WhenAll(appIds.Select(id => source.TryFetchAsync(CoverKey.Steam(id))));
        Assert.All(bytes, b => Assert.NotNull(b));

        // The point of ResolveBySteamAppIdsAsync: 40 covers, not 40 lookups.
        Assert.True(igdb.BatchCount <= 2, $"{igdb.BatchCount} lookups for 40 covers");
        Assert.Equal(40, igdb.AppIdsRequested);
    }

    [Fact]
    public async Task A_repeated_key_is_never_looked_up_twice()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();
        igdb.AddWithoutCover("228980");

        var source = Source(igdb, cdn, dir.Options());

        Assert.Null(await source.TryFetchAsync(CoverKey.Steam("228980")));
        Assert.Null(await source.TryFetchAsync(CoverKey.Steam("228980")));
        Assert.Null(await source.TryFetchAsync(CoverKey.Steam("228980")));

        // "IGDB has no cover for this appid" is an answer worth remembering.
        Assert.Equal(1, igdb.BatchCount);
    }

    [Fact]
    public async Task The_previous_runs_negative_markers_are_pre_warmed_in_one_batch()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var disk = new CoverDiskCache(options);
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        // What a Steam-only run leaves behind: one .none per game with no
        // capsule. That set is exactly the set IGDB should be asked about.
        var appIds = Enumerable.Range(9000, 30).Select(i => i.ToString()).ToArray();
        foreach (var appId in appIds)
        {
            disk.MarkMissing(CoverKey.Steam(appId), "steam-capsule");
            igdb.AddCover(appId, "co" + appId);
            cdn.AddIgdbCover("co" + appId, TestArt.Capsule(264, 352));
        }

        var source = Source(igdb, cdn, options, new IgdbCoverOptions
        {
            BatchLinger = TimeSpan.FromMilliseconds(30),
            PrewarmFromNegativeMarkers = true,
        });

        // One key asked for; the pre-warm answers the other 29 at the same time.
        Assert.NotNull(await source.TryFetchAsync(CoverKey.Steam("9000")));
        Assert.Equal(1, igdb.BatchCount);

        foreach (var appId in appIds)
        {
            Assert.NotNull(await source.TryFetchAsync(CoverKey.Steam(appId)));
        }

        Assert.Equal(1, igdb.BatchCount);
        Assert.Equal(30, igdb.AppIdsRequested);
    }

    [Fact]
    public async Task A_failed_lookup_surfaces_rather_than_reading_as_no_art()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient { FailWith = new HttpRequestException("igdb is down") };

        var source = Source(igdb, cdn, options);
        await Assert.ThrowsAsync<HttpRequestException>(
            () => source.TryFetchAsync(CoverKey.Steam("223710")));

        // And through the pipeline that means "not yet", with no 30-day marker.
        using var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            Source(igdb, cdn, options));

        Assert.Null(await pipeline.GetAsync(CoverKey.Steam("223710"), 320));
        Assert.False(
            File.Exists(new CoverDiskCache(options).NegativePath(CoverKey.Steam("223710"))),
            "an IGDB outage wrote a 30-day .none marker");
    }

    [Fact]
    public async Task An_igdb_image_404_is_an_answer_but_any_other_status_is_not()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();

        // A cover url IGDB knows about whose asset is not on the CDN.
        igdb.AddCover("223710", "co6m51");

        var source = Source(igdb, cdn, dir.Options());
        Assert.Null(await source.TryFetchAsync(CoverKey.Steam("223710")));
    }

    [Fact]
    public async Task A_key_shape_nobody_mints_is_not_this_sources_business()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();
        var source = Source(igdb, cdn, dir.Options());

        var key = new CoverKey("some-other-store", "1234");
        Assert.False(source.CanHandle(key));
        Assert.Null(await source.TryFetchAsync(key));
        Assert.Equal(0, igdb.BatchCount);
        Assert.Equal(0, cdn.RequestCount);
    }

    // ── Keys that name the artwork, not the game ─────────────────────────────

    /// <summary>
    /// <b>This used to assert the opposite, and the opposite was the bug.</b>
    /// An IGDB-provider key was "not this source's business" because every
    /// cover key in the app was a Steam appid — which meant a release with no
    /// Steam id, i.e. every Epic and GOG title in the library, had no key at
    /// all and rendered a placeholder no matter what metadata had been fetched
    /// for it. The key now carries IGDB's <c>image_id</c>, taken from the
    /// <c>cover_url</c> enrichment already stored, and this source serves it.
    /// </summary>
    [Fact]
    public async Task An_igdb_image_id_key_is_fetched_straight_from_the_cdn()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        var source = Source(igdb, cdn, dir.Options());
        var key = CoverKey.Igdb("co6m51");

        Assert.True(source.CanHandle(key));
        Assert.NotNull(await source.TryFetchAsync(key));

        // No external_games lookup: the key IS the asset, so re-deriving it
        // would spend a request to learn an id already in hand.
        Assert.Equal(0, igdb.BatchCount);
        Assert.Equal("/igdb/image/upload/t_cover_big_2x/co6m51.jpg", cdn.Requests[^1]);
    }

    /// <summary>
    /// And it works with no credentials at all. images.igdb.com is
    /// unauthenticated, so a machine whose Twitch credentials were revoked keeps
    /// the art it has already learned about — which is most of what makes this
    /// route worth having for the stores that cannot reach IGDB directly.
    /// </summary>
    [Fact]
    public async Task An_igdb_image_id_key_does_not_need_credentials()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient { Configured = false };
        cdn.AddIgdbCover("co1r76", TestArt.Capsule(528, 704));

        var source = Source(igdb, cdn, dir.Options());

        Assert.NotNull(await source.TryFetchAsync(CoverKey.Igdb("co1r76")));
        Assert.Equal(0, igdb.BatchCount);
    }

    /// <summary>
    /// A 404 from the image CDN is still "no art", not a transport failure — the
    /// same distinction the appid path draws, so the pipeline's 30-day negative
    /// marker means the same thing on both.
    /// </summary>
    [Fact]
    public async Task An_unknown_image_id_declines_rather_than_throwing()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();
        var source = Source(igdb, cdn, dir.Options());

        Assert.Null(await source.TryFetchAsync(CoverKey.Igdb("conotreal")));
    }

    /// <summary>
    /// The whole point, at pipeline level: a key with no Steam appid behind it
    /// still resolves. Steam's capsule source declines it on shape without a
    /// request, and IGDB answers.
    /// </summary>
    [Fact]
    public async Task The_pipeline_resolves_a_key_that_has_no_steam_appid_behind_it()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient();
        cdn.AddIgdbCover("co6m51", TestArt.Capsule(528, 704));

        using var pipeline = dir.Pipeline(
            options,
            new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance),
            Source(igdb, cdn, options));

        using var art = await pipeline.GetAsync(CoverKey.Igdb("co6m51"), 320);

        Assert.NotNull(art);

        // One request in total: Steam never guessed at an appid it does not have.
        Assert.Equal(1, cdn.RequestCount);
        Assert.Equal("/igdb/image/upload/t_cover_big_2x/co6m51.jpg", cdn.Requests[0]);
    }

    // ── cover_url → key ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg", "co1r76")]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_thumb/co6m51.png", "co6m51")]
    [InlineData("//images.igdb.com/igdb/image/upload/t_cover_big_2x/co2abc.jpg", "co2abc")]
    // A cache-busting query string does not change which asset is named.
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/co1r76.jpg?v=2", "co1r76")]
    public void An_igdb_cover_url_yields_its_image_id(string url, string expected)
        => Assert.Equal(expected, IgdbImageUrl.ImageId(url));

    /// <summary>
    /// Strict on purpose. A guessed id becomes a 404, a 404 becomes a 30-day
    /// negative marker, and the user gets a month of placeholder art for a game
    /// whose cover we were holding all along — so a URL shape we do not
    /// recognise yields no key rather than a plausible one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://cdn.example.com/some/other/art.jpg")]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/co 1r76.jpg")]
    public void A_url_that_is_not_an_igdb_image_yields_no_id(string? url)
        => Assert.Null(IgdbImageUrl.ImageId(url));
}
