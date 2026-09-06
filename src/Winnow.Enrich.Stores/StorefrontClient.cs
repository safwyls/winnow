using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Winnow.Core.Repositories;

namespace Winnow.Enrich.Stores;

/// <summary>Anonymous storefront metadata. All calls belong to background sync.</summary>
public sealed class StorefrontClient(HttpClient http, StorefrontCache cache, TimeProvider time)
{
    public const string EpicMappingUrl = "https://store-content.ak.epicgames.com/api/content/productmapping";

    public async Task RefreshEpicAsync(CancellationToken ct = default)
        => await FetchAsync("epic", EpicMappingUrl, ct);

    /// <summary>The bulk map omits active products; ask Epic by namespace for those misses.</summary>
    public async Task RefreshEpicNamespacesAsync(IEnumerable<string> namespaces, CancellationToken ct = default)
    {
        await RefreshEpicAsync(ct);
        var bulk = ParseEpic((await cache.GetAsync("epic", ct))?.Payload);
        foreach (var ns in namespaces.Distinct(StringComparer.Ordinal))
        {
            if (!IsNamespace(ns) || bulk.ContainsKey(ns)) continue;
            var query = "query { Catalog { catalogNs(namespace:\"" + ns
                + "\") { mappings(pageType:\"productHome\") { pageSlug pageType } } } }";
            await FetchAsync("epic-namespace:" + ns,
                "https://store.epicgames.com/graphql?query=" + Uri.EscapeDataString(query), ct);
        }
    }

    private static bool IsNamespace(string ns)
        => ns.Length is > 0 and <= 64 && ns.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public async Task RefreshGogAsync(string id, CancellationToken ct = default)
    {
        if (id.Length is > 0 and <= 12 && id.All(char.IsAsciiDigit))
            await FetchAsync("gog:" + id, $"https://api.gog.com/products/{id}?expand=changelog", ct);
    }

    private async Task FetchAsync(string key, string url, CancellationToken ct)
    {
        var old = await cache.GetAsync(key, ct);
        if (old is not null && time.GetUtcNow().UtcDateTime - old.FetchedAt < TimeSpan.FromHours(24)) return;
        try
        {
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                await cache.SaveAsync(key, response.StatusCode == HttpStatusCode.Forbidden ? old?.Payload ?? "{}" : "{}",
                    time.GetUtcNow().UtcDateTime, ct);
                return;
            }
            if (!response.IsSuccessStatusCode) return;
            var payload = await response.Content.ReadAsStringAsync(ct);
            using var parsed = JsonDocument.Parse(payload);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) return;
            if (key.StartsWith("gog:", StringComparison.Ordinal)
                && (!parsed.RootElement.TryGetProperty("id", out var id) || id.ToString() != key[4..])) return;
            if (key.StartsWith("epic-namespace:", StringComparison.Ordinal)
                && !TryParseEpicNamespace(payload, out _)) return;
            await cache.SaveAsync(key, payload, time.GetUtcNow().UtcDateTime, ct);
        }
        catch (HttpRequestException) { }
        catch (JsonException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    public static IReadOnlyDictionary<string, string> ParseEpic(string? payload)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(payload ?? "{}");
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                var slug = property.Value.GetString();
                if (EpicStoreUrl(slug) is { } url) result[property.Name] = url;
            }
        }
        catch (JsonException) { }
        return result;
    }

    private static string? EpicStoreUrl(string? slug)
        => slug is { Length: > 0 and <= 200 } && slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? "https://store.epicgames.com/p/" + slug : null;

    /// <summary>Accept only an unambiguous product-home mapping, never an offer or a title-derived slug.</summary>
    public static bool TryParseEpicNamespace(string? payload, out string? storeUrl)
    {
        storeUrl = null;
        try
        {
            using var document = JsonDocument.Parse(payload ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("errors", out _)
                || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("Catalog", out var catalog) || catalog.ValueKind != JsonValueKind.Object
                || !catalog.TryGetProperty("catalogNs", out var ns)) return false;
            if (ns.ValueKind == JsonValueKind.Null) return true;
            if (ns.ValueKind != JsonValueKind.Object || !ns.TryGetProperty("mappings", out var mappings)) return false;
            if (mappings.ValueKind == JsonValueKind.Null) return true;
            if (mappings.ValueKind != JsonValueKind.Array) return false;
            var urls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in mappings.EnumerateArray())
            {
                if (mapping.ValueKind == JsonValueKind.Object
                    && mapping.TryGetProperty("pageType", out var type) && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "productHome"
                    && mapping.TryGetProperty("pageSlug", out var slug) && slug.ValueKind == JsonValueKind.String
                    && EpicStoreUrl(slug.GetString()) is { } url) urls.Add(url);
            }
            if (urls.Count == 1) storeUrl = urls.Single();
            return true;
        }
        catch (JsonException) { return false; }
    }

    public static StorefrontDetails ParseGog(string? payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(null, null);
            string? url = null;
            if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object
                && links.TryGetProperty("product_card", out var card) && card.ValueKind == JsonValueKind.String
                && Uri.TryCreate(card.GetString(), UriKind.Absolute, out var uri)
                && uri.Scheme == "https" && uri.Host == "www.gog.com" && string.IsNullOrEmpty(uri.UserInfo))
                url = uri.AbsoluteUri;
            string? notes = null;
            if (root.TryGetProperty("changelog", out var changelog) && changelog.ValueKind == JsonValueKind.String)
            {
                // Render text only: no remote images, scripts, styles, or links execute.
                using var html = new HtmlParser().ParseDocument(changelog.GetString() ?? "");
                foreach (var node in html.QuerySelectorAll("script,style,iframe")) node.Remove();
                foreach (var node in html.QuerySelectorAll("h1,h2,h3,h4,p,li,br"))
                    node.Parent?.InsertBefore(html.CreateTextNode("\n"), node);
                notes = html.Body?.TextContent.Trim();
                if (string.IsNullOrWhiteSpace(notes)) notes = null;
            }
            return new(url, notes);
        }
        catch (JsonException) { return new(null, null); }
    }
}
