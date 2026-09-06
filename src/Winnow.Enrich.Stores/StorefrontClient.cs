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
                if (slug is { Length: > 0 and <= 200 } && slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                    result[property.Name] = "https://store.epicgames.com/p/" + slug;
            }
        }
        catch (JsonException) { }
        return result;
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
