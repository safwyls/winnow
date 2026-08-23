using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Hoard.Enrich.Igdb.Credentials;
using Hoard.Enrich.Igdb.Storage;
using Microsoft.Extensions.Logging;

namespace Hoard.Enrich.Igdb.Auth;

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
/// </summary>
public sealed class TwitchTokenProvider : IIgdbTokenProvider
{
    /// <summary>Named <see cref="HttpClient"/> used for token minting.</summary>
    public const string HttpClientName = "igdb-token";

    /// <summary>Settings key: client id the stored token belongs to.</summary>
    public const string TokenClientIdKey = "igdb.token.client_id";

    /// <summary>Settings key: the access token itself.</summary>
    public const string TokenValueKey = "igdb.token.access_token";

    /// <summary>Settings key: token expiry, ISO-8601 round-trip, UTC.</summary>
    public const string TokenExpiresAtKey = "igdb.token.expires_at";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIgdbCredentialProvider _credentials;
    private readonly ISettingsStore _settings;
    private readonly IgdbOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<TwitchTokenProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IgdbAccessToken? _cached;
    private bool _loadedFromStore;

    public TwitchTokenProvider(
        IHttpClientFactory httpClientFactory,
        IIgdbCredentialProvider credentials,
        ISettingsStore settings,
        IgdbOptions options,
        TimeProvider clock,
        ILogger<TwitchTokenProvider> log)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _settings = settings;
        _options = options;
        _clock = clock;
        _log = log;
    }

    /// <summary>How many token requests this provider has actually sent. Test hook.</summary>
    public int MintCount { get; private set; }

    public async Task<IgdbAccessToken?> GetAsync(CancellationToken ct = default)
    {
        var credentials = await _credentials.GetAsync(ct);
        if (credentials is null)
        {
            return null;
        }

        // Fast path outside the lock: a valid cached token is the overwhelmingly
        // common case and must not serialise callers.
        var cached = _cached;
        if (IsUsable(cached, credentials))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (IsUsable(_cached, credentials))
            {
                return _cached;
            }

            if (!_loadedFromStore)
            {
                _loadedFromStore = true;
                var persisted = await LoadAsync(ct);
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
        var credentials = await _credentials.GetAsync(ct);
        if (credentials is null)
        {
            return null;
        }

        await _gate.WaitAsync(ct);
        try
        {
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

    private bool IsUsable(IgdbAccessToken? token, IgdbCredentials credentials)
        => token is not null
           && string.Equals(token.ClientId, credentials.ClientId, StringComparison.Ordinal)
           && token.ExpiresAt - _clock.GetUtcNow() > _options.TokenRefreshSkew;

    private async Task<IgdbAccessToken?> MintAsync(IgdbCredentials credentials, CancellationToken ct)
    {
        // Twitch takes client-credentials parameters in the query string.
        // Escaped, not interpolated raw: a secret containing '&' would
        // otherwise silently truncate the request.
        var uri = new UriBuilder(_options.TokenEndpoint)
        {
            Query = string.Join('&',
                "client_id=" + Uri.EscapeDataString(credentials.ClientId),
                "client_secret=" + Uri.EscapeDataString(credentials.ClientSecret),
                "grant_type=client_credentials"),
        }.Uri;

        var http = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
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

    private async Task<IgdbAccessToken?> LoadAsync(CancellationToken ct)
    {
        var clientId = await _settings.GetAsync(TokenClientIdKey, ct);
        var value = await _settings.GetAsync(TokenValueKey, ct);
        var expiresAtRaw = await _settings.GetAsync(TokenExpiresAtKey, ct);

        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(value)
            || !DateTimeOffset.TryParse(
                expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            return null;
        }

        return new IgdbAccessToken(clientId, value, expiresAt);
    }

    private async Task SaveAsync(IgdbAccessToken token, CancellationToken ct)
    {
        await _settings.SetAsync(TokenClientIdKey, token.ClientId, ct);
        await _settings.SetAsync(TokenValueKey, token.AccessToken, ct);
        await _settings.SetAsync(
            TokenExpiresAtKey, token.ExpiresAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), ct);
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
