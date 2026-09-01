using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Enrich.SteamWeb.Credentials;

/// <summary>
/// The three unattended requests section 4.7's second amendment permits, and
/// nothing else. The whole closed list is the three URI constants below, so an
/// audit of condition 3 reads one file:
/// <list type="number">
///   <item><c>POST login.steampowered.com/jwt/finalizelogin</c>, spending the
///   stored refresh token as the nonce.</item>
///   <item>The <c>transfer_info</c> POSTs that call returns, narrowed to the
///   store host, whose responses carry <c>steamLoginSecure</c>.</item>
///   <item><c>GET store.steampowered.com/pointssummary/ajaxgetasyncconfig</c>,
///   a JSON endpoint, for the fresh access token.</item>
/// </list>
///
/// <para>The mint step is a JSON endpoint rather than an authenticated HTML
/// page precisely so no unattended request ever parses HTML. No browser is
/// involved at any point.</para>
///
/// <para><c>steamLoginSecure</c> exists only as a local string for the length
/// of one call. The primary handler is registered with
/// <c>UseCookies=false</c> so there is no cookie jar to leak into, and
/// <c>AllowAutoRedirect=false</c> so a redirect cannot carry it to a host this
/// file did not name. Nothing here writes a cookie anywhere, and condition 2's
/// stored shape is untouched: this class does not persist at all;
/// <see cref="SteamSessionProvider"/> does, through the same eleven-field
/// blob.</para>
/// </summary>
public sealed class SteamSessionRenewer : ISteamSessionRenewer
{
    /// <summary>
    /// The named client. Its own client rather than the two typed API clients
    /// because it talks to two different hosts, sends POSTs with form bodies,
    /// and must not carry a cookie jar.
    /// </summary>
    public const string HttpClientName = "steam-session-renewal";

    /// <summary>Request kind 1: the refresh token is spent here as the nonce.</summary>
    public const string FinalizeLoginUri = "https://login.steampowered.com/jwt/finalizelogin";

    /// <summary>
    /// Request kind 3: a JSON endpoint (not an authenticated HTML page) for
    /// the fresh access token
    /// (docs/spikes/steam-web-session-auth.md §1, route 1).
    /// </summary>
    public const string TokenMintUri = "https://store.steampowered.com/pointssummary/ajaxgetasyncconfig";

    /// <summary>
    /// The only host request kind 2 is executed against. The
    /// <c>finalizelogin</c> response names several; only the store's cookie is
    /// spent by request kind 3, so the others are not requested at all.
    /// Narrowing here is what stops a reshaped response body from directing
    /// Winnow's traffic.
    /// </summary>
    public const string TransferHost = "store.steampowered.com";

    private const string RedirectTarget = "https://store.steampowered.com/login/";
    private const string LoginSecureCookie = "steamLoginSecure";
    private const string RefreshCookie = "steamRefresh_steam";
    private const string SessionIdCookie = "sessionid";

    /// <summary>
    /// Valve EResult values that mean this credential will never work again.
    /// <c>AccessDenied</c> (15) is the one node-steam-session issue #56
    /// reports on every refresh route. Everything not on this list is read as
    /// transient, because latching a user out on an unrecognised code would
    /// turn a Valve deploy into a sign-out.
    /// </summary>
    private static readonly int[] DenialResults = [8, 10, 15, 26, 27];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SteamSessionRenewer> _log;

    public SteamSessionRenewer(
        IHttpClientFactory httpClientFactory, ILogger<SteamSessionRenewer>? log = null)
    {
        _httpClientFactory = httpClientFactory;
        _log = log ?? NullLogger<SteamSessionRenewer>.Instance;
    }

    /// <summary>
    /// How many renewal exchanges this renewer has started. Test hook; the
    /// thing single-flight is proved against.
    /// </summary>
    public int Attempts { get; private set; }

    public async Task<SteamRenewalOutcome> RenewAsync(SteamSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.RefreshToken is not { } refreshToken)
        {
            return SteamRenewalOutcome.NotRenewable("no refresh token");
        }

        Attempts++;

        var http = _httpClientFactory.CreateClient(HttpClientName);

        // A fresh sessionid per renewal, sent as both the form field and the
        // cookie because that pairing is Valve's CSRF check. Generated here,
        // used for one exchange, and never stored: condition 2 names sessionid
        // as something that must not reach disk.
        var sessionId = NewSessionId();

