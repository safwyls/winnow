namespace Winnow.Core.Queries;

/// <summary>
/// Classifies Steam/Epic app types that are not games (tools, soundtracks, videos, etc.).
/// Used by the library view's "show non-game entries" toggle and by the soft-match sweep.
/// Derived at query time, never stored. NULL/unknown types are always treated as games.
/// Demos are handled by <see cref="DemoConsolidation"/>, not here.
/// </summary>
public static class NonGameEntries
{
    /// <summary>
    /// App types hidden by default. Compared case-insensitively (Valve's casing is unstable).
    /// Demos are absent: <see cref="DemoConsolidation"/> handles those.
    /// Unrecognised types stay visible.
    /// </summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool",
        "application",
        "config",
        "music",
        "video",
        "movie",
        "episode",
        "series",
        "media",
        "hardware",
    };

    /// <summary>The hidden type strings, sorted. Use <see cref="IsNonGame"/> for membership testing.</summary>
    public static IReadOnlyCollection<string> HiddenTypes { get; } = [.. Hidden.Order(StringComparer.Ordinal)];

    /// <summary>True when <paramref name="steamAppType"/> is a hidden non-game type. False for null/unknown.</summary>
    public static bool IsNonGame(string? steamAppType)
        => !string.IsNullOrWhiteSpace(steamAppType) && Hidden.Contains(steamAppType.Trim());

    /// <summary>True when Epic's categories indicate this is not a game. Delegates to <see cref="EpicGameFilter"/>.</summary>
    public static bool IsNonGameEpicCategories(string? epicCategories)
        => EpicGameFilter.IsGame(epicCategories) == false;

    /// <summary>True when either store's classification says this is not a game.</summary>
    public static bool IsNonGame(string? steamAppType, string? epicCategories)
        => IsNonGame(steamAppType) || IsNonGameEpicCategories(epicCategories);
}
