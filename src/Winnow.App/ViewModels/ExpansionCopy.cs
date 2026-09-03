namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the expansion relation as the game details modal
/// shows it. The Merges screen's own copy, including the EXPANSIONS, PARTS
/// and TEST BUILDS sections, lives in <see cref="MergeCopy"/>.
///
/// <para>The one thing every string here has to get right: grouping an
/// expansion under a base game changes PRESENTATION ONLY. No count, no
/// playtime, no bucket and no recommendation moves. Nothing here may say or
/// imply that hours are combined, that the library got smaller, or that the
/// expansion stopped being a game the user owns.</para>
/// </summary>
public static class ExpansionCopy
{
    // ══ The details modal ═════════════════════════════════════════════════

    /// <summary>Section heading listing the packs grouped under this game.</summary>
    public const string ExpansionsHeading = "EXPANSIONS";

    /// <summary>
    /// Caption under that heading. The hours on the rows below are each pack's
    /// own and are not added to this game's, which is the one visible
    /// difference from ALSO COVERS, where a total is the point.
    /// </summary>
    public const string ExpansionsNote = "Counted separately. Not added above.";

    /// <summary>
    /// Uppercase label on a pack's own modal, naming the base game it extends.
    /// The other end of the same relation.
    /// </summary>
    public const string ExtendsHeading = "EXTENDS";

    /// <summary>
    /// Caption under that label. Says the thing a grouping could otherwise be
    /// read to deny: this is still its own game in the library.
    /// </summary>
    public const string ExtendsNote = "A separate game, grouped for display.";

    /// <summary>
    /// Control that ungroups one row. Ordinary and repeatable, so it must not
    /// read as a last chance.
    /// </summary>
    public const string UngroupButton = "Ungroup";

    /// <summary>
    /// Tooltip on that control. Describes the display change, which is the only
    /// change there is.
    /// </summary>
    public const string UngroupTooltip = "Show it on its own again";

    /// <summary>
    /// Automation name for the ungroup control. <c>{0}</c> the title,
    /// <c>{1}</c> the store names. Names the item, not the verb alone (§8),
    /// and carries the store because two rows can share a title.
    /// </summary>
    public const string UngroupAutomationFormat = "Ungroup {0} ({1})";

    /// <summary>A write that did not land. Amber, non-blocking.</summary>
    public const string UngroupProblem = "Couldn't ungroup that just now.";
}
