using System.Net;
using Winnow.App.Services;
using Winnow.Covers;
using Winnow.Enrich.Updates;
using Winnow.Enrich.Updates.Model;
using Xunit;

namespace Winnow.Tests.Updates;

public sealed class SteamLibraryAssetLookupTests
{
    private const string Capsule = "f72e5a29377820dd3d1d76e0c2eb5a8fcfa34c7d/library_capsule_2x.jpg";
    private const string LegacyCapsule = "30259337ac341e9fbe5410650e7aabf1b63796b6/library_600x900_2x.jpg";

    [Fact]
    public async Task Published_paths_preserve_rendition_order_and_share_the_appinfo_cache()
    {
        using var host = Host($$$"""
            {"name":"RuneScape: Dragonwilds","type":"Game","library_assets_full":{
              "library_capsule":{"image2x":{"german":"{{{LegacyCapsule}}}","english":"{{{Capsule}}}"},
                "image":{"english":"small/library_capsule.jpg","french":"{{{Capsule}}}"}},
              "library_hero":{"image":{"english":"hero/library_hero.jpg"},
                "image2x":{"english":"hero/library_hero_2x.jpg"}}
            }}
            """);
        var lookup = new SteamLibraryAssetLookup(host.Builds);
        Assert.Equal(new[] { Capsule, LegacyCapsule, "small/library_capsule.jpg" },
            await lookup.GetPathsAsync(CoverKey.Steam("1374490")));
        Assert.Equal(new[] { "hero/library_hero_2x.jpg" }, await lookup.GetPathsAsync(CoverKey.SteamHero("1374490")));
        Assert.Equal(new[] { "hero/library_hero.jpg" }, await lookup.GetPathsAsync(CoverKey.SteamHeroStandard("1374490")));
        var info = await host.Builds.GetAppInfoAsync("1374490");
        Assert.True(info.ServedFromCache);
        Assert.Equal("RuneScape: Dragonwilds", info.Info!.Name);
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    [Fact]
    public async Task Missing_english_uses_other_locales_and_standard_hero_never_answers_high_resolution()
    {
        using var host = Host("""
            {"name":"Game","library_assets_full":{
              "library_capsule":{"image2x":{"german":"de/capsule.jpg","french":"fr/capsule.jpg"}},
              "library_hero":{"image":{"english":"hero.jpg"}}
            }}
            """);
        var lookup = new SteamLibraryAssetLookup(host.Builds);
        Assert.Equal(new[] { "fr/capsule.jpg", "de/capsule.jpg" }, await lookup.GetPathsAsync(CoverKey.Steam("1374490")));
        Assert.Empty((await lookup.GetPathsAsync(CoverKey.SteamHero("1374490")))!);
        Assert.Equal(new[] { "hero.jpg" }, await lookup.GetPathsAsync(CoverKey.SteamHeroStandard("1374490")));
    }

    [Fact]
    public async Task Artwork_reuses_recent_appinfo_but_refreshes_after_seven_days()
    {
        var path = "original/capsule.jpg";
        using var host = new UpdateSignalTestHost((_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK,
            "{\"data\":{\"1374490\":{\"common\":{\"name\":\"Game\",\"library_assets_full\":{\"library_capsule\":{\"image2x\":{\"english\":\""
            + path + "\"}}}}}}}"), options => options.AppInfoCacheTtl = TimeSpan.FromDays(30),
            now: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        await host.Builds.GetAppInfoAsync("1374490");
        var lookup = new SteamLibraryAssetLookup(host.Builds);
        host.Clock.Advance(TimeSpan.FromDays(6));
        path = "updated/capsule.jpg";
        Assert.Equal(new[] { "original/capsule.jpg" }, await lookup.GetPathsAsync(CoverKey.Steam("1374490")));
        Assert.Equal(1, host.Handler.CountFor(UpdateHost.SteamCmd));
        host.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(new[] { "updated/capsule.jpg" }, await lookup.GetPathsAsync(CoverKey.Steam("1374490")));
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamCmd));
        Assert.True((await host.Builds.GetAppInfoAsync("1374490")).ServedFromCache);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("false")]
    [InlineData("{\"library_capsule\":17,\"library_hero\":[]}")]
    [InlineData("{\"library_capsule\":{\"image\":\"bad\",\"image2x\":{\"english\":{},\"french\":null,\"german\":\" \"}}}")]
    [InlineData("{\"library_capsule\":{\"image2x\":{\"english\":\"\"}}}")]
    [InlineData("{\"library_capsule\":{\"image2x\":{\"english\":\"valid.jpg\",\"german\":false}}}")]
    public async Task Malformed_optional_artwork_keeps_name_type_and_parent(string assets)
    {
        using var host = Host("{\"name\":\"Example\",\"type\":\"Demo\",\"parent\":\"620\",\"library_assets_full\":" + assets + "}");
        var info = await host.Builds.GetAppInfoAsync("1374490");
        Assert.Equal(AppInfoOutcome.Ok, info.Outcome);
        Assert.Equal("Example", info.Info!.Name);
        Assert.Equal("Demo", info.Info.Type);
        Assert.Equal("620", info.Info.ParentAppId);
        Assert.True(info.Info.LibraryAssets!.IsMalformed);
        Assert.Null(await new SteamLibraryAssetLookup(host.Builds).GetPathsAsync(CoverKey.Steam("1374490")));
    }

