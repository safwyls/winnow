using System.Globalization;
using System.Text.Json;

namespace Winnow.Ingest.Epic.Web.Model;

/// <summary>One page of <c>/library/api/public/items</c>.</summary>
/// <param name="Records">The artifacts on this page.</param>
/// <param name="NextCursor">Cursor for the following page, or null when this was the last.</param>
internal sealed record EpicLibraryPage(IReadOnlyList<EpicLibraryRecord> Records, string? NextCursor);

/// <summary>One raw <c>records[]</c> entry, before playtime is joined on.</summary>
internal sealed record EpicLibraryRecord(
    string CatalogItemId,
    string AppName,
    string? Namespace,
    string? Title,
    DateTime? AcquiredAt);

/// <summary>
/// Reads authenticated Epic responses with <see cref="JsonDocument"/>, preserving
/// the distinction between absent and default-valued fields. Returns null for
/// unparseable responses.
/// </summary>
internal static class EpicWebJson
{
    /// <summary>One page of library items, or null when the body is not a library page.</summary>
    public static EpicLibraryPage? TryReadLibraryPage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("records", out var records)
                || records.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new List<EpicLibraryRecord>(records.GetArrayLength());
            foreach (var record in records.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var catalogItemId = ReadString(record, "catalogItemId");
                var appName = ReadString(record, "appName");
                if (catalogItemId is null || appName is null)
                {
                    continue;
                }

                parsed.Add(new EpicLibraryRecord(
                    catalogItemId,
                    appName,
                    ReadString(record, "namespace"),
                    // The library service returns identifiers, not display
                    // metadata, so this is normally absent. Read it anyway: if a
                    // future response does carry a name, it costs nothing to
                    // take, and null keeps the local title in charge when it
                    // does not.
                    ReadString(record, "title") ?? ReadString(record, "productName"),
                    ReadUtc(record, "acquisitionDate")));
            }

            string? cursor = null;
            if (root.TryGetProperty("responseMetadata", out var metadata)
                && metadata.ValueKind == JsonValueKind.Object)
            {
                cursor = ReadString(metadata, "nextCursor");
            }

            return new EpicLibraryPage(parsed, cursor);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The playtime list keyed by <c>artifactId</c>, or null when the body is not
    /// a playtime list. An empty dictionary means Epic answered with no playtime.
    /// </summary>
    public static IReadOnlyDictionary<string, long>? TryReadPlaytime(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parsed = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var artifactId = ReadString(entry, "artifactId");
                if (artifactId is null
                    || !entry.TryGetProperty("totalTime", out var totalTime)
                    || totalTime.ValueKind != JsonValueKind.Number
                    || !totalTime.TryGetInt64(out var seconds))
                {
                    continue;
                }

                // Last write wins on a duplicate artifact. Epic has not been
                // observed sending one; if it ever does, one figure per artifact
                // is still the only shape the rest of the module can use.
                parsed[artifactId] = seconds;
            }

            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a catalog bulk-items response (object keyed by catalog item id), or
    /// null when the body is not that shape.
    /// </summary>
    public static IReadOnlyDictionary<string, EpicCatalogItemInfo>? TryReadCatalogItems(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var parsed = new Dictionary<string, EpicCatalogItemInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    // An error envelope ({"errorCode": …}) has string values at
                    // the top level, so this is also what stops one being read
                    // as a catalog of zero items.
                    continue;
                }

                if (TryReadCatalogItem(property.Name, property.Value) is { } item)
                {
                    parsed[item.CatalogItemId] = item;
                }
            }

            return parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// One entry of the bulk response. Returns null only when the entry has no
    /// usable id at all; everything else it cannot read arrives as null or empty,
    /// because a title Epic did not send and a title this reader failed to parse
    /// must be indistinguishable to the caller — both mean "leave the stored
    /// value alone".
    /// </summary>
    public static EpicCatalogItemInfo? TryReadCatalogItem(string? key, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // The service echoes `id` inside the entry; the object key is the same
        // value. Prefer the body's own copy and fall back to the key.
        var catalogItemId = ReadString(entry, "id") ?? (string.IsNullOrWhiteSpace(key) ? null : key.Trim());
        if (catalogItemId is null)
        {
            return null;
        }

        var categories = new List<string>();
        if (entry.TryGetProperty("categories", out var categoryArray)
            && categoryArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var category in categoryArray.EnumerateArray())
            {
                if (category.ValueKind == JsonValueKind.Object && ReadString(category, "path") is { } path)
                {
                    categories.Add(path);
                }
            }
        }

        // releaseInfo[0].appId, defensively: the spike measured exactly one
        // element on every one of 73 games and warns against assuming that holds.
        string? appName = null;
        if (entry.TryGetProperty("releaseInfo", out var releases)
            && releases.ValueKind == JsonValueKind.Array)
        {
            foreach (var release in releases.EnumerateArray())
            {
                if (release.ValueKind == JsonValueKind.Object && ReadString(release, "appId") is { } appId)
                {
                    appName = appId;
                    break;
                }
            }
        }

        return new EpicCatalogItemInfo(
            catalogItemId,
            ReadString(entry, "namespace"),
            ReadString(entry, "title"),
            categories,
            appName,
            ReadMainGameCatalogItemId(entry));
    }

    /// <summary>The parent catalog item id from <c>mainGameItem</c> or <c>mainGameItemList</c>, or null.</summary>
    private static string? ReadMainGameCatalogItemId(JsonElement entry)
    {
        if (entry.TryGetProperty("mainGameItem", out var mainGame)
            && mainGame.ValueKind == JsonValueKind.Object
            && ReadString(mainGame, "id") is { } id)
        {
            return id;
        }

        if (entry.TryGetProperty("mainGameItemList", out var list)
            && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in list.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object && ReadString(element, "id") is { } listed)
                {
                    return listed;
                }
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()
                : null;

    /// <summary>An ISO-8601 timestamp field as UTC, or null when absent or unparseable.</summary>
    private static DateTime? ReadUtc(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed.UtcDateTime
                : null;
}
