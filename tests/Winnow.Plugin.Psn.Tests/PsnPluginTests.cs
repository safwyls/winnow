using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.Psn.Tests;

public sealed class PsnPluginTests
{
    private const string AccountId = "1111111111111111";
    private const string PurchaseId = "title:PPSA11111_00";
    private const string HistoryId = "title:CUSA22222_00";
    private const string LegacyId = "trophy:trophy:NPWR33333_00";
    private static readonly PluginGame PurchaseGame = new("opaque-host-id", "Fixture game", new Dictionary<string, string> { ["plugin:psn"] = PurchaseId });

    [Fact]
    public async Task Unconfigured_plugin_has_no_library_and_does_not_request_the_network()
    {
        var h = await Host.Create();
        h.SecretValues.Clear();

        Assert.Null(await h.Plugin.GetLibraryAsync());

        Assert.Null(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Null(await h.Plugin.GetArtworkAsync(PurchaseGame));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Unrelated_releases_return_complete_empty_artwork_so_grouped_games_can_use_Psn_covers()
    {
        var h = await Host.Create();
        var unrelated = PurchaseGame with { ExternalIds = new Dictionary<string, string> { ["steam"] = "123" } };

        Assert.Empty((await h.Plugin.GetArtworkAsync(unrelated))!);
        Assert.Null(await h.Plugin.GetArtworkAsync(PurchaseGame));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Default_import_publishes_purchase_identity_without_claiming_installation_or_playtime()
    {
        var h = await Host.Create();

        var game = Assert.Single((await h.Plugin.GetLibraryAsync())!);

        Assert.Equal(PurchaseId, game.SourceId);
        Assert.Equal("Fixture purchase", game.Title);
        Assert.Equal(AccountId, game.AccountRef);
        Assert.Equal(PurchaseId, Assert.Single(game.ExternalIds).Value);
        Assert.Equal("plugin:psn", Assert.Single(game.ExternalIds).Key);
        Assert.Null(game.Installed);
        Assert.Null(game.InstallPath);
        Assert.Null(game.PlaytimeMinutes);
        Assert.Null(game.LastPlayedAt);
        Assert.Null(game.AcquiredAt);
        Assert.Empty(game.Actions);
        Assert.Contains("purchase library", game.LibrarySourceLabel);
        Assert.Contains("subscription", game.LibrarySourceLabel);
        Assert.DoesNotContain(h.Requests, r => r.Url.Contains("gamelist", StringComparison.Ordinal) || r.Url.Contains("trophyTitles", StringComparison.Ordinal));
        var metadata = Assert.IsType<PluginMetadata>(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Equal(["PS5"], metadata.Tags);
        Assert.Null(await h.Plugin.GetMetadataAsync(PurchaseGame with { ExternalIds = new Dictionary<string, string> { ["plugin:xbox"] = PurchaseId } }));
    }

    [Fact]
    public async Task Optional_history_merges_matching_title_ids_and_keeps_trophy_ids_separate()
    {
        var h = await Host.Create();
        h.SettingValues["import-history"] = "true";
        h.SettingValues["include-legacy"] = "true";

        var games = (await h.Plugin.GetLibraryAsync())!;

        Assert.Equal(3, games.Count);
        var purchase = games.Single(x => x.SourceId == PurchaseId);
        Assert.Equal("Fixture purchase", purchase.Title);
        Assert.Equal(150, purchase.PlaytimeMinutes);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), purchase.LastPlayedAt);
        Assert.Contains("purchase library and played history", purchase.LibrarySourceLabel);
        var played = games.Single(x => x.SourceId == HistoryId);
        Assert.Contains("ownership unverified", played.LibrarySourceLabel);
        Assert.Null(played.PlaytimeMinutes);
        var legacy = games.Single(x => x.SourceId == LegacyId);
        Assert.Contains("trophy history", legacy.LibrarySourceLabel);
        Assert.Null(legacy.PlaytimeMinutes);
        Assert.Null(legacy.LastPlayedAt);
        Assert.All(games, x =>
        {
            Assert.Equal(AccountId, x.AccountRef);
            Assert.Null(x.Installed);
            Assert.Null(x.InstallPath);
            Assert.Null(x.AcquiredAt);
            Assert.Empty(x.Actions);
        });
        var metadata = Assert.IsType<PluginMetadata>(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Equal(["Adventure"], metadata.Genres);
        Assert.Equal(["PS5"], metadata.Tags);
        Assert.All(h.CacheEntries, entry =>
        {
            var payload = Encoding.UTF8.GetString(entry.Value.Payload);
            Assert.DoesNotContain("access-one", payload);
            Assert.DoesNotContain("refresh-one", payload);
            Assert.DoesNotContain(new string('A', 64), payload);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Credential_change_during_library_response_discards_library_and_metadata(bool remove)
    {
        var h = await Host.Create();
        Assert.NotNull(await h.Plugin.GetLibraryAsync());
        Assert.NotNull(await h.Plugin.GetMetadataAsync(PurchaseGame));
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.OnRequest = request =>
        {
            if (!request.Url.Contains("operationName=getPurchasedGameList", StringComparison.Ordinal)) return;
            if (remove) h.SecretValues.Remove("npsso");
            else h.SecretValues["npsso"] = new string('B', 64);
        };

        Assert.Null(await h.Plugin.GetLibraryAsync());

        Assert.Null(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Null(await h.Plugin.GetArtworkAsync(PurchaseGame));
    }

    [Theory]
    [InlineData("import-history")]
    [InlineData("include-legacy")]
    public async Task Settings_change_during_library_response_discards_the_stale_import(string key)
    {
        var h = await Host.Create();
        h.OnRequest = request =>
        {
            if (request.Url.Contains("operationName=getPurchasedGameList", StringComparison.Ordinal)) h.SettingValues[key] = "true";
        };

        Assert.Null(await h.Plugin.GetLibraryAsync());

        Assert.Null(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Null(await h.Plugin.GetArtworkAsync(PurchaseGame));
        h.OnRequest = null;
        Assert.Equal(2, (await h.Plugin.GetLibraryAsync())!.Count);
    }

    [Fact]
    public async Task Disconnect_immediately_hides_previously_published_metadata_and_artwork()
    {
        var h = await Host.Create();
        Assert.NotNull(await h.Plugin.GetLibraryAsync());
        Assert.NotNull(await h.Plugin.GetMetadataAsync(PurchaseGame));
        var before = h.Requests.Count;
        h.SecretValues.Remove("npsso");

        Assert.Null(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Null(await h.Plugin.GetArtworkAsync(PurchaseGame));
        Assert.Null(await h.Plugin.GetLibraryAsync());

        Assert.Equal(before, h.Requests.Count);
    }

    [Fact]
    public async Task Switching_accounts_cannot_reuse_the_old_accounts_cached_purchase_library()
    {
        var h = await Host.Create();
        Assert.NotNull(await h.Plugin.GetLibraryAsync());
        var oldCacheKeys = h.CacheEntries.Keys.ToArray();
        h.SecretValues["npsso"] = new string('B', 64);
        h.ProfileAccountId = "2222222222222222";
        h.PurchasesAvailable = false;

        Assert.Null(await h.Plugin.GetLibraryAsync());

        Assert.Null(await h.Plugin.GetMetadataAsync(PurchaseGame));
        Assert.Equal(oldCacheKeys, h.CacheEntries.Keys);
        Assert.Equal(2, h.Requests.Count(x => x.Url.Contains("operationName=getPurchasedGameList", StringComparison.Ordinal)));
        h.PurchasesAvailable = true;
        var next = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Equal(h.ProfileAccountId, next.AccountRef);
        Assert.Equal(2, h.CacheEntries.Count);
    }

    [Fact]
    public void Manifest_declares_the_sdk_capabilities_and_hides_the_managed_refresh_secret()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "psn.plugin.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("psn", manifest.Id);
        Assert.Equal(typeof(PsnPlugin).FullName, manifest.EntryType);
        Assert.Equal("Winnow.Plugin.Psn.dll", manifest.EntryAssembly);
        Assert.Equal(["library", "metadata", "artwork"], manifest.Capabilities);
        Assert.True(typeof(ILibrarySourcePlugin).IsAssignableFrom(typeof(PsnPlugin)));
        Assert.True(typeof(IMetadataProviderPlugin).IsAssignableFrom(typeof(PsnPlugin)));
        Assert.True(typeof(IArtworkProviderPlugin).IsAssignableFrom(typeof(PsnPlugin)));
        var npsso = manifest.Settings.Single(x => x.Key == "npsso");
        Assert.True(npsso.Secret);
        Assert.False(npsso.ManagedByPlugin);
        Assert.Equal("https://ca.account.sony.com/api/v1/ssocookie", npsso.SetupUrl);
        var refresh = manifest.Settings.Single(x => x.Key == "refresh-token");
        Assert.True(refresh.Secret);
        Assert.True(refresh.ManagedByPlugin);
        Assert.True(manifest.Settings.Single(x => x.Key == "import-history").IsBoolean);
        Assert.True(manifest.Settings.Single(x => x.Key == "include-legacy").IsBoolean);
        Assert.All(new[] { "ca.account.sony.com", "us-prof.np.community.playstation.net", "m.np.playstation.com", "web.np.playstation.com" },
            host => Assert.Contains(host, manifest.Network.AllowedHosts));
        Assert.Equal(["Winnow.PluginSdk"], typeof(PsnPlugin).Assembly.GetReferencedAssemblies().Select(x => x.Name).Where(x => x!.StartsWith("Winnow", StringComparison.Ordinal)));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Host : IPluginContext, IPluginHttp, IPluginSecrets, IPluginSettings, IPluginCache
    {
        public string PluginId => "psn";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public Dictionary<string, string> SecretValues { get; } = new() { ["npsso"] = new string('A', 64) };
        public Dictionary<string, string> SettingValues { get; } = [];
        public Dictionary<string, PluginCacheEntry> CacheEntries { get; } = [];
        public List<PluginHttpRequest> Requests { get; } = [];
        public Clock Clock { get; } = new();
        public PsnPlugin Plugin { get; private set; } = null!;
        public Action<PluginHttpRequest>? OnRequest { get; set; }
        public string ProfileAccountId { get; set; } = AccountId;
        public bool PurchasesAvailable { get; set; } = true;

        public static async Task<Host> Create()
        {
            var h = new Host();
            h.Plugin = new(h.Clock);
            await h.Plugin.InitializeAsync(h);
            return h;
        }

        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            OnRequest?.Invoke(request);
            if (request.Url.Contains("/authorize?", StringComparison.Ordinal))
                return Task.FromResult(new PluginHttpResponse(302, [], new Dictionary<string, string> { ["Location"] = "com.scee.psxandroid.scecompcall://redirect?code=code-one" }));
            object body;
            var status = 200;
            if (request.Url.EndsWith("/token", StringComparison.Ordinal))
                body = new { access_token = "access-one", refresh_token = "refresh-one", expires_in = 3600, refresh_token_expires_in = 86400, token_type = "bearer" };
            else if (request.Url.Contains("/profile2?", StringComparison.Ordinal))
                body = new { profile = new { accountId = ProfileAccountId } };
            else if (request.Url.Contains("operationName=getPurchasedGameList", StringComparison.Ordinal))
            {
                body = new { data = new { purchasedTitlesRetrieve = new { games = new[]
                {
                    new { titleId = "PPSA11111_00", name = "Fixture purchase", platform = "PS5", entitlementId = "fixture-entitlement", image = new { url = "https://image.api.playstation.com/fixture.png" } }
                } } } };
                if (!PurchasesAvailable) status = 503;
            }
            else if (request.Url.Contains("/gamelist/", StringComparison.Ordinal))
                body = new { accountId = ProfileAccountId, totalItemCount = 2, titles = new object[]
                {
                    new { titleId = "PPSA11111_00", localizedName = "Fixture history name", category = "ps5_native_game", playDuration = "PT2H30M", lastPlayedDateTime = "2026-09-01T10:00:00Z", concept = new { genres = new[] { "Adventure" } } },
                    new { titleId = "CUSA22222_00", localizedName = "Fixture played game", category = "ps4_game" }
                } };
            else if (request.Url.Contains("/trophyTitles?", StringComparison.Ordinal))
                body = new { accountId = ProfileAccountId, totalItemCount = 1, trophyTitles = new[]
                {
                    new { npServiceName = "trophy", npCommunicationId = "NPWR33333_00", trophyTitleName = "Fixture PS3 game", trophyTitlePlatform = "PS3", lastUpdatedDateTime = "2026-09-01T10:00:00Z" }
                } };
            else throw new Xunit.Sdk.XunitException("Unexpected fixture endpoint: " + request.Url);
            return Task.FromResult(new PluginHttpResponse(status, JsonSerializer.SerializeToUtf8Bytes(body), new Dictionary<string, string>()));
        }

        ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SecretValues.GetValueOrDefault(key));
        }
        ValueTask IPluginSecrets.SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecretValues[key] = value;
            return ValueTask.CompletedTask;
        }
        ValueTask IPluginSecrets.RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecretValues.Remove(key);
            return ValueTask.CompletedTask;
        }
        ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SettingValues.GetValueOrDefault(key));
        }
        ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken cancellationToken)
        {
            if (value is null) SettingValues.Remove(key);
            else SettingValues[key] = value;
            return ValueTask.CompletedTask;
        }
        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(CacheEntries.GetValueOrDefault(key));
        ValueTask IPluginCache.SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken)
        {
            CacheEntries[key] = entry;
            return ValueTask.CompletedTask;
        }
    }
}
