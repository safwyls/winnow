using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Igdb.Auth;

/// <summary>
/// Mints and caches Twitch app access tokens (§4.4):
/// <c>POST https://id.twitch.tv/oauth2/token?client_id=…&amp;client_secret=…&amp;grant_type=client_credentials</c>.
///
/// <para>Three layers of cache, cheapest first: the in-memory field, the
/// <c>settings</c> table (so a restart reuses a token that has 59 days left),
/// then Twitch. A single <see cref="SemaphoreSlim"/> serialises minting so a
/// burst of parallel enrichment calls on a cold start produces one token
/// request, not one per call.</para>
///
/// <para>The persisted token is bound to the client id that minted it; changing
/// credentials therefore invalidates it automatically rather than sending a
/// token belonging to a different application.</para>
///
/// <para>What reaches disk is one DPAPI-protected blob — the token is a bearer
/// credential, so it gets the same protection the client secret does, and the
/// same refusal on a host that cannot encrypt: the token is minted and used in
/// memory and simply not remembered across restarts. The three plaintext rows
/// an older build wrote are migrated into the blob and left empty on first
/// load; the token is machine-minted and free to re-mint, so those rows are
/// emptied even on a host that cannot encrypt, unlike the user-typed client
/// secret.</para>
/// </summary>
public sealed class TwitchTokenProvider : IIgdbTokenProvider, IIgdbCredentialUpdater
{
    /// <summary>Named <see cref="HttpClient"/> used for token minting.</summary>
    public const string HttpClientName = "igdb-token";

    /// <summary>Settings key holding the protected token blob.</summary>
    public const string TokenBlobKey = "igdb.token.v1";

    /// <summary>Settings key that held the minting client id in the clear, before the blob.</summary>
    public const string TokenClientIdKey = "igdb.token.client_id";

    /// <summary>Settings key that held the access token itself in the clear.</summary>
    public const string TokenValueKey = "igdb.token.access_token";

    /// <summary>Settings key that held the expiry in the clear.</summary>
    public const string TokenExpiresAtKey = "igdb.token.expires_at";

    private static readonly JsonSerializerOptions BlobSerializerOptions = new()
    {
        // Nulls are written, not skipped, so the persisted shape is closed.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIgdbCredentialProvider _credentials;
    private readonly ISettingsStore _settings;
    private readonly IIgdbSecretProtector _protector;
    private readonly IgdbOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<TwitchTokenProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IgdbAccessToken? _cached;
    private bool _loadedFromStore;
    private bool _warnedAboutProtection;

    public TwitchTokenProvider(
        IHttpClientFactory httpClientFactory,
        IIgdbCredentialProvider credentials,
        ISettingsStore settings,
        IIgdbSecretProtector protector,
        IgdbOptions options,
        TimeProvider clock,
        ILogger<TwitchTokenProvider> log)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _settings = settings;
        _protector = protector;
        _options = options;
        _clock = clock;
        _log = log;
    }

    /// <summary>How many token requests this provider has actually sent. Test hook.</summary>
    public int MintCount { get; private set; }

    public async Task<IgdbAccessToken?> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var credentials = await _credentials.GetAsync(ct);
            if (credentials is null) return null;

            if (IsUsable(_cached, credentials))
            {
                return _cached;
            }

            if (!_loadedFromStore)
            {
                var persisted = await LoadAsync(ct);
                _loadedFromStore = true;
                if (IsUsable(persisted, credentials))
                {
                    _cached = persisted;
                    _log.LogDebug("Reused persisted IGDB token; expires {ExpiresAt:O}.", persisted!.ExpiresAt);
                    return _cached;
                }
            }

            return await MintAsync(credentials, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IgdbAccessToken?> RefreshAsync(IgdbAccessToken? staleToken, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var credentials = await _credentials.GetAsync(ct);
            if (credentials is null) return null;

            // Someone else already replaced the token this caller was holding —
            // theirs is fresh, so hand it back instead of minting again.
            if (staleToken is not null
                && _cached is not null
                && !string.Equals(_cached.AccessToken, staleToken.AccessToken, StringComparison.Ordinal)
                && IsUsable(_cached, credentials))
            {
                return _cached;
            }

            _cached = null;
            return await MintAsync(credentials, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateCredentialsAsync(Func<Task> update, CancellationToken ct = default)
    {
        // Keep persistence within the mint/load gate: an older in-flight token
        // must finish before the transaction removes it, not restore it later.
        await _gate.WaitAsync(ct);
        try
        {
            await _credentials.UpdateAsync(update, ct);
            _cached = null;
            _loadedFromStore = false;
        }
        finally { _gate.Release(); }
    }

    private bool IsUsable(IgdbAccessToken? token, IgdbCredentials credentials)
        => token is not null
           && string.Equals(token.ClientId, credentials.ClientId, StringComparison.Ordinal)
           && token.ExpiresAt - _clock.GetUtcNow() > _options.TokenRefreshSkew;

    private async Task<IgdbAccessToken?> MintAsync(IgdbCredentials credentials, CancellationToken ct)
    {
        // §4.4 documents the client-credentials call with the parameters in the
        // query string, and Twitch does accept them there — but a URI is the
        // most-copied string in any HTTP stack. It lands in HttpClient logging,
        // in `HttpRequestException` messages, in proxy and reverse-proxy access
        // logs, in Polly's telemetry, and in the request-replay diagnostics this
        // very module ships. A form-encoded body goes to none of those, and
        // Twitch accepts `application/x-www-form-urlencoded` for the same
        // parameters. FormUrlEncodedContent also escapes each value, so a secret
        // containing '&' cannot truncate the request.
        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["client_id"] = credentials.ClientId,
                ["client_secret"] = credentials.ClientSecret,
                ["grant_type"] = "client_credentials",
            }),
        };

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Status only. The response body of a failed token request can echo
            // request parameters, so it is never logged.
            _log.LogWarning(
                "Twitch token request failed with {StatusCode}; IGDB enrichment is unavailable this run.",
                (int)response.StatusCode);
            return null;
        }

