namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the Merges screen. All strings in one file so the
/// header, the cut bar, the five sections, the cards, the strips and the dock
/// can be read together. Answering writes a link, never a delete; every label
/// must be exact about what pressing it does, and every dock note must say
/// that nothing was deleted.
/// </summary>
public static class MergeCopy
{
    // ══ The app's one separator ═══════════════════════════════════════════

    /// <summary>The one character the screen separates metadata with (§7).</summary>
    public const string Separator = " · ";

    /// <summary>Joins the stores a row is owned on, inside one badge column.</summary>
    public const string StoreJoiner = " / ";

    /// <summary>Joins member labels in an automation name.</summary>
    public const string MemberSeparator = ", ";

    /// <summary>The em dash the data face prints for an absence.</summary>
    public const string NoValue = "—";

    // ══ Chrome ════════════════════════════════════════════════════════════

    /// <summary>The rail row. A screen, not a cut, so it carries no count.</summary>
    public const string RailLabel = "MERGES";

    /// <summary>Tooltip on the rail row.</summary>
    public const string RailTooltip = "Entries that might be one game, and what you have rolled up";

    /// <summary>The h1.</summary>
    public const string Title = "Merges";

    /// <summary>The count line at zero.</summary>
    public const string NothingWaiting = "nothing waiting";

    /// <summary>The count line at one. <c>{0}</c> the number.</summary>
    public const string PendingOneFormat = "{0} proposal · non-destructive";

    /// <summary>The count line above one. <c>{0}</c> the number.</summary>
    public const string PendingManyFormat = "{0} proposals · non-destructive";

    /// <summary>The sort button. <c>{0}</c> the current order's label.</summary>
    public const string SortButtonFormat = "Sort · {0}";

    /// <summary>Tooltip on the sort button.</summary>
    public const string SortTooltip = "Sort order";

    /// <summary>Sort menu row: EXACT MATCH, LIKELY, WORTH A LOOK.</summary>
    public const string SortStrongestMatch = "Strongest match";

    /// <summary>Sort menu row: summed hours, descending.</summary>
    public const string SortPlaytimeAtStake = "Playtime at stake";

    /// <summary>Sort menu row: the header title.</summary>
    public const string SortTitle = "Title";

    /// <summary>The bulk accept button. <c>{0}</c> how many exact cross-store groups are pending.</summary>
    public const string AcceptExactFormat = "Accept {0} exact matches";

    /// <summary>The bulk accept button for exactly one. <c>{0}</c> is 1.</summary>
    public const string AcceptExactOneFormat = "Accept {0} exact match";

    /// <summary>The bulk accept button at zero, disabled.</summary>
    public const string AcceptExactNone = "No exact matches left";

    /// <summary>Tooltip on the bulk accept button.</summary>
    public const string AcceptExactTooltip = "Cross-store duplicates with the same title only";

    /// <summary>The primary button with a selection. <c>{0}</c> how many cards are checked.</summary>
    public const string MergeSelectedFormat = "Merge {0} selected";

    /// <summary>The primary button with nothing checked, disabled.</summary>
    public const string MergeSelectedNone = "Merge selected";

    /// <summary>Tooltip on the primary button.</summary>
    public const string MergeSelectedTooltip = "Roll up every checked group under the header you picked";

    // ══ The cut bar ═══════════════════════════════════════════════════════

    /// <summary>The first segment, every section.</summary>
    public const string KindAll = "ALL";

    /// <summary>Segment label for ACROSS STORES.</summary>
    public const string KindStores = "STORES";

    /// <summary>Segment label for EDITIONS.</summary>
    public const string KindEditions = "EDITIONS";

    /// <summary>Segment label for EXPANSIONS.</summary>
    public const string KindExpansions = "EXPANSIONS";

    /// <summary>Segment label for PARTS.</summary>
    public const string KindParts = "PARTS";

    /// <summary>Segment label for TEST BUILDS.</summary>
    public const string KindTests = "TESTS";

    /// <summary>Tooltip on the cut chip.</summary>
    public const string KindChipTooltip = "You set this — showing one grouping kind";

    /// <summary>Tooltip on the chip's ✕.</summary>
    public const string KindChipClearTip = "Show every kind";

    /// <summary>The cut count while filtered. <c>{0}</c> pending in all, <c>{1}</c> pending shown. The one arrow in the interface.</summary>
    public const string CutCountFormat = "{0} → {1}";

    // ══ The sections ══════════════════════════════════════════════════════

    /// <summary>Section title.</summary>
    public const string SectionStores = "ACROSS STORES";

