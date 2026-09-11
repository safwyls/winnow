using System.Globalization;

namespace Winnow.App.ViewModels.Filters;

/// <summary>The text-entry contract shared by desktop filters and fullscreen drafts.</summary>
public readonly record struct ReleaseYearRange(int? From, int? To)
{
    public const string ValidationMessage = "Enter four-digit years from 1000 to 9999, with the earlier year first.";

    public static bool TryParse(string? fromText, string? toText, out ReleaseYearRange range)
    {
        range = default;
        if (!TryYear(fromText, out var from) || !TryYear(toText, out var to) || from > to) return false;
        range = new(from, to);
        return true;
    }

    private static bool TryYear(string? text, out int? year)
    {
        year = null;
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return true;
        if (trimmed.Length != 4 || !int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed is < 1000 or > 9999) return false;
        year = parsed;
        return true;
    }
}
