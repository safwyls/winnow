using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Winnow.Ingest.Epic.Web.Credentials;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web.Auth;

/// <summary>
/// Owns the Epic OAuth session end to end: code exchange, refresh, persistence,
/// and giving up cleanly.
///
/// <para><b>The one endpoint.</b>
/// <c>POST /account/api/oauth/token</c> on
/// <c>account-public-service-prod03.ol.epicgames.com</c>, HTTP Basic with the
/// user-supplied client pair, form-encoded body, <c>token_type=eg1</c>. Verified
/// live 2026-08-26: the service validates the client pair before the grant, so a
/// wrong id/secret answers <c>invalid_client</c> (numeric 18033) whatever the
/// grant is — which is why <see cref="EpicSignInFailure"/> tells the two apart
/// and the UI can say something true about which one is wrong.</para>
///
/// <para><b>Secrets travel in the body, never the URI.</b> The authorization
/// code, the refresh token and the client secret all go into a
/// <c>FormUrlEncodedContent</c>. This is the same reasoning
/// <c>TwitchTokenProvider</c> records at length and it applies harder here: a
/// URI is the most-copied string in an HTTP stack — it reaches request logging,
/// <c>HttpRequestException</c> messages, proxy access logs and Polly telemetry —
/// and Epic's own login page describes the code as "full access to your Epic
/// account".</para>
///
/// <para><b>Three layers of cache, cheapest first:</b> the in-memory field, the
/// encrypted <see cref="IEpicTokenStore"/> (so a restart reuses a live session),
/// then Epic. One <see cref="SemaphoreSlim"/> serialises every mint and refresh,
/// so a burst of parallel calls on a cold start spends the refresh token once
/// rather than racing several exchanges of the same value against each other —
/// which, because Epic rotates the refresh token on every use, would leave all
/// but one of them holding a token that has already been superseded.</para>
///
/// <para><b>Expiry is read from the response, never assumed.</b> No lifetime is
/// hardcoded. Public claims about Epic's token lifetimes (8 hours, 23 days, 30
/// days) circulate widely and could not be confirmed from any authoritative
/// source during the spike, so this client believes only what the response says:
/// <c>expires_at</c> if present, else <c>expires_in</c> seconds, else a short
/// conservative floor. The refresh expiry is allowed to be absent entirely —
/// see <see cref="EpicOAuthToken.IsRefreshUsable"/>.</para>
/// </summary>
public sealed class EpicTokenProvider : IEpicTokenProvider
{
    /// <summary>Named <see cref="HttpClient"/> used for token minting and refresh.</summary>
    public const string HttpClientName = "epic-oauth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IEpicCredentialProvider _credentials;
    private readonly IEpicTokenStore _store;
    private readonly EpicWebOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EpicTokenProvider> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private EpicOAuthToken? _cached;
    private bool _loadedFromStore;

    /// <summary>
    /// Set once the refresh token has been rejected or has lapsed. Stops this
    /// process re-attempting a refresh that cannot succeed on every subsequent
    /// sync — the session is gone until the user signs in again, and hammering
    /// the endpoint to rediscover that on a 15-minute scheduler is exactly the
    /// traffic pattern the rate limiter exists to prevent.
    /// </summary>
    private bool _sessionLapsed;

    public EpicTokenProvider(
        IHttpClientFactory httpClientFactory,
        IEpicCredentialProvider credentials,
        IEpicTokenStore store,
        EpicWebOptions options,
        TimeProvider clock,
        ILogger<EpicTokenProvider> log)
    {
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
        _store = store;
        _options = options;
        _clock = clock;
        _log = log;
    }

    /// <summary>How many token requests this provider has actually sent. Test hook.</summary>
    public int TokenRequestCount { get; private set; }

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => await _credentials.GetAsync(ct) is not null;

    public async ValueTask<bool> IsSignedInAsync(CancellationToken ct = default)
    {
        if (await _credentials.GetAsync(ct) is not { } credentials)
        {
            return false;
        }

        var token = await PeekAsync(ct);
        return token is not null
            && string.Equals(token.ClientId, credentials.ClientId, StringComparison.Ordinal)
            && token.IsRefreshUsable(_clock.GetUtcNow(), _options.TokenRefreshSkew);
    }

