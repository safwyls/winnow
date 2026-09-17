using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.Psn.Tests;

public sealed class PsnAccountClientTests
{
    private const string AccountId = "1111111111111111";
    private const string OtherAccountId = "2222222222222222";
    private const string Redirect = "com.scee.psxandroid.scecompcall://redirect";
    private static readonly string Npsso = new('A', 64);
    private static readonly string OtherNpsso = new('B', 64);

    [Fact]
    public async Task Npsso_exchange_uses_the_documented_client_and_resolves_identity_from_authenticated_profile()
    {
        var h = new Host();

        var session = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));

        Assert.Equal(AccountId, session.AccountId);
        Assert.Equal("access-one", session.AccessToken);
        Assert.Equal(3, h.Requests.Count);
        var authorize = h.Requests[0];
        Assert.Equal("GET", authorize.Method);
        Assert.Equal("ca.account.sony.com", new Uri(authorize.Url).Host);
        Assert.Equal("/api/authz/v3/oauth/authorize", new Uri(authorize.Url).AbsolutePath);
        Assert.Equal("npsso=" + Npsso, authorize.Headers["Cookie"]);
        Assert.DoesNotContain(Npsso, authorize.Url);
        var query = Fields(new Uri(authorize.Url).Query.TrimStart('?'));
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("09515159-7237-4370-9b40-3806e67c0891", query["client_id"]);
        Assert.Equal(Redirect, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("psn:mobile.v2.core psn:clientapp", query["scope"]);

        var token = h.Requests[1];
        Assert.Equal("https://ca.account.sony.com/api/authz/v3/oauth/token", token.Url);
        Assert.Equal("POST", token.Method);
        Assert.Equal("application/x-www-form-urlencoded", token.ContentType);
        Assert.StartsWith("Basic ", token.Headers["Authorization"]);
        Assert.False(token.Headers.ContainsKey("Cookie"));
        var body = Fields(Encoding.UTF8.GetString(token.Body!));
        Assert.Equal("authorization_code", body["grant_type"]);
        Assert.Equal("code-one", body["code"]);
        Assert.Equal(Redirect, body["redirect_uri"]);
        Assert.Equal("jwt", body["token_format"]);
        Assert.Equal(4, body.Count);

        var profile = h.Requests[2];
        Assert.Equal("https://us-prof.np.community.playstation.net/userProfile/v1/users/me/profile2?fields=accountId", profile.Url);
        Assert.Equal("Bearer access-one", profile.Headers["Authorization"]);
        Assert.False(profile.Headers.ContainsKey("Cookie"));
        Assert.True(await h.Client.IsCurrentAsync(session, default));
        using var saved = JsonDocument.Parse(h.SecretsValues["refresh-token"]);
        Assert.Equal("refresh-one", saved.RootElement.GetProperty("RefreshToken").GetString());
        Assert.Equal(AccountId, saved.RootElement.GetProperty("AccountId").GetString());
        Assert.DoesNotContain(Npsso, h.SecretsValues["refresh-token"]);
        Assert.DoesNotContain("access-one", h.SecretsValues["refresh-token"]);
        Assert.DoesNotContain(Npsso, session.Scope);
        Assert.DoesNotContain(AccountId, session.Scope);
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(303)]
    public async Task Redirect_header_is_case_insensitive_and_exchange_decodes_the_code_once(int status)
    {
        var h = new Host { AuthorizeStatus = status, LocationHeader = "location", Location = Redirect + "?code=code%2Bone%2F%3D" };

        Assert.NotNull(await h.Client.GetSessionAsync(default));

        var exchange = h.Requests.Single(x => x.Method == "POST");
        Assert.Equal("code+one/=", Fields(Encoding.UTF8.GetString(exchange.Body!))["code"]);
        Assert.Contains("code=code%2Bone%2F%3D", Encoding.UTF8.GetString(exchange.Body!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://attacker.example/?code=code-one")]
    [InlineData("https://redirect/?code=code-one")]
    [InlineData("com.scee.psxandroid.scecompcall://other?code=code-one")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect.attacker.example?code=code-one")]
    [InlineData("com.scee.psxandroid.scecompcall://name@redirect?code=code-one")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect:443?code=code-one")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect/other?code=code-one")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?code=code-one#fragment")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?code=first&code=second")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?code=code-one&error=access_denied")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?error=access_denied")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?code=")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?code=line%0Abreak")]
    [InlineData("com.scee.psxandroid.scecompcall://redirect?code=%252B")]
    public async Task Invalid_authorization_redirect_never_sends_a_token_request(string? location)
    {
        var h = new Host { Location = location };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Single(h.Requests);
        Assert.False(h.SecretsValues.ContainsKey("refresh-token"));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(301)]
    [InlineData(307)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Only_authorization_code_redirect_statuses_allow_an_exchange(int status)
    {
        var h = new Host { AuthorizeStatus = status };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Single(h.Requests);
        Assert.Empty(h.SecretWrites);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAé")]
    public async Task Missing_or_invalid_npsso_drops_the_refresh_credential_without_network(string? value)
    {
        var h = new Host();
        Assert.NotNull(await h.Client.GetSessionAsync(default));
        h.SetNpsso(value);
        var before = h.Requests.Count;

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Equal(before, h.Requests.Count);
        Assert.False(h.SecretsValues.ContainsKey("refresh-token"));
    }

    [Fact]
    public async Task Access_session_is_reused_until_safety_margin_then_refresh_credentials_rotate()
    {
        var h = new Host();
        var first = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));
        h.Clock.Now = h.Clock.Now.AddSeconds(3569);
        Assert.Same(first, await h.Client.GetSessionAsync(default));
        Assert.Equal(3, h.Requests.Count);
        h.Clock.Now = h.Clock.Now.AddSeconds(1);
        h.AccessToken = "access-two";
        h.RefreshToken = "refresh-two";

        var refreshed = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));

        Assert.Equal("access-two", refreshed.AccessToken);
        Assert.Equal(first.Scope, refreshed.Scope);
        Assert.Equal(5, h.Requests.Count);
        var body = Fields(Encoding.UTF8.GetString(h.Requests[3].Body!));
        Assert.Equal("refresh_token", body["grant_type"]);
        Assert.Equal("refresh-one", body["refresh_token"]);
        Assert.Equal("jwt", body["token_format"]);
        Assert.Equal("psn:mobile.v2.core psn:clientapp", body["scope"]);
        Assert.Contains("refresh-two", h.SecretsValues["refresh-token"]);
        Assert.DoesNotContain("refresh-one", h.SecretsValues["refresh-token"]);
        Assert.DoesNotContain("access-two", h.SecretsValues["refresh-token"]);
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
    }

    [Fact]
    public async Task A_new_client_uses_the_protected_refresh_credential_without_a_new_cookie_exchange()
    {
        var h = new Host();
        var first = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));
        h.Client = new PsnAccountClient(h, h.Clock);

        var restarted = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));

        Assert.Equal(first.Scope, restarted.Scope);
        Assert.Equal(5, h.Requests.Count);
        Assert.Equal("refresh_token", Fields(Encoding.UTF8.GetString(h.Requests[3].Body!))["grant_type"]);
    }

    [Fact]
    public async Task Expired_refresh_credential_uses_npsso_authorization()
    {
        var h = new Host { RefreshSeconds = 3600 };
        Assert.NotNull(await h.Client.GetSessionAsync(default));
        h.Clock.Now = h.Clock.Now.AddSeconds(3600);

        Assert.NotNull(await h.Client.GetSessionAsync(default));

        Assert.Equal(6, h.Requests.Count);
        Assert.Contains("/authorize?", h.Requests[3].Url);
        Assert.Equal("authorization_code", Fields(Encoding.UTF8.GetString(h.Requests[4].Body!))["grant_type"]);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Rejected_refresh_is_removed_before_trying_cookie_authorization(int status)
    {
        var h = new Host();
        Assert.NotNull(await h.Client.GetSessionAsync(default));
        h.Client = new PsnAccountClient(h, h.Clock);
        h.RefreshStatus = status;
        h.OnRequest = request =>
        {
            if (request.Url.Contains("/authorize?", StringComparison.Ordinal))
                Assert.False(h.SecretsValues.ContainsKey("refresh-token"));
        };

        Assert.NotNull(await h.Client.GetSessionAsync(default));

        Assert.Equal(7, h.Requests.Count);
        Assert.Equal("refresh_token", Fields(Encoding.UTF8.GetString(h.Requests[3].Body!))["grant_type"]);
        Assert.Contains("/authorize?", h.Requests[4].Url);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Transient_refresh_failure_preserves_the_credential_without_ad_hoc_retries(int status)
    {
        var h = new Host();
        Assert.NotNull(await h.Client.GetSessionAsync(default));
        var saved = h.SecretsValues["refresh-token"];
        h.Client = new PsnAccountClient(h, h.Clock);
        h.RefreshStatus = status;

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Equal(4, h.Requests.Count);
        Assert.Equal(saved, h.SecretsValues["refresh-token"]);
    }

    [Fact]
    public async Task Replacing_npsso_retires_the_previous_session_and_does_not_reuse_its_refresh_token()
    {
        var h = new Host();
        var first = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));
        h.SetNpsso(OtherNpsso);
        h.ProfileAccountId = OtherAccountId;
        Assert.False(await h.Client.IsCurrentAsync(first, default));

        var second = Assert.IsType<PsnSession>(await h.Client.GetSessionAsync(default));

        Assert.Equal(OtherAccountId, second.AccountId);
        Assert.NotEqual(first.Scope, second.Scope);
        Assert.Equal(6, h.Requests.Count);
        Assert.Contains("/authorize?", h.Requests[3].Url);
        Assert.Equal("npsso=" + OtherNpsso, h.Requests[3].Headers["Cookie"]);
        Assert.Equal("authorization_code", Fields(Encoding.UTF8.GetString(h.Requests[4].Body!))["grant_type"]);
        Assert.DoesNotContain("refresh-one", Encoding.UTF8.GetString(h.Requests[4].Body!));
    }

    [Fact]
    public async Task Scope_distinguishes_accounts_and_distinct_credentials_for_the_same_account()
    {
        var first = new Host();
        var second = new Host { ProfileAccountId = OtherAccountId };
        var third = new Host();
        third.SetNpsso(OtherNpsso);

        var sessions = await Task.WhenAll(first.Client.GetSessionAsync(default), second.Client.GetSessionAsync(default), third.Client.GetSessionAsync(default));

        Assert.All(sessions, x => Assert.NotNull(x));
        Assert.Equal(3, sessions.Select(x => x!.Scope).Distinct().Count());
    }

    [Fact]
    public async Task Refresh_must_resolve_to_the_original_authenticated_account()
    {
        var h = new Host();
        Assert.NotNull(await h.Client.GetSessionAsync(default));
        var saved = h.SecretsValues["refresh-token"];
        h.Client = new PsnAccountClient(h, h.Clock);
        h.ProfileAccountId = OtherAccountId;
        h.RefreshToken = "refresh-wrong-account";

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Equal(saved, h.SecretsValues["refresh-token"]);
        Assert.Single(h.SecretWrites);
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Credential_change_during_authentication_cannot_publish_the_old_session(bool remove, bool duringWrite)
    {
        var h = new Host();
        void Change() => h.SetNpsso(remove ? null : OtherNpsso);
        if (duringWrite) h.AfterSecretWrite = Change;
        else h.OnRequest = request => { if (request.Url.Contains("/profile2?", StringComparison.Ordinal)) Change(); };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.False(h.SecretsValues.ContainsKey("refresh-token"));
        Assert.Equal(duringWrite ? 1 : 0, h.SecretWrites.Count);
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"access_token\":\"access-one\",\"refresh_token\":\"refresh-one\",\"expires_in\":\"3600\",\"refresh_token_expires_in\":86400,\"token_type\":\"bearer\"}")]
    [InlineData("{\"access_token\":\"access-one\",\"refresh_token\":\"refresh-one\",\"expires_in\":3600,\"refresh_token_expires_in\":86400,\"token_type\":\"Basic\"}")]
    [InlineData("{\"access_token\":\"access-one\\r\\nInjected\",\"refresh_token\":\"refresh-one\",\"expires_in\":3600,\"refresh_token_expires_in\":86400,\"token_type\":\"bearer\"}")]
    [InlineData("{\"access_token\":\"access-one\",\"expires_in\":3600,\"refresh_token_expires_in\":86400,\"token_type\":\"bearer\"}")]
    public async Task Malformed_token_response_never_reaches_profile_or_persists_credentials(string body)
    {
        var h = new Host { RawToken = body };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Equal(2, h.Requests.Count);
        Assert.Empty(h.SecretWrites);
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
    }

    [Theory]
    [InlineData(0, 86400)]
    [InlineData(59, 86400)]
    [InlineData(86401, 86400)]
    [InlineData(3600, 0)]
    [InlineData(3600, 31536001)]
    public async Task Unusable_token_lifetimes_are_rejected(int accessSeconds, int refreshSeconds)
    {
        var h = new Host { AccessSeconds = accessSeconds, RefreshSeconds = refreshSeconds };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Equal(2, h.Requests.Count);
        Assert.Empty(h.SecretWrites);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"accountId\":\"1111111111111111\"}")]
    [InlineData("{\"profile\":{\"accountId\":1111111111111111}}")]
    [InlineData("{\"profile\":{\"accountId\":\"0\"}}")]
    [InlineData("{\"profile\":{\"accountId\":\"-1\"}}")]
    [InlineData("{\"profile\":{\"accountId\":\"someone\"}}")]
    [InlineData("{\"profile\":{\"accountId\":\"18446744073709551616\"}}")]
    public async Task Malformed_authenticated_identity_cannot_establish_a_session(string body)
    {
        var h = new Host { RawProfile = body };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Equal(3, h.Requests.Count);
        Assert.Empty(h.SecretWrites);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Version\":2,\"Fingerprint\":\"unused\",\"AccountId\":\"1111111111111111\",\"RefreshToken\":\"refresh-old\",\"ExpiresAt\":\"2026-10-01T00:00:00Z\"}")]
    public async Task Invalid_saved_refresh_record_falls_back_to_npsso(string saved)
    {
        var h = new Host();
        h.SecretsValues["refresh-token"] = saved;

        Assert.NotNull(await h.Client.GetSessionAsync(default));

        Assert.Contains("/authorize?", h.Requests[0].Url);
        Assert.Equal(3, h.Requests.Count);
    }

    [Fact]
    public async Task Refused_protected_write_does_not_cache_tokens_or_keep_an_authenticated_session()
    {
        var h = new Host { RefuseSecretWrite = true };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.False(h.SecretsValues.ContainsKey("refresh-token"));
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
        h.RefuseSecretWrite = false;
        Assert.NotNull(await h.Client.GetSessionAsync(default));
        Assert.Equal(6, h.Requests.Count);
        Assert.Contains("/authorize?", h.Requests[3].Url);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_releases_the_authentication_gate()
    {
        var h = new Host { BlockProfile = true };
        using var cancellation = new CancellationTokenSource();
        var pending = h.Client.GetSessionAsync(cancellation.Token);
        await h.ProfileStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(h.SecretWrites);
        h.BlockProfile = false;
        Assert.NotNull(await h.Client.GetSessionAsync(default).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Cancellation_while_waiting_for_the_authentication_gate_does_not_disrupt_its_owner()
    {
        var h = new Host { BlockProfile = true };
        var first = h.Client.GetSessionAsync(default);
        await h.ProfileStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var waiting = h.Client.GetSessionAsync(cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(3, h.Requests.Count);
        h.ReleaseProfile.TrySetResult();
        Assert.NotNull(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(h.SecretWrites);
    }

    [Fact]
    public async Task A_transport_timeout_soft_fails_without_persisting_partial_authentication()
    {
        var h = new Host { OnRequest = _ => throw new OperationCanceledException() };

        Assert.Null(await h.Client.GetSessionAsync(default));

        Assert.Empty(h.SecretWrites);
        Assert.Empty(h.SettingsValues);
        Assert.Empty(h.CacheEntries);
    }

    private static Dictionary<string, string> Fields(string value) => value.Split('&')
        .Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]), x => Uri.UnescapeDataString(x[1]));

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Host : IPluginContext, IPluginHttp, IPluginSecrets, IPluginSettings, IPluginCache
    {
        public string PluginId => "psn";
        public IPluginHttp Http => this;
        public IPluginSecrets Secrets => this;
        public IPluginSettings Settings => this;
        public IPluginCache Cache => this;
        public Dictionary<string, string> SecretsValues { get; } = new() { ["npsso"] = Npsso };
        public Dictionary<string, string> SettingsValues { get; } = [];
        public Dictionary<string, PluginCacheEntry> CacheEntries { get; } = [];
        public List<PluginHttpRequest> Requests { get; } = [];
        public List<(string Key, string Value)> SecretWrites { get; } = [];
        public Clock Clock { get; } = new();
        public PsnAccountClient Client { get; set; }
        public int AuthorizeStatus { get; init; } = 302;
        public int RefreshStatus { get; set; } = 200;
        public string LocationHeader { get; init; } = "Location";
        public string? Location { get; init; } = Redirect + "?code=code-one";
        public string AccessToken { get; set; } = "access-one";
        public string RefreshToken { get; set; } = "refresh-one";
        public string ProfileAccountId { get; set; } = AccountId;
        public int AccessSeconds { get; init; } = 3600;
        public int RefreshSeconds { get; init; } = 86400;
        public string? RawToken { get; init; }
        public string? RawProfile { get; init; }
        public bool RefuseSecretWrite { get; set; }
        public Action<PluginHttpRequest>? OnRequest { get; set; }
        public Action? AfterSecretWrite { get; set; }
        public bool BlockProfile { get; set; }
        public TaskCompletionSource ProfileStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseProfile { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Host() => Client = new PsnAccountClient(this, Clock);

        public void SetNpsso(string? value)
        {
            if (value is null) SecretsValues.Remove("npsso");
            else SecretsValues["npsso"] = value;
        }

        public async Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            OnRequest?.Invoke(request);
            var uri = new Uri(request.Url);
            if (uri.Host == "ca.account.sony.com" && uri.AbsolutePath == "/api/authz/v3/oauth/authorize")
                return new(AuthorizeStatus, [], Location is null ? [] : new Dictionary<string, string> { [LocationHeader] = Location });
            if (uri.Host == "ca.account.sony.com" && uri.AbsolutePath == "/api/authz/v3/oauth/token")
            {
                var status = Fields(Encoding.UTF8.GetString(request.Body!))["grant_type"] == "refresh_token" ? RefreshStatus : 200;
                var bytes = RawToken is null ? JsonSerializer.SerializeToUtf8Bytes(new
                {
                    access_token = AccessToken, refresh_token = RefreshToken, expires_in = AccessSeconds,
                    refresh_token_expires_in = RefreshSeconds, token_type = "bearer"
                }) : Encoding.UTF8.GetBytes(RawToken);
                return new(status, bytes, new Dictionary<string, string>());
            }
            if (uri.Host == "us-prof.np.community.playstation.net" && uri.AbsolutePath == "/userProfile/v1/users/me/profile2")
            {
                if (BlockProfile)
                {
                    ProfileStarted.TrySetResult();
                    await ReleaseProfile.Task.WaitAsync(cancellationToken);
                }
                var bytes = RawProfile is null ? JsonSerializer.SerializeToUtf8Bytes(new { profile = new { accountId = ProfileAccountId } }) : Encoding.UTF8.GetBytes(RawProfile);
                return new(200, bytes, new Dictionary<string, string>());
            }
            throw new Xunit.Sdk.XunitException("Unexpected fixture endpoint: " + request.Url);
        }

        ValueTask<string?> IPluginSecrets.GetAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(SecretsValues.GetValueOrDefault(key));
        }
        ValueTask IPluginSecrets.SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RefuseSecretWrite) throw new NotSupportedException("Protection unavailable.");
            SecretWrites.Add((key, value));
            SecretsValues[key] = value;
            AfterSecretWrite?.Invoke();
            return ValueTask.CompletedTask;
        }
        ValueTask IPluginSecrets.RemoveAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SecretsValues.Remove(key);
            return ValueTask.CompletedTask;
        }
        ValueTask<string?> IPluginSettings.GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(SettingsValues.GetValueOrDefault(key));
        ValueTask IPluginSettings.SetAsync(string key, string? value, CancellationToken cancellationToken)
        {
            if (value is null) SettingsValues.Remove(key);
            else SettingsValues[key] = value;
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
