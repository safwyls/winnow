namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the unread-update signal (§5.2) wherever it has to be
/// said in words instead of drawn. The Flare dot on a cover tile and the Flare
/// pip beside the rail's Patched count are marks, and §8's decorative-redundant
/// rule requires the same fact to be reachable as text: idle time has hover
/// text and a sortable column behind it, the store mark has chips and a
/// tooltip, and the unread badge had nothing.
/// <para>Read by <see cref="GameTileViewModel.UnreadText"/>, which the tile's
/// accessible name and, through it, the feed card's are built from, and by
/// <see cref="BucketViewModel.AutomationName"/>. Kept in one place so the tile
/// and the rail state one fact in one vocabulary.</para>
/// </summary>
public static class UnreadCopy
{
    /// <summary>
    /// A count of updates in words, for a sentence that has to carry it. It is
    /// spelled into the string because there is nowhere else to put it:
    /// Avalonia compiles <c>AutomationProperties.PositionInSet</c> and
    /// <c>SizeOfSet</c> but reads them nowhere — verified against the 11.3.20
    /// source, where each has exactly one reference, its own declaration — so
    /// "3 of 12" is not expressible. Grouped like every other count the
    /// interface states. Handles singular and plural.
    /// </summary>
    public static string Updates(int count) =>
        count == 1 ? "1 update" : $"{count:N0} updates";

    /// <summary>
    /// What the Flare dot on a cover tile means, in words, for the tile's
    /// accessible name. Empty when the tile carries no dot, so an ordinary tile
    /// gains nothing.
    /// <para>"Patched since you played" is the phrase the rail label and the
    /// merge queue's row tooltip already use for this bucket (§7); the badge
    /// stays inside that vocabulary rather than giving one fact a second name.
    /// At zero the count is left out rather than read: the badge is bucket
    /// membership and the count comes from a separate column, so a badge whose
    /// count did not arrive still says what it is instead of announcing a bare
    /// zero.</para>
    /// </summary>
    public static string TileBadge(bool hasUnread, int count)
    {
        if (!hasUnread)
        {
            return string.Empty;
        }

        return count > 0
            ? $"Patched since you played: {Updates(count)}."
            : "Patched since you played.";
    }

    /// <summary>
    /// The accessible name of one update row in the details modal. The
    /// <c>Flare</c> dot is a mark and §8's decorative-redundant rule wants the
    /// same fact as text, so the row's accessible name states in words whether
    /// it landed since the last session.
    /// </summary>
    public static string UpdateRow(string headline, string dateText, bool isUnread) =>
        isUnread
            ? $"{headline}, {dateText}. Landed since you played."
            : $"{headline}, {dateText}.";

    /// <summary>
    /// The accessible name of one rail bucket row. The row is a Button whose
    /// content is a Grid, and <c>ContentControlAutomationPeer.GetNameCore</c>
    /// falls back to <c>Content?.ToString()</c> when the button has no name of
    /// its own, so every row on the rail announced "Avalonia.Controls.Grid".
    /// <para><paramref name="railLabel"/> is the bucket's Name — "Patched",
    /// "Never played" — and never the all-caps <c>RailLabel</c> the row draws,
    /// because capitals are the type style rather than the word.
    /// <paramref name="countText"/> arrives already formatted. The pip's clause
    /// is the one thing the Patched row says that no other row does, which is
    /// exactly what the pip means (§6).</para>
    /// </summary>

    public static string RailBucket(string railLabel, string countText, bool showsFlarePip) =>
        showsFlarePip
            ? $"{railLabel}, {countText} games with unread updates"
            : $"{railLabel}, {countText} games";
}