    public Task<EpicSignInResult> SignInWithAuthorizationCodeAsync(
        string authorizationCode, CancellationToken ct = default)
        => SignInWithGrantAsync("authorization_code", "code", authorizationCode, ct);

    public Task<EpicSignInResult> SignInWithExchangeCodeAsync(
        string exchangeCode, CancellationToken ct = default)
        => SignInWithGrantAsync("exchange_code", "exchange_code", exchangeCode, ct);

    /// <summary>
    /// The one code-for-session exchange, shared by both interactive grants.
    ///
    /// <para><b>Why two grants at all.</b> The manual flow yields an
    /// <c>authorization_code</c> from Epic's redirect page; the embedded browser
    /// yields an <c>exchange_code</c>, because Epic's sign-in page pushes that
    /// value out through the launcher's <c>window.ue</c> bridge rather than
    /// rendering a code anywhere. They differ only in <c>grant_type</c> and in
    /// the name of the field carrying the value — everything after the response
    /// arrives is identical, so it lives here once rather than twice.</para>
    /// </summary>
    /// <param name="grantType">Epic's <c>grant_type</c> value.</param>
    /// <param name="codeField">Form field the code goes in for that grant.</param>
    /// <param name="code">The code. Never logged, never stored, never in a URI.</param>
    /// <param name="ct">Cancellation.</param>
    private async Task<EpicSignInResult> SignInWithGrantAsync(
        string grantType, string codeField, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return EpicSignInResult.Failed(EpicSignInFailure.InvalidAuthorizationCode);
        }

        if (await _credentials.GetAsync(ct) is not { } credentials)
        {
            return EpicSignInResult.Failed(EpicSignInFailure.NotConfigured);
        }

        await _gate.WaitAsync(ct);
        try
        {
            // The code is trimmed because it can arrive by copy-paste and a
            // trailing newline is the single most common way this fails. It is
            // not logged, not stored, and not echoed back in any result.
            var outcome = await RequestTokenAsync(
                credentials,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = grantType,
                    [codeField] = code.Trim(),
                    ["token_type"] = "eg1",
                },
                ct);

            if (outcome.Token is not { } token)
            {
                return EpicSignInResult.Failed(outcome.Failure);
            }

            _cached = token;
            _loadedFromStore = true;
            _sessionLapsed = false;
            await _store.SaveAsync(token, ct);

            // Account id and display name are deliberately absent from this line:
            // the interesting fact is that a session now exists, and naming the
            // account only puts a real person into the log file.
            _log.LogInformation(
                "Signed in to Epic. Access token expires {ExpiresAt:O}; refresh {RefreshExpiry}; "
                + "session {Persistence}.",
                token.ExpiresAt,
                token.RefreshExpiresAt is { } refreshExpiry
                    ? refreshExpiry.ToString("O", CultureInfo.InvariantCulture)
                    : "expiry not stated by Epic",
                _store.CanPersist ? "stored encrypted" : "held in memory for this run only");

            return new EpicSignInResult(
                Succeeded: true,
                Failure: EpicSignInFailure.None,
                AccountId: token.AccountId,
                DisplayName: token.DisplayName,
                Persisted: _store.CanPersist);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EpicOAuthToken?> GetAsync(CancellationToken ct = default)
    {
        if (await _credentials.GetAsync(ct) is not { } credentials)
        {
            return null;
        }

        // Fast path outside the lock: a live access token is the overwhelmingly
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
                _cached = await _store.LoadAsync(ct);
                if (IsUsable(_cached, credentials))
                {
                    _log.LogDebug("Reused stored Epic session; access token expires {ExpiresAt:O}.", _cached!.ExpiresAt);
                    return _cached;
                }
            }

