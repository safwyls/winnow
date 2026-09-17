using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.Xbox.Tests;

public sealed class XboxPluginTests
{
    private const string Pfn = "Example.Game_abcdefghijklm";
    private const string Source = "pfn:example.game_abcdefghijklm";
    private static readonly XboxLocalGame Installed = new(Pfn, "Example game") { InstallPath = @"C:\Games\Example", AppUserModelId = Pfn + "!Game" };
    private static readonly PluginGame Game = new("opaque", "Example game", new Dictionary<string, string> { ["plugin:xbox"] = Source });

    [Fact]
    public async Task Local_discovery_needs_no_credentials_and_complete_absence_clears_only_installation()
    {
        var h = await Host.Create();
        h.Local.Scan = new([Installed], true);
        var game = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.True(game.Installed);
        Assert.Equal(Source, game.SourceId);
        Assert.Null(game.PlaytimeMinutes);
        Assert.Contains("ownership unverified", game.LibrarySourceLabel);
        Assert.Empty(h.Requests);
        h.Local.Scan = new([], false);
        game = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Null(game.Installed);
        h.Local.Scan = new([], true);
        game = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.False(game.Installed);
        Assert.DoesNotContain(PluginGameActionKind.Play, game.Actions);
        Assert.Null(game.AcquiredAt);
    }

    [Fact]
    public async Task Device_flow_is_short_respects_intervals_and_stores_only_protected_refresh_credentials()
    {
        var h = await Host.Create();
        h.SettingsValues["client-id"] = Host.ClientId;
        var challenge = await h.Plugin.BeginSignInAsync();
        Assert.NotNull(challenge);
        Assert.Equal("ABCD-EFGH", challenge.UserCode);
        Assert.DoesNotContain("device-secret", challenge.AttemptId);
        var requestCount = h.Requests.Count;
        Assert.Equal(PluginSignInState.Pending, (await h.Plugin.PollSignInAsync(challenge.AttemptId)).State);
        Assert.Equal(requestCount, h.Requests.Count);
        h.Clock.Now = h.Clock.Now.AddSeconds(5);
        h.TokenError = "authorization_pending";
        Assert.Equal(PluginSignInState.Pending, (await h.Plugin.PollSignInAsync(challenge.AttemptId)).State);
        h.Clock.Now = h.Clock.Now.AddSeconds(5);
        h.TokenError = "slow_down";
        Assert.Equal(PluginSignInState.SlowDown, (await h.Plugin.PollSignInAsync(challenge.AttemptId)).State);
        h.Clock.Now = h.Clock.Now.AddSeconds(5);
        requestCount = h.Requests.Count;
        Assert.Equal(PluginSignInState.Pending, (await h.Plugin.PollSignInAsync(challenge.AttemptId)).State);
        Assert.Equal(requestCount, h.Requests.Count);
        h.Clock.Now = h.Clock.Now.AddSeconds(5);
        h.TokenError = null;
        Assert.Equal(PluginSignInState.Connected, (await h.Plugin.PollSignInAsync(challenge.AttemptId)).State);
        Assert.True((await h.Plugin.GetAccountStatusAsync()).Connected);
        Assert.Contains("refresh-one", h.Secret);
        Assert.DoesNotContain("access-secret", h.Secret);
        Assert.DoesNotContain("xsts-secret", h.Secret);
        Assert.Empty(h.CacheEntries);
        var userRequest = h.Requests.Single(x => x.Url.Contains("user/authenticate"));
        using var userJson = JsonDocument.Parse(userRequest.Body!);
        Assert.Equal("d=access-secret", userJson.RootElement.GetProperty("Properties").GetProperty("RpsTicket").GetString());
        Assert.Equal("http://auth.xboxlive.com", userJson.RootElement.GetProperty("RelyingParty").GetString());
    }

    [Theory]
    [InlineData("https://attacker.example/devicelogin")]
    [InlineData("http://microsoft.com/devicelogin")]
    [InlineData("https://microsoft.com@attacker.example/devicelogin")]
    [InlineData("https://microsoft.com/other")]
    public async Task Sign_in_refuses_untrusted_verification_destinations(string uri)
    {
        var h = await Host.Create();
        h.SettingsValues["client-id"] = Host.ClientId;
        h.Verification = uri;
        Assert.Null(await h.Plugin.BeginSignInAsync());
        Assert.Null(h.Secret);
    }

