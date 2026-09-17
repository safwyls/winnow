using System.Text.Json;
using Winnow.PluginSdk;
using static Winnow.Plugin.Xbox.XboxProtocol;

namespace Winnow.Plugin.Xbox;

internal sealed class XboxCatalogClient(IPluginContext context, TimeProvider clock)
{
    private DateTimeOffset _pauseUntil;
    internal async Task<XboxCatalogProduct?> GetAsync(string sourceId, CancellationToken ct)
    {
        var lookup = Lookup(sourceId);
        if (lookup is null) return null;
        var key = "catalog:v1:" + Hash(sourceId);
        var cached = await context.Cache.GetAsync(key, ct);
        var prior = Read(cached, sourceId);
        if (prior is not null && cached!.ExpiresAt > clock.GetUtcNow()) return prior;
        if (_pauseUntil > clock.GetUtcNow()) return prior;
        try
        {
            var response = await context.Http.SendAsync(new(lookup), ct);
            if (response.StatusCode != 200) { _pauseUntil = clock.GetUtcNow().AddSeconds(30); return prior; }
            using var json = Parse(response.Body);
            var products = Rows(Field(json.RootElement, "Products")).Where(p => Correlates(p, sourceId)).ToArray();
            if (products.Length != 1) return prior;
            var parsed = Project(products[0], sourceId);
            if (parsed is null) return prior;
            await context.Cache.SetAsync(key, new(JsonSerializer.SerializeToUtf8Bytes(parsed, Json), clock.GetUtcNow().AddDays(7)), ct);
            return parsed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException)
        { _pauseUntil = clock.GetUtcNow().AddSeconds(30); return prior; }
    }

    private static string? Lookup(string sourceId)
    {
        const string url = "https://displaycatalog.mp.microsoft.com/v7.0/products";
        const string options = "&fieldsTemplate=details&languages=en-US&market=US";
        if (sourceId.StartsWith("pfn:", StringComparison.Ordinal) && Pfn(sourceId[4..]))
            return url + "/lookup?alternateId=PackageFamilyName&value=" + Uri.EscapeDataString(sourceId[4..]) + options + "&top=25";
        if (sourceId.StartsWith("title:", StringComparison.Ordinal) && TitleId(sourceId[6..]))
            return url + "/lookup?alternateId=XboxTitleId&value=" + sourceId[6..] + options + "&top=25";
        if (sourceId.StartsWith("store:", StringComparison.Ordinal) && StoreId(sourceId[6..]))
            return url + "?bigIds=" + sourceId[6..] + options + "&actionFilter=Browse";
        return null;
    }

    private static bool Correlates(JsonElement product, string source)
    {
        if (!StoreId(Text(product, "ProductId"))) return false;
        if (source.StartsWith("store:", StringComparison.Ordinal)) return string.Equals(Text(product, "ProductId"), source[6..], StringComparison.OrdinalIgnoreCase);
        var pfn = source.StartsWith("pfn:", StringComparison.Ordinal);
        var expected = source[(pfn ? 4 : 6)..];
        if (pfn && string.Equals(Text(Field(product, "Properties"), "PackageFamilyName"), expected, StringComparison.OrdinalIgnoreCase)) return true;
        return Rows(Field(product, "AlternateIds")).Any(id => Text(id, "IdType") == (pfn ? "PackageFamilyName" : "XboxTitleId")
            && string.Equals(Text(id, "Value"), expected, StringComparison.OrdinalIgnoreCase));
    }

    private static XboxCatalogProduct? Project(JsonElement product, string source)
    {
        var properties = Field(product, "Properties");
        var kind = Text(product, "ProductKind");
        // Microsoft also returns apps from these endpoints; exact ID correlation alone does not make an app a game.
        if (kind != "Game" && !string.Equals(Text(properties, "Category"), "Games", StringComparison.OrdinalIgnoreCase)) return null;
        var localized = Rows(Field(product, "LocalizedProperties")).FirstOrDefault(p => string.Equals(Text(p, "Language"), "en-us", StringComparison.OrdinalIgnoreCase));
        if (localized.ValueKind != JsonValueKind.Object) localized = Rows(Field(product, "LocalizedProperties")).FirstOrDefault();
        if (Clean(Text(localized, "ProductTitle")) is null) return null;
        var genres = Rows(Field(properties, "Categories")).Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => Clean(x.GetString(), 100)).OfType<string>().Where(x => !string.Equals(x, "Games", StringComparison.OrdinalIgnoreCase)).Distinct().Take(32).ToArray();
        var category = Clean(Text(properties, "Category"), 100);
        if (genres.Length == 0 && kind == "Game" && category is not null && !string.Equals(category, "Games", StringComparison.OrdinalIgnoreCase)) genres = [category];
        var summary = Clean(Text(localized, "ProductDescription"), 32768) ?? Clean(Text(localized, "ShortDescription"), 32768);
        var release = Rows(Field(product, "MarketProperties")).Select(x => Date(x, "OriginalReleaseDate")).FirstOrDefault(x => x is not null);
        var images = Rows(Field(localized, "Images")).Select(Image).OfType<PluginArtwork>().DistinctBy(x => x.Url).Take(40).ToArray();
        return new(1, source, Text(product, "ProductId")!, new() { Summary = summary, ReleaseDate = release, Genres = genres }, images);
    }

    private static PluginArtwork? Image(JsonElement image)
    {
        var purpose = Text(image, "ImagePurpose");
        var kind = purpose switch
        {
            "Poster" or "BoxArt" => PluginArtworkKind.Cover,
            "Screenshot" => PluginArtworkKind.Screenshot,
            "SuperHeroArt" or "TitledHeroArt" or "BrandedKeyArt" => PluginArtworkKind.Background,
            _ => (PluginArtworkKind?)null
        };
        var url = ImageUrl(Text(image, "Uri"));
        var width = Number(image, "Width");
        var height = Number(image, "Height");
        if (kind is null || url is null || !Dimensions(width, height)) return null;
        if (kind == PluginArtworkKind.Background && width <= height) return null;
        return new(Hash(url), url, width, height) { Kind = kind.Value, ImageType = purpose };
    }

    private static string? ImageUrl(string? value)
    {
        if (value?.StartsWith("//", StringComparison.Ordinal) == true) value = "https:" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host != "store-images.s-microsoft.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.StartsWith("/image/", StringComparison.Ordinal)
            || uri.Scheme is not ("https" or "http") || value!.Length > 4096) return null;
        return new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri;
    }
    private static bool Dimensions(int width, int height) => width is > 0 and <= 8192 && height is > 0 and <= 8192 && (long)width * height <= 32 * 1024 * 1024;
    private static XboxCatalogProduct? Read(PluginCacheEntry? entry, string source)
    {
        if (entry is null || entry.Payload.Length > MaxBytes) return null;
        try
        {
            var p = JsonSerializer.Deserialize<XboxCatalogProduct>(entry.Payload, Json);
            return p is { Version: 1, Metadata: not null, Artwork: not null } && p.SourceId == source && StoreId(p.ProductId)
                && p.Artwork.Count <= 40 && p.Artwork.All(x => x is not null && ImageUrl(x.Url) == x.Url && Dimensions(x.Width, x.Height) && !x.Animated)
                ? p : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record XboxCatalogProduct(int Version, string SourceId, string ProductId, PluginMetadata Metadata, IReadOnlyList<PluginArtwork> Artwork);