            return await RefreshLockedAsync(credentials, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EpicOAuthToken?> RefreshAsync(EpicOAuthToken? staleToken, CancellationToken ct = default)
    {
        if (await _credentials.GetAsync(ct) is not { } credentials)
        {
            return null;
        }

        await _gate.WaitAsync(ct);
        try
        {
            // Someone else already replaced the token this caller was holding.
            // Hand theirs back rather than spending the refresh token again —
            // Epic rotates it on use, so a second exchange of the same value is
            // an exchange of a value that no longer exists.
            if (staleToken is not null
                && _cached is not null
                && !string.Equals(_cached.AccessToken, staleToken.AccessToken, StringComparison.Ordinal)
                && IsUsable(_cached, credentials))
            {
                return _cached;
            }

            return await RefreshLockedAsync(credentials, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _cached = null;
            _loadedFromStore = true;
            _sessionLapsed = false;
            await _store.ClearAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads the current session without refreshing or minting anything.</summary>
    private async Task<EpicOAuthToken?> PeekAsync(CancellationToken ct)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        if (_loadedFromStore)
        {
            return null;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (!_loadedFromStore)
            {
                _loadedFromStore = true;
                _cached = await _store.LoadAsync(ct);
            }

            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Refreshes the session. Caller must hold <see cref="_gate"/>.
    ///
    /// <para><b>This is the degrade-to-local path, and it never throws.</b>
    /// Every exit that is not a fresh token returns null, and null means the
    /// caller contributes no candidates this pass while the local readers carry
    /// on exactly as they would with the API switched off.</para>
    /// </summary>
    private async Task<EpicOAuthToken?> RefreshLockedAsync(
        EpicClientCredentials credentials, CancellationToken ct)
    {
        if (_sessionLapsed)
        {
            return null;
        }

        var current = _cached;
        if (current is null || !string.Equals(current.ClientId, credentials.ClientId, StringComparison.Ordinal))
        {
            // No session, or one belonging to a different client because the
            // user edited their credentials. Either way there is nothing to
            // refresh and the user has to sign in again. Not an error, and not
            // logged as one — a fresh install sits here permanently.
            return null;
        }

        if (!current.IsRefreshUsable(_clock.GetUtcNow(), _options.TokenRefreshSkew))
        {
            // Epic told us when the refresh token would lapse, and it has. Drop
            // it: keeping it would produce one doomed request per sync forever.
            _log.LogInformation(
                "The stored Epic session expired on {RefreshExpiresAt:O}. Epic ownership is disabled until "
                + "the user signs in again; the local Epic readers are unaffected.",
                current.RefreshExpiresAt);
            await LapseAsync(ct);
            return null;
        }

        var outcome = await RequestTokenAsync(
            credentials,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = current.RefreshToken,
                ["token_type"] = "eg1",
            },
            ct);

        if (outcome.Token is { } refreshed)
        {
            _cached = refreshed;
            await _store.SaveAsync(refreshed, ct);
            _log.LogDebug("Refreshed the Epic session; access token expires {ExpiresAt:O}.", refreshed.ExpiresAt);
            return refreshed;
        }

        switch (outcome.Failure)
        {
            case EpicSignInFailure.InvalidAuthorizationCode:
                // On a refresh grant this is Epic rejecting the refresh token
                // itself: lapsed, revoked, superseded, or invalidated by a
                // password change. Unrecoverable without a new interactive login.
                _log.LogInformation(
                    "Epic rejected the stored refresh token, so the session has ended. Epic ownership is "
                    + "disabled until the user signs in again; the local Epic readers are unaffected.");
                await LapseAsync(ct);
                return null;

            case EpicSignInFailure.InvalidClientCredentials:
                // The pair is wrong. The session may well still be fine, so it
                // is NOT cleared — fixing the credentials should not also cost
                // the user their login.
                _log.LogWarning(
                    "Epic rejected the configured OAuth client credentials. Epic ownership is skipped this "
                    + "pass; the local Epic readers are unaffected.");
                _sessionLapsed = true;
                return null;

            default:
                // Transient: offline, a 5xx the retry policy could not outlast,
                // a 429, or an unparseable body. Nothing is cleared and nothing
                // is latched — the next sync tries again.
                _log.LogWarning(
                    "Could not refresh the Epic session ({Failure}). Epic ownership is skipped this pass; "
                    + "the local Epic readers are unaffected.",
                    outcome.Failure);
                return null;
        }
    }

    /// <summary>Latches the session off for this process and forgets the stored one.</summary>
    private async Task LapseAsync(CancellationToken ct)
    {
        _cached = null;
        _sessionLapsed = true;
        await _store.ClearAsync(ct);
    }

    private bool IsUsable(EpicOAuthToken? token, EpicClientCredentials credentials)
        => token is not null
            && string.Equals(token.ClientId, credentials.ClientId, StringComparison.Ordinal)
            && token.IsAccessUsable(_clock.GetUtcNow(), _options.TokenRefreshSkew);

    /// <summary>
    /// One request to the token endpoint. Returns a token or a reason, never an
    /// exception and never anything carrying a secret.
    /// </summary>
    private async Task<TokenOutcome> RequestTokenAsync(
        EpicClientCredentials credentials, Dictionary<string, string> form, CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient(HttpClientName);

        // Counted before the attempt, not after a particular outcome: the point
        // of the counter is "how much did this provider talk to Epic", which a
        // failed request also answers.
        TokenRequestCount++;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(form),
            };

            // HTTP Basic of client_id:client_secret, per the OAuth spec and what
            // Epic requires. Built here, used once, and never handed to a logger.
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(credentials.ClientId + ":" + credentials.ClientSecret)));

            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var token = EpicOAuthJson.TryReadToken(body, credentials.ClientId, _clock.GetUtcNow(), _options);
                return token is null
                    ? new TokenOutcome(null, EpicSignInFailure.UnexpectedResponse)
                    : new TokenOutcome(token, EpicSignInFailure.None);
            }

            // Epic's error bodies are structured and their errorCode is safe to
            // reason about — but the body as a whole can echo request parameters,
            // so only the classification and the status code ever leave here.
            var failure = ClassifyFailure(response.StatusCode, body);
            _log.LogDebug(
                "Epic token request returned {StatusCode}, classified as {Failure}.",
                (int)response.StatusCode, failure);
            return new TokenOutcome(null, failure);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not an auth failure, and must not be
            // swallowed into a silent degrade.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Type only, never the exception object: an inner exception is free
            // to quote the request, and the request carries the client secret.
            _log.LogDebug("Epic token request failed ({ExceptionType}).", ex.GetType().Name);
            return new TokenOutcome(null, EpicSignInFailure.Unreachable);
        }
    }

    /// <summary>
    /// Maps an Epic error response onto the reason the caller acts on.
    ///
    /// <para>Epic distinguishes these by <c>errorCode</c> rather than by status —
    /// both a bad client pair and a spent authorization code come back 400 — so
    /// the code is what is matched. Matching is by substring on the stable middle
    /// of the identifier, because Epic's full codes are long, versioned, and vary
    /// by grant (<c>…oauth.corrective_action_required</c>,
    /// <c>…oauth.expired_exchange_code_session</c> and friends).</para>
    /// </summary>
    private static EpicSignInFailure ClassifyFailure(HttpStatusCode status, string body)
    {
        var errorCode = EpicOAuthJson.TryReadErrorCode(body);

        if (errorCode is not null)
        {
            if (errorCode.Contains("invalid_client", StringComparison.OrdinalIgnoreCase))
            {
                return EpicSignInFailure.InvalidClientCredentials;
            }

            if (errorCode.Contains("authorization_code", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("exchange_code", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("invalid_refresh", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase)
                || errorCode.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                return EpicSignInFailure.InvalidAuthorizationCode;
            }

            if (errorCode.Contains("throttl", StringComparison.OrdinalIgnoreCase))
            {
                return EpicSignInFailure.Unreachable;
            }
        }

        return status switch
        {
            // A 401 from the token endpoint is about the client pair, not about
            // a bearer token — there is no bearer token on this request.
            HttpStatusCode.Unauthorized => EpicSignInFailure.InvalidClientCredentials,
            HttpStatusCode.BadRequest => EpicSignInFailure.InvalidAuthorizationCode,
            HttpStatusCode.TooManyRequests => EpicSignInFailure.Unreachable,
            >= HttpStatusCode.InternalServerError => EpicSignInFailure.Unreachable,
            _ => EpicSignInFailure.UnexpectedResponse,
        };
    }

    private readonly record struct TokenOutcome(EpicOAuthToken? Token, EpicSignInFailure Failure);
}

/// <summary>
/// Reads Epic's OAuth responses. Hand-walked with <see cref="JsonDocument"/>
/// rather than bound to a POCO, matching the local Epic readers and for the same
/// reason: the shape has optional fields whose <i>absence</i> is meaningful, and
/// a binder turns absence into a default that reads as an answer.
/// </summary>
internal static class EpicOAuthJson
{
    /// <summary>
    /// Builds a token from a successful response body, or null when the body is
    /// not one.
    ///
    /// <para><b>Expiry is taken from whatever Epic actually sent.</b>
    /// <c>expires_at</c> is preferred because it is absolute and immune to the
    /// round-trip latency an <c>expires_in</c> countdown silently absorbs;
    /// <c>expires_in</c> is the fallback; and if neither is present the token
    /// gets a deliberately short floor so it is refreshed soon rather than
    /// trusted indefinitely.</para>
    ///
    /// <para><b>The refresh expiry is allowed to be missing and is left null when
    /// it is.</b> Not a sentinel, not <c>DateTimeOffset.MaxValue</c>, not
    /// "now + a guess" — null, meaning Epic did not say. See
    /// <see cref="EpicOAuthToken.IsRefreshUsable"/> for why that distinction
    /// decides whether a live session keeps working.</para>
    /// </summary>
    public static EpicOAuthToken? TryReadToken(
        string body, string clientId, DateTimeOffset now, EpicWebOptions options)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessToken = ReadString(root, "access_token");
            var refreshToken = ReadString(root, "refresh_token");
            var accountId = ReadString(root, "account_id");

            if (string.IsNullOrWhiteSpace(accessToken)
                || string.IsNullOrWhiteSpace(refreshToken)
                || string.IsNullOrWhiteSpace(accountId))
            {
                return null;
            }

            var expiresAt =
                ReadDate(root, "expires_at")
                ?? ReadOffset(root, "expires_in", now)
                ?? now + options.FallbackAccessTokenLifetime;

            var refreshExpiresAt =
                ReadDate(root, "refresh_expires_at")
                ?? ReadOffset(root, "refresh_expires", now);

            return new EpicOAuthToken(
                clientId,
                accessToken,
                refreshToken,
                accountId,
                ReadString(root, "displayName"),
                expiresAt,
                refreshExpiresAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>errorCode</c> from an Epic error body, or null. Only the code is
    /// ever extracted — never <c>errorMessage</c>, which can quote the request.
    /// </summary>
    public static string? TryReadErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Epic sends both its own errorCode and an OAuth-standard `error`
            // on the token endpoint (verified live 2026-08-26: a bad client pair
            // returns errorCode errors.com.epicgames.account.invalid_client_credentials
            // alongside error "invalid_client"). Either will do.
            return ReadString(document.RootElement, "errorCode")
                ?? ReadString(document.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

    /// <summary>An absolute ISO-8601 timestamp field, or null when absent or unparseable.</summary>
    private static DateTimeOffset? ReadDate(JsonElement root, string name)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;

    /// <summary>
    /// A seconds-from-now field, or null. A zero or negative count is treated as
    /// absent: it would produce a token that is already expired, and a token
    /// this client refuses to use is worse than one it re-checks.
    /// </summary>
    private static DateTimeOffset? ReadOffset(JsonElement root, string name, DateTimeOffset now)
        => root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var seconds)
            && seconds > 0
                ? now.AddSeconds(seconds)
                : null;
}
