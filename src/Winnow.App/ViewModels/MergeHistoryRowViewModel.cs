using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Merging;

namespace Winnow.App.ViewModels;

/// <summary>
/// One line of the counts detail: what moved and how many. Counts live here,
/// never on the history row itself, so the disclosure can be collapsed without
/// discarding them.
/// </summary>
/// <param name="Label">The table or category name for this count.</param>
/// <param name="Count">How many rows moved.</param>
public sealed record MergeCountRowViewModel(string Label, int Count)
{
    /// <summary>Plex Mono, tabular, grouped — every number in the app.</summary>
    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>
/// One applied merge, as the history list reads it: which two games became one,
/// and when. The undo verdict it carries is gate one's, recomputed by the screen
/// on every load and never cached, because reversibility depends on every merge
/// applied after this one.
/// </summary>
public partial class MergeHistoryRowViewModel : ObservableObject
{
    public MergeHistoryRowViewModel(MergeUndoPlan plan, string? blockingDescription = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Plan = plan;
        BlockingDescription = blockingDescription;
        Counts = BuildCounts(plan.Application?.Counts);
    }

    /// <summary>The undo plan, including the application record and every blocker.</summary>
    public MergeUndoPlan Plan { get; }

    /// <summary>The <c>merge_applications.id</c> this row represents.</summary>
    public long ApplicationId => Plan.ApplicationId;

    /// <summary>The application row, or null when it has gone from under the screen.</summary>
    public MergeApplicationRecord? Application => Plan.Application;

    /// <summary>The mode the merge ran in, or <see cref="MergeMode.NothingToDo"/> when the application record is absent.</summary>
    public MergeMode Mode => Application?.Mode ?? MergeMode.NothingToDo;

    /// <summary>The surviving game's current name, falling back to a release label when unknown.</summary>
    public string SurvivingTitle =>
        Application?.SurvivingTitle is { Length: > 0 } surviving
            ? surviving
            : MergeApplyViewModel.ReleaseLabel(Application?.SurvivingReleaseId);

    /// <summary>The absorbed game's name as the undo journal recorded it, or null for a pre-journal merge.</summary>
    public string? AbsorbedTitle => Application?.AbsorbedTitle;

    /// <summary>The row in user language: which game was folded into which.</summary>
    public string Description => AbsorbedTitle is { Length: > 0 } absorbed
        ? string.Format(CultureInfo.CurrentCulture, MergeCopy.HistoryRowFormat, absorbed, SurvivingTitle)
        : string.Format(CultureInfo.CurrentCulture, MergeCopy.HistoryRowUnnamedFormat, SurvivingTitle);

    /// <summary>The application date, formatted for display.</summary>
    public string AppliedAtText => FormatStamp(Application?.AppliedAt);

    /// <summary>The row as another row's blocker names it: what it did, and when.</summary>
    public string BlockingLabel =>
        string.Create(CultureInfo.CurrentCulture, $"{Description} ({AppliedAtText})");

    /// <summary>True when this merge has been reversed.</summary>
    public bool IsUndone => Application?.UndoneAt is not null;

    /// <summary>The undo date, formatted for display.</summary>
    public string UndoneAtText => FormatStamp(Application?.UndoneAt);

    /// <summary>The first blocker on this merge's reversibility, or <see cref="MergeUndoBlocker.None"/>.</summary>
    public MergeUndoBlocker Blocker => Plan.PrimaryBlocker;

    /// <summary>The id of the later merge that stands in this one's way, when one does.</summary>
    public long? BlockingApplicationId => Plan.BlockingApplicationId;

    /// <summary>The description of the merge that stands in this one's way, when it has one.</summary>
    public string? BlockingDescription { get; }

    /// <summary>
    /// Latched across an in-flight undo so a double click cannot ask for the
    /// same reversal twice.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUndo))]
    public partial bool IsUndoing { get; set; }

    /// <summary>True when the plan says this merge is reversible and no undo is in flight.</summary>
    public bool CanUndo => Plan.Reversible && !IsUndoing;

    /// <summary>
    /// False for an already-undone merge, which is history with no control at
    /// all. This is the one disabled reason that removes the affordance rather
    /// than disabling it.
    /// </summary>
    public bool ShowUndoControl => !IsUndone;

    /// <summary>True when this merge cannot be reversed and has not already been undone.</summary>
    public bool IsBlocked => !Plan.Reversible && !IsUndone;

    /// <summary>
    /// The disabled reason in plain language. Four reasons exist; exactly one of
    /// them names another merge and offers a way through.
    /// </summary>
    public string BlockedText => BlockedTextFor(Blocker, BlockingDescription, BlockingApplicationId);

