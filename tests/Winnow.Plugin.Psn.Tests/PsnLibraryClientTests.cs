using System.Text;
using System.Text.Json;
using Winnow.Plugin.Psn;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.Psn.Tests;

public sealed class PsnLibraryClientTests
{
    [Fact]
    public async Task Purchases_use_verified_query_parameters_and_leave_unobserved_facts_unknown()
    {
        var h = new Host();
        var snapshot = (await h.Read())!;
        var game = Assert.Single(snapshot.Games);
        Assert.Equal("title:CUSA00001_00", game.Library.SourceId);
        Assert.Equal(Host.Account, game.Library.AccountRef);
        Assert.Equal("Fixture game", game.Library.Title);
        Assert.Equal("title:CUSA00001_00", game.Library.ExternalIds["plugin:psn"]);
        Assert.Contains("purchase library", game.Library.LibrarySourceLabel);
        Assert.Null(game.Library.Installed);
        Assert.Null(game.Library.PlaytimeMinutes);
        Assert.Null(game.Library.LastPlayedAt);
        Assert.Null(game.Library.AcquiredAt);
        Assert.Empty(game.Library.Actions);
        Assert.Equal(["PS4"], game.Metadata.Tags);
        var request = Assert.Single(h.Requests);
        Assert.Equal("Bearer fixture-access-token", request.Headers["Authorization"]);
        var query = Uri.UnescapeDataString(request.Url);
        Assert.Contains("operationName=getPurchasedGameList", query);
        Assert.Contains("\"isActive\":true", query);
        Assert.Contains("\"platform\":[\"ps4\",\"ps5\"]", query);
        Assert.Contains("\"size\":24", query);
        Assert.Contains("\"start\":0", query);
        Assert.Contains("827a423f6a8ddca4107ac01395af2ec0eafd8396fc7fa204aaf9b7ed2eefa168", query);
        Assert.DoesNotContain(h.CacheEntries.Values, x => Encoding.UTF8.GetString(x.Payload).Contains("fixture-access-token"));
    }

    [Fact]
    public async Task Played_history_joins_only_exact_title_and_preserves_unknown_duration()
    {
        var h = new Host
        {
            Played = History("titles", [Played("CUSA00001_00", "PT2H3M59S"), Played("PPSA00002_00", "bad-duration", "ps5_native_game")])
        };
        var snapshot = (await h.Read(played: true))!;
        Assert.Equal(2, snapshot.Games.Count);
        var owned = snapshot.Games.Single(x => x.Library.SourceId == "title:CUSA00001_00");
        Assert.Equal(123, owned.Library.PlaytimeMinutes);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T10:00:00Z"), owned.Library.LastPlayedAt);
        Assert.Equal("Fixture game", owned.Library.Title);
        Assert.Contains("purchase library and played history", owned.Library.LibrarySourceLabel);
        Assert.Equal(["ACTION", "ADVENTURE"], owned.Metadata.Genres);
        var history = snapshot.Games.Single(x => x.Library.SourceId == "title:PPSA00002_00");
        Assert.Null(history.Library.PlaytimeMinutes);
        Assert.Contains("ownership unverified", history.Library.LibrarySourceLabel);
        Assert.Null(history.Library.AcquiredAt);
        Assert.Contains(h.Requests, x => x.Url.Contains("/users/" + Host.Account + "/titles?limit=200&offset=0&categories="));
    }

    [Theory]
    [InlineData("PT0S", 0L)]
    [InlineData("PT59S", 0L)]
    [InlineData("P1DT1H2M", 1502L)]
    [InlineData("P1Y", null)]
    [InlineData("P1M", null)]
    [InlineData("-PT1H", null)]
    [InlineData("PT999999999999999H", null)]
    [InlineData(null, null)]
    public async Task Duration_is_converted_without_inventing_unknown_time(string? duration, long? expected)
    {
        var h = new Host { Played = History("titles", [Played("CUSA00001_00", duration)]) };
        Assert.Equal(expected, Assert.Single((await h.Read(played: true))!.Games).Library.PlaytimeMinutes);
    }

