using System.Globalization;
using Winnow.Core.Merging;

namespace Winnow.App.ViewModels;

/// <summary>
/// The inline outcome block on a pending review card. Two lines, no more:
/// <see cref="SurvivorLine"/> names which identity the library keeps,
/// <see cref="EffectLine"/> says what becomes of the two store entries.
/// Because answering "Same game" now writes immediately, this is the user's
/// only statement of the result before the merge runs, and it must be exact.
/// It must also never overstate: when the plan refuses the merge, the card
/// says so here above the answers rather than promising something the engine
/// will not do.
/// </summary>
public sealed class MergePreviewViewModel
{
    public MergePreviewViewModel(
        MergePlan plan,
        string survivingTitle,
        string? absorbedTitle,
        long? absorbedReleaseId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(survivingTitle);

        Plan = plan;
        SurvivingTitle = survivingTitle;
        AbsorbedTitle = absorbedTitle;
        AbsorbedReleaseId = absorbedReleaseId;
    }

    /// <summary>The engine's plan for this pair, including mode and blocker.</summary>
    public MergePlan Plan { get; }

    /// <summary>The title the library will keep after the merge.</summary>
    public string SurvivingTitle { get; }

    /// <summary>The title the merge absorbs, or null when the absorbed side has no title on record.</summary>
    public string? AbsorbedTitle { get; }

    /// <summary>Last-resort label when <see cref="AbsorbedTitle"/> is null.</summary>
    public long? AbsorbedReleaseId { get; }

    /// <summary>The merge mode the plan chose: collapse, work-only, or nothing.</summary>
    public MergeMode Mode => Plan.Mode;

    /// <summary>The reason a collapse was refused, or <see cref="MergeBlocker.None"/>.</summary>
    public MergeBlocker Blocker => Plan.Blocker;

    /// <summary>
    /// True when the plan can do nothing at all. A pending pair whose two
    /// releases already sit under one work is now filtered out of
    /// <c>GetPendingAsync</c> and pruned from the queue at refresh time, so
    /// this state is unreachable from a review card under normal operation.
    /// The machinery stays as the honest fallback for a pair answered out
    /// from under the screen (for instance, by applying a neighbour that
    /// puts both releases under one work). Both answers stay enabled so
    /// the pair does not strand in the queue; an answer that files the
    /// decision and changes nothing is honest as long as the card says so
    /// first.
    /// </summary>
    public bool IsBlocked => Mode == MergeMode.NothingToDo;

    /// <summary>OUTCOME when the plan will act, BLOCKED when it will not.</summary>
    public string Label => IsBlocked ? MergeCopy.BlockedLabel : MergeCopy.OutcomeLabel;

    /// <summary>Line one. Names the surviving identity, or states the block.</summary>
    public string SurvivorLine => IsBlocked
        ? BlockedLine
        : AbsorbedTitle is { Length: > 0 } absorbed
            ? string.Format(
                CultureInfo.CurrentCulture, MergeCopy.PreviewSurvivorFormat, SurvivingTitle, absorbed)
            : string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.PreviewSurvivorUnnamedFormat,
                SurvivingTitle,
                MergeApplyViewModel.ReleaseLabel(AbsorbedReleaseId));

    /// <summary>
    /// Which rung of the ladder decided <see cref="MergePlan.SurvivingWorkId"/>.
    /// The reason arrives as an enum from the plan and is worded here, so the
    /// repository never builds a sentence.
    /// </summary>
    public MergeSurvivorReason SurvivorReason => Plan.SurvivorReason;

    /// <summary>The reason worded for display, or empty when there is nothing to say.</summary>
    public string SurvivorReasonText => SurvivorReason switch
    {
        MergeSurvivorReason.IgdbMatch => MergeCopy.SurvivorReasonIgdbMatch,
        MergeSurvivorReason.NamedByStore => MergeCopy.SurvivorReasonNamedByStore,
        MergeSurvivorReason.MostStoreEntries => MergeCopy.SurvivorReasonMostStoreEntries,
        MergeSurvivorReason.AddedFirst => MergeCopy.SurvivorReasonAddedFirst,
        MergeSurvivorReason.ChosenByYou => MergeCopy.SurvivorReasonChosenByYou,
        _ => string.Empty,
    };

    /// <summary>
    /// True when the card should show the WHY line. False when the plan is
    /// blocked (no survivor was chosen) or when the reason is one the UI
    /// does not word (None, AlreadyOneGame).
    /// </summary>
    public bool HasSurvivorReason => !IsBlocked && SurvivorReasonText.Length > 0;

    /// <summary>Small uppercase label rendered beside <see cref="SurvivorReasonText"/>.</summary>
    public string SurvivorReasonLabel => MergeCopy.SurvivorReasonLabel;

    /// <summary>
    /// Line two. What happens to the two store entries, or, on a blocked pair,
    /// what pressing the answer will actually do (file the decision, change
    /// nothing).
    /// </summary>
    public string EffectLine => Mode switch
    {
        MergeMode.ReleaseCollapse => MergeCopy.PreviewCollapse,
        MergeMode.WorkOnly => Blocker switch
        {
            MergeBlocker.DistinctEditions => MergeCopy.PreviewWorkOnlyDistinctEditions,
            MergeBlocker.AchievementsOnBothSides => MergeCopy.PreviewWorkOnlyAchievements,
            MergeBlocker.ConflictingUpdateEvents => MergeCopy.PreviewWorkOnlyUpdateEvents,
            _ => MergeCopy.PreviewWorkOnlyOther,
        },
        _ => MergeCopy.PreviewBlockedAnswerEffect,
    };

    /// <summary>
    /// Tooltip on "Same game". A blocked pair gets a tooltip that does not
    /// promise a merge, because the plan will not perform one.
    /// </summary>
    public string SameGameTooltip =>
        IsBlocked ? MergeCopy.SameGameBlockedTooltip : MergeCopy.SameGameTooltip;

    // A pending pair whose two releases already sit under one work is now
    // filtered out of GetPendingAsync, so PreviewBlockedAlreadyOneGame is
    // unreachable from a review card. It remains as the fallback for a pair
    // answered out from under the screen. CandidateNotFound has its own
    // sentence. Every other blocker falls through to a sentence that asserts
    // nothing about the library's state.
    private string BlockedLine => Blocker switch
    {
        MergeBlocker.DistinctEditions
            or MergeBlocker.AchievementsOnBothSides
            or MergeBlocker.ConflictingUpdateEvents => MergeCopy.PreviewBlockedAlreadyOneGame,
        MergeBlocker.CandidateNotFound => MergeCopy.PreviewBlockedNotFound,
        _ => MergeCopy.PreviewBlockedNothingToDo,
    };
}
