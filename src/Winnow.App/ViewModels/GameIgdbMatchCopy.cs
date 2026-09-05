using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the details modal's IGDB reassignment control.
/// The control sits in the left column, under the cover art and the install
/// path, where the user searches IGDB by title and picks the right entry by
/// hand.
/// </summary>
public static class GameIgdbMatchCopy
{
    /// <summary>Face of the disclosure control, at rest.</summary>
    public const string OpenLabel = "Wrong game?";

    /// <summary>Face of the disclosure control while the search surface is open.</summary>
    public const string CloseLabel = "Close";

    /// <summary>Tooltip on the disclosure control.</summary>
    public const string OpenTooltip = "Search IGDB for the right entry";

    /// <summary>Watermark in the title search field.</summary>
    public const string FieldWatermark = "Search by title";

    /// <summary>Accessible name for the title search field.</summary>
    public const string FieldLabel = "IGDB title search";

    /// <summary>Face of the button that runs the search.</summary>
    public const string SearchLabel = "Search";

    /// <summary>Face of the button on each candidate row.</summary>
    public const string AssignLabel = "Use this";

    /// <summary>Face of the control that returns the work to automatic resolution.</summary>
    public const string ClearLabel = "Clear";

    /// <summary>Tooltip on the clear control.</summary>
    public const string ClearTooltip = "Return to automatic matching";

    /// <summary>Standing note while a pin is live, read beside the Clear control.</summary>
    public const string PinnedNote = "Matched by you.";

    /// <summary>Status field while a search is in flight. Words, never a spinner (section 8).</summary>
    public const string SearchingStatus = "Searching IGDB…";

    /// <summary>Status field while the assignment is being written.</summary>
    public const string AssigningStatus = "Saving…";

    /// <summary>Shown when a search returned no candidates. Not an error.</summary>
    public const string NoMatchesText = "No results for that title.";

    /// <summary>Confirmation after a successful assignment. <paramref name="name"/> is the chosen entry's title.</summary>
    public static string AssignedNote(string name) => $"Now using {name}.";

    /// <summary>Confirmation after a pin is cleared.</summary>
    public const string ClearedNote = "Returned to automatic matching.";

    /// <summary>Shown when the clear write did not land. Amber; the controls stay for a retry.</summary>
    public const string ClearFailedText = "Couldn't clear that. Nothing changed.";

    /// <summary>
    /// Accessible name and tooltip for one candidate's assign button.
    /// <paramref name="name"/> is the candidate's IGDB title.
    /// </summary>
    public static string AssignAutomationName(string name) => $"Use {name}";

    /// <summary>
    /// The sentence for each refusal. Every outcome except
    /// <see cref="IgdbAssignmentOutcome.Assigned"/> lands here. Each one
    /// carries a different explanation: the work is gone, IGDB had no
    /// metadata, another game already holds that entry, or the write failed.
    /// </summary>
    public static string ProblemFor(IgdbAssignmentOutcome outcome) => outcome switch
    {
        IgdbAssignmentOutcome.WorkNotFound =>
            "That game is no longer in your library.",
        IgdbAssignmentOutcome.MetadataUnavailable =>
            "IGDB has no metadata for that entry. Nothing changed.",
        IgdbAssignmentOutcome.IgdbIdClaimedByAnotherWork =>
            "Another game in your library already uses that IGDB entry.",
        _ => "Couldn't save that. Nothing changed.",
    };
}
