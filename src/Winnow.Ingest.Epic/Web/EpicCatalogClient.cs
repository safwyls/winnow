using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Winnow.Ingest.Epic.Web.Auth;
using Winnow.Ingest.Epic.Web.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.Ingest.Epic.Web;

/// <summary>
/// Client for Epic's catalog service: resolves title, categories, and AppName for
/// owned catalog items. Caches hits and definite misses; transport failures are
/// not cached. Soft-fails on every error.
/// </summary>
public sealed class EpicCatalogClient : IEpicCatalogClient
{
    /// <summary>Named/typed <see cref="HttpClient"/> for the catalog service.</summary>
    public const string HttpClientName = "epic-catalog";

    /// <summary>Bulk-items route. <c>{0}</c> is the namespace; ids follow as <c>id=</c> parameters.</summary>
    private const string BulkItemsPathFormat = "catalog/api/shared/namespace/{0}/bulk/items";

    private static readonly JsonSerializerOptions CacheSerializerOptions = new();

    private readonly HttpClient _http;
    private readonly IEpicTokenProvider _tokens;
    private readonly IEpicAccountClient _library;
    private readonly IEpicCatalogCache _cache;
    private readonly EpicWebOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EpicCatalogClient> _log;

    public EpicCatalogClient(
        HttpClient http,
        IEpicTokenProvider tokens,
        IEpicAccountClient library,
        IEpicCatalogCache cache,
        EpicWebOptions options,
        TimeProvider clock,
        ILogger<EpicCatalogClient> log)
    {
        _http = http;
        _tokens = tokens;
        _library = library;
        _cache = cache;
        _options = options;
        _clock = clock;
        _log = log;

        _http.BaseAddress ??= _options.CatalogBaseAddress;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, EpicCatalogItemInfo>> GetItemsAsync(
        IReadOnlyCollection<string> catalogItemIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalogItemIds);

        var answers = new Dictionary<string, EpicCatalogItemInfo>(StringComparer.OrdinalIgnoreCase);
        var wanted = catalogItemIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (wanted.Length == 0)
        {
            return answers;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var cutoff = Cutoff(_options.CatalogCacheTtl, now);

        // The cache first, and a cached MISS counts: an id Epic answered about
        // and did not recognise stays off the wire for the whole TTL, exactly
        // like steamcmd's _missing_token appids.
        var outstanding = new List<string>();
        foreach (var id in wanted)
        {
            var entry = await _cache.GetAsync(id, ct);
            if (entry is not { } cached || cached.FetchedAt < cutoff)
            {
                outstanding.Add(id);
                continue;
            }

            if (cached.PayloadJson is null)
            {
                // Cached miss. Not an answer to give the caller — absence is how
                // "learned nothing" is expressed — but it is a reason not to ask.
                continue;
            }

            if (TryReadCache(cached.PayloadJson) is { } item)
            {
                answers[item.CatalogItemId] = item;
            }
            else
            {
                outstanding.Add(id);
            }
        }

        if (outstanding.Count == 0)
        {
            return answers;
        }

        // No session means no request is possible. Not logged as a problem: an
        // install nobody opted in on is the ordinary case, and the local Epic
        // readers are unaffected.
        if (await _tokens.GetAsync(ct) is null)
        {
            _log.LogDebug(
                "No Epic session; {Count} catalog items keep whatever classification and name they have.",
                outstanding.Count);
            return answers;
        }

        // The service is keyed by (namespace, catalogItemId) and the caller has
        // only the id, so the namespace comes from the owned library — the same
        // cached fetch ownership already makes, so this is normally free.
        var namespaces = await ReadNamespacesAsync(ct);
        if (namespaces.Count == 0)
        {
            _log.LogInformation(
                "Epic owned library unavailable, so no namespace is known for {Count} catalog items; "
                + "none can be looked up this pass and every stored title and classification stands.",
                outstanding.Count);
            return answers;
        }

        var byNamespace = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unknownNamespace = 0;
        foreach (var id in outstanding)
        {
            if (!namespaces.TryGetValue(id, out var ns) || string.IsNullOrWhiteSpace(ns))
            {
                unknownNamespace++;
                continue;
            }

            if (!byNamespace.TryGetValue(ns, out var ids))
            {
                byNamespace[ns] = ids = [];
            }

            ids.Add(id);
        }

        var batchSize = Math.Max(1, _options.CatalogBatchSize);
        var fetched = 0;
        var missed = 0;
        foreach (var (ns, ids) in byNamespace)
        {
            foreach (var batch in ids.Chunk(batchSize))
            {
                ct.ThrowIfCancellationRequested();

                var parsed = await FetchBatchAsync(ns, batch, ct);
                if (parsed is null)
                {
                    // Unanswered. Nothing cached, nothing returned; the ids stay
                    // outstanding for the next pass.
                    continue;
                }

                foreach (var id in batch)
                {
                    if (parsed.TryGetValue(id, out var item))
                    {
                        answers[item.CatalogItemId] = item;
                        await _cache.SetAsync(id, WriteCache(item), now, ct);
                        fetched++;
                    }
                    else
                    {
                        // Epic answered and does not know this id. A definite
                        // "no", cached as one.
                        await _cache.SetAsync(id, payloadJson: null, now, ct);
                        missed++;
                    }
                }
            }
        }

        if (fetched > 0 || missed > 0 || unknownNamespace > 0)
        {
            _log.LogInformation(
                "Epic catalog: {Fetched} items named and classified, {Missed} not recognised by the service, "
                + "{Unknown} with no namespace in the owned library, across {Namespaces} namespaces.",
                fetched, missed, unknownNamespace, byNamespace.Count);
        }

        return answers;
    }

    /// <summary>Resolves <c>catalogItemId → namespace</c> from the owned library. Empty when unanswered.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ReadNamespacesAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var library = await _library.GetOwnedLibraryAsync(ct: ct);
            if (!library.Succeeded)
            {
                return map;
            }

            foreach (var item in library.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Namespace))
                {
                    map.TryAdd(item.CatalogItemId, item.Namespace.Trim());
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The account client soft-fails internally, so reaching here means
            // something unforeseen. It still must not abort enrichment.
            _log.LogWarning(
                "Reading the Epic owned library for catalog namespaces failed ({ExceptionType}); "
                + "no catalog lookups this pass.", ex.GetType().Name);
        }

