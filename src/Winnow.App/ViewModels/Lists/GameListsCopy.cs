namespace Winnow.App.ViewModels.Lists;

/// <summary>
/// User-facing copy for the per-game list membership section in the game
/// details modal. The section lists every hand-built list the user has,
/// each with a checkbox showing whether THIS game belongs to it. Ticking
/// adds the game; unticking removes it. Live lists are excluded — they
/// hold rules and cannot be added to by hand. The section sits in the
/// modal's scrolling right column alongside ALSO COVERS and EXPANSIONS.
/// </summary>
public static class GameListsCopy
{
    /// <summary>
    /// All-caps section heading in the Label type style, matching the
    /// ALSO COVERS and EXPANSIONS headings beside it.
    /// </summary>
    public const string Heading = "LISTS";

    /// <summary>
    /// Shown in place of the checkboxes when the user has no hand-built
    /// lists. An empty state is a direction (§7): points at the action bar.
    /// </summary>
    public const string EmptyText =
        "Choose Add to list above to create a list for this game.";

    /// <summary>
    /// Accessible name for an unticked checkbox. <c>{0}</c> is the list name.
    /// </summary>
    public static string AddAutomationName(string listName) =>
        $"Add to {listName}";

    /// <summary>
    /// Accessible name for a ticked checkbox. <c>{0}</c> is the list name.
    /// </summary>
    public static string RemoveAutomationName(string listName) =>
        $"Remove from {listName}";
}