        try
        {
            var finalize = await FinalizeAsync(http, refreshToken, sessionId, ct);
            if (finalize.Failure is { } finalizeFailure)
            {
                return finalizeFailure;
            }

            var rotated = finalize.RotatedRefreshToken;

            var transfer = await TransferAsync(http, finalize.SteamId, finalize.Transfers, sessionId, ct);
            if (transfer.Failure is { } transferFailure)
            {
                return transferFailure;
            }

            rotated = transfer.RotatedRefreshToken ?? rotated;

            var mint = await MintAsync(http, sessionId, transfer.LoginSecure!, ct);
            return mint.Failure ?? SteamRenewalOutcome.Renewed(mint.AccessToken!, rotated);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not a renewal failure, and it must not be
            // recorded as one: a cancelled shutdown would otherwise walk the
            // session towards RenewalFailing.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Type only, never the exception object: an inner exception is free
            // to quote the request, and one of these requests carries the refresh
            // token in its body.
            _log.LogDebug("Steam session renewal failed ({ExceptionType}).", ex.GetType().Name);
            return SteamRenewalOutcome.Transient("unreachable");
        }
    }

    /// <summary>
    /// Request kind 1. The refresh token is spent here, as the nonce, in a
    /// form body, never in a URI where the framework's own request logging
    /// would see it.
    /// </summary>
    private async Task<FinalizeResult> FinalizeAsync(
        HttpClient http, string refreshToken, string sessionId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, FinalizeLoginUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["nonce"] = refreshToken,
                ["sessionid"] = sessionId,
                ["redir"] = RedirectTarget,
            }),
        };

        request.Headers.TryAddWithoutValidation("Cookie", SessionIdCookie + "=" + sessionId);

        using var response = await http.SendAsync(request, ct);

        if (Classify(response, "finalizelogin") is { } failure)
        {
            return new FinalizeResult(failure, null, [], null);
        }

        var rotated = ReadCookie(response, RefreshCookie);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!TryReadFinalize(body, out var steamId, out var transfers))
        {
            // A 200 that is not a finalize response. If Steam said why, and the
            // reason is one of the denial codes, the refresh token is done;
            // otherwise this is a shape we do not recognise and the session is
            // kept.
            return new FinalizeResult(
                IsDenial(response, body)
                    ? SteamRenewalOutcome.Rejected("finalizelogin refused the refresh token")
                    : SteamRenewalOutcome.Transient("finalizelogin returned no transfer targets"),
                null,
                [],
                null);
        }

        return new FinalizeResult(null, steamId, transfers, rotated);
    }

    /// <summary>
    /// Request kind 2. One POST per store transfer target, stopping at the
    /// first that hands over <c>steamLoginSecure</c>. The cookie is returned
    /// as a plain string and lives only inside this call.
    /// </summary>
    private async Task<TransferResult> TransferAsync(
        HttpClient http,
        string? steamId,
        IReadOnlyList<TransferTarget> targets,
        string sessionId,
        CancellationToken ct)
    {
        string? rotated = null;

        foreach (var target in targets)
        {
            var form = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, value) in target.Parameters)
            {
                form[name] = value;
            }

            if (steamId is { Length: > 0 })
            {
                form["steamID"] = steamId;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
            {
                Content = new FormUrlEncodedContent(form),
            };

            request.Headers.TryAddWithoutValidation("Cookie", SessionIdCookie + "=" + sessionId);

            using var response = await http.SendAsync(request, ct);

            if (Classify(response, "transfer") is { } failure)
            {
                return new TransferResult(failure, null, null);
            }

            rotated ??= ReadCookie(response, RefreshCookie);

            if (ReadCookie(response, LoginSecureCookie) is { Length: > 0 } loginSecure)
            {
                return new TransferResult(null, loginSecure, rotated);
            }
        }

        // Every target answered without the cookie the mint needs. Not a
        // rejection: the exchange got this far, so the refresh token was
        // accepted, and discarding it over a missing header would sign the user
        // out for a Valve change.
        return new TransferResult(
            SteamRenewalOutcome.Transient("no session cookie returned"), null, rotated);
    }

    /// <summary>
    /// Request kind 3, JSON, and the only place a fresh access token comes
    /// from. A body that states an empty <c>webapi_token</c> is Steam saying
    /// "you are not signed in", which is a rejection; a body that does not
    /// parse at all is a shape change, which is transient.
    /// </summary>
    private async Task<MintResult> MintAsync(
        HttpClient http, string sessionId, string loginSecure, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TokenMintUri);
        request.Headers.TryAddWithoutValidation(
            "Cookie", SessionIdCookie + "=" + sessionId + "; " + LoginSecureCookie + "=" + loginSecure);

        using var response = await http.SendAsync(request, ct);

        if (Classify(response, "token mint") is { } failure)
        {
            return new MintResult(failure, null);
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        return TryReadMintedToken(body, out var token, out var statedButEmpty) switch
        {
            true => new MintResult(null, token),
            false when statedButEmpty => new MintResult(
                SteamRenewalOutcome.Rejected("the token mint answered as signed out"), null),
            _ => new MintResult(SteamRenewalOutcome.Transient("the token mint returned no token"), null),
        };
    }

    /// <summary>
    /// The failure table, in one place so the two kinds cannot drift apart
    /// between steps. 401, 403 and 400 are the refresh token being refused:
    /// hard. 408, 429 and every 5xx are the network or Valve having a bad
    /// minute: transient. An unrecognised status is transient, because the
    /// cost of being wrong in that direction is one skipped pass and the cost
    /// of being wrong the other way is a sign-out.
    /// </summary>
    private SteamRenewalOutcome? Classify(HttpResponseMessage response, string stage)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var status = (int)response.StatusCode;

        var rejected = response.StatusCode
            is HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.BadRequest;

        // The stage name and the status code, never the URI and never the body.
        _log.LogDebug(
            "Steam session renewal: {Stage} returned {StatusCode}, classified as {Kind}.",
            stage, status, rejected ? "rejected" : "transient");

        return rejected
            ? SteamRenewalOutcome.Rejected(stage + " returned " + status.ToString(CultureInfo.InvariantCulture))
            : SteamRenewalOutcome.Transient(stage + " returned " + status.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Whether a 200 body or its <c>x-eresult</c> header names one of Valve's
    /// denial codes.
    /// </summary>
    private static bool IsDenial(HttpResponseMessage response, string body)
    {
        if (response.Headers.TryGetValues("x-eresult", out var values)
            && int.TryParse(
                values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var header)
            && DenialResults.Contains(header))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("eresult", out var eresult)
                && eresult.ValueKind == JsonValueKind.Number
                && eresult.TryGetInt32(out var value)
                && DenialResults.Contains(value);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the <c>finalizelogin</c> response. Only entries whose URL is an
    /// absolute HTTPS URI on <see cref="TransferHost"/> are kept; anything
    /// else in the array is ignored rather than requested, which is what makes
    /// the closed list closed even if the response says otherwise.
    /// </summary>
    private static bool TryReadFinalize(
        string body, out string? steamId, out IReadOnlyList<TransferTarget> transfers)
    {
        steamId = null;
        transfers = [];

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (root.TryGetProperty("steamID", out var id) && id.ValueKind == JsonValueKind.String)
            {
                steamId = id.GetString();
            }

            if (!root.TryGetProperty("transfer_info", out var info) || info.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var found = new List<TransferTarget>();
            foreach (var entry in info.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("url", out var url)
                    || url.ValueKind != JsonValueKind.String
                    || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var target)
                    || target.Scheme != Uri.UriSchemeHttps
                    || !string.Equals(target.Host, TransferHost, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parameters = new List<KeyValuePair<string, string>>();
                if (entry.TryGetProperty("params", out var raw) && raw.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in raw.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String
                            && property.Value.GetString() is { } value)
                        {
                            parameters.Add(new KeyValuePair<string, string>(property.Name, value));
                        }
                    }
                }

                found.Add(new TransferTarget(target, parameters));
            }

            transfers = found;
            return found.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the mint response. <paramref name="statedButEmpty"/> carries the
    /// distinction the caller needs: Steam naming the field and leaving it
    /// blank is a signed-out answer, and a body with no such field at all is a
    /// shape this client does not know.
    /// </summary>
    private static bool TryReadMintedToken(string body, out string? token, out bool statedButEmpty)
    {
        token = null;
        statedButEmpty = false;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var container = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? data
                : root;

            if (!container.TryGetProperty("webapi_token", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var minted = value.GetString();
            if (string.IsNullOrWhiteSpace(minted))
            {
                statedButEmpty = true;
                return false;
            }

            token = minted;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// One named cookie out of a response's <c>Set-Cookie</c> headers, as a
    /// string. Deliberately not a <see cref="System.Net.CookieContainer"/>:
    /// there is no jar on this pipeline, and the two values this reads are
    /// used once and dropped.
    /// </summary>
    private static string? ReadCookie(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var attributes = header.IndexOf(';');
            var pair = attributes >= 0 ? header[..attributes] : header;

            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            if (!string.Equals(pair[..equals].Trim(), name, StringComparison.Ordinal))
            {
                continue;
            }

            var value = pair[(equals + 1)..].Trim();
            if (value.Length > 0 && !string.Equals(value, "deleted", StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>24 hex characters from the cryptographic RNG, the shape Steam's own pages use.</summary>
    private static string NewSessionId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    private readonly record struct TransferTarget(
        Uri Url, IReadOnlyList<KeyValuePair<string, string>> Parameters);

    private readonly record struct FinalizeResult(
        SteamRenewalOutcome? Failure,
        string? SteamId,
        IReadOnlyList<TransferTarget> Transfers,
        string? RotatedRefreshToken);

    private readonly record struct TransferResult(
        SteamRenewalOutcome? Failure, string? LoginSecure, string? RotatedRefreshToken);

    private readonly record struct MintResult(SteamRenewalOutcome? Failure, string? AccessToken);
}