        return map;
    }

    /// <summary>
    /// One bulk request. Returns what the service said, or null when it did not
    /// say anything — the single place where "Epic did not answer" becomes "no
    /// data" instead of an exception.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, EpicCatalogItemInfo>?> FetchBatchAsync(
        string catalogNamespace, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var path = new StringBuilder(
            string.Format(
                CultureInfo.InvariantCulture,
                BulkItemsPathFormat,
                Uri.EscapeDataString(catalogNamespace)));

        path.Append("?country=").Append(Uri.EscapeDataString(_options.CatalogCountry));
        path.Append("&locale=").Append(Uri.EscapeDataString(_options.CatalogLocale));
        foreach (var id in ids)
        {
            path.Append("&id=").Append(Uri.EscapeDataString(id));
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path.ToString());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // The Authorization header is attached by EpicAuthenticationHandler,
            // which also owns the single 401 refresh. Nothing in this class ever
            // holds a bearer token.
            using var response = await _http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? "Epic catalog returned {StatusCode} for {Count} items; the session was rejected and "
                          + "could not be refreshed. Nothing is cached and no stored title or classification "
                          + "is changed."
                        : "Epic catalog returned {StatusCode} for {Count} items; skipping this batch.",
                    (int)response.StatusCode,
                    ids.Count);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            var parsed = EpicWebJson.TryReadCatalogItems(body);
            if (parsed is null)
            {
                _log.LogWarning(
                    "Epic catalog returned a body this client could not parse for {Count} items. Treating it as "
                    + "unanswered rather than as \"Epic knows none of these\": caching that would record a "
                    + "parse failure as a fact about the user's library for a whole TTL.",
                    ids.Count);
            }

            return parsed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Offline, DNS, TLS, or a timeout the retry policy already
            // exhausted. Type only, never the exception object: an inner
            // exception can quote the request, and every path here names a
            // namespace and a set of owned catalog items.
            _log.LogWarning(
                "Epic catalog request failed ({ExceptionType}); {Count} items keep their stored values.",
                ex.GetType().Name, ids.Count);
            return null;
        }
    }

    /// <summary>Serialises one normalised catalog entry for the cache.</summary>
    private static string WriteCache(EpicCatalogItemInfo item)
        => JsonSerializer.Serialize(item, CacheSerializerOptions);

    private EpicCatalogItemInfo? TryReadCache(string payloadJson)
    {
        try
        {
            return JsonSerializer.Deserialize<EpicCatalogItemInfo>(payloadJson, CacheSerializerOptions);
        }
        catch (JsonException)
        {
            // Written by an older shape. A cache miss, not a failure.
            _log.LogDebug("A cached Epic catalog entry could not be read; refetching it.");
            return null;
        }
    }

    private static DateTime Cutoff(TimeSpan ttl, DateTime now)
        => ttl <= TimeSpan.Zero ? DateTime.MaxValue : now - ttl;
}
