namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the metadata-fetch status field on the left rail.
/// Winnow fills in titles, release years, publishers, summaries and cover
/// URLs from IGDB, the Steam store, steamcmd.net and Epic's catalog after
/// the local library scan. The pass runs once per launch, in the background,
/// only when there is a backlog. While it runs, a Volt-edged inset note
/// sits at the bottom of the rail, above the settings gear, stating what is
/// happening and how many titles have not been reached yet. It disappears
/// when the pass finishes. Words only — no spinner, no animation (§8).
/// </summary>
public static class FetchStatusCopy
{
    /// <summary>
    /// All-caps label line drawn in the Label type style (10px, letterspaced).
    /// Two words naming the activity, not a state.
    /// </summary>
    public const string Label = "FETCHING DETAILS";

    /// <summary>
    /// Words that follow the caller-formatted count on the second line.
    /// The count is set in the Data font; this string is the unit and verb
    /// that complete the sentence. No leading space — the caller inserts it.
    /// Handles singular and plural.
    /// </summary>
    public static string Remaining(int remaining) =>
        remaining == 1 ? "title left" : "titles left";

    /// <summary>
    /// Screen-reader name for the status field. One sentence naming the
    /// state and the count, so the indicator is a state rather than
    /// nothing (§8).
    /// </summary>
    public static string AutomationName(int remaining) =>
        remaining == 1
            ? "Fetching details, 1 title left"
            : $"Fetching details, {remaining:N0} titles left";
}
