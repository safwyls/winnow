using System.Text.Json;
using Winnow.PluginSdk;
using static Winnow.Plugin.Xbox.XboxProtocol;

namespace Winnow.Plugin.Xbox;

internal sealed class XboxAccountClient(IPluginContext context, TimeProvider clock)
{
    private const string TokenUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private const string Scope = "XboxLive.SignIn XboxLive.offline_access";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Attempt? _attempt;
    private XboxSession? _session;
    private DateTimeOffset _retryAfter;

    internal async Task<PluginAccountStatus> StatusAsync(CancellationToken ct)
    {
        var clientId = await ClientIdAsync(ct);
        var saved = await ReadSavedAsync(ct);
        return saved is not null && saved.ClientId == clientId
            ? new(true, "Connected to Xbox. Played history does not establish ownership.")
            : new(false, clientId is null ? "Enter a Microsoft public-client application ID to connect. Local PC discovery works without sign-in." : "Not connected. Local PC discovery works without sign-in.");
    }

    internal async Task<PluginSignInChallenge?> BeginAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _attempt = null;
            var clientId = await ClientIdAsync(ct);
            if (clientId is null) return null;
            var response = await context.Http.SendAsync(Form("https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
                new Dictionary<string, string> { ["client_id"] = clientId, ["scope"] = Scope }), ct);
            if (response.StatusCode != 200) return null;
            using var json = Parse(response.Body);
            var root = json.RootElement;
            var code = Text(root, "device_code");
            var userCode = Text(root, "user_code");
            var verification = Text(root, "verification_uri");
            var seconds = Number(root, "expires_in");
            if (!Token(code) || userCode is not { Length: > 0 and <= 32 } || !userCode.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || !SafeVerification(verification) || seconds is < 1 or > 1800) return null;
            var now = clock.GetUtcNow();
            var interval = Math.Clamp(Number(root, "interval"), 5, 60);
            var id = Guid.NewGuid().ToString("N");
            _attempt = new(id, clientId, code!, now.AddSeconds(seconds), interval, now.AddSeconds(interval), new CancellationTokenSource());
            return new(id, verification!, userCode, _attempt.Expires, interval);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { return null; }
        finally { _gate.Release(); }
    }

    internal async Task<PluginSignInResult> PollAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct, _attempt?.Cancellation.Token ?? CancellationToken.None);
        budget.CancelAfter(TimeSpan.FromSeconds(75));
        var operationCt = budget.Token;
        try
        {
            var attempt = _attempt;
            operationCt.ThrowIfCancellationRequested();
            var now = clock.GetUtcNow();
            if (attempt is null || attempt.Id != id) return Failed("This sign-in attempt is no longer available.");
            if (attempt.Expires <= now || await ClientIdAsync(ct) != attempt.ClientId)
            { _attempt = null; return Failed("Sign-in expired or the application ID changed. Connect again."); }
            if (now < attempt.NextPoll) return new(PluginSignInState.Pending, "Waiting for Microsoft sign-in.");
            _attempt = attempt with { NextPoll = now.AddSeconds(attempt.Interval) };
            var response = await context.Http.SendAsync(Form(TokenUrl, new Dictionary<string, string>
            {
                ["client_id"] = attempt.ClientId, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = attempt.DeviceCode
            }), operationCt);
            using var json = Parse(response.Body);
            var error = Text(json.RootElement, "error");
            if (error == "authorization_pending") return new(PluginSignInState.Pending, "Waiting for Microsoft sign-in.");
            if (error == "slow_down")
            {
                var interval = Math.Min(attempt.Interval + 5, 60);
                _attempt = attempt with { Interval = interval, NextPoll = now.AddSeconds(interval) };
                return new(PluginSignInState.SlowDown, "Microsoft requested a longer wait.");
            }
            if (response.StatusCode != 200 || error is not null) { _attempt = null; return Failed("Microsoft sign-in was declined or expired. Connect again."); }
            var access = Text(json.RootElement, "access_token");
            var refresh = Text(json.RootElement, "refresh_token");
            if (!Token(access) || !Token(refresh)) { _attempt = null; return Failed("Microsoft returned an incomplete sign-in response."); }
            var xbox = await ExchangeAsync(access!, operationCt);
            if (xbox is null) { _attempt = null; return Failed("Xbox sign-in is unavailable for this account or application."); }
            var saved = new Saved(1, attempt.ClientId, xbox.Xuid, refresh!);
            var serialized = JsonSerializer.Serialize(saved, Json);
            await context.Secrets.SetAsync("refresh-token", serialized, operationCt);
            _attempt = null;
            _session = xbox with { Stamp = Hash(serialized), ClientId = attempt.ClientId };
            _retryAfter = default;
            return new(PluginSignInState.Connected, "Connected to Xbox.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { _attempt = null; throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException)
        { _attempt = null; return Failed("Xbox sign-in could not be completed or stored securely."); }
        finally { _gate.Release(); }
    }

    internal async Task CancelAsync(string id, CancellationToken ct)
    {
        var pending = _attempt;
        if (pending?.Id == id) pending.Cancellation.Cancel();
        await _gate.WaitAsync(ct);
        try { if (_attempt?.Id == id) _attempt = null; }
        finally { _gate.Release(); }
    }

    internal async Task SignOutAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _attempt = null;
            _session = null;
            _retryAfter = default;
            await context.Secrets.RemoveAsync("refresh-token", ct);
        }
        finally { _gate.Release(); }
    }

    internal async Task<XboxSession?> SessionAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var clientId = await ClientIdAsync(ct);
            var saved = await ReadSavedAsync(ct);
            if (saved is null || saved.ClientId != clientId) { _session = null; return null; }
            var stamp = Hash(JsonSerializer.Serialize(saved, Json));
            if (_session is { } current && current.Stamp == stamp && current.Expires > clock.GetUtcNow().AddMinutes(2)) return current;
            if (_retryAfter > clock.GetUtcNow()) return null;
            var response = await context.Http.SendAsync(Form(TokenUrl, new Dictionary<string, string>
            {
                ["client_id"] = clientId!, ["grant_type"] = "refresh_token", ["refresh_token"] = saved.RefreshToken, ["scope"] = Scope
            }), ct);
            if (response.StatusCode != 200) { _retryAfter = clock.GetUtcNow().AddMinutes(2); return null; }
            using var json = Parse(response.Body);
            var access = Text(json.RootElement, "access_token");
            var refresh = Text(json.RootElement, "refresh_token");
            if (!Token(access) || (refresh is not null && !Token(refresh))) return null;
            // A successfully rotated refresh token must survive a subsequent Xbox service outage.
            if (refresh is not null)
            {
                saved = saved with { RefreshToken = refresh };
                await context.Secrets.SetAsync("refresh-token", JsonSerializer.Serialize(saved, Json), ct);
            }
            var xbox = await ExchangeAsync(access!, ct);
            if (xbox is null || xbox.Xuid != saved.Xuid) { _session = null; _retryAfter = clock.GetUtcNow().AddMinutes(2); return null; }
            _session = xbox with { ClientId = saved.ClientId, Stamp = Hash(JsonSerializer.Serialize(saved, Json)) };
            return _session;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException)
        { _retryAfter = clock.GetUtcNow().AddMinutes(2); return null; }
        finally { _gate.Release(); }
    }

    internal async Task<bool> IsCurrentAsync(XboxSession session, CancellationToken ct)
    {
        var saved = await ReadSavedAsync(ct);
        return saved is not null && await ClientIdAsync(ct) == session.ClientId && saved.Xuid == session.Xuid
            && Hash(JsonSerializer.Serialize(saved, Json)) == session.Stamp;
    }

    internal async Task<string?> CacheScopeAsync(CancellationToken ct)
    {
        var saved = await ReadSavedAsync(ct);
        return saved is not null && saved.ClientId == await ClientIdAsync(ct) ? Hash(saved.ClientId + ":" + saved.Xuid) : null;
    }

    private async Task<XboxSession?> ExchangeAsync(string access, CancellationToken ct)
    {
        var headers = new Dictionary<string, string> { ["x-xbl-contract-version"] = "1" };
        var user = await context.Http.SendAsync(Post("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + access }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
        }, headers), ct);
        if (user.StatusCode != 200) return null;
        using var first = Parse(user.Body);
        var token = Text(first.RootElement, "Token");
        var userClaims = Rows(Field(Field(first.RootElement, "DisplayClaims"), "xui")).ToArray();
        var hash = userClaims.Length == 1 ? Text(userClaims[0], "uhs") : null;
        if (!Token(token) || !Xuid(hash)) return null;
        var result = await context.Http.SendAsync(Post("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { token } }, RelyingParty = "http://xboxlive.com", TokenType = "JWT"
        }, headers), ct);
        if (result.StatusCode != 200) return null;
        using var second = Parse(result.Body);
        var claims = Rows(Field(Field(second.RootElement, "DisplayClaims"), "xui")).ToArray();
        var xid = claims.Length == 1 ? Text(claims[0], "xid") : null;
        var uhs = claims.Length == 1 ? Text(claims[0], "uhs") : null;
        var finalToken = Text(second.RootElement, "Token");
        var expires = Date(second.RootElement, "NotAfter");
        return Xuid(xid) && uhs == hash && Token(finalToken) && expires > clock.GetUtcNow()
            ? new(xid!, uhs!, finalToken!, expires.Value, "", "") : null;
    }

    private async Task<string?> ClientIdAsync(CancellationToken ct)
    {
        var id = (await context.Settings.GetAsync("client-id", ct))?.Trim();
        return Guid.TryParse(id, out var parsed) && parsed != Guid.Empty ? parsed.ToString("D") : null;
    }
    private async Task<Saved?> ReadSavedAsync(CancellationToken ct)
    {
        var value = await context.Secrets.GetAsync("refresh-token", ct);
        if (value is null || value.Length > 65536) return null;
        try
        {
            var saved = JsonSerializer.Deserialize<Saved>(value, Json);
            return saved is { Version: 1 } && Guid.TryParse(saved.ClientId, out _) && Xuid(saved.Xuid) && Token(saved.RefreshToken) ? saved : null;
        }
        catch (JsonException) { return null; }
    }
    private static bool SafeVerification(string? url) => url is "https://microsoft.com/devicelogin" or "https://www.microsoft.com/devicelogin" or "https://microsoft.com/link" or "https://www.microsoft.com/link";
    private static PluginSignInResult Failed(string message) => new(PluginSignInState.Failed, message);
    private sealed record Saved(int Version, string ClientId, string Xuid, string RefreshToken);
    private sealed record Attempt(string Id, string ClientId, string DeviceCode, DateTimeOffset Expires, int Interval, DateTimeOffset NextPoll, CancellationTokenSource Cancellation);
}

internal sealed record XboxSession(string Xuid, string UserHash, string Token, DateTimeOffset Expires, string ClientId, string Stamp)
{
    internal IReadOnlyDictionary<string, string> Headers => new Dictionary<string, string>
    {
        ["Authorization"] = "XBL3.0 x=" + UserHash + ";" + Token, ["x-xbl-contract-version"] = "2", ["Accept-Language"] = "en-US"
    };
}
