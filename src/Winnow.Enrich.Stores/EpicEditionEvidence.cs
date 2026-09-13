using System.Net;
using System.Text.Json;
using Winnow.Core.Identity;

namespace Winnow.Enrich.Stores;

public sealed record EpicEditionTarget(string Namespace, string CatalogItemId, string AppName);
public sealed record EpicEditionPageEvidence(
    IReadOnlyList<string> NativeIds, bool Conflicting, CachedEvidenceSource Source, DateTime ValidUntilUtc);

public sealed partial class StorefrontClient
{
    /// <summary>CMS pages only qualify after their complete native item identity matches the owned release.</summary>
    public async Task<EpicEditionPageEvidence?> GetEpicEditionIdsAsync(EpicEditionTarget target, CancellationToken ct = default)
    {
        if (!IsNamespace(target.Namespace) || string.IsNullOrWhiteSpace(target.CatalogItemId)
            || string.IsNullOrWhiteSpace(target.AppName)) return null;
        await RefreshEpicNamespacesAsync([target.Namespace], ct);
        var pages = await cache.ReadAllAsync(ct);
        if (!pages.TryGetValue("epic:" + target.Namespace, out var details) || details.StoreUrl is not { } url) return null;
        const string storePrefix = "https://store.epicgames.com/p/";
        if (!url.StartsWith(storePrefix, StringComparison.Ordinal)) return null;
        var slug = url[storePrefix.Length..];
        if (EpicStoreUrl(slug) != url) return null;
        var key = "epic-edition-v1:" + slug;
        var entry = await cache.GetAsync(key, ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (entry is null || entry.FetchedAt > now || entry.FetchedAt + TimeSpan.FromHours(24) <= now)
        {
            try
            {
                using var response = await http.GetAsync("https://store-content.ak.epicgames.com/api/en-US/content/products/" + slug, ct);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    await cache.SaveAsync(key, "{}", now, ct);
                    return null;
                }
                if (!response.IsSuccessStatusCode) return null;
                var payload = await response.Content.ReadAsStringAsync(ct);
                if (!TryParseEpicEditionIds(payload, target, out _, out _)) return null;
                await cache.SaveAsync(key, payload, now, ct);
                entry = new(payload, now);
            }
            catch (HttpRequestException) { return null; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        }
        if (!TryParseEpicEditionIds(entry.Payload, target, out var ids, out var conflicting)) return null;
        return new(ids, conflicting, CachedEvidenceSource.FromPayload("storefront-v1", key, entry.Payload),
            entry.FetchedAt + TimeSpan.FromHours(24));
    }

    public static bool TryParseEpicEditionIds(string payload, EpicEditionTarget target,
        out IReadOnlyList<string> ids, out bool conflicting)
    {
        ids = [];
        conflicting = false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!UniqueObject(root)) return false;
            if (!root.TryGetProperty("pages", out var pages)) return root.EnumerateObject().Any() is false;
            if (pages.ValueKind != JsonValueKind.Array) return false;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var page in pages.EnumerateArray())
            {
                if (!UniqueObject(page)) return false;
                if (!page.TryGetProperty("item", out var item) || !UniqueObject(item)) continue;
                if (Text(item, "catalogId") != target.CatalogItemId) continue;
                if (Text(page, "namespace") != target.Namespace || Text(item, "namespace") != target.Namespace
                    || Text(item, "appName") != target.AppName || !True(item, "hasItem"))
                {
                    conflicting = true;
                    continue;
                }
                if (NativeId(Text(page, "_id")) is { } pageId) found.Add(pageId);
                if (!page.TryGetProperty("offer", out var offer) || !UniqueObject(offer) || !True(offer, "hasOffer")) continue;
                if (Text(offer, "namespace") != target.Namespace) { conflicting = true; continue; }
                if (NativeId(Text(offer, "id")) is { } offerId) found.Add(offerId);
            }
            ids = found.Order(StringComparer.Ordinal).ToArray();
            return true;
        }
        catch (JsonException) { return false; }

        static string? Text(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        static bool True(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
        static bool UniqueObject(JsonElement element)
            => element.ValueKind == JsonValueKind.Object && element.EnumerateObject().Select(property => property.Name)
                .Distinct(StringComparer.Ordinal).Count() == element.EnumerateObject().Count();
        static string? NativeId(string? id)
            => id is { Length: 32 } && id.All(char.IsAsciiHexDigit) || id is { Length: 36 } && Guid.TryParseExact(id, "D", out _)
                ? id : null;
    }
}