    /// <summary>Section blurb.</summary>
    public const string SectionStoresBlurb =
        "The same game bought more than once. Playtime rolls up under whichever copy you keep.";

    /// <summary>Section title.</summary>
    public const string SectionEditions = "EDITIONS";

    /// <summary>Section blurb.</summary>
    public const string SectionEditionsBlurb =
        "Remasters and re-releases. Winnow cannot tell a re-release from a sequel on its own — these are yours to call.";

    /// <summary>Section title.</summary>
    public const string SectionExpansions = "EXPANSIONS";

    /// <summary>Section blurb.</summary>
    public const string SectionExpansionsBlurb =
        "Content that needs the base game to run. Nesting these keeps one row per game in the library.";

    /// <summary>Section title.</summary>
    public const string SectionParts = "PARTS";

    /// <summary>Section blurb.</summary>
    public const string SectionPartsBlurb =
        "Entries the store lists separately but ships as one release.";

    /// <summary>Section title.</summary>
    public const string SectionTests = "TEST BUILDS";

    /// <summary>Section blurb.</summary>
    public const string SectionTestsBlurb =
        "Demos, betas and playtests that shipped as their own entry.";

    /// <summary>A section with nothing in it. §7: a direction, not a mood.</summary>
    public const string SectionEmpty = "Nothing left to decide here.";

    /// <summary>A same-game section before the matcher has run once.</summary>
    public const string SectionNotSwept = "Still scanning your library.";

    // ══ The card ══════════════════════════════════════════════════════════

    /// <summary>Confidence badge, top tier.</summary>
    public const string ConfidenceExact = "EXACT MATCH";

    /// <summary>Confidence badge, middle tier.</summary>
    public const string ConfidenceLikely = "LIKELY";

    /// <summary>Confidence badge, bottom tier. Amber.</summary>
    public const string ConfidenceWorthALook = "WORTH A LOOK";

    /// <summary>Roll-up clause. <c>{0}</c> the summed hours.</summary>
    public const string RollupPlaytimeFormat = "{0} rolled up";

    /// <summary>Roll-up clause. <c>{0}</c> the entry count.</summary>
    public const string RollupEntriesFormat = "{0} entries";

    /// <summary>Roll-up clause. <c>{0}</c> the earliest ownership year.</summary>
    public const string RollupOwnedSinceFormat = "owned since {0}";

    /// <summary>Roll-up clause at one unread row. <c>{0}</c> is 1.</summary>
    public const string RollupUnreadOneFormat = "{0} entry patched since you played";

    /// <summary>Roll-up clause above one unread row. <c>{0}</c> the count.</summary>
    public const string RollupUnreadManyFormat = "{0} entries patched since you played";

    /// <summary>Tooltip on the card's unread dot. <c>{0}</c> how many rows.</summary>
    public const string CardUnreadTipFormat = "{0} of these have been patched since you played";

    /// <summary>Label on the affirmative answer (§7: "Same game", never "Merge records").</summary>
    public const string SameGameButton = "Same game";

    /// <summary>Label on the negative answer. Not a cancel; the other half of the answer.</summary>
    public const string DifferentGamesButton = "Different games";

    /// <summary>Tooltip on Same game.</summary>
    public const string SameGameTooltip = "Nest the other rows under the header (S)";

    /// <summary>Tooltip on Different games.</summary>
    public const string DifferentGamesTooltip = "Leave them separate, not asked again (D)";

    /// <summary>The word beside the promoted row's title. Volt.</summary>
    public const string HeaderMark = "HEADER";

    /// <summary>The word beside every other row's title. TextFaint.</summary>
    public const string NestsUnderMark = "NESTS UNDER";

    /// <summary>The word beside a row the user unchecked. TextFaint.</summary>
    public const string LeftOutMark = "LEFT OUT";

    /// <summary>Tooltip on the header radio.</summary>
    public const string PromoteTip = "Make this the header";

    /// <summary>Tooltip on the row: a click opens the game's details.</summary>
    public const string DetailsTip = "Open details";

    /// <summary>Tooltip on a checked include box.</summary>
    public const string LeaveOutTip = "Leave this entry out of the roll-up";

    /// <summary>Tooltip on an unchecked include box.</summary>
    public const string IncludeTip = "Bring this entry back into the roll-up";

    /// <summary>Roll-up clause when rows are left out. <c>{0}</c> how many.</summary>
    public const string RollupLeftOutFormat = "{0} left out";

    /// <summary>Tooltip on a row's unread dot, and the last clause of its detail.</summary>
    public const string RowUnreadTip = "Patched since you played";

