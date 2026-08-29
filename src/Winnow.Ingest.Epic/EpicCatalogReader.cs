using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Reads <c>Data\Catalog\catcache.bin</c>, the launcher's entitlement catalog
/// (base64 of plain JSON). Contains the owned library whether installed or not.
/// </summary>
public sealed class EpicCatalogReader
{
    private readonly ILogger<EpicCatalogReader> _logger;

    /// <param name="logger">Optional logger.</param>
    public EpicCatalogReader(ILogger<EpicCatalogReader>? logger = null)
        => _logger = logger ?? NullLogger<EpicCatalogReader>.Instance;

    /// <summary>
    /// Reads and decodes the catalog. Returns an empty list when the file is
    /// absent, unreadable, not valid base64, or not a JSON array — never throws.
    /// An absent catalog is the ordinary state of a machine where the launcher has
    /// never been signed in.
    /// </summary>
    public IReadOnlyList<EpicCatalogEntry> Read(string catalogCachePath)
    {
        ArgumentNullException.ThrowIfNull(catalogCachePath);

        if (!File.Exists(catalogCachePath))
        {
            _logger.LogDebug("Epic catalog cache {Path} does not exist", catalogCachePath);
            return [];
        }

        byte[] json;
        try
        {
            var base64 = File.ReadAllBytes(catalogCachePath);
            json = DecodeBase64(base64);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _logger.LogWarning(ex, "Could not decode Epic catalog cache {Path}", catalogCachePath);
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Epic catalog cache {Path} is not a JSON array; ignoring", catalogCachePath);
                return [];
            }

            var entries = new List<EpicCatalogEntry>(document.RootElement.GetArrayLength());
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var entry = ReadEntry(element);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            _logger.LogDebug(
                "Epic catalog cache {Path}: {Entries} entries", catalogCachePath, entries.Count);
            return entries;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Epic catalog cache {Path} is not valid JSON", catalogCachePath);
            return [];
        }
    }

    private static byte[] DecodeBase64(byte[] base64)
    {
        // The file is ASCII base64 with no wrapper; Convert wants a string, and
        // Latin1 is a byte-preserving decode for that alphabet. Whitespace (the
        // file has none observed, but a future build might wrap lines) is legal
        // input to Convert.FromBase64String.
        var text = System.Text.Encoding.Latin1.GetString(base64);
        return Convert.FromBase64String(text);
    }

    private static EpicCatalogEntry? ReadEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = EpicJson.String(element, "id");
        if (id.Length == 0)
        {
            return null;
        }

        return new EpicCatalogEntry(
            CatalogItemId: id,
            CatalogNamespace: EpicJson.String(element, "namespace"),
            Title: EpicJson.String(element, "title"),
            Developer: EpicJson.String(element, "developer"),
            AppName: FirstReleaseAppId(element),
            Categories: CategoryPaths(element),
            MainGameCatalogItemId: EpicJson.NestedString(element, "mainGameItem", "id"),
            MainGameNamespace: EpicJson.NestedString(element, "mainGameItem", "namespace"),
            ThirdPartyManagedProvider: CustomAttribute(element, "ThirdPartyManagedProvider"),
            RegistryPath: CustomAttribute(element, "RegistryPath"),
            RegistryKey: CustomAttribute(element, "RegistryKey"),
            CoverImageUrl: KeyImageUrl(element, "DieselGameBoxTall"));
    }

    private static IReadOnlyList<string> CategoryPaths(JsonElement element)
    {
        if (!element.TryGetProperty("categories", out var categories)
            || categories.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var paths = new List<string>(categories.GetArrayLength());
        foreach (var category in categories.EnumerateArray())
        {
            var path = EpicJson.String(category, "path");
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// <c>releaseInfo[0].appId</c>. Taken defensively: every observed game entry
    /// had exactly one release, none had zero and none had more, but the launcher
    /// makes no such promise.
    /// </summary>
    private static string FirstReleaseAppId(JsonElement element)
    {
        if (!element.TryGetProperty("releaseInfo", out var releases)
            || releases.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var release in releases.EnumerateArray())
        {
            var appId = EpicJson.String(release, "appId");
            if (appId.Length > 0)
            {
                return appId;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// <c>customAttributes</c> is a <c>{name: {type, value}}</c> map whose
    /// <c>value</c> is always a string, even for booleans and numbers.
    /// </summary>
    private static string CustomAttribute(JsonElement element, string name)
    {
        if (!element.TryGetProperty("customAttributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Object
            || !attributes.TryGetProperty(name, out var attribute))
        {
            return string.Empty;
        }

        return EpicJson.String(attribute, "value");
    }

    private static string? KeyImageUrl(JsonElement element, string type)
    {
        if (!element.TryGetProperty("keyImages", out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var image in images.EnumerateArray())
        {
            if (string.Equals(EpicJson.String(image, "type"), type, StringComparison.Ordinal))
            {
                var url = EpicJson.String(image, "url");
                return url.Length > 0 ? url : null;
            }
        }

        return null;
    }
}