    [Fact]
    public async Task Cancellation_expiration_and_client_changes_retire_device_attempts()
    {
        var h = await Host.Create();
        h.SettingsValues["client-id"] = Host.ClientId;
        var c = (await h.Plugin.BeginSignInAsync())!;
        await h.Plugin.CancelSignInAsync(c.AttemptId);
        Assert.Equal(PluginSignInState.Failed, (await h.Plugin.PollSignInAsync(c.AttemptId)).State);
        c = (await h.Plugin.BeginSignInAsync())!;
        h.Clock.Now = h.Clock.Now.AddMinutes(20);
        Assert.Equal(PluginSignInState.Failed, (await h.Plugin.PollSignInAsync(c.AttemptId)).State);
        c = (await h.Plugin.BeginSignInAsync())!;
        h.SettingsValues["client-id"] = Guid.NewGuid().ToString();
        Assert.Equal(PluginSignInState.Failed, (await h.Plugin.PollSignInAsync(c.AttemptId)).State);
        Assert.DoesNotContain(h.Requests, x => x.Url.EndsWith("/token"));
    }

    [Fact]
    public async Task Failed_protected_write_never_reports_connected()
    {
        var h = await Host.Create();
        h.WriteFailure = true;
        Assert.Equal(PluginSignInState.Failed, (await h.Connect()).State);
        Assert.False((await h.Plugin.GetAccountStatusAsync()).Connected);
        Assert.Null(h.Secret);
    }

    [Fact]
    public async Task History_is_opt_in_and_consoles_are_a_separate_opt_in()
    {
        var h = await Host.Create();
        await h.Connect();
        var before = h.Requests.Count;
        Assert.Empty((await h.Plugin.GetLibraryAsync())!);
        Assert.Equal(before, h.Requests.Count);
        h.SettingsValues["import-history"] = "true";
        var pc = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Equal(Source, pc.SourceId);
        Assert.Equal(Host.Xuid, pc.AccountRef);
        Assert.Equal(120, pc.PlaytimeMinutes);
        var statsRequest = h.Requests.Single(x => x.Url.Contains("userstats"));
        using (var statsJson = JsonDocument.Parse(statsRequest.Body!))
        {
            var stat = Assert.Single(statsJson.RootElement.GetProperty("stats").EnumerateArray());
            Assert.Equal(Host.Scid, stat.GetProperty("scid").GetString());
            Assert.False(stat.TryGetProperty("titleid", out _));
        }
        Assert.NotNull(pc.LastPlayedAt);
        Assert.False(pc.Installed);
        Assert.Contains("played history", pc.LibrarySourceLabel);
        h.SettingsValues["include-console"] = "true";
        var games = (await h.Plugin.GetLibraryAsync())!;
        Assert.Equal(2, games.Count);
        var console = games.Single(x => x.SourceId == "title:456");
        Assert.Null(console.Installed);
        Assert.Null(console.PlaytimeMinutes);
        Assert.DoesNotContain(PluginGameActionKind.Play, console.Actions);
        Assert.False((await h.Plugin.ExecuteGameActionAsync(console.SourceId, PluginGameActionKind.Play)).HandedOff);
        Assert.All(h.CacheEntries.Values, e =>
        {
            var content = Encoding.UTF8.GetString(e.Payload);
            Assert.DoesNotContain("refresh-one", content);
            Assert.DoesNotContain("access-secret", content);
            Assert.DoesNotContain("xsts-secret", content);
            Assert.DoesNotContain("device-secret", content);
        });
    }

