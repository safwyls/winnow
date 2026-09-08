using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the SETTINGS › LIBRARY screen and the Hide controls
/// the library's context menu and details modal carry. Three controls answer one
/// question — what is in the library: the explicit-content toggle (TASK-88),
/// the hidden-games list (TASK-87) and the hand-added games form (TASK-99).
/// The screen sits beside Platforms, Appearance and Application rather than inside another,
/// because connecting to a store and choosing a theme are different questions
/// from deciding what the library shows.
/// </summary>
public static class LibrarySettingsCopy
{
    // ══ The screen ════════════════════════════════════════════════════════

    /// <summary>Segmented control label. Display S caps, beside PLATFORMS and APPEARANCE.</summary>
    public const string SegmentLabel = "LIBRARY";

    /// <summary>Tooltip on the LIBRARY segment button. Sentence fragment, no trailing period.</summary>
    public const string SegmentTooltip = "What the library shows, and what it hides";

    /// <summary>Screen title in Display L.</summary>
    public const string Title = "Library";

    /// <summary>One line under the title. Names the three surfaces the settings affect.</summary>
    public const string IntroMessage = "What appears in the grid, the list and the feed.";

    // ══ Explicit content (TASK-88) ═════════════════════════════════════════

    /// <summary>Section heading for the explicit-content card. Label caps.</summary>
    public const string ExplicitSectionLabel = "EXPLICIT CONTENT";

    /// <summary>Toggle label. Off by default; the note beside it states the default.</summary>
    public const string ExplicitToggleLabel = "Show explicit content";

    /// <summary>
    /// States the default beside the control and draws the line between
    /// adults-only sexual content and a game rated for violence.
    /// </summary>
    public const string ExplicitDefaultNote =
        "Off by default. Filters adults-only sexual content, not games rated for violence. A game with no rating data stays.";

    /// <summary>Shown when nothing would be hidden. Ratings arrive with
    /// enrichment, so the figure is zero until that has run.</summary>
    public const string ExplicitPendingNote = "No ratings read yet.";

    /// <summary>Unit beside a count of one. Pairs with the Data-set figure.</summary>
    public const string ExplicitHiddenUnitSingular = "title hidden";

    /// <summary>Unit beside a count of more than one.</summary>
    public const string ExplicitHiddenUnitPlural = "titles hidden";

    // ══ Hidden games (TASK-87) ═════════════════════════════════════════════

    /// <summary>Section heading for the hidden-games card. Label caps.</summary>
    public const string HiddenSectionLabel = "HIDDEN GAMES";

    /// <summary>
    /// Empty state. A direction (§7): nothing is hidden, and says where
    /// hiding is done.
    /// </summary>
    public const string HiddenEmptyMessage = "Nothing hidden. Hide a game from its tile or its details.";

    /// <summary>Control that puts one game back. Ordinary and repeatable.</summary>
    public const string UnhideButton = "Unhide";

    /// <summary>Tooltip on the unhide control. Says what happens.</summary>
    public const string UnhideTooltip = "Put it back in the library";

    /// <summary>Automation name for the unhide control. <c>{0}</c> the title.</summary>
    public const string UnhideAutomationFormat = "Unhide {0}";

    /// <summary>Unit beside a store-entry count of one.</summary>
    public const string StoreEntryUnitSingular = "store entry";

    /// <summary>Unit beside a store-entry count of more than one.</summary>
    public const string StoreEntryUnitPlural = "store entries";

    /// <summary>Context menu item when one tile is selected.</summary>
    public const string HideMenuItem = "Hide";

    /// <summary>Context menu item with a multi-selection. <c>{0}</c> the count.</summary>
    public const string HideManyMenuFormat = "Hide {0} games";

    /// <summary>
    /// The details modal's Hide button. Reversible, and the game is not
    /// deleted; a later ingest does not bring it back on screen.
    /// </summary>
    public const string HideDetailsButton = "Hide";

    /// <summary>
    /// Tooltip on Hide, shared by the context menu and the details modal.
    /// States the effect and where to undo it.
    /// </summary>
    public const string HideTooltip = "Take it out of the library. Undo in Settings › Library.";

