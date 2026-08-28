using System.Text.Json;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Tolerant field accessors over Epic's local JSON. Everything is optional and
/// nothing throws on a type surprise, because Epic's own files contain several:
/// <c>OwnershipToken</c> is the <i>string</i> <c>"false"</c> where a bool is
/// implied, <c>InstallSize</c> is a number where other size fields elsewhere are
/// strings, and <c>MainGame*</c> are empty strings rather than absent keys
/// (docs/spikes/epic-gog-local-files.md sections 2, 9).
/// </summary>
internal static class EpicJson
{
    /// <summary>String value, or empty when absent, null, or another kind.</summary>
    internal static string String(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// Boolean value, or false when absent. Accepts the string forms too — Epic
    /// writes <c>"false"</c> as a string in at least one place, so a reader that
    /// only accepts <see cref="JsonValueKind.True"/>/<see cref="JsonValueKind.False"/>
    /// would silently read a "true" string as false.
    /// </summary>
    internal static bool Bool(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false,
        };
    }

    /// <summary>Int64 value, or 0 when absent or unparseable. Accepts numeric strings.</summary>
    internal static long Int64(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt64(out var number) ? number : 0,
            JsonValueKind.String => long.TryParse(value.GetString(), out var parsed) ? parsed : 0,
            _ => 0,
        };
    }

    /// <summary>String array, or empty when absent. Non-string elements are dropped.</summary>
    internal static IReadOnlyList<string> StringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var items = new List<string>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } text)
            {
                items.Add(text);
            }
        }

        return items;
    }

    /// <summary>
    /// String at <c>property.child</c>, or empty. Used for the catalog's
    /// <c>mainGameItem.id</c>, which is an object whose members are empty strings
    /// on a base game.
    /// </summary>
    internal static string NestedString(JsonElement element, string property, string child)
        => element.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? String(nested, child)
            : string.Empty;
}
