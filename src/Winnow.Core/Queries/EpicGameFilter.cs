namespace Winnow.Core.Queries;

/// <summary>
/// Classifies Epic catalog entries as games vs non-games (engine builds, assets, tools,
/// cosmetic entitlements). Requires both <c>games</c> and <c>applications</c> categories.
/// Shared by ingest, the library view filter, and the soft-match sweep.
/// </summary>
public static class EpicGameFilter
{
    /// <summary>Category marking a store product rather than an engine or asset.</summary>
    public const string GamesCategory = "games";

    /// <summary>Category marking something the launcher can install and run.</summary>
    public const string ApplicationsCategory = "applications";

    /// <summary>Separator for the stored comma-joined category list (matches Epic's own format).</summary>
    public const char CategorySeparator = ',';

    /// <summary>True when categories contain both <c>games</c> and <c>applications</c>.</summary>
    public static bool IsGame(IReadOnlyCollection<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var games = false;
        var applications = false;
        foreach (var category in categories)
        {
            games |= string.Equals(category?.Trim(), GamesCategory, StringComparison.OrdinalIgnoreCase);
            applications |= string.Equals(category?.Trim(), ApplicationsCategory, StringComparison.OrdinalIgnoreCase);
        }

        return games && applications;
    }

    /// <summary>Three-valued: true/false when categories are known, null when no categories have been read.</summary>
    public static bool? IsGame(string? commaJoinedCategories)
        => Split(commaJoinedCategories) is { Count: > 0 } categories ? IsGame(categories) : null;

    /// <summary>Splits the stored comma-joined form into paths. Empty list means "cannot say".</summary>
    public static IReadOnlyList<string> Split(string? commaJoinedCategories)
        => string.IsNullOrWhiteSpace(commaJoinedCategories)
            ? []
            : commaJoinedCategories
                .Split(CategorySeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Joins category paths into the stored comma-joined form, preserving storefront order.</summary>
    public static string Join(IEnumerable<string> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        return string.Join(
            CategorySeparator,
            categories
                .Where(static c => !string.IsNullOrWhiteSpace(c))
                .Select(static c => c.Trim().Replace(CategorySeparator, ' ')));
    }

    /// <summary>True when the entry has a non-empty parent catalog item id (categories cannot distinguish DLC).</summary>
    public static bool IsDlc(string? mainGameCatalogItemId)
        => !string.IsNullOrWhiteSpace(mainGameCatalogItemId);
}