    [Fact]
    public async Task Unavailable_metadata_is_not_an_authoritative_absence_or_cached()
    {
        var failing = true;
        using var host = new UpdateSignalTestHost((_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK,
            failing ? "not-json" : "{\"data\":{\"1374490\":{\"common\":{\"name\":\"Game\"}}}}"));
        var lookup = new SteamLibraryAssetLookup(host.Builds);
        Assert.Null(await lookup.GetPathsAsync(CoverKey.Steam("1374490")));
        failing = false;
        Assert.Empty((await lookup.GetPathsAsync(CoverKey.Steam("1374490")))!);
        Assert.Empty((await lookup.GetPathsAsync(CoverKey.Steam("1374490")))!);
        Assert.Equal(2, host.Handler.CountFor(UpdateHost.SteamCmd));
    }

    [Fact]
    public async Task Candidate_count_is_bounded_after_deduplication()
    {
        var locales = string.Join(",", Enumerable.Range(0, 30).Select(i => $"\"locale{i:00}\":\"{i}.jpg\""));
        using var host = Host("{\"library_assets_full\":{\"library_capsule\":{\"image2x\":{" + locales
            + "},\"image\":{\"english\":\"standard.jpg\",\"french\":\"fr.jpg\",\"german\":\"de.jpg\",\"japanese\":\"jp.jpg\"}}}}");
        var paths = await new SteamLibraryAssetLookup(host.Builds).GetPathsAsync(CoverKey.Steam("1374490"));
        Assert.Equal(8, paths!.Count);
        Assert.Equal("standard.jpg", paths[4]);
    }

    [Fact]
    public async Task Oversized_path_is_unavailable_without_discarding_name()
    {
        using var host = Host("{\"name\":\"Game\",\"library_assets_full\":{\"library_capsule\":{\"image2x\":{\"english\":\""
            + new string('a', 513) + "\"}}}}");
        var info = await host.Builds.GetAppInfoAsync("1374490");
        Assert.Equal("Game", info.Info!.Name);
        Assert.Null(await new SteamLibraryAssetLookup(host.Builds).GetPathsAsync(CoverKey.Steam("1374490")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"library_capsule\":{}}")]
    [InlineData("{\"library_capsule\":{\"image2x\":{}}}")]
    public async Task Absent_assets_remain_authoritative(string assets)
    {
        using var host = Host("{\"name\":\"Game\",\"library_assets_full\":" + assets + "}");
        Assert.Empty((await new SteamLibraryAssetLookup(host.Builds).GetPathsAsync(CoverKey.Steam("1374490")))!);
    }

    private static UpdateSignalTestHost Host(string common)
        => new((_, _) => FakeUpdateHandler.Json(HttpStatusCode.OK,
            "{\"data\":{\"1374490\":{\"common\":" + common + "}}}"));

    [Fact]
    public async Task Concurrent_renditions_wait_for_the_same_apps_cache_fill()
    {
        var client = new DelayedClient();
        var lookup = new SteamLibraryAssetLookup(client);
        var capsule = lookup.GetPathsAsync(CoverKey.Steam("1374490"));
        var hero = lookup.GetPathsAsync(CoverKey.SteamHero("1374490"));
        using var cancelled = new CancellationTokenSource();
        var waiting = lookup.GetPathsAsync(CoverKey.SteamHeroStandard("1374490"), cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(1, client.Fetches);
        client.Release.SetResult();
        await Task.WhenAll(capsule, hero);
        Assert.Equal(1, client.Fetches);
    }

    private sealed class DelayedClient : IBuildInfoClient
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Fetches { get; private set; }
        private bool _cached;

        public async Task<AppInfoFetch> GetAppInfoAsync(string appId, TimeSpan? cacheTtl = null,
            bool cachedOnly = false, CancellationToken ct = default)
        {
            if (!_cached)
            {
                Fetches++;
                await Release.Task.WaitAsync(ct);
                _cached = true;
            }
            return AppInfoFetch.Ok(new SteamAppInfo(appId, "Game", "Game", null));
        }

        public Task<BuildInfoFetch> GetPublicBranchAsync(string appId, TimeSpan? cacheTtl = null,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}