    [Fact]
    public async Task Matching_history_classifies_legacy_installed_packages_and_launches_current_identity()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.Local.Scan = new([], true) { Packages = [Installed] };
        var game = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.True(game.Installed);
        Assert.Contains(PluginGameActionKind.Play, game.Actions);
        Assert.True((await h.Plugin.ExecuteGameActionAsync(Source, PluginGameActionKind.Play)).HandedOff);
        Assert.Equal((Pfn, "Game"), h.Local.LastLaunch);
        h.Local.Scan = new([], true);
        Assert.False((await h.Plugin.ExecuteGameActionAsync(Source, PluginGameActionKind.Play)).HandedOff);
    }

    [Fact]
    public async Task Account_mismatch_and_statistics_failure_never_invent_zero_or_replace_good_data()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.HistoryXuid = "2222222222222222";
        Assert.Empty((await h.Plugin.GetLibraryAsync())!);
        Assert.DoesNotContain(h.CacheEntries.Keys, key => key.StartsWith("history:"));
        h.HistoryXuid = Host.Xuid;
        h.StatsFailure = true;
        var pc = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Null(pc.PlaytimeMinutes);
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.StatsFailure = false;
        pc = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Equal(120, pc.PlaytimeMinutes);
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.StatsFailure = true;
        pc = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Equal(120, pc.PlaytimeMinutes);
    }

    [Fact]
    public async Task Refresh_rotation_survives_xbox_outage_and_does_not_switch_accounts()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.Clock.Now = h.Clock.Now.AddHours(2);
        h.XstsFailure = true;
        h.RefreshToken = "refresh-two";
        await h.Plugin.GetLibraryAsync();
        Assert.Contains("refresh-two", h.Secret);
        h.XstsFailure = false;
        h.Clock.Now = h.Clock.Now.AddMinutes(3);
        h.SessionXuid = "2222222222222222";
        var requests = h.Requests.Count(x => x.Url.Contains("titlehub"));
        await h.Plugin.GetLibraryAsync();
        Assert.Equal(requests, h.Requests.Count(x => x.Url.Contains("titlehub")));
        Assert.Contains(Host.Xuid, h.Secret);
    }

    [Fact]
    public async Task Disconnect_and_app_change_do_not_serve_account_history_cache()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        Assert.Single((await h.Plugin.GetLibraryAsync())!);
        h.SettingsValues["client-id"] = Guid.NewGuid().ToString();
        Assert.Empty((await h.Plugin.GetLibraryAsync())!);
        h.SettingsValues["client-id"] = Host.ClientId;
        await h.Plugin.SignOutAsync();
        Assert.Empty((await h.Plugin.GetLibraryAsync())!);
        Assert.Null(h.Secret);
    }

    [Fact]
    public async Task Catalog_correlates_exact_ids_filters_artwork_and_retains_stale_success()
    {
        var h = await Host.Create();
        var metadata = await h.Plugin.GetMetadataAsync(Game);
        Assert.Equal("A fixture description.", metadata?.Summary);
        var art = (await h.Plugin.GetArtworkAsync(Game))!;
        Assert.Equal(2, art.Count);
        Assert.Contains(art, x => x.Kind == PluginArtworkKind.Cover && x.Url.StartsWith("https://store-images.s-microsoft.com/"));
        Assert.Contains(art, x => x.Kind == PluginArtworkKind.Background && x.Width == 1920);
        Assert.Single(h.Requests);
        Assert.True((await h.Plugin.ExecuteGameActionAsync(Source, PluginGameActionKind.OpenStore)).HandedOff);
        Assert.Equal("9ABCDEF12345", h.Local.LastStore);
        h.Clock.Now = h.Clock.Now.AddDays(8);
        h.CatalogFailure = true;
        Assert.Equal(metadata?.Summary, (await h.Plugin.GetMetadataAsync(Game))?.Summary);
        Assert.Equal(art, await h.Plugin.GetArtworkAsync(Game));
    }

    [Theory]
    [InlineData("other.game_abcdefghijklm", "Game")]
    [InlineData(Pfn, "Application")]
    public async Task Unrelated_products_and_generic_apps_cannot_become_metadata_or_store_actions(string pfn, string kind)
    {
        var h = await Host.Create();
        h.CatalogPfn = pfn;
        h.CatalogKind = kind;
        Assert.Null(await h.Plugin.GetMetadataAsync(Game));
        Assert.False((await h.Plugin.ExecuteGameActionAsync(Source, PluginGameActionKind.OpenStore)).HandedOff);
        Assert.Null(h.Local.LastStore);
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task Unrelated_releases_have_no_xbox_artwork_while_unavailable_xbox_artwork_stays_unknown()
    {
        var h = await Host.Create();
        var steam = Game with { ExternalIds = new Dictionary<string, string> { ["steam"] = "123" } };
        Assert.Empty((await h.Plugin.GetArtworkAsync(steam))!);
        Assert.Empty(h.Requests);
        h.CatalogFailure = true;
        Assert.Null(await h.Plugin.GetArtworkAsync(Game));
        Assert.Single(h.Requests);
        Assert.Empty(h.CacheEntries);
    }

    [Theory]
    [InlineData("pfn:../evil")]
    [InlineData("title:123?secret=bad")]
    [InlineData("store:invalid")]
    [InlineData("https://evil.example")]
    public async Task Invalid_source_ids_never_issue_requests_or_launch(string source)
    {
        var h = await Host.Create();
        Assert.Null(await h.Plugin.GetMetadataAsync(Game with { ExternalIds = new Dictionary<string, string> { ["plugin:xbox"] = source } }));
        Assert.False((await h.Plugin.ExecuteGameActionAsync(source, PluginGameActionKind.Play)).HandedOff);
        Assert.False((await h.Plugin.ExecuteGameActionAsync(source, PluginGameActionKind.OpenStore)).HandedOff);
        Assert.Empty(h.Requests);
        Assert.Null(h.Local.LastLaunch);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        var h = await Host.Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Plugin.GetLibraryAsync(cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Plugin.GetMetadataAsync(Game, cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Plugin.BeginSignInAsync(cts.Token));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Remembered_game_with_registered_package_remains_installed_without_optional_game_config()
    {
        var h = await Host.Create();
        h.Local.Scan = new([Installed], true);
        await h.Plugin.GetLibraryAsync();
        h.Local.Scan = new([], true) { Packages = [Installed with { Title = "Unlocalized shell name" }] };
        var game = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.True(game.Installed);
        Assert.Equal("Example game", game.Title);
        Assert.Contains(PluginGameActionKind.Play, game.Actions);
    }

    [Fact]
    public async Task Pc_history_without_package_identity_does_not_create_a_second_identity()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.RawHistory = """{"xuid":"1111111111111111","titles":[{"titleId":"123","name":"Example game","type":"Game","devices":["PC"]}]}""";
        Assert.Empty((await h.Plugin.GetLibraryAsync())!);
        h.Local.Scan = new([Installed], true);
        Assert.Equal(Source, Assert.Single((await h.Plugin.GetLibraryAsync())!).SourceId);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"xuid\":\"1111111111111111\",\"titles\":null}")]
    [InlineData("{\"xuid\":\"1111111111111111\",\"titles\":[{\"titleId\":\"123\",\"type\":\"Game\",\"name\":\"A\"},{\"titleId\":\"123\",\"type\":\"Game\",\"name\":\"B\"}]}")]
    public async Task Malformed_or_conflicting_history_preserves_older_account_observations(string invalid)
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        var before = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        var cache = h.CacheEntries.Single(x => x.Key.StartsWith("history:"));
        h.Clock.Now = h.Clock.Now.AddHours(7);
        h.RawHistory = invalid;
        var after = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.Equal(before.PlaytimeMinutes, after.PlaytimeMinutes);
        Assert.Equal(before.LastPlayedAt, after.LastPlayedAt);
        Assert.Same(cache.Value, h.CacheEntries[cache.Key]);
    }

    [Theory]
    [InlineData("2222222222222222", "120")]
    [InlineData("1111111111111111", "-1")]
    [InlineData("1111111111111111", "not-a-number")]
    public async Task Statistics_require_exact_account_and_valid_nonnegative_minutes(string xuid, string value)
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.RawStats = JsonSerializer.Serialize(new { statlistscollection = new[] { new { arrangebyfield = "xuid", arrangebyfieldid = xuid, stats = new[] { new { scid = Host.Scid, name = "MinutesPlayed", value } } } } });
        Assert.Null(Assert.Single((await h.Plugin.GetLibraryAsync())!).PlaytimeMinutes);
    }

    [Fact]
    public async Task Changed_client_during_history_request_cannot_publish_or_cache_the_previous_account()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.OnRequest = r => { if (r.Url.Contains("titlehub")) h.SettingsValues["client-id"] = Guid.NewGuid().ToString(); };
        Assert.Empty((await h.Plugin.GetLibraryAsync())!);
        Assert.DoesNotContain(h.CacheEntries.Keys, x => x.StartsWith("history:"));
    }

    [Fact]
    public async Task Token_exchange_rejects_a_changed_user_hash_before_saving_credentials()
    {
        var h = await Host.Create();
        h.XstsUserHash = "99999";
        Assert.Equal(PluginSignInState.Failed, (await h.Connect()).State);
        Assert.Null(h.Secret);
        Assert.False((await h.Plugin.GetAccountStatusAsync()).Connected);
    }

    [Fact]
    public async Task Legacy_install_classification_survives_disconnect_and_reconciles_removal()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.Local.Scan = new([], true) { Packages = [Installed] };
        Assert.True(Assert.Single((await h.Plugin.GetLibraryAsync())!).Installed);
        await h.Plugin.SignOutAsync();
        var present = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.True(present.Installed);
        Assert.Null(present.AccountRef);
        Assert.True((await h.Plugin.ExecuteGameActionAsync(Source, PluginGameActionKind.Play)).HandedOff);
        h.Local.Scan = new([], true);
        var removed = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.False(removed.Installed);
        Assert.DoesNotContain(PluginGameActionKind.Play, removed.Actions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000999")]
    public async Task Uncorrelated_service_configuration_cannot_supply_minutes(string? scid)
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.RawStats = JsonSerializer.Serialize(new { statlistscollection = new[] { new { arrangebyfield = "xuid", arrangebyfieldid = Host.Xuid, stats = new[] { new { scid, name = "MinutesPlayed", value = "99999" } } } } });
        Assert.Null(Assert.Single((await h.Plugin.GetLibraryAsync())!).PlaytimeMinutes);
    }

    [Fact]
    public async Task Shared_service_configuration_is_ambiguous_and_is_not_requested()
    {
        var h = await Host.Create();
        await h.Connect();
        h.SettingsValues["import-history"] = "true";
        h.RawHistory = JsonSerializer.Serialize(new { xuid = Host.Xuid, titles = new[]
        {
            new { titleId = "123", pfn = Pfn, serviceConfigId = Host.Scid, name = "First", type = "Game", devices = new[] { "PC" } },
            new { titleId = "456", pfn = "Other.Game_abcdefghijklm", serviceConfigId = Host.Scid, name = "Second", type = "Game", devices = new[] { "PC" } }
        } });
        var games = (await h.Plugin.GetLibraryAsync())!;
        Assert.Equal(2, games.Count);
        Assert.All(games, x => Assert.Null(x.PlaytimeMinutes));
        Assert.DoesNotContain(h.Requests, x => x.Url.Contains("userstats"));
    }

    [Fact]
    public async Task Cancel_during_xbox_exchange_does_not_persist_a_connection()
    {
        var h = await Host.Create();
        h.SettingsValues["client-id"] = Host.ClientId;
        var challenge = (await h.Plugin.BeginSignInAsync())!;
        h.Clock.Now = h.Clock.Now.AddSeconds(5);
        h.BlockXsts = true;
        var polling = h.Plugin.PollSignInAsync(challenge.AttemptId);
        await h.XstsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cancellation = h.Plugin.CancelSignInAsync(challenge.AttemptId);
        Assert.Equal(PluginSignInState.Failed, (await polling).State);
        await cancellation;
        Assert.Null(h.Secret);
    }

    [Fact]
    public async Task Provisional_local_titles_can_be_promoted_and_do_not_replace_remembered_real_names()
    {
        var h = await Host.Create();
        h.Local.Scan = new([Installed with { Title = "Example.Identity", IsTitleProvisional = true }], true);
        Assert.True(Assert.Single((await h.Plugin.GetLibraryAsync())!).TitleIsProvisional);
        h.Local.Scan = new([Installed], true);
        var promoted = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.False(promoted.TitleIsProvisional);
        Assert.Equal("Example game", promoted.Title);
        h.Local.Scan = new([Installed with { Title = "Example.Identity", IsTitleProvisional = true }], true);
        var later = Assert.Single((await h.Plugin.GetLibraryAsync())!);
        Assert.False(later.TitleIsProvisional);
        Assert.Equal("Example game", later.Title);
    }

    [Fact]
    public void Manifest_and_dependency_surface_support_sdk_only_packaging()
    {
        var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "xbox.plugin.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("xbox", m.Id);
        Assert.Equal(typeof(XboxPlugin).FullName, m.EntryType);
        Assert.Equal(["library", "metadata", "artwork", "account", "game-actions"], m.Capabilities);
        Assert.True(m.Settings.Single(x => x.Key == "refresh-token").ManagedByPlugin);
        Assert.True(m.Settings.Single(x => x.Key == "refresh-token").Secret);
        Assert.True(m.Settings.Single(x => x.Key == "import-history").IsBoolean);
        Assert.All(new[] { "microsoft.com", "www.microsoft.com" }, host => Assert.Contains(host, m.Network.AllowedHosts));
        Assert.Equal(["Winnow.PluginSdk"], typeof(XboxPlugin).Assembly.GetReferencedAssemblies().Select(x => x.Name).Where(x => x!.StartsWith("Winnow")));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Local : IXboxLocalLibrary
    {
        public XboxLocalScan? Scan { get; set; } = new([], true);
        public (string, string)? LastLaunch { get; private set; }
        public string? LastStore { get; private set; }
        public Task<XboxLocalScan?> ScanAsync(CancellationToken cancellationToken = default) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Scan); }
        public Task<bool> LaunchAsync(string packageFamilyName, string applicationId, CancellationToken cancellationToken = default) { LastLaunch = (packageFamilyName, applicationId); return Task.FromResult(true); }
        public Task<bool> OpenStoreAsync(string productId, CancellationToken cancellationToken = default) { LastStore = productId; return Task.FromResult(true); }
    }
    private sealed class Host : IPluginContext, IPluginHttp, IPluginCache, IPluginSecrets, IPluginSettings
    {
        internal const string ClientId = "00000001-0002-0003-0004-000000000005";
        internal const string Xuid = "1111111111111111";
        internal const string Scid = "00000000-0000-0000-0000-000000000123";
        public string PluginId => "xbox";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => this;
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public Dictionary<string, string> SettingsValues { get; } = [];
        public Dictionary<string, PluginCacheEntry> CacheEntries { get; } = [];
        public List<PluginHttpRequest> Requests { get; } = [];
        public string? Secret { get; private set; }
        public Local Local { get; } = new();
        public Clock Clock { get; } = new();
        public XboxPlugin Plugin { get; private set; } = null!;
        public string Verification { get; set; } = "https://microsoft.com/devicelogin";
        public string? TokenError { get; set; }
        public string RefreshToken { get; set; } = "refresh-one";
        public string SessionXuid { get; set; } = Xuid;
        public string HistoryXuid { get; set; } = Xuid;
        public bool WriteFailure { get; set; }
        public bool StatsFailure { get; set; }
        public bool XstsFailure { get; set; }
        public bool CatalogFailure { get; set; }
        public string CatalogPfn { get; set; } = Pfn;
        public string CatalogKind { get; set; } = "Game";
        public string? RawHistory { get; set; }
        public string? RawStats { get; set; }
        public string XstsUserHash { get; set; } = "12345";
        public Action<PluginHttpRequest>? OnRequest { get; set; }
        public bool BlockXsts { get; set; }
        public TaskCompletionSource XstsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static async Task<Host> Create()
        {
            var h = new Host();
            h.Plugin = new(h.Local, h.Clock);
            await h.Plugin.InitializeAsync(h);
            return h;
        }
        public async Task<PluginSignInResult> Connect()
        {
            SettingsValues["client-id"] = ClientId;
            var challenge = (await Plugin.BeginSignInAsync())!;
            Clock.Now = Clock.Now.AddSeconds(challenge.PollIntervalSeconds);
            return await Plugin.PollSignInAsync(challenge.AttemptId);
        }
        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            OnRequest?.Invoke(request);
            var raw = request.Url.Contains("titlehub") ? RawHistory : request.Url.Contains("userstats") ? RawStats : null;
            if (raw is not null) return Task.FromResult(new PluginHttpResponse(200, Encoding.UTF8.GetBytes(raw), new Dictionary<string, string>()));
            object body;
            var status = 200;
            if (request.Url.EndsWith("/devicecode")) body = new { device_code = "device-secret", user_code = "ABCD-EFGH", verification_uri = Verification, interval = 5, expires_in = 900 };
            else if (request.Url.EndsWith("/token"))
            {
                if (TokenError is not null) { body = new { error = TokenError }; status = 400; }
                else body = new { access_token = "access-secret", refresh_token = RefreshToken, expires_in = 3600 };
            }
            else if (request.Url.Contains("user/authenticate")) body = new { Token = "user-secret", DisplayClaims = new { xui = new[] { new { uhs = "12345" } } } };
            else if (request.Url.Contains("xsts/authorize"))
            {
                if (BlockXsts)
                {
                    XstsStarted.TrySetResult();
                    return new TaskCompletionSource<PluginHttpResponse>().Task.WaitAsync(cancellationToken);
                }
                body = new { Token = "xsts-secret", NotAfter = Clock.Now.AddHours(1), DisplayClaims = new { xui = new[] { new { uhs = XstsUserHash, xid = SessionXuid } } } };
                if (XstsFailure) status = 503;
            }
            else if (request.Url.Contains("titlehub")) body = new { xuid = HistoryXuid, titles = new object[]
            {
                new { titleId = "123", pfn = Pfn, serviceConfigId = Scid, type = "Game", name = "Example game", devices = new[] { "PC" }, titleHistory = new { lastTimePlayed = "2026-09-01T10:00:00Z" } },
                new { titleId = "456", type = "Game", name = "Console game", devices = new[] { "XboxSeries" }, titleHistory = new { lastTimePlayed = "2026-08-01T10:00:00Z" } },
                new { titleId = "789", type = "App", name = "Not a game", devices = new[] { "PC" } }
            } };
            else if (request.Url.Contains("userstats"))
            {
                body = new { statlistscollection = new[] { new { arrangebyfield = "xuid", arrangebyfieldid = Xuid, stats = new[] { new { scid = Scid, xuid = Xuid, name = "MinutesPlayed", type = "Integer", value = "120" } } } } };
                if (StatsFailure) status = 503;
            }
            else if (request.Url.Contains("displaycatalog"))
            {
                body = new { Products = new[] { new { ProductId = "9ABCDEF12345", ProductKind = CatalogKind, Properties = new { PackageFamilyName = CatalogPfn, Category = "Entertainment" }, LocalizedProperties = new[]
                { new { Language = "en-us", ProductTitle = "Example game", ProductDescription = "A fixture description.", Images = new[]
                    { new { Uri = "//store-images.s-microsoft.com/image/poster", Width = 600, Height = 900, ImagePurpose = "Poster" },
                      new { Uri = "https://store-images.s-microsoft.com/image/hero", Width = 1920, Height = 1080, ImagePurpose = "SuperHeroArt" },
                      new { Uri = "https://attacker.example/image/hero", Width = 1920, Height = 1080, ImagePurpose = "SuperHeroArt" },
                      new { Uri = "https://store-images.s-microsoft.com/image/logo", Width = 100, Height = 100, ImagePurpose = "Logo" } } } } } } };
                if (CatalogFailure) status = 503;
            }
            else throw new InvalidOperationException("Unexpected fixture endpoint.");
            return Task.FromResult(new PluginHttpResponse(status, JsonSerializer.SerializeToUtf8Bytes(body), new Dictionary<string, string>()));
        }
        ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(SettingsValues.GetValueOrDefault(key));
        ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken cancellationToken) { if (value is null) SettingsValues.Remove(key); else SettingsValues[key] = value; return ValueTask.CompletedTask; }
        ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(Secret);
        ValueTask IPluginSecrets.SetAsync(string key, string value, CancellationToken cancellationToken) { if (WriteFailure) throw new NotSupportedException(); Secret = value; return ValueTask.CompletedTask; }
        ValueTask IPluginSecrets.RemoveAsync(string key, CancellationToken cancellationToken) { Secret = null; return ValueTask.CompletedTask; }
        ValueTask<PluginCacheEntry?> IPluginCache.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(CacheEntries.GetValueOrDefault(key));
        ValueTask IPluginCache.SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken) { CacheEntries[key] = entry; return ValueTask.CompletedTask; }
    }
}
