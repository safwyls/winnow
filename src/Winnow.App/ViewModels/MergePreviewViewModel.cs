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
    /// True when the plan can do nothing at all. For a pending pair this means
    /// the two releases already sit under one work and a collapse blocker
    /// (different editions, achievements on both sides, or conflicting update
    /// events) forbids collapsing the two rows as well. Both answers stay
    /// enabled on purpose: disabling them would strand the pair in the queue
    /// forever, and the only way out would be "Different games", which would
    /// record a rejection that is false. An answer that files a decision and
    /// changes nothing is honest as long as the card says so first; a queue
    /// that cannot be emptied is not.
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

    // Only the three collapse blockers (DistinctEditions, AchievementsOnBothSides,
    // ConflictingUpdateEvents) imply the two entries already sit under one work,
    // and only they may claim so. CandidateNotFound has its own sentence. Every
    // other blocker, including AlreadyApplied which the planner cannot produce
    // but the type admits, falls through to a sentence that asserts nothing about
    // the library's state; asserting "already one game" for a pair answered out
    // from under the screen would be false.
    private string BlockedLine => Blocker switch
    {
        MergeBlocker.DistinctEditions
            or MergeBlocker.AchievementsOnBothSides
            or MergeBlocker.ConflictingUpdateEvents => MergeCopy.PreviewBlockedAlreadyOneGame,
        MergeBlocker.CandidateNotFound => MergeCopy.PreviewBlockedNotFound,
        _ => MergeCopy.PreviewBlockedNothingToDo,
    };
}
