using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Winnow.PluginSdk;

namespace Winnow.Plugin.Psn;

internal sealed record PsnSession(string AccountId, string Scope, string AccessToken);

internal sealed class PsnAccountClient(IPluginContext context, TimeProvider clock)
{
    private const string Auth = "https://ca.account.sony.com/api/authz/v3/oauth";
    private const string Redirect = "com.scee.psxandroid.scecompcall://redirect";
    // Public PS App client identification used by the referenced PSN protocol, not a user credential.
    private const string ClientAuthorization = "Basic MDk1MTUxNTktNzIzNy00MzcwLTliNDAtMzgwNmU2N2MwODkxOnVjUGprYTV0bnRCMktxc1A=";
    private const string Scopes = "psn:mobile.v2.core psn:clientapp";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PsnSession? _session;
    private DateTimeOffset _expires;

    internal async Task<bool> IsCurrentAsync(PsnSession session, CancellationToken ct)
    {
        var npsso = await context.Secrets.GetAsync("npsso", ct);
        return Npsso(npsso) && session.Scope == Scope(npsso!, session.AccountId);
    }

    internal async Task<PsnSession?> GetSessionAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var npsso = await context.Secrets.GetAsync("npsso", ct);
            if (!Npsso(npsso))
            {
                _session = null;
                await context.Secrets.RemoveAsync("refresh-token", ct);
                return null;
            }
            var fingerprint = Hash(npsso!);
            if (_session is not null && await IsCurrentAsync(_session, ct) && _expires > clock.GetUtcNow()) return _session;
            _session = null;
            var saved = await ReadCredentialAsync(ct);
            if (saved?.Fingerprint != fingerprint) saved = null;
            PluginHttpResponse response;
            if (saved is not null && saved.ExpiresAt > clock.GetUtcNow())
            {
                response = await context.Http.SendAsync(Form(new()
                {
                    ["grant_type"] = "refresh_token", ["refresh_token"] = saved.RefreshToken,
                    ["token_format"] = "jwt", ["scope"] = Scopes
                }), ct);
                if (response.StatusCode is 400 or 401 or 403)
                {
                    await context.Secrets.RemoveAsync("refresh-token", ct);
                    saved = null;
                    response = await AuthorizeAsync(npsso!, ct);
                }
            }
            else response = await AuthorizeAsync(npsso!, ct);
            if (response.StatusCode != 200) return null;
            using var json = Parse(response.Body);
            var root = json.RootElement;
            var access = Text(root, "access_token");
            var refresh = Text(root, "refresh_token");
            if (!Token(access) || !Token(refresh) || !Integer(root, "expires_in", out var seconds) || seconds is < 60 or > 86400
                || !Integer(root, "refresh_token_expires_in", out var refreshSeconds) || refreshSeconds is < 1 or > 31536000
                || !string.Equals(Text(root, "token_type"), "bearer", StringComparison.OrdinalIgnoreCase)) return null;

            // Resolve the authenticated account from Sony, never from a supplied username or decoded JWT.
            var profile = await context.Http.SendAsync(new("https://us-prof.np.community.playstation.net/userProfile/v1/users/me/profile2?fields=accountId")
            { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + access } }, ct);
            if (profile.StatusCode != 200) return null;
            using var identity = Parse(profile.Body);
            var accountId = identity.RootElement.TryGetProperty("profile", out var value) ? Text(value, "accountId") : null;
            if (!AccountId(accountId) || saved is not null && saved.AccountId != accountId) return null;
            var session = new PsnSession(accountId!, Scope(npsso!, accountId!), access!);
            if (!await IsCurrentAsync(session, ct)) return null;
            var credential = new Credential(1, fingerprint, accountId!, refresh!, clock.GetUtcNow().AddSeconds(refreshSeconds));
            // Failure to protect the refresh credential must not silently persist it elsewhere.
            await context.Secrets.SetAsync("refresh-token", JsonSerializer.Serialize(credential), ct);
            if (!await IsCurrentAsync(session, ct))
            {
                await context.Secrets.RemoveAsync("refresh-token", ct);
                return null;
            }
            _expires = clock.GetUtcNow().AddSeconds(seconds - 30);
            return _session = session;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { return null; }
        finally { _gate.Release(); }
    }

    private async Task<PluginHttpResponse> AuthorizeAsync(string npsso, CancellationToken ct)
    {
        var query = Encode(new()
        {
            ["access_type"] = "offline", ["client_id"] = "09515159-7237-4370-9b40-3806e67c0891",
            ["redirect_uri"] = Redirect, ["response_type"] = "code", ["scope"] = Scopes
        });
        var response = await context.Http.SendAsync(new(Auth + "/authorize?" + query)
        { Headers = new Dictionary<string, string> { ["Cookie"] = "npsso=" + npsso } }, ct);
        var location = response.Headers.FirstOrDefault(x => x.Key.Equals("Location", StringComparison.OrdinalIgnoreCase)).Value;
        if (response.StatusCode is not (302 or 303) || location is null || location.Length > 32768
            || !Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme != "com.scee.psxandroid.scecompcall"
            || uri.Host != "redirect" || uri.Port != -1 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath is not ("" or "/"))
            return Failed();
        var values = uri.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2)).ToArray();
        var codes = values.Where(x => x.Length == 2 && x[0] == "code").ToArray();
        if (codes.Length != 1 || values.Any(x => x[0] == "error")) return Failed();
        var code = Uri.UnescapeDataString(codes[0][1]);
        if (!Token(code)) return Failed();
        return await context.Http.SendAsync(Form(new()
        {
            ["code"] = code, ["redirect_uri"] = Redirect, ["grant_type"] = "authorization_code", ["token_format"] = "jwt"
        }), ct);
    }

    private async Task<Credential?> ReadCredentialAsync(CancellationToken ct)
    {
        var raw = await context.Secrets.GetAsync("refresh-token", ct);
        if (raw is null || raw.Length > 65536) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Credential>(raw);
            return value is { Version: 1 } && AccountId(value.AccountId) && Token(value.RefreshToken) ? value : null;
        }
        catch (JsonException) { return null; }
    }

    private static PluginHttpRequest Form(Dictionary<string, string> fields) => new(Auth + "/token")
    {
        Method = "POST", ContentType = "application/x-www-form-urlencoded", Body = Encoding.UTF8.GetBytes(Encode(fields)),
        Headers = new Dictionary<string, string> { ["Authorization"] = ClientAuthorization }
    };
    private static string Encode(Dictionary<string, string> fields) => string.Join('&', fields.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
    private static PluginHttpResponse Failed() => new(401, [], new Dictionary<string, string>());
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Scope(string npsso, string accountId) => Hash(Hash(npsso) + ":" + accountId);
    private static bool Npsso(string? value) => value is { Length: 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static bool AccountId(string? value) => value is { Length: > 0 and <= 20 } && value.All(char.IsAsciiDigit) && ulong.TryParse(value, out var id) && id > 0;
    private static bool Token(string? value) => value is { Length: > 0 and <= 32768 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~' or '+' or '/' or '=');
    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static bool Integer(JsonElement value, string name, out int result)
    { result = 0; return value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out result); }
    private static JsonDocument Parse(byte[] body) => body.Length <= 2 * 1024 * 1024 ? JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 }) : throw new InvalidDataException();
    internal static bool SoftFailure(Exception ex) => ex is IOException or HttpRequestException or JsonException or InvalidOperationException or ArgumentException or NotSupportedException or FormatException or CryptographicException;
    private sealed record Credential(int Version, string Fingerprint, string AccountId, string RefreshToken, DateTimeOffset ExpiresAt);
}
