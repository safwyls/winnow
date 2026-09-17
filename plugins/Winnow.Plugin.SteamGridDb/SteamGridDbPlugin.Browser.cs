using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Winnow.PluginSdk;

namespace Winnow.Plugin.SteamGridDb;

public sealed partial class SteamGridDbPlugin
{
    private const int BrowserPayloadVersion = 1;
    private const int BrowserPageSize = 50;
    private static readonly IReadOnlyList<PluginArtworkKind> BrowserKinds = Array.AsReadOnly(
        new[] { PluginArtworkKind.Background, PluginArtworkKind.Cover, PluginArtworkKind.Icon });

    public IReadOnlyList<PluginArtworkKind> SupportedArtworkKinds => BrowserKinds;

    public async Task<PluginArtworkPage> BrowseArtworkAsync(PluginGame game, PluginArtworkKind kind,
        string? cursor = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _context ?? throw new InvalidOperationException("The plugin has not been initialized.");
        if (!BrowserKinds.Contains(kind))
            return BrowserState(PluginArtworkAvailability.Unsupported, "SteamGridDB does not provide this artwork kind here.");
        if (!game.ExternalIds.TryGetValue("steam", out var appId) || string.IsNullOrEmpty(appId)
            || appId.Length > 10 || !appId.All(char.IsAsciiDigit)
            || !uint.TryParse(appId, CultureInfo.InvariantCulture, out var numericId) || numericId == 0)
            return BrowserState(PluginArtworkAvailability.Unsupported, "SteamGridDB needs a known Steam app ID for this game.");

        var assetType = AssetType(kind);
        var cursorPrefix = $"v1:{appId}:{assetType}:";
        var page = 0;
        if (cursor is not null && (!cursor.StartsWith(cursorPrefix, StringComparison.Ordinal)
            || !int.TryParse(cursor.AsSpan(cursorPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out page)
            || page < 1 || page == int.MaxValue || cursor != cursorPrefix + page.ToString(CultureInfo.InvariantCulture)))
            return BrowserState(PluginArtworkAvailability.Unavailable, "The artwork page is invalid. Start browsing again.");

        var cacheKey = $"browse:v1:{assetType}:steam:{appId}:page:{page.ToString(CultureInfo.InvariantCulture)}";
        var cached = await context.Cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        var payload = ReadBrowserCache(cached, kind, cursorPrefix, page);
        var now = _clock.GetUtcNow();
        if (payload is not null && cached!.ExpiresAt > now)
            return new PluginArtworkPage(payload.Items, payload.NextCursor);
        var fallback = payload is { Items.Count: > 0 }
            ? new PluginArtworkPage(payload.Items, payload.NextCursor) { Message = "Showing cached SteamGridDB artwork." }
            : null;
        var key = (await context.Secrets.GetAsync("apikey", cancellationToken).ConfigureAwait(false))?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512
            || key.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            return fallback ?? BrowserState(PluginArtworkAvailability.SetupRequired, "Add a SteamGridDB API key in Settings → Plugins.");
        if (IsPaused(key, now))
            return fallback ?? BrowserState(PluginArtworkAvailability.Unavailable, "SteamGridDB is temporarily unavailable. Check the API key in Settings → Plugins and try again shortly.");

        try
        {
            var mimes = kind == PluginArtworkKind.Icon ? "image/png" : "image/png,image/jpeg,image/webp";
            var dimensions = kind == PluginArtworkKind.Cover ? "&dimensions=600x900,342x482,660x930" : "";
            var endpoint = kind == PluginArtworkKind.Background ? "heroes" : assetType + "s";
            var request = new PluginHttpRequest(
                $"https://www.steamgriddb.com/api/v2/{endpoint}/steam/{appId}?types=static&nsfw=false&humor=false&epilepsy=false&mimes={mimes}{dimensions}&limit={BrowserPageSize}&page={page.ToString(CultureInfo.InvariantCulture)}")
            {
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + key },
            };
            var response = await context.Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != 404 && response.StatusCode is < 200 or >= 300)
            {
                var unauthorized = response.StatusCode is 401 or 403;
                Pause(key, unauthorized);
                return fallback ?? BrowserState(unauthorized ? PluginArtworkAvailability.SetupRequired : PluginArtworkAvailability.Unavailable,
                    unauthorized ? "Check the SteamGridDB API key in Settings → Plugins." : "SteamGridDB could not load artwork. Try again shortly.");
            }

            BrowserPayload result;
            if (response.StatusCode == 404) result = new(BrowserPayloadVersion, [], null);
            else
            {
                if (response.Body.Length > MaxResponseBytes) throw new InvalidDataException();
                using var document = JsonDocument.Parse(response.Body);
                result = ReadBrowserResponse(document.RootElement, kind, cursorPrefix, page);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await context.Cache.SetAsync(cacheKey, new PluginCacheEntry(
                JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions), now + CacheTtl), cancellationToken).ConfigureAwait(false);
            return new PluginArtworkPage(result.Items, result.NextCursor);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
        {
            Pause(key, unauthorized: false);
            return fallback ?? BrowserState(PluginArtworkAvailability.Unavailable, "SteamGridDB could not load artwork. Try again shortly.");
        }
    }

