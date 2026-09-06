namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the reception line in Band 1 of the details modal, directly under
/// the identity line. Up to three attributed figures — IGDB users, IGDB
/// critics, Steam — each with source, value and count. A source with no
/// figure contributes nothing; when no source has a figure the line is absent.
/// The three figures are never blended.
/// </summary>
public static class GameReceptionCopy
{
    /// <summary>Short uppercase attribution for IGDB's own user rating.</summary>
    public const string SourceIgdbUsers = "IGDB USERS";

    /// <summary>Short uppercase attribution for IGDB's aggregation of external
    /// critic scores. Distinguishable from <see cref="SourceIgdbUsers"/> at a
    /// glance in a 420px column.</summary>
    public const string SourceIgdbCritics = "IGDB CRITICS";

    /// <summary>Short uppercase attribution for Steam's user reviews.</summary>
    public const string SourceSteam = "STEAM";

    /// <summary>The count drawn beside the IGDB user rating value.</summary>
    public static string IgdbUsersCount(int count) =>
        count == 1 ? "1 rating" : $"{count:N0} ratings";

    /// <summary>The count drawn beside the IGDB aggregated critic value.</summary>
    public static string IgdbCriticsCount(int count) =>
        count == 1 ? "1 critic score" : $"{count:N0} critic scores";

    /// <summary>The count drawn beside the Steam review value.</summary>
    public static string SteamCount(int count) =>
        count == 1 ? "1 review" : $"{count:N0} reviews";

    /// <summary>Tooltip for the IGDB user rating figure.</summary>
    public static string IgdbUsersTooltip(int score, int count) =>
        $"IGDB user rating: {score} out of 100, from {count:N0} ratings.";

    /// <summary>Tooltip for the IGDB aggregated critic figure. Names IGDB as the
    /// aggregator, not the source of the scores.</summary>
    public static string IgdbCriticsTooltip(int score, int count) =>
        $"Critic score aggregated by IGDB: {score} out of 100, from {count:N0} critic scores.";

    /// <summary>Tooltip for the Steam review figure. Includes Steam's own verbatim
    /// label when present.</summary>
    public static string SteamTooltip(string? label, int percent, int count) =>
        label is not null
            ? $"{label} on Steam: {percent}% positive, from {count:N0} reviews."
            : $"{percent}% positive on Steam, from {count:N0} reviews.";

    /// <summary>Accessible name for the IGDB user rating figure. Spells out the
    /// scale because symbols are not reliably announced.</summary>
    public static string IgdbUsersAutomationName(int score, int count) =>
        $"IGDB user rating, {score} out of 100, {count:N0} ratings";

    /// <summary>Accessible name for the IGDB aggregated critic figure.</summary>
    public static string IgdbCriticsAutomationName(int score, int count) =>
        $"IGDB aggregated critic score, {score} out of 100, {count:N0} critic scores";

    /// <summary>Accessible name for the Steam review figure.</summary>
    public static string SteamAutomationName(string? label, int percent, int count) =>
        label is not null
            ? $"Steam, {label}, {percent} percent positive, {count:N0} reviews"
            : $"Steam, {percent} percent positive, {count:N0} reviews";

    /// <summary>Accessible name for the reception line as a group.</summary>
    public const string LineAutomationName = "Reception";
}
