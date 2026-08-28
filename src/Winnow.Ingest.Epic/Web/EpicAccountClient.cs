using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Winnow.Core.Ingest;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Winnow.Ingest.Epic.Web.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// Client for Epic's library service: the owned library, and per-artifact
/// playtime.
///
/// <para>Everything that could go wrong slowly — retry, 429 backoff, the request
/// rate, the 401 refresh — lives in the <see cref="HttpClient"/> handler
/// pipeline, so this class only has to worry about three things: what to ask,
/// what not to ask twice, and what never to write down.</para>
///
/// <para><b>Soft-fail is the contract, not a courtesy</b> (§5.1). No
/// credentials, no session, a lapsed refresh token, a 401 the auth handler could
/// not refresh past, a 429 the retries could not outlast, a dead network, a body
/// that will not parse: all become an unanswered
/// <see cref="EpicOwnedLibrary"/>, logged, never thrown at a caller, and —
/// critically — never written to the cache. Caching a failure would record "this
/// account owns nothing" for a whole TTL on the strength of one 503.</para>
///
/// <para><b>Playtime failing does not fail the library.</b> The two are separate
/// calls and the library is the more valuable of the two, so a playtime call that
/// 401s, 429s or returns nonsense leaves every item's playtime null and
/// <see cref="EpicOwnedLibrary.PlaytimeAnswered"/> false while the ownership
/// data still lands. Null there means "not told", and the local readers are
/// unaffected either way.</para>
/// </summary>
public sealed class EpicAccountClient : IEpicAccountClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the authenticated library service.</summary>
    public const string HttpClientName = "epic-library";

    /// <summary><c>CandidateOwnership.Source</c> value for candidates this module emits (§5.1 provenance).</summary>
    public const string SourceName = "epic_api";

    /// <summary>Cache key for the owned library. One account per install, so one key.</summary>
    public const string LibraryCacheKey = "epic:library";

    /// <summary>
    /// Owned artifacts, cursor-paginated. <c>includeMetadata=true</c> matches
    /// what Legendary sends; the endpoint answers without it, and the flag costs
    /// nothing.
    /// </summary>
    private const string LibraryItemsPath = "library/api/public/items?includeMetadata=true";

    /// <summary>
    /// Per-artifact playtime for the whole account.
    ///
    /// <para>Verified to exist live on 2026-08-26 by routing discrimination: this
    /// path answers 401 while a bogus sibling under the same prefix answers 404,
    /// so the service routes it and merely requires auth. The corresponding
    /// GraphQL query <c>PlaytimeTracking.total(accountId:)</c> returns
    /// <c>[Playtime]</c> of <c>{ accountId, artifactId, totalTime }</c>.</para>
    /// </summary>
    private const string PlaytimePathFormat = "library/api/public/playtime/account/{0}/all";

    private static readonly JsonSerializerOptions CacheSerializerOptions = new();

    private readonly HttpClient _http;
    private readonly IEpicTokenProvider _tokens;
    private readonly IEpicCredentialProvider _credentials;
    private readonly IEpicLibraryCache _cache;
    private readonly EpicWebOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EpicAccountClient> _log;

    public EpicAccountClient(
        HttpClient http,
        IEpicTokenProvider tokens,
        IEpicCredentialProvider credentials,
        IEpicLibraryCache cache,
        EpicWebOptions options,
        TimeProvider clock,
        ILogger<EpicAccountClient> log)
    {
        _http = http;
        _tokens = tokens;
        _credentials = credentials;
        _cache = cache;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.LibraryBaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => _tokens.IsConfiguredAsync(ct);

    public ValueTask<bool> IsSignedInAsync(CancellationToken ct = default)
        => _tokens.IsSignedInAsync(ct);

    public Task<EpicSignInResult> SignInAsync(string authorizationCode, CancellationToken ct = default)
        => _tokens.SignInWithAuthorizationCodeAsync(authorizationCode, ct);

    public Task SignOutAsync(CancellationToken ct = default) => _tokens.SignOutAsync(ct);

    public async ValueTask<string?> AuthorizationCodeUrl(CancellationToken ct = default)
        => await _credentials.GetAsync(ct) is { } credentials
            ? string.Format(
                CultureInfo.InvariantCulture,
                _options.AuthorizationCodeUrlFormat,
                Uri.EscapeDataString(credentials.ClientId))
            : null;

    public async Task<EpicOwnedLibrary> GetOwnedLibraryAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = Cutoff(cacheTtl ?? _options.CacheTtl, now);

        var entry = await _cache.GetAsync(LibraryCacheKey, ct);
        if (entry is { } cached && cached.FetchedAt >= cutoff
            && TryReadCache(cached.PayloadJson) is { } fresh)
        {
            _log.LogDebug("Epic owned library served from cache ({Count} items).", fresh.Count);
            return new EpicOwnedLibrary(
                Succeeded: true,
                Items: fresh,
                ObservedAt: cached.FetchedAt,
                FromCache: true,
                // The cached payload records whatever the fetch that produced it
                // learned. If any item carries a figure, playtime answered then.
                PlaytimeAnswered: fresh.Any(static i => i.TotalPlaytime is not null));
        }

        // Not configured and not signed in are the same thing to this method: no
        // request is possible. Neither is logged as a problem — the first is an
        // install nobody opted in on, the second is one whose session lapsed, and
        // the token provider has already said so at the right level.
        if (await _tokens.GetAsync(ct) is not { } token)
        {
            return ServeStale(entry, now);
        }

        var records = await FetchLibraryAsync(ct);
        if (records is null)
        {
            return ServeStale(entry, now);
        }

        // Playtime is a separate, optional call. Its failure must not cost the
        // ownership data that already arrived.
        var playtime = await FetchPlaytimeAsync(token.AccountId, ct);

        var items = records
            .Select(record => new EpicLibraryItem(
                record.CatalogItemId,
                record.AppName,
                record.Namespace,
                record.Title,
                record.AcquiredAt,
                // Keyed by appName, because that is what Epic calls artifactId.
                // Absent from the playtime list means Epic has no figure, which
                // stays null — see EpicLibraryItem.PlaytimeMinutes for why that
                // must not become zero.
                playtime is not null && playtime.TryGetValue(record.AppName, out var total)
                    ? total
                    : null))
            .OrderBy(static i => i.CatalogItemId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Only a real answer reaches the cache.
        await _cache.SetAsync(LibraryCacheKey, WriteCache(items), now, ct);

        _log.LogInformation(
            "Epic library: {Count} owned titles, {WithPlaytime} with a playtime figure, "
            + "{WithAcquired} with an acquisition date. Playtime endpoint {PlaytimeOutcome}.",
            items.Length,
            items.Count(static i => i.TotalPlaytime is not null),
            items.Count(static i => i.AcquiredAt is not null),
            playtime is null ? "did not answer" : "answered");

        return new EpicOwnedLibrary(
            Succeeded: true,
            Items: items,
            ObservedAt: now,
            FromCache: false,
            PlaytimeAnswered: playtime is not null);
    }

    public async Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var library = await GetOwnedLibraryAsync(cacheTtl, ct);
        return library.Succeeded
            // ObservedAt is stamped now rather than taken from the library, so a
            // cache hit does not backdate the observation. The cached FACTS are
            // hours old; the observation that they are still Winnow's best answer
            // is current, and play_records.observed_at has to stay monotonic
            // across syncs.
            ? library.ToCandidates(SourceName, _options.PlaytimeUnit, _clock.GetUtcNow().UtcDateTime)
            : [];
    }

    /// <summary>
    /// A stale cache entry beats nothing: ownership does not un-happen, so
    /// yesterday's library is a strictly better answer than an empty one when
    /// today's request failed. Falls back to unanswered when there is no entry at
    /// all.
    /// </summary>
    private EpicOwnedLibrary ServeStale(EpicCacheEntry? entry, DateTime now)
        => entry?.PayloadJson is { } payload && TryReadCache(payload) is { } stale
            ? new EpicOwnedLibrary(
                Succeeded: true,
                Items: stale,
                ObservedAt: entry.Value.FetchedAt,
                FromCache: true,
                PlaytimeAnswered: stale.Any(static i => i.TotalPlaytime is not null))
            : EpicOwnedLibrary.Unanswered(now);

    /// <summary>
    /// Walks every page of the library. Returns null when Epic did not answer —
    /// the single place where "Epic said no" becomes "no data" instead of an
    /// exception.
    ///
    /// <para>A partial walk is discarded rather than returned. Half a library
    /// looks exactly like a library that shrank, and the candidate feed has no
    /// way to distinguish them; returning null costs one skipped pass, while
    /// returning half would present a truncated entitlement list as a complete
    /// one.</para>
    /// </summary>
    private async Task<IReadOnlyList<EpicLibraryRecord>?> FetchLibraryAsync(CancellationToken ct)
    {
        var records = new List<EpicLibraryRecord>();
        string? cursor = null;

        for (var page = 0; page < _options.MaxLibraryPages; page++)
        {
            var path = cursor is null
                ? LibraryItemsPath
                : LibraryItemsPath + "&cursor=" + Uri.EscapeDataString(cursor);

            var body = await SendAsync(path, "library items", ct);
            if (body is null)
            {
                return null;
            }

            var parsed = EpicWebJson.TryReadLibraryPage(body);
            if (parsed is null)
            {
                _log.LogWarning(
                    "Epic library items returned a body this client could not parse. Treating the response "
                    + "as unanswered rather than as an empty library: an unparseable page is "
                    + "indistinguishable from one, and caching it would record the account as owning "
                    + "nothing for a whole TTL.");
                return null;
            }

            records.AddRange(parsed.Records);

            if (string.IsNullOrWhiteSpace(parsed.NextCursor))
            {
                return records;
            }

            cursor = parsed.NextCursor;
        }

        // Ran out of pages rather than out of cursor. Either Epic's pagination
        // changed or something is looping; both are anomalies, and neither
        // justifies presenting a truncated library as complete.
        _log.LogWarning(
            "Epic library pagination did not terminate within {MaxPages} pages ({Records} records so far); "
            + "discarding the partial result rather than treating it as the whole library.",
            _options.MaxLibraryPages, records.Count);
        return null;
    }

    /// <summary>
    /// Per-artifact playtime, or null when Epic did not answer.
    ///
    /// <para>Null and empty are different and both are returned faithfully: null
    /// means the call failed and nothing can be said about anyone's playtime,
    /// while an empty dictionary means Epic answered and has no figures. Only the
    /// second is evidence.</para>
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>?> FetchPlaytimeAsync(
        string accountId, CancellationToken ct)
    {
        var path = string.Format(
            CultureInfo.InvariantCulture, PlaytimePathFormat, Uri.EscapeDataString(accountId));

        var body = await SendAsync(path, "playtime", ct);
        if (body is null)
        {
            // Already logged by SendAsync at the right level. Not escalated: the
            // library is the valuable half and it has already succeeded.
            return null;
        }

        var parsed = EpicWebJson.TryReadPlaytime(body);
        if (parsed is null)
        {
            _log.LogWarning(
                "Epic playtime returned a body this client could not parse; every Epic title's playtime "
                + "stays unknown for this pass. Unknown is not zero, and no stored figure is overwritten.");
        }

        return parsed;
    }

    /// <summary>
    /// One GET against the library service. Returns the body, or null when the
    /// request did not produce one.
    ///
    /// <para><b>No message here names the URI.</b> Every path in this service
    /// carries either the account id or an artifact id, so the endpoint
    /// description passed in by the caller is what gets logged.</para>
    /// </summary>
    private async Task<string?> SendAsync(string path, string what, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // The Authorization header is attached by EpicAuthenticationHandler,
            // not here. That keeps the bearer token out of this class entirely
            // and puts the 401-refresh in one place.
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(ct);
            }

            _log.LogWarning(
                response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? "Epic {What} returned {StatusCode}; the session was rejected and could not be refreshed. "
                    + "Epic API data is skipped this pass and the local Epic readers are unaffected."
                    : "Epic {What} returned {StatusCode}; skipping this request.",
                what,
                (int)response.StatusCode);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller asked to stop. Not an enrichment failure, and must not
            // be swallowed into a silent empty result.
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Offline, DNS failure, TLS failure, or a timeout the retry policy
            // already exhausted. A degraded run, never a crashed one (§5.1).
            //
            // Type only, never the exception object: a full stack dump can carry
            // an inner exception that quoted the request, and the request path
            // carries the account id.
            _log.LogWarning("Epic {What} request failed ({ExceptionType}); skipping.", what, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Serialises the normalised items for the cache.
    ///
    /// <para><b>Normalised rather than verbatim, unlike the Steam Web module</b>,
    /// which stores the raw response body so unprojected fields survive. There is
    /// no single raw body to store here: a library is N paginated responses plus
    /// a separate playtime call, and stitching them back together on read would
    /// duplicate the join this class already does. The fields not projected are
    /// display metadata Winnow sources locally anyway.</para>
    ///
    /// <para>The payload contains no account id, no token and no display name —
    /// only catalog ids, codenames and figures.</para>
    /// </summary>
    private static string WriteCache(IReadOnlyList<EpicLibraryItem> items)
        => JsonSerializer.Serialize(items, CacheSerializerOptions);

    private IReadOnlyList<EpicLibraryItem>? TryReadCache(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<EpicLibraryItem>>(payloadJson, CacheSerializerOptions);
        }
        catch (JsonException)
        {
            // A payload written by an older shape. Treat it as a cache miss and
            // refetch rather than failing the pass.
            _log.LogDebug("Cached Epic library could not be read; refetching.");
            return null;
        }
    }

    private static DateTime Cutoff(TimeSpan ttl, DateTime now)
        => ttl <= TimeSpan.Zero ? DateTime.MaxValue : now - ttl;
}