    /// <summary>A write that did not land. Amber, non-blocking.</summary>
    public const string HideProblem = "Couldn't hide that just now.";

    // ══ Hand-added games (TASK-99) ═════════════════════════════════════════

    /// <summary>Section heading for the hand-added games card. Label caps.</summary>
    public const string ManualSectionLabel = "ADDED BY HAND";

    /// <summary>
    /// Empty state. A direction (§7): nothing here yet, and says what this
    /// section is for.
    /// </summary>
    public const string ManualEmptyMessage =
        "Nothing added by hand. For games no launcher knows about.";

    /// <summary>Button that opens the empty add form.</summary>
    public const string AddButton = "Add a game";

    /// <summary>Form heading while adding a new entry.</summary>
    public const string FormAddTitle = "Add a game";

    /// <summary>Form heading while editing an existing entry. <c>{0}</c> the title.</summary>
    public const string FormEditTitleFormat = "Edit {0}";

    /// <summary>Control that opens an existing entry in the form for editing.</summary>
    public const string EditButton = "Edit";

    /// <summary>Automation name for the edit control. <c>{0}</c> the title.</summary>
    public const string EditAutomationFormat = "Edit {0}";

    /// <summary>Control that begins removing an entry. Opens the confirmation.</summary>
    public const string DeleteButton = "Delete";

    /// <summary>Automation name for the delete control. <c>{0}</c> the title.</summary>
    public const string DeleteAutomationFormat = "Delete {0}";

    /// <summary>
    /// Confirmation question (§12.3 — it asks first and says what survives).
    /// <c>{0}</c> the title. Only the hand-typed entry goes; nothing a store
    /// sold the user is touched.
    /// </summary>
    public const string DeleteConfirmFormat = "Delete “{0}”? Only the entry you typed goes.";

    /// <summary>Confirm button on the delete question. Danger, the only Danger on this screen.</summary>
    public const string DeleteConfirmButton = "Delete";

    /// <summary>Cancels the confirmation or the form.</summary>
    public const string CancelButton = "Cancel";

    /// <summary>Commits the add or edit form.</summary>
    public const string SaveButton = "Save";

    /// <summary>Field label for the game title. Label caps. Required.</summary>
    public const string TitleFieldLabel = "TITLE";

    /// <summary>Watermark in the title field.</summary>
    public const string TitleWatermark = "What the game is called";

    /// <summary>Field label for the release year. Label caps.</summary>
    public const string YearFieldLabel = "YEAR";

    /// <summary>Watermark in the year field.</summary>
    public const string YearWatermark = "Released";

    /// <summary>Field label for the platform. Label caps. Free text.</summary>
    public const string PlatformFieldLabel = "PLATFORM";

    /// <summary>Watermark in the platform field. Free text; examples hint at the range.</summary>
    public const string PlatformWatermark = "itch.io, Switch, a disc…";

    /// <summary>Field label for the executable path. Label caps.</summary>
    public const string ExecutableFieldLabel = "EXECUTABLE";

    /// <summary>Watermark in the executable field.</summary>
    public const string ExecutableWatermark = "Full path to the .exe";

    /// <summary>
    /// Hint under the executable field. Naming an executable is what lets
    /// Winnow record play time for a hand-added game.
    /// </summary>
    public const string ExecutableHint = "Optional. Naming it lets Winnow time your sessions.";

    /// <summary>Field label for the IGDB numeric id. Label caps.</summary>
    public const string IgdbFieldLabel = "IGDB ID";

    /// <summary>Watermark in the IGDB id field.</summary>
    public const string IgdbWatermark = "Numeric";

    /// <summary>Field label for the Steam appid. Label caps.</summary>
    public const string SteamAppIdFieldLabel = "STEAM APPID";

    /// <summary>Watermark in the Steam appid field.</summary>
    public const string SteamAppIdWatermark = "Numeric";