        MintCount++;

        var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            _log.LogWarning("Twitch token response contained no access_token; IGDB enrichment is unavailable.");
            return null;
        }

        var expiresAt = _clock.GetUtcNow().AddSeconds(payload.ExpiresIn > 0 ? payload.ExpiresIn : 3600);
        var token = new IgdbAccessToken(credentials.ClientId, payload.AccessToken, expiresAt);
        _cached = token;
        _loadedFromStore = true;

        await SaveAsync(token, ct);
        _log.LogInformation("Minted IGDB access token; expires {ExpiresAt:O}.", token.ExpiresAt);
        return token;
    }

    /// <summary>
    /// The persisted token, or null. One protected blob holds all three facts
    /// (client id, value, expiry) so the unit of storage is the unit of meaning —
    /// the alternative would leave the client id and the expiry sitting in the
    /// clear next to an encrypted value, or allow a partial write whose halves
    /// disagreed.
    ///
    /// <para>The three plaintext rows an older build wrote are migrated here:
    /// read, re-stored protected, left empty. They are emptied even when the
    /// protector refuses — the token is machine-minted and costs one mint to
    /// replace, so keeping it in the clear is never worth what it protects.</para>
    /// </summary>
    private async Task<IgdbAccessToken?> LoadAsync(CancellationToken ct)
    {
        var stored = await _settings.GetAsync(TokenBlobKey, ct);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            await ClearLegacyRowsAsync(ct);
            var json = _protector.Unprotect(stored);
            if (json is null)
            {
                // The protector has already said why, at the right level.
                return null;
            }

            try
            {
                var blob = JsonSerializer.Deserialize<StoredToken>(json, BlobSerializerOptions);
                return blob?.ToToken();
            }
            catch (JsonException)
            {
                // Decrypted successfully but did not parse: a shape change or a
                // truncated write. Same remedy as an unreadable blob: no token.
                // The exception object is never logged; its message quotes the
                // JSON, and the JSON is the token.
                _log.LogWarning("The persisted IGDB token could not be parsed; a new one will be minted.");
                return null;
            }
        }

        return await MigrateLegacyRowsAsync(ct);
    }

    private async Task<IgdbAccessToken?> MigrateLegacyRowsAsync(CancellationToken ct)
    {
        var clientId = await _settings.GetAsync(TokenClientIdKey, ct);
        var value = await _settings.GetAsync(TokenValueKey, ct);
        var expiresAtRaw = await _settings.GetAsync(TokenExpiresAtKey, ct);

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            await ClearLegacyRowsAsync(ct);
            return null;
        }

        var token = new IgdbAccessToken(clientId, value, expiresAt);

        var protectedJson = _protector.Protect(
            JsonSerializer.Serialize(StoredToken.From(token), BlobSerializerOptions));
        if (protectedJson is not null)
        {
            await _settings.SetAsync(TokenBlobKey, protectedJson, ct);
            _log.LogInformation(
                "The persisted IGDB token was migrated to protected storage; the plaintext rows were emptied.");
        }

        // Emptied either way, including on a host that cannot encrypt: the
        // token was minted, not typed, and refusing to use it makes the row
        // worthless to everyone except whatever else reads the disk.
        await ClearLegacyRowsAsync(ct);

        return protectedJson is null ? null : token;
    }

    private async Task ClearLegacyRowsAsync(CancellationToken ct)
    {
        foreach (var key in new[] { TokenValueKey, TokenClientIdKey, TokenExpiresAtKey })
        {
            if (!string.IsNullOrEmpty(await _settings.GetAsync(key, ct)))
            {
                await _settings.SetAsync(key, string.Empty, ct);
            }
        }
    }

    private async Task SaveAsync(IgdbAccessToken token, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(StoredToken.From(token), BlobSerializerOptions);
        var protectedJson = _protector.Protect(json);
        if (protectedJson is null)
        {
            if (!_warnedAboutProtection)
            {
                _warnedAboutProtection = true;
                _log.LogWarning(
                    "The IGDB access token cannot be encrypted at rest on this host ({Protector}), so it "
                    + "will not be stored. Tokens are minted on demand, so enrichment still works and simply "
                    + "mints again after a restart. Storing it unencrypted is deliberately not offered.",
                    _protector.Name);
            }

            return;
        }

        await _settings.SetAsync(TokenBlobKey, protectedJson, ct);
    }

    /// <summary>The persisted shape, and the whole of it.</summary>
    private sealed class StoredToken
    {
        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_at")]
        public string? ExpiresAt { get; set; }

        public static StoredToken From(IgdbAccessToken token) => new()
        {
            ClientId = token.ClientId,
            AccessToken = token.AccessToken,
            ExpiresAt = token.ExpiresAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        };

        public IgdbAccessToken? ToToken()
        {
            if (string.IsNullOrWhiteSpace(ClientId)
                || string.IsNullOrWhiteSpace(AccessToken)
                || !DateTimeOffset.TryParse(
                    ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
            {
                return null;
            }

            return new IgdbAccessToken(ClientId, AccessToken, expiresAt);
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
