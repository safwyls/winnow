using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the details modal's IGDB reassignment control.
/// The disclosure opens from a "Wrong game?" link in the action band and the
/// search field and candidate list draw full width in the right column's rest
/// band; the Clear line is the one part of the control in the left column,
/// under the cover and ON DISK.
/// </summary>
public static class GameIgdbMatchCopy
{
    /// <summary>Face of the disclosure control, at rest.</summary>
    public const string OpenLabel = "Wrong game?";

    /// <summary>Face of the disclosure control while the search surface is open.</summary>
    public const string CloseLabel = "Close";

    /// <summary>Tooltip on the disclosure control.</summary>
    public const string OpenTooltip = "Search IGDB for the right entry";

    /// <summary>Watermark in the search field, which takes a title or an IGDB id.</summary>
    public const string FieldWatermark = "Title or IGDB id";

    /// <summary>Accessible name for the search field.</summary>
    public const string FieldLabel = "IGDB search by title or id";

    /// <summary>Face of the button that runs the search.</summary>
    public const string SearchLabel = "Search";

    /// <summary>Face of the button on each candidate row.</summary>
    public const string AssignLabel = "Use this";

    /// <summary>Face of the control that returns the work to automatic resolution.</summary>
    public const string ClearLabel = "Clear";

    /// <summary>Tooltip on the clear control.</summary>
    public const string ClearTooltip = "Return to automatic metadata matching";

    /// <summary>Standing note while a pin is live, drawn in the right column
    /// above the search disclosure.</summary>
    public const string PinnedNote = "Matched by you.";

    /// <summary>Status field while a search is in flight. Words, never a spinner (section 8).</summary>
    public const string SearchingStatus = "Searching IGDB…";

    /// <summary>Status field while the assignment is being written.</summary>
    public const string AssigningStatus = "Saving…";

    /// <summary>Shown when a search returned no candidates. Not an error.</summary>
    public const string NoMatchesText = "No results for that title.";

    /// <summary>Mark on the row the id lookup returned, drawn in the outlined
    /// store-chip idiom before the candidate's name.</summary>
    public const string IdMatchLabel = "ID MATCH";

    /// <summary>Shown when an all-digit query matched no IGDB entry. Not an error;
    /// drawn in <c>TextDim</c>, the same register as <see cref="NoMatchesText"/>.</summary>
    public const string IdMissText = "No IGDB entry for that id.";

    /// <summary>Confirmation after a successful assignment. <paramref name="name"/> is the chosen entry's title.</summary>
    public static string AssignedNote(string name) => $"Now using {name}.";

    /// <summary>Confirmation after a pin is cleared.</summary>
    public const string ClearedNote = "Returned to automatic metadata matching.";

    /// <summary>Shown when the clear write did not land. Amber; the controls stay for a retry.</summary>
    public const string ClearFailedText = "Couldn't clear that. Nothing changed.";

    // ── The same-game offer ─────────────────────────────────────────────────
    //
    // works.igdb_id is UNIQUE. Two works claiming one entry ARE the same
    // game, so the collision refusal becomes an offer that names and shows
    // the holder and lets the user link the two.

    /// <summary>The offer's headline, naming the game that already holds the
    /// chosen IGDB entry. A question, not a failure sentence.</summary>
    public static string ClaimHeadline(string name) => $"{name} already uses that IGDB entry. Is this the same game?";

    /// <summary>Face of the control that accepts the offer and writes a
    /// <c>same_game</c> identity link.</summary>
    public const string ClaimLinkLabel = "Same game";

    /// <summary>Face of the control that declines the offer and restores
    /// the refusal sentence.</summary>
    public const string ClaimDeclineLabel = "No";

    /// <summary>Accessible name and tooltip for the accept control.
    /// <paramref name="name"/> is the holder's title.</summary>
    public static string ClaimLinkAutomationName(string name) => $"Link as the same game as {name}";

    /// <summary>Status field while the identity link is being written.
    /// Words, never a spinner (section 8).</summary>
    public const string LinkingStatus = "Linking…";

    /// <summary>Confirmation carried across the reload after the link
    /// lands. <paramref name="name"/> is the holder's title.</summary>
    public static string LinkedNote(string name) => $"Linked with {name}.";

    /// <summary>Shown when the link write did not land. Amber; the offer
    /// stays for a retry.</summary>
    public const string LinkFailedText = "Couldn't link those. Nothing changed.";

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