    /// <summary>Playtime column at zero for an entry that is a game.</summary>
    public const string ZeroHours = "0h";

    /// <summary>Idle column for an entry never opened.</summary>
    public const string NeverIdle = "never";

    /// <summary>Label on the strip's control.</summary>
    public const string SeparateButton = "Separate again";

    /// <summary>Tooltip on that control.</summary>
    public const string SeparateTooltip = "Undo this roll-up. Nothing was deleted.";

    /// <summary>The strip's meta line. <c>{0}</c> entries, <c>{1}</c> hours.</summary>
    public const string ResolvedMetaFormat = "{0} entries · {1} · nested, nothing deleted";

    // ══ Row detail ════════════════════════════════════════════════════════

    /// <summary>Detail clause. <c>{0}</c> hours, <c>{1}</c> the ownership year.</summary>
    public const string DetailPlaytimeSinceFormat = "{0} since {1}";

    /// <summary>Detail clause for a game never opened.</summary>
    public const string DetailNeverOpened = "never opened";

    /// <summary>Detail clause for a pack with no hours of its own.</summary>
    public const string DetailPackNoPlaytime = "no separate playtime recorded";

    /// <summary>Detail clause. <c>{0}</c> the local date.</summary>
    public const string DetailLastPlayedFormat = "last played {0}";

    /// <summary>Detail clause for a game never opened. <c>{0}</c> the local ownership date.</summary>
    public const string DetailAddedFormat = "added {0}";

    /// <summary>Detail clause.</summary>
    public const string DetailInstalled = "installed";

    /// <summary>Detail clause.</summary>
    public const string DetailNotInstalled = "not installed";

    // ══ Reasons ═══════════════════════════════════════════════════════════

    /// <summary>Same-game reason opener when the normalised titles match. <c>{0}</c> the store list.</summary>
    public const string ReasonSameTitleOnFormat = "Same title on {0}.";

    /// <summary>Same-game reason opener when the normalised titles match and no store is known.</summary>
    public const string ReasonSameTitle = "Same title.";

    /// <summary>Same-game reason opener when the titles agree only once an edition is set aside.</summary>
    public const string ReasonSameTitleApartFromEdition = "Same title apart from the edition.";

    /// <summary>Same-game reason opener for a near match. <c>{0}</c> the similarity, two decimals.</summary>
    public const string ReasonNameMatchFormat = "{0} name match.";

    /// <summary>Reason clause.</summary>
    public const string ReasonSamePublisher = "Same publisher";

    /// <summary>Reason clause.</summary>
    public const string ReasonDifferentPublishers = "Different publishers";

    /// <summary>Reason clause.</summary>
    public const string ReasonSameYear = "same year";

    /// <summary>Reason clause. <c>{0}</c> the absolute year gap.</summary>
    public const string ReasonYearsApartFormat = "{0} years apart";

    /// <summary>Reason clause for a one-year gap.</summary>
    public const string ReasonOneYearApart = "a year apart";

    /// <summary>Reason clause for a row no proposal named with the header. <c>{0}</c> the row, <c>{1}</c> the sibling it came through.</summary>
    public const string ReasonIndirectFormat = "{0} reached this group through {1}.";

    /// <summary>Same-game reason when the row carried no recorded breakdown.</summary>
    public const string ReasonNoBreakdown = "The matcher recorded no breakdown for this pair.";

    /// <summary>Expansion reason when a storefront declared every pair. <c>{0}</c> the count, <c>{1}</c> the base title.</summary>
    public const string ReasonDeclaredManyFormat = "{0} entries declare {1} as their parent on the store.";

    /// <summary>Expansion reason when a storefront declared the one pair. <c>{0}</c> the pack, <c>{1}</c> the base title.</summary>
    public const string ReasonDeclaredOneFormat = "“{0}” is listed under {1} on the store.";

    /// <summary>Expansion reason from the title heuristic. <c>{0}</c> the extending words, <c>{1}</c> the base title.</summary>
    public const string ReasonSuffixFormat = "“{0}” added to {1}.";

    /// <summary>Expansion reason from the title heuristic for several packs. <c>{0}</c> the count, <c>{1}</c> the base title.</summary>
    public const string ReasonSuffixManyFormat = "{0} titles extend {1} by name.";

    // ══ The dock ══════════════════════════════════════════════════════════

    /// <summary>Dock title after one card was rolled up. <c>{0}</c> the header title.</summary>
    public const string DockRolledUnderFormat = "Rolled up under {0}.";

    /// <summary>Dock note after one card with one child.</summary>
    public const string DockNestedOne = "1 entry nested · nothing was deleted.";

