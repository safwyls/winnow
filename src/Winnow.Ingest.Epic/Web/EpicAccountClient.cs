using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Winnow.Core.Ingest;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Credentials;
using Winnow.Ingest.Epic.Web.Model;
using Winnow.Ingest.Epic.Web.Http;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// Client for Epic's library service: owned library and per-artifact playtime.
/// Soft-fails on every error; failures are never cached.
/// </summary>
public sealed class EpicAccountClient : IEpicAccountClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the authenticated library service.</summary>
    public const string HttpClientName = "epic-library";

    /// <summary><c>CandidateOwnership.Source</c> value for candidates this module emits (§5.1 provenance).</summary>
    public const string SourceName = "epic_api";

    /// <summary>Account-scoped cache key. Legacy unscoped entries cannot identify their owner.</summary>
    public static string LibraryCacheKey(string accountId) => "epic:library:v2:" + accountId;

    /// <summary>
    /// Owned artifacts, cursor-paginated. <c>includeMetadata=true</c> matches
    /// what Legendary sends; the endpoint answers without it, and the flag costs
    /// nothing.
    /// </summary>
    private const string LibraryItemsPath = "library/api/public/items?includeMetadata=true";

    /// <summary>Per-artifact playtime for the whole account.</summary>
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

        if (await _tokens.GetIdentityAsync(ct) is not { } identity)
            return EpicOwnedLibrary.Unanswered(now);
        var cacheKey = LibraryCacheKey(identity.AccountId);
        var entry = await _cache.GetAsync(cacheKey, ct);
        if (!await IsCurrentAsync(identity, ct)) return EpicOwnedLibrary.Unanswered(now);
        if (entry is { } cached && cached.FetchedAt >= cutoff
            && TryReadCache(cached.PayloadJson, identity.AccountId) is { } fresh)
        {
            _log.LogDebug("Epic owned library served from cache ({Count} items).", fresh.Items.Count);
            return Build(fresh, identity, cached.FetchedAt, fromCache: true);
        }

        var records = await FetchLibraryAsync(identity, ct);
        if (records is null)
        {
            return await IsCurrentAsync(identity, ct) ? ServeStale(entry, identity, now) : EpicOwnedLibrary.Unanswered(now);
        }

        // Playtime is a separate, optional call. Its failure must not cost the
        // ownership data that already arrived.
        var playtime = await FetchPlaytimeAsync(identity, ct);
        if (!await IsCurrentAsync(identity, ct)) return EpicOwnedLibrary.Unanswered(now);

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
        var payload = new CachePayload(2, identity.AccountId, items, playtime is not null);
        await _cache.SetAsync(cacheKey, JsonSerializer.Serialize(payload, CacheSerializerOptions), now, ct);
        if (!await IsCurrentAsync(identity, ct)) return EpicOwnedLibrary.Unanswered(now);

        _log.LogInformation(
            "Epic library: {Count} owned titles, {WithPlaytime} with a playtime figure, "
            + "{WithAcquired} with an acquisition date. Playtime endpoint {PlaytimeOutcome}.",
            items.Length,
            items.Count(static i => i.TotalPlaytime is not null),
            items.Count(static i => i.AcquiredAt is not null),
            playtime is null ? "did not answer" : "answered");

        return Build(payload, identity, now, fromCache: false);
    }

    public async Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
        TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        var library = await GetOwnedLibraryAsync(cacheTtl, ct);
        return library.Succeeded && library.SessionIdentity is { } identity && await IsCurrentAsync(identity, ct)
            // ObservedAt is stamped now rather than taken from the library, so a
            // cache hit does not backdate the observation. The cached facts may
            // be hours old; the observation that they are still Winnow's best
            // answer is current. A backdated candidate sorts behind the newest
            // stored row in LibraryQueryRepository's latest_play CTE (ORDER BY
            // observed_at DESC, id DESC) and loses that comparison, so the
            // bucket, dormancy signal and "last played" date the user reads
            // all keep reporting a stale row.
            ? library.ToCandidates(SourceName, _options.PlaytimeUnit, _clock.GetUtcNow().UtcDateTime)
            : [];
    }

    /// <summary>
    /// A stale cache entry beats nothing: ownership does not un-happen, so
    /// yesterday's library is a strictly better answer than an empty one when
    /// today's request failed. Falls back to unanswered when there is no entry at
    /// all.
    /// </summary>
    private EpicOwnedLibrary ServeStale(EpicCacheEntry? entry, EpicSessionIdentity identity, DateTime now)
        => entry?.PayloadJson is { } payload && TryReadCache(payload, identity.AccountId) is { } stale
            ? Build(stale, identity, entry.Value.FetchedAt, fromCache: true)
            : EpicOwnedLibrary.Unanswered(now);

    private async ValueTask<bool> IsCurrentAsync(EpicSessionIdentity identity, CancellationToken ct)
        => await _tokens.GetIdentityAsync(ct) == identity;

    private static EpicOwnedLibrary Build(CachePayload payload, EpicSessionIdentity identity, DateTime observedAt, bool fromCache)
        => new(true, payload.Items, observedAt, fromCache, payload.PlaytimeAnswered)
            { AccountId = identity.AccountId, SessionIdentity = identity };

    /// <summary>Walks every page of the library. Returns null when Epic did not answer; partial results are discarded.</summary>
    private async Task<IReadOnlyList<EpicLibraryRecord>?> FetchLibraryAsync(EpicSessionIdentity identity, CancellationToken ct)
    {
        var records = new List<EpicLibraryRecord>();
        string? cursor = null;

        for (var page = 0; page < _options.MaxLibraryPages; page++)
        {
            var path = cursor is null
                ? LibraryItemsPath
                : LibraryItemsPath + "&cursor=" + Uri.EscapeDataString(cursor);

            var body = await SendAsync(path, "library items", identity, ct);
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

    /// <summary>Per-artifact playtime, or null when Epic did not answer.</summary>
    private async Task<IReadOnlyDictionary<string, long>?> FetchPlaytimeAsync(
        EpicSessionIdentity identity, CancellationToken ct)
    {
        var path = string.Format(
            CultureInfo.InvariantCulture, PlaytimePathFormat, Uri.EscapeDataString(identity.AccountId));

        var body = await SendAsync(path, "playtime", identity, ct);
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

    /// <summary>One GET against the library service. Returns the body, or null on failure.</summary>
    private async Task<string?> SendAsync(string path, string what, EpicSessionIdentity identity, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Options.Set(EpicAuthenticationHandler.ExpectedSession, identity);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // The Authorization header is attached by EpicAuthenticationHandler,
            // not here. That keeps the bearer token out of this class entirely
            // and puts the 401-refresh in one place.
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                return await IsCurrentAsync(identity, ct) ? body : null;
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

    private sealed record CachePayload(int Version, string AccountId, IReadOnlyList<EpicLibraryItem> Items, bool PlaytimeAnswered);

    private CachePayload? TryReadCache(string? payloadJson, string accountId)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<CachePayload>(payloadJson, CacheSerializerOptions);
            return payload is { Version: 2, Items: not null } && payload.AccountId == accountId
                ? payload : null;
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
