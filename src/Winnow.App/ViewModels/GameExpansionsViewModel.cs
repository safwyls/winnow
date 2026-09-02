using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Identity;

namespace Winnow.App.ViewModels;

/// <summary>
/// The EXPANSIONS section of the details modal, and the EXTENDS line on an
/// expansion's own modal.
///
/// <para>It follows the treatment TASK-70.4 set for
/// ALSO COVERS — a band-4 section, one row per title, each row's own playtime
/// beside that same row's own last-played, and a Separate-shaped control that
/// retracts one link from the place the user noticed it. It differs from ALSO
/// COVERS in exactly one way, and the difference is the whole feature: THERE
/// IS NO TOTAL. Coverage sums, because two store entries of one game are one
/// game being played. Expansions do not, because Civilization IV's hours are
/// Civilization IV's and Beyond the Sword's are its own, and a sum of the two
/// is a number no source reported about either.</para>
///
/// <para>There is no property on this type that adds two rows together, and no
/// <see cref="CoveragePlaytime"/> spanning the section, so the rule is
/// structural rather than remembered.</para>
///
/// <para>The two sections are never merged: a covered title is the same game
/// on another store, and an expansion is a different product. They answer
/// different questions and are drawn as different sections.</para>
/// </summary>
public sealed partial class GameExpansionsViewModel : ObservableObject
{
    private readonly Func<long, Task>? _ungroup;

    /// <summary>Creates the section.</summary>
    /// <param name="expansions">The packs grouped under this game. Empty draws no section.</param>
    /// <param name="extends">The base game this one extends, or null when it extends nothing.</param>
    /// <param name="ungroup">Retracts one expansion link by child work id. Null leaves the control inert.</param>
    public GameExpansionsViewModel(
        IReadOnlyList<ExpansionRowViewModel> expansions,
        ExpansionRowViewModel? extends = null,
        Func<long, Task>? ungroup = null)
    {
        ArgumentNullException.ThrowIfNull(expansions);

        Expansions = expansions;
        Extends = extends;
        _ungroup = ungroup;
    }

    /// <summary>An empty section, which draws as no section rather than as an empty one.</summary>
    public static GameExpansionsViewModel Empty { get; } = new([]);

    /// <summary>The packs grouped under this game, work id order.</summary>
    public IReadOnlyList<ExpansionRowViewModel> Expansions { get; }

    /// <summary>
    /// The base game this one extends, or null when it extends nothing. Present
    /// on the PACK's own modal, which is the other half of the relation being
    /// visible from both ends.
    /// </summary>
    public ExpansionRowViewModel? Extends { get; }

    /// <summary>True when this game has packs under it.</summary>
    public bool HasExpansions => Expansions.Count > 0;

    /// <summary>True when this game is itself a pack.</summary>
    public bool HasBase => Extends is not null;

    /// <summary>Section heading.</summary>
    public string Heading => ExpansionCopy.ExpansionsHeading;

    /// <summary>The sentence that says the hours below are not added above.</summary>
    public string Note => ExpansionCopy.ExpansionsNote;

    /// <summary>Label above the base game on a pack's own modal.</summary>
    public string ExtendsHeading => ExpansionCopy.ExtendsHeading;

    /// <summary>Caption saying this is still a separate game in the library.</summary>
    public string ExtendsNote => ExpansionCopy.ExtendsNote;

    /// <summary>A write that did not land. Amber, non-blocking.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    /// <summary>True while a problem is showing.</summary>
    public bool HasProblem => Problem is not null;

    /// <summary>Retracts the one expansion link the row names.</summary>
    [RelayCommand]
    private async Task UngroupAsync(ExpansionRowViewModel? row)
    {
        if (row is null || _ungroup is null)
        {
            return;
        }

        try
        {
            Problem = null;

            // The CHILD of the link is retracted, whichever end the row is
            // drawn at: on the base game's modal the row is the pack, and on
            // the pack's own modal the row is the base but the link's child is
            // still this game. ChildWorkId names the link, never the row.
            await _ungroup(row.ChildWorkId);
        }
        catch
        {
            Problem = ExpansionCopy.UngroupProblem;
        }
    }
}

/// <summary>
/// One game in the expansion section. Its minutes and its last-played are its
/// own and are never crossed with another row's (F10), and nothing on this type
/// or above it adds two of them together.
/// </summary>
public sealed class ExpansionRowViewModel
{
    /// <summary>Creates a row.</summary>
    /// <param name="workId">The game this row is about.</param>
    /// <param name="childWorkId">The child end of the link this row's control retracts.</param>
    /// <param name="title">The title the library shows for it.</param>
    /// <param name="storeChips">Store badges.</param>
    /// <param name="storeNames">Store names, comma-joined, for a screen reader.</param>
    /// <param name="playtimeMinutes">This game's own minutes.</param>
    /// <param name="lastPlayedAt">This game's own last-played, or null when never recorded.</param>
    public ExpansionRowViewModel(
        long workId,
        long childWorkId,
        string title,
        IReadOnlyList<string> storeChips,
        string storeNames,
        long playtimeMinutes,
        DateTime? lastPlayedAt)
    {
        ArgumentNullException.ThrowIfNull(storeChips);

        WorkId = workId;
        ChildWorkId = childWorkId;
        Title = title;
        StoreChips = storeChips;
        StoreNames = storeNames;
        PlaytimeMinutes = playtimeMinutes;
        LastPlayedAt = lastPlayedAt;

        PlaytimeText = GameTileViewModel.BuildPlaytimeText(playtimeMinutes);
        LastPlayedText = lastPlayedAt is { } played
            ? UpdateEventViewModel.LocalDateText(played)
            : playtimeMinutes <= 0 ? "Never played" : "Not recorded";
    }

    /// <summary>The game this row is about.</summary>
    public long WorkId { get; }

    /// <summary>
    /// The child end of the link this row's control retracts. On a base game's
    /// modal that is <see cref="WorkId"/>; on a pack's own modal it is the game
    /// whose modal is open, because the link points from the pack at its base.
    /// </summary>
    public long ChildWorkId { get; }

    /// <summary>The title the library shows for this game.</summary>
    public string Title { get; }

    /// <summary>Store badges for this game.</summary>
    public IReadOnlyList<string> StoreChips { get; }

    /// <summary>Store names, comma-joined, for a screen reader.</summary>
    public string StoreNames { get; }

    /// <summary>False when no visible ownership row named a store.</summary>
    public bool HasStores => StoreChips.Count > 0;

    /// <summary>This row's own minutes, and nobody else's.</summary>
    public long PlaytimeMinutes { get; }

    /// <summary>This row's own last-played, paired with this row's own minutes.</summary>
    public DateTime? LastPlayedAt { get; }

    /// <summary>The minutes, formatted. Plex Mono, tabular (§3).</summary>
    public string PlaytimeText { get; }

    /// <summary>
    /// The last-played, local. "Never played" when there are no minutes either,
    /// "Not recorded" when there are minutes but no date.
    /// </summary>
    public string LastPlayedText { get; }

    /// <summary>Button label for the retraction control.</summary>
    public string UngroupLabel => ExpansionCopy.UngroupButton;

    /// <summary>Tooltip for the retraction control.</summary>
    public string UngroupTooltip => ExpansionCopy.UngroupTooltip;

    /// <summary>Names the title and its stores, because two rows can share a title (§8).</summary>
    public string UngroupAutomationName => string.Format(
        System.Globalization.CultureInfo.CurrentCulture,
        ExpansionCopy.UngroupAutomationFormat,
        Title,
        HasStores ? StoreNames : "—");
}