    /// <summary>Hint under the two id fields. Either id gets the game cover art.</summary>
    public const string CoverHint = "Optional. Either one gets it cover art.";

    /// <summary>Field-level error when the title is blank.</summary>
    public const string TitleRequiredError = "A title is needed.";

    /// <summary>Field-level error when the year is not four digits.</summary>
    public const string YearInvalidError = "Four digits.";

    /// <summary>Field-level error when a numeric id field contains non-digits.</summary>
    public const string NumberInvalidError = "Digits only.";

    /// <summary>Field-level error when the id already belongs to another game
    /// in the library. Raised before anything is written.</summary>
    public const string IdConflictError = "Another game in your library already has this.";

    // ══ From a file (TASK-104) ═════════════════════════════════════════════

    /// <summary>Section-header control that opens the add form and the OS file dialog together.</summary>
    public const string AddFromFileButton = "Add from a file";

    /// <summary>Tooltip on the "Add from a file" control. Says what it does.</summary>
    public const string AddFromFileTooltip = "Browse for an executable and look it up on IGDB";

    /// <summary>Face of the control beside the executable field.</summary>
    public const string BrowseButton = "Browse…";

    /// <summary>Tooltip on the browse control.</summary>
    public const string BrowseTooltip = "Pick an executable on this machine";

    /// <summary>Title bar of the OS file dialog.</summary>
    public const string BrowseDialogTitle = "Choose a game executable";

    /// <summary>Face of the control that searches IGDB for the title in the form.</summary>
    public const string MatchSearchButton = "Look up on IGDB";

    /// <summary>Tooltip on the IGDB search control. The title field is the query.</summary>
    public const string MatchSearchTooltip = "Search IGDB for the title above";

    /// <summary>Face of the control that folds the proposal away and leaves the form as typed.</summary>
    public const string MatchDismissButton = "Dismiss";

    /// <summary>Status while a search is in flight. Words, never a spinner (design-system.md §8).</summary>
    public const string MatchSearchingStatus = "Searching IGDB…";

    /// <summary>Shown when a search returned nothing. Not an error; says the form can still be typed by hand.</summary>
    public const string MatchNoMatchesText = "No matches on IGDB. You can still fill the form in by hand.";

    /// <summary>
    /// Note after the user picks a candidate. Says the form now carries that
    /// entry's details and that nothing is saved yet.
    /// </summary>
    /// <param name="name">The chosen entry's title.</param>
    public static string MatchChosenNote(string name) => $"Filled from {name}. Review and save when ready.";

    /// <summary>
    /// The sentence under the executable field saying what was read from the
    /// chosen file. Four cases that read differently because they carry
    /// different amounts of confidence:
    /// <see cref="ExecutableTitleSource.FileDescription"/> and
    /// <see cref="ExecutableTitleSource.ProductName"/> come from the file's own
    /// version info and are strong; <see cref="ExecutableTitleSource.FolderName"/>
    /// and <see cref="ExecutableTitleSource.FileName"/> are guesses from the
    /// path and should be read as such;
    /// <see cref="ExecutableTitleSource.None"/> means the file said nothing
    /// usable and the title has to be typed. A publisher from the file's
    /// company name is stated as corroboration when present.
    /// </summary>
    public static string ExecutableNote(ExecutableFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var publisher = !string.IsNullOrWhiteSpace(facts.Publisher)
            ? $" Published by {facts.Publisher}."
            : string.Empty;

        return facts.TitleSource switch
        {
            ExecutableTitleSource.FileDescription or ExecutableTitleSource.ProductName =>
                $"The file identifies itself as {facts.Title}.{publisher}",
            ExecutableTitleSource.FolderName or ExecutableTitleSource.FileName =>
                $"Guessed {facts.Title} from the path.{publisher}",
            _ => "No title found in the file. Type one above.",
        };
    }

    /// <summary>A save that did not land. Amber, non-blocking.</summary>
    public const string SaveProblem = "Couldn't save that just now.";

    /// <summary>A delete that did not land. Amber, non-blocking.</summary>
    public const string DeleteProblem = "Couldn't delete that just now.";
}