    /// <summary>Dock note after one card. <c>{0}</c> how many rows nested.</summary>
    public const string DockNestedManyFormat = "{0} entries nested · nothing was deleted.";

    /// <summary>Dock note after one card with rows left out. <c>{0}</c> nested, <c>{1}</c> left out.</summary>
    public const string DockNestedLeftOutFormat = "{0} nested · {1} left out · nothing was deleted.";

    /// <summary>Dock title after Merge selected. <c>{0}</c> how many groups.</summary>
    public const string DockRolledGroupsFormat = "Rolled up {0} groups.";

    /// <summary>Dock title after Merge selected with one group.</summary>
    public const string DockRolledOneGroup = "Rolled up 1 group.";

    /// <summary>Dock note after Merge selected.</summary>
    public const string DockEachKeptHeader = "Each kept the header you picked · nothing was deleted.";

    /// <summary>Dock title after Accept exact. <c>{0}</c> how many.</summary>
    public const string DockRolledExactFormat = "Rolled up {0} exact matches.";

    /// <summary>Dock title after Accept exact with one.</summary>
    public const string DockRolledOneExact = "Rolled up 1 exact match.";

    /// <summary>Dock note after Accept exact.</summary>
    public const string DockCrossStoreOnly = "Cross-store duplicates only · nothing was deleted.";

    /// <summary>Dock title after Different games. <c>{0}</c> how many groups in the run.</summary>
    public const string DockLeftAloneFormat = "Left {0} groups alone.";

    /// <summary>Dock title after one Different games.</summary>
    public const string DockLeftOneAlone = "Left 1 group alone.";

    /// <summary>Dock note after Different games.</summary>
    public const string DockStaySeparate = "They stay separate in your library. Winnow will not ask again.";

    /// <summary>The dock's one control.</summary>
    public const string UndoButton = "Undo";

    /// <summary>Tooltip on Undo.</summary>
    public const string UndoTooltip = "Put it back the way it was";

    /// <summary>Tooltip on the dock's ✕.</summary>
    public const string DockCloseTip = "Dismiss";

    // ══ Automation ════════════════════════════════════════════════════════

    /// <summary>Automation name for Same game. <c>{0}</c> the row labels, <c>{1}</c> the header.</summary>
    public const string SameGameAutomationFormat = "Same game: {0}, under {1}";

    /// <summary>Automation name for Different games. <c>{0}</c> the row labels.</summary>
    public const string DifferentGamesAutomationFormat = "Different games: {0}";

    /// <summary>Automation name for Separate again. <c>{0}</c> the header.</summary>
    public const string SeparateAutomationFormat = "Separate again: {0}";

    /// <summary>Automation name for the card's checkbox. <c>{0}</c> the header.</summary>
    public const string SelectAutomationFormat = "Select {0}";

    /// <summary>Automation name for a row that can be promoted. <c>{0}</c> the row label.</summary>
    public const string PromoteAutomationFormat = "Make {0} the header";

    /// <summary>Automation name for the header row's radio. <c>{0}</c> the row label.</summary>
    public const string HeaderRowAutomationFormat = "{0}, the header";

    /// <summary>Automation name for a row's include checkbox. <c>{0}</c> the row label.</summary>
    public const string IncludeAutomationFormat = "Include {0}";

    /// <summary>Automation name for the row itself, whose click opens details. <c>{0}</c> the row label.</summary>
    public const string DetailsAutomationFormat = "Details of {0}";

    /// <summary>
    /// One row for a screen reader when its title alone would name two rows.
    /// <c>{0}</c> the title, <c>{1}</c> the qualifying facts, comma-joined:
    /// stores, year, publisher, added one at a time and only while a
    /// collision remains (§10.5 rejected surfacing database ids).
    /// </summary>
    public const string MemberLabelFormat = "{0} ({1})";

    /// <summary>Joins the qualifying facts inside a member label.</summary>
    public const string MemberQualifierSeparator = ", ";

    /// <summary>
    /// Last resort for two rows a storefront describes identically down to
    /// the publisher. <c>{0}</c> this row's position on the card, <c>{1}</c>
    /// how many rows the card holds.
    /// </summary>
    public const string MemberPositionFormat = "{0} of {1}";

    /// <summary>Automation name for the sort button. <c>{0}</c> the current order.</summary>
    public const string SortAutomationFormat = "Sort, {0}";

    /// <summary>Automation name for a kind segment. <c>{0}</c> the segment.</summary>
    public const string KindAutomationFormat = "Show {0}";
}