    [Fact]
    public async Task Legacy_history_keeps_its_distinct_ids_and_never_uses_trophy_sync_as_last_played()
    {
        var h = new Host
        {
            Legacy = History("trophyTitles", [Trophy("NPWR00001_00", "PS3,PSVITA"), Trophy("NPWR00002_00", "PS4"), Trophy("NPWR00003_00", "PS3,PS4"), Trophy("NPWR00004_00", "PS5")])
        };
        var snapshot = (await h.Read(legacy: true))!;
        var legacy = Assert.Single(snapshot.Games, x => x.Library.SourceId.StartsWith("trophy:"));
        Assert.Equal("trophy:trophy:NPWR00001_00", legacy.Library.SourceId);
        Assert.Equal(["PS3", "PS Vita"], legacy.Metadata.Tags);
        Assert.Null(legacy.Library.LastPlayedAt);
        Assert.Null(legacy.Library.PlaytimeMinutes);
        Assert.Contains("trophy history", legacy.Library.LibrarySourceLabel);
        Assert.Equal(2, snapshot.Games.Count);
        h.Requests.Clear();
        Assert.Equal(2, (await h.Read(legacy: true))!.Games.Count);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Purchases_page_until_short_result_and_collapse_multiple_entitlements_for_one_title()
    {
        var h = new Host();
        h.Respond = request => PurchaseOffset(request) switch
        {
            0 => Response(Purchases(Enumerable.Range(1, 24).Select(i => Purchase($"CUSA{i:00000}_00")).ToArray())),
            24 => Response(Purchases([Purchase("CUSA00001_00", "another-entitlement"), Purchase("CUSA00025_00")])),
            _ => throw new InvalidOperationException()
        };
        var snapshot = (await h.Read())!;
        Assert.Equal(25, snapshot.Games.Count);
        Assert.Equal(2, h.Requests.Count);
        Assert.Single(h.CacheEntries);
    }

    [Fact]
    public async Task Repeated_page_preserves_complete_stale_purchases()
    {
        var h = new Host();
        await h.Read();
        var original = Assert.Single(h.CacheEntries).Value;
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.Respond = _ => Response(Purchases(Enumerable.Range(1, 24).Select(i => Purchase($"CUSA{i:00000}_00")).ToArray()));
        Assert.Single((await h.Read())!.Games);
        Assert.Same(original, Assert.Single(h.CacheEntries).Value);
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task Failed_later_page_keeps_prior_complete_history_and_other_endpoint_can_refresh()
    {
        var h = new Host { Played = History("titles", [Played("PPSA00002_00", "PT3H", "ps5_native_game")]) };
        await h.Read(played: true);
        var original = h.CacheEntries.Single(x => x.Key.EndsWith(":played")).Value;
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.Respond = request => request.Url.Contains("graphql") ? Response(Purchases([Purchase("CUSA00003_00")])) :
            request.Url.Contains("offset=0") ? Response(History("titles", [Played("CUSA00004_00", "PT1H")], 2, 1)) : new(503, [], new Dictionary<string, string>());
        var snapshot = (await h.Read(played: true))!;
        Assert.Equal(["title:CUSA00003_00", "title:PPSA00002_00"], snapshot.Games.Select(x => x.Library.SourceId));
        Assert.Same(original, h.CacheEntries.Single(x => x.Key.EndsWith(":played")).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(null)]
    public async Task Noncontiguous_or_absent_continuation_cannot_replace_complete_history(int? next)
    {
        var h = new Host { PurchaseStatus = 503, Played = History("titles", [Played("CUSA00001_00", "PT1H")], 2, next) };
        Assert.Null(await h.Read(played: true));
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task History_total_changes_and_duplicate_titles_are_rejected()
    {
        var h = new Host { PurchaseStatus = 503 };
        h.Respond = request => request.Url.Contains("graphql") ? new(503, [], new Dictionary<string, string>()) :
            request.Url.Contains("offset=0") ? Response(History("titles", [Played("CUSA00001_00", "PT1H")], 2, 1)) :
            Response(History("titles", [Played("CUSA00002_00", "PT1H")], 3, 2));
        Assert.Null(await h.Read(played: true));
        h.Respond = null;
        h.Played = History("titles", [Played("CUSA00001_00", "PT1H"), Played("CUSA00001_00", "PT1H")]);
        Assert.Null(await h.Read(played: true));
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task Complete_contiguous_history_is_cached_after_its_final_page()
    {
        var h = new Host { PurchaseStatus = 503 };
        h.Respond = request => request.Url.Contains("graphql") ? new(503, [], new Dictionary<string, string>()) :
            request.Url.Contains("offset=0") ? Response(History("titles", [Played("CUSA00001_00", "PT1H")], 2, 1)) :
            Response(History("titles", [Played("CUSA00002_00", "PT2H")], 2));
        var games = (await h.Read(played: true))!.Games;
        Assert.Equal(2, games.Count);
        Assert.Equal([60L, 120L], games.Select(x => x.Library.PlaytimeMinutes));
        var cache = Assert.Single(h.CacheEntries);
        Assert.EndsWith(":played", cache.Key);
        h.Respond = _ => throw new HttpRequestException();
        h.Clock.Now = h.Clock.Now.AddHours(7);
        Assert.Equal(2, (await h.Read(played: true))!.Games.Count);
    }

    [Fact]
    public async Task Purchase_item_ceiling_cannot_cache_a_truncated_inventory()
    {
        var h = new Host();
        h.Respond = request => Response(Purchases(Enumerable.Range(PurchaseOffset(request) + 1, 24)
            .Select(i => Purchase($"CUSA{i:00000}_00")).ToArray()));
        Assert.Null(await h.Read());
        Assert.Equal(417, h.Requests.Count);
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task Confirmed_empty_inventory_and_disabled_history_are_distinct_from_unavailable()
    {
        var h = new Host { Purchases = Purchases([]), Played = History("titles", [Played("CUSA00001_00", "PT1H")]) };
        Assert.Empty((await h.Read())!.Games);
        Assert.Single(h.Requests);
        Assert.Single((await h.Read(played: true))!.Games);
        Assert.Equal(2, h.Requests.Count);
        Assert.Empty((await h.Read())!.Games);
        Assert.Equal(2, h.CacheEntries.Count);
    }

    [Fact]
    public async Task Invalid_cache_payload_and_mismatched_account_cache_cannot_supply_results()
    {
        var h = new Host();
        await h.Read();
        var cached = Assert.Single(h.CacheEntries);
        h.PurchaseStatus = 503;
        h.CacheEntries[cached.Key] = cached.Value with { Payload = Encoding.UTF8.GetBytes("null") };
        Assert.Null(await h.Read());
        h.CacheEntries[cached.Key] = cached.Value with
        {
            Payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(cached.Value.Payload).Replace(Host.Account, "2222222222222222"))
        };
        Assert.Null(await h.Read());
    }

    [Fact]
    public async Task Unsupported_platforms_are_not_imported_and_same_names_do_not_merge()
    {
        var h = new Host
        {
            Played = History("titles", [Played("PPSA00002_00", "PT1H", "ps5_native_game"), Played("PPSA00003_00", "PT1H", "unknown")])
        };
        Assert.Equal(2, (await h.Read(played: true))!.Games.Count);
    }

    [Fact]
    public async Task Fresh_and_expired_caches_are_account_scoped_and_failed_auth_cannot_replace_them()
    {
        var h = new Host();
        await h.Read();
        await h.Read();
        Assert.Single(h.Requests);
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.PurchaseStatus = 401;
        Assert.Single((await h.Read())!.Games);
        var second = Host.Session with { AccountId = "2222222222222222", Scope = "other-account-hash" };
        Assert.Null(await h.Client.GetAsync(second, false, false, CancellationToken.None));
        Assert.Single(h.CacheEntries);
        h.PurchaseStatus = 403;
        Assert.Single((await h.Read())!.Games);
    }

    [Fact]
    public async Task Unavailable_purchase_endpoint_leaves_played_and_legacy_endpoints_available()
    {
        var h = new Host
        {
            PurchaseStatus = 503,
            Played = History("titles", [Played("CUSA00001_00", "PT1H")]),
            Legacy = History("trophyTitles", [Trophy("NPWR00001_00", "PS3")])
        };
        Assert.Equal(2, (await h.Read(played: true, legacy: true))!.Games.Count);
        Assert.Equal(2, h.CacheEntries.Count);
    }

    [Fact]
    public async Task Graphql_errors_oversize_responses_and_invalid_identity_preserve_prior_snapshot()
    {
        var h = new Host();
        await h.Read();
        h.Clock.Now = h.Clock.Now.AddHours(7);
        var original = Assert.Single(h.CacheEntries).Value;
        foreach (var body in new[]
        {
            "{\"errors\":[{\"message\":\"fixture error\"}],\"data\":{\"purchasedTitlesRetrieve\":{\"games\":[]}}}",
            new string(' ', 2 * 1024 * 1024 + 1),
            Purchases([Purchase("../other-account")]),
            Purchases([new { titleId = "CUSA00001_00", name = "Fixture game" }]),
            "{\"data\":null}"
        })
        {
            h.Purchases = body;
            Assert.Single((await h.Read())!.Games);
            Assert.Same(original, Assert.Single(h.CacheEntries).Value);
        }
    }

    [Fact]
    public async Task Wrong_account_and_excessive_total_are_not_cached()
    {
        var h = new Host { PurchaseStatus = 503 };
        h.Played = "{\"titles\":[],\"totalItemCount\":0,\"accountId\":\"2222222222222222\"}";
        Assert.Null(await h.Read(played: true));
        h.Played = History("titles", [], 10001, 200);
        Assert.Null(await h.Read(played: true));
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task Malformed_history_rows_cannot_become_confirmed_empty_results()
    {
        var h = new Host
        {
            PurchaseStatus = 503,
            Played = History("titles", [new { titleId = "CUSA00001_00", name = "Fixture game" }]),
            Legacy = History("trophyTitles", [new { trophyTitleName = "Fixture game", trophyTitlePlatform = "PS3" }])
        };
        Assert.Null(await h.Read(played: true, legacy: true));
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task Cancellation_after_transport_return_does_not_publish_or_cache_late_data()
    {
        var h = new Host();
        using var cancellation = new CancellationTokenSource();
        h.Respond = _ => { cancellation.Cancel(); return Response(h.Purchases); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Read(ct: cancellation.Token));
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task Transport_failure_preserves_stale_cache_but_cache_write_failure_keeps_current_complete_result()
    {
        var h = new Host();
        await h.Read();
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.Respond = _ => throw new HttpRequestException();
        Assert.Single((await h.Read())!.Games);
        h.Respond = null;
        h.CacheWriteFailure = true;
        h.Purchases = Purchases([Purchase("CUSA00002_00")]);
        Assert.Equal("title:CUSA00002_00", Assert.Single((await h.Read())!.Games).Library.SourceId);
    }

    [Fact]
    public async Task Invalid_dates_and_untrusted_image_hosts_stay_unknown()
    {
        var h = new Host { Purchases = Purchases([]) };
        h.Played = History("titles", [new
        {
            titleId = "CUSA00001_00", name = "Fixture game", category = "ps4_game", lastPlayedDateTime = "2099-01-01T00:00:00Z",
            playDuration = "PT1H", imageUrl = "https://attacker.example/icon.png", localizedImageUrl = "https://image.api.playstation.com:1234/icon.png"
        }]);
        var game = Assert.Single((await h.Read(played: true))!.Games);
        Assert.Null(game.Library.LastPlayedAt);
        Assert.Null(game.IconUrl);
    }

    private static object Purchase(string id, string? entitlement = null) => new
    {
        titleId = id, name = "Fixture game", platform = "PS4", entitlementId = entitlement ?? "fixture-" + id,
        image = new { url = "https://image.api.playstation.com/fixture/icon.png" }, isActive = true, isDownloadable = true
    };
    private static object Played(string id, string? duration, string category = "ps4_game") => new
    {
        titleId = id, name = "Fixture game", localizedName = "Fixture game", category, playDuration = duration,
        lastPlayedDateTime = "2026-09-01T10:00:00Z", firstPlayedDateTime = "2020-01-01T10:00:00Z",
        concept = new { genres = new[] { "ACTION", "ADVENTURE" }, titleIds = new[] { "CUSA00001_00", "PPSA00002_00" } }
    };
    private static object Trophy(string id, string platform) => new
    {
        npCommunicationId = id, npServiceName = "trophy", trophyTitleName = "Fixture game", trophyTitlePlatform = platform,
        lastUpdatedDateTime = "2026-09-01T10:00:00Z", trophyTitleIconUrl = "https://psnobj.prod.dl.playstation.net/fixture/icon.png"
    };
    private static string Purchases(object[] games) => JsonSerializer.Serialize(new { data = new { purchasedTitlesRetrieve = new { games } } });
    private static string History(string key, object[] games, int? total = null, int? next = null)
    {
        var value = new Dictionary<string, object> { [key] = games, ["totalItemCount"] = total ?? games.Length };
        if (next is not null) value["nextOffset"] = next.Value;
        return JsonSerializer.Serialize(value);
    }
    private static PluginHttpResponse Response(string json) => new(200, Encoding.UTF8.GetBytes(json), new Dictionary<string, string>());
    private static int PurchaseOffset(PluginHttpRequest request)
    {
        var values = new Uri(request.Url).Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0], x => Uri.UnescapeDataString(x[1]));
        using var variables = JsonDocument.Parse(values["variables"]);
        return variables.RootElement.GetProperty("start").GetInt32();
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Host : IPluginContext, IPluginHttp, IPluginCache, IPluginSecrets, IPluginSettings
    {
        internal const string Account = "1111111111111111";
        internal static readonly PsnSession Session = new(Account, "fixture-account-hash", "fixture-access-token");
        public Clock Clock { get; } = new();
        public PsnLibraryClient Client { get; }
        public Host() => Client = new(this, Clock);
        public Task<PsnLibrarySnapshot?> Read(bool played = false, bool legacy = false, CancellationToken ct = default) => Client.GetAsync(Session, played, legacy, ct);
        public string PluginId => "psn";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public Dictionary<string, PluginCacheEntry> CacheEntries { get; } = [];
        public List<PluginHttpRequest> Requests { get; } = [];
        public string Purchases { get; set; } = PsnLibraryClientTests.Purchases([Purchase("CUSA00001_00")]);
        public string Played { get; set; } = History("titles", []);
        public string Legacy { get; set; } = History("trophyTitles", []);
        public int PurchaseStatus { get; set; } = 200;
        public bool CacheWriteFailure { get; set; }
        public Func<PluginHttpRequest, PluginHttpResponse>? Respond { get; set; }
        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(Respond?.Invoke(request) ?? (request.Url.Contains("graphql") ?
                new(PurchaseStatus, Encoding.UTF8.GetBytes(Purchases), new Dictionary<string, string>()) :
                Response(request.Url.Contains("trophyTitles") ? Legacy : Played)));
        }
        ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
        ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<string?>(null);
        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(CacheEntries.GetValueOrDefault(key));
        ValueTask IPluginCache.SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CacheWriteFailure) throw new IOException();
            CacheEntries[key] = entry;
            return ValueTask.CompletedTask;
        }
    }
}