    /// <summary>The sentence for one disabled reason, shared with the undo report.</summary>
    public static string BlockedTextFor(
        MergeUndoBlocker blocker,
        string? blockingDescription = null,
        long? blockingApplicationId = null) => blocker switch
        {
            MergeUndoBlocker.AlreadyUndone => MergeCopy.UndoBlockedAlreadyUndone,
            MergeUndoBlocker.PredatesUndoSupport => MergeCopy.UndoBlockedPredatesUndoSupport,
            MergeUndoBlocker.GameNoLongerExists => MergeCopy.UndoBlockedGameNoLongerExists,
            MergeUndoBlocker.LaterMergeConsumedIdentity => blockingDescription is { Length: > 0 } named
                ? string.Format(CultureInfo.CurrentCulture, MergeCopy.UndoBlockedLaterMergeFormat, named)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    MergeCopy.UndoBlockedLaterMergeUnnamedFormat,
                    blockingApplicationId ?? 0),
            _ => string.Empty,
        };

    /// <summary>
    /// True when the blocker is a later merge that consumed one of this merge's
    /// identities. The only disabled reason with a user action: the row names
    /// that merge and offers to undo it first.
    /// </summary>
    public bool HasBlockingMerge =>
        IsBlocked
        && Blocker == MergeUndoBlocker.LaterMergeConsumedIdentity
        && BlockingApplicationId is not null;

    // ── Chrome the view binds to ─────────────────────────────────────────────

    /// <summary>Small uppercase label before the date.</summary>
    public string AppliedAtLabel => MergeCopy.AppliedAtLabel;

    /// <summary>Small uppercase label marking a reversed merge.</summary>
    public string UndoneLabelText => MergeCopy.UndoneLabel;

    /// <summary>The undo control's label.</summary>
    public string UndoButtonText => MergeCopy.UndoButton;

    /// <summary>Tooltip on the undo control: states the all-or-nothing guarantee.</summary>
    public string UndoTooltip => MergeCopy.UndoTooltip;

    /// <summary>Label for the control that undoes the merge standing in this one's way.</summary>
    public string UndoBlockingButtonText => MergeCopy.UndoBlockingButton;

    /// <summary>Introduction sentence for the per-table counts panel.</summary>
    public string CountsIntro => MergeCopy.CountsIntro;

    /// <summary>
    /// Automation name so a screen reader hears which merge is being reversed,
    /// not "Undo" repeated down the list (section 8).
    /// </summary>
    public string UndoAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.UndoButton}. {Description}");

    /// <summary>Automation name for the "undo that merge" control, naming the blocking merge.</summary>
    public string UndoBlockingAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.UndoBlockingButton}. {BlockingDescription}");

    /// <summary>Per-table breakdown of what the merge moved, or empty when no summary was recorded.</summary>
    public IReadOnlyList<MergeCountRowViewModel> Counts { get; }

    /// <summary>True when the merge recorded a per-table breakdown.</summary>
    public bool HasCounts => Counts.Count > 0;

    /// <summary>Whether the per-table counts disclosure is expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountsToggleText))]
    public partial bool IsCountsOpen { get; set; }

    /// <summary>Label on the disclosure toggle, reflecting the current state.</summary>
    public string CountsToggleText => IsCountsOpen ? MergeCopy.CountsHide : MergeCopy.CountsShow;

    [RelayCommand]
    private void ToggleCounts() => IsCountsOpen = !IsCountsOpen;

    private static string FormatStamp(DateTime? stamp)
        => stamp is { } value
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : "—";

    // Only the tables that moved are listed. A table with zero rows moved is
    // not a zero worth printing.
    private static IReadOnlyList<MergeCountRowViewModel> BuildCounts(MergeRepointCounts? counts)
    {
        if (counts is null)
        {
            return [];
        }

        var rows = new List<MergeCountRowViewModel>();
        void Add(string label, int count)
        {
            if (count > 0)
            {
                rows.Add(new MergeCountRowViewModel(label, count));
            }
        }

        Add(MergeCopy.CountReleases, counts.Releases);
        Add(MergeCopy.CountExternalIds, counts.ExternalIds);
        Add(MergeCopy.CountOwnerships, counts.Ownerships);
        Add(MergeCopy.CountOwnershipsFolded, counts.OwnershipsFolded);
        Add(MergeCopy.CountOwnershipAccounts, counts.OwnershipAccounts);
        Add(MergeCopy.CountPlayRecords, counts.PlayRecords);
        Add(MergeCopy.CountPlaytimeSnapshots, counts.PlaytimeSnapshots);
        Add(MergeCopy.CountSessions, counts.Sessions);
        Add(MergeCopy.CountUpdateEvents, counts.UpdateEvents);
        Add(MergeCopy.CountUpdateAcknowledgements, counts.UpdateAcknowledgements);
        Add(MergeCopy.CountListItems, counts.ListItems);
        Add(MergeCopy.CountWorkFacets, counts.WorkFacets);
        Add(MergeCopy.CountReleaseFacets, counts.ReleaseFacets);
        Add(MergeCopy.CountFeedVerdicts, counts.FeedVerdicts);
        Add(MergeCopy.CountFeedSurfacings, counts.FeedSurfacings);
        Add(MergeCopy.CountMergeCandidates, counts.MergeCandidates);
        Add(MergeCopy.CountAchievements, counts.Achievements);
        Add(MergeCopy.CountAchievementUnlocks, counts.AchievementUnlocks);
        Add(MergeCopy.CountDuplicateRowsDropped, counts.DuplicateRowsDropped);

        return rows;
    }
}