    private static BrowserPayload ReadBrowserResponse(JsonElement root, PluginArtworkKind kind, string cursorPrefix, int page)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > BrowserPageSize)
            throw new InvalidDataException();
        var limit = BrowserPageSize;
        if (root.TryGetProperty("page", out var reportedPage) && (!reportedPage.TryGetInt32Safe(out var value) || value != page))
            throw new InvalidDataException();
        if (root.TryGetProperty("limit", out var reportedLimit)
            && (!reportedLimit.TryGetInt32Safe(out limit) || limit is < 1 or > BrowserPageSize))
            throw new InvalidDataException();
        var count = data.GetArrayLength();
        if (count > limit) throw new InvalidDataException();
        var hasNext = count == limit;
        if (root.TryGetProperty("total", out var total))
        {
            if (!total.TryGetInt32Safe(out var totalCount) || totalCount < 0) throw new InvalidDataException();
            hasNext = count > 0 && ((long)page + 1) * limit < totalCount;
        }
        var images = data.EnumerateArray().Select(row => ProjectBrowserArtwork(row, kind)).OfType<PluginArtwork>()
            .DistinctBy(image => image.Id, StringComparer.Ordinal).ToArray();
        return new(BrowserPayloadVersion, images,
            hasNext ? cursorPrefix + (page + 1).ToString(CultureInfo.InvariantCulture) : null);
    }

    private static PluginArtwork? ProjectBrowserArtwork(JsonElement row, PluginArtworkKind kind)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number
            || !id.TryGetInt64(out var assetId) || assetId <= 0
            || !row.TryGetProperty("width", out var width) || !width.TryGetInt32Safe(out var w)
            || !row.TryGetProperty("height", out var height) || !height.TryGetInt32Safe(out var h)
            || !row.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) return null;
        foreach (var flag in new[] { "nsfw", "humor", "epilepsy", "animated" })
            if (row.TryGetProperty(flag, out var value) && value.ValueKind is not (JsonValueKind.False or JsonValueKind.Null)) return null;
        if (row.TryGetProperty("tags", out var tags) && (tags.ValueKind != JsonValueKind.Array
            || tags.EnumerateArray().Any(tag => tag.ValueKind != JsonValueKind.String
                || tag.GetString()?.ToLowerInvariant() is "nsfw" or "humor" or "epilepsy" or "animated"))) return null;
        if (row.TryGetProperty("type", out var type) && (type.ValueKind != JsonValueKind.String || type.GetString() != "static")) return null;
        if (row.TryGetProperty("mime", out var mime) && (mime.ValueKind != JsonValueKind.String
            || (kind == PluginArtworkKind.Icon ? mime.GetString() != "image/png" : mime.GetString() is not ("image/png" or "image/jpeg" or "image/webp")))) return null;

        var assetType = AssetType(kind);
        var artwork = new PluginArtwork(assetId.ToString(CultureInfo.InvariantCulture), url.GetString()!, w, h)
        {
            Kind = kind,
            ImageType = assetType,
            PageUrl = $"https://www.steamgriddb.com/{assetType}/{assetId.ToString(CultureInfo.InvariantCulture)}",
            ThumbnailUrl = row.TryGetProperty("thumb", out var thumb) && thumb.ValueKind == JsonValueKind.String
                && IsBrowserUrl(thumb.GetString(), assetType, thumbnail: true) ? thumb.GetString() : null,
            Creator = row.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
                && author.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? SafeCreator(name.GetString()) : null,
        };
        return IsBrowserArtwork(artwork, kind) ? artwork : null;
    }

    private static BrowserPayload? ReadBrowserCache(PluginCacheEntry? entry, PluginArtworkKind kind, string cursorPrefix, int page)
    {
        if (entry is null || entry.Payload.Length > MaxResponseBytes) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<BrowserPayload>(entry.Payload, JsonOptions);
            return payload is { Version: BrowserPayloadVersion, Items: not null } && payload.Items.Count <= BrowserPageSize
                && payload.Items.All(image => image is not null && IsBrowserArtwork(image, kind))
                && (payload.NextCursor is null || payload.NextCursor == cursorPrefix + (page + 1).ToString(CultureInfo.InvariantCulture)) ? payload : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsBrowserArtwork(PluginArtwork image, PluginArtworkKind kind)
    {
        var assetType = AssetType(kind);
        return image.Kind == kind && image.ImageType == assetType && !image.Animated
            && !string.IsNullOrEmpty(image.Id) && long.TryParse(image.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            && image.Width is > 0 and <= 8192 && image.Height is > 0 and <= 8192 && (long)image.Width * image.Height <= 32 * 1024 * 1024
            && (kind switch { PluginArtworkKind.Background => image.Width > image.Height, PluginArtworkKind.Cover => image.Height > image.Width,
                PluginArtworkKind.Icon => image.Width == image.Height, _ => false })
            && IsBrowserUrl(image.Url, assetType, thumbnail: false)
            && (kind != PluginArtworkKind.Icon || image.Url.EndsWith(".png", StringComparison.Ordinal))
            && (image.ThumbnailUrl is null || IsBrowserUrl(image.ThumbnailUrl, assetType, thumbnail: true))
            && (image.Creator is null || SafeCreator(image.Creator) == image.Creator)
            && image.PageUrl == $"https://www.steamgriddb.com/{assetType}/{image.Id}";
    }

    private static bool IsBrowserUrl(string? url, string assetType, bool thumbnail)
    {
        if (url is null || url.Length > 512) return false;
        if (thumbnail)
        {
            var thumb = ThumbnailUrl().Match(url);
            if (thumb.Success && thumb.Groups[1].Value == (assetType == "grid" ? "thumb" : assetType + "_thumb")) return true;
            if (assetType == "icon" && IconThumbnailUrl().IsMatch(url)) return true;
        }
        var match = BrowserUrl().Match(url);
        return match.Success && match.Groups[1].Value == assetType && (thumbnail || !match.Groups[2].Success);
    }

    private static string? SafeCreator(string? name) => name is { Length: > 0 and <= 200 } && !name.Any(char.IsControl) ? name : null;
    private static string AssetType(PluginArtworkKind kind) => kind switch
    {
        PluginArtworkKind.Background => "hero", PluginArtworkKind.Cover => "grid", PluginArtworkKind.Icon => "icon",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    private static PluginArtworkPage BrowserState(PluginArtworkAvailability availability, string message) => new([]) { Availability = availability, Message = message };

    [GeneratedRegex("^https://cdn2\\.steamgriddb\\.com/(?:file/sgdb-cdn/)?(hero|grid|icon)/(thumb/)?[a-fA-F0-9]{32}\\.(png|jpg|webp)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex BrowserUrl();

    [GeneratedRegex("^https://cdn2\\.steamgriddb\\.com/(?:file/sgdb-cdn/)?(thumb|hero_thumb|icon_thumb)/[a-fA-F0-9]{32}\\.(png|jpg|webp)\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbnailUrl();

    [GeneratedRegex("^https://cdn2\\.steamgriddb\\.com/(?:file/sgdb-cdn/)?icon/[a-fA-F0-9]{32}/32/[1-9][0-9]{0,3}x[1-9][0-9]{0,3}\\.png\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IconThumbnailUrl();

    private sealed record BrowserPayload(int Version, IReadOnlyList<PluginArtwork> Items, string? NextCursor);
}

internal static class BrowserJson
{
    internal static bool TryGetInt32Safe(this JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }
}
