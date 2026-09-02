using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.Core.Merging;

namespace Winnow.App.ViewModels;

/// <summary>
/// One confirmed pair waiting to be applied. Its job is the preview: which
/// identity survives, what mode the merge runs in, and any blocker, so the user
/// commits to something specific rather than to a verb.
/// </summary>
public partial class MergeApplyViewModel : ObservableObject
{
    public MergeApplyViewModel(
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

    /// <summary>The engine's plan for this pair, including mode, blocker and identity choices.</summary>
    public MergePlan Plan { get; }

    /// <summary>The <c>merge_candidates.id</c> an apply names.</summary>
    public long Id => Plan.CandidateId;

    /// <summary>The name of the game that will remain after the merge.</summary>
    public string SurvivingTitle { get; }

    /// <summary>The name of the game that will be folded in, or null when no title is on record.</summary>
    public string? AbsorbedTitle { get; }

    /// <summary>The release id of the absorbed side, used as a last-resort label when no title exists.</summary>
    public long? AbsorbedReleaseId { get; }

    /// <summary>Whether this merge unifies at the work layer only or collapses the releases too.</summary>
    public MergeMode Mode => Plan.Mode;

    /// <summary>What prevented a full release collapse, or <see cref="MergeBlocker.None"/>.</summary>
    public MergeBlocker Blocker => Plan.Blocker;

    /// <summary>
    /// Latched the moment an apply starts, so a double click cannot ask for the
    /// same merge twice.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial bool IsApplying { get; set; }

    /// <summary>True when the plan has something to do and no apply is in flight.</summary>
    public bool CanApply => Mode != MergeMode.NothingToDo && !IsApplying;

    /// <summary>The sentence that names the surviving identity and the one being folded in.</summary>
    public string SurvivorLine => AbsorbedTitle is { Length: > 0 } absorbed
        ? string.Format(CultureInfo.CurrentCulture, MergeCopy.SurvivorLineFormat, SurvivingTitle, absorbed)
        : string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.SurvivorLineUnnamedFormat,
            SurvivingTitle,
            ReleaseLabel(AbsorbedReleaseId));

    /// <summary>Plain-language description of the merge mode, or empty when nothing to do.</summary>
    public string ModeText => Mode switch
    {
        MergeMode.ReleaseCollapse => MergeCopy.ModeReleaseCollapse,
        MergeMode.WorkOnly => MergeCopy.ModeWorkOnly,
        _ => string.Empty,
    };

    /// <summary>True when the merge has a mode worth displaying.</summary>
    public bool HasMode => Mode != MergeMode.NothingToDo;

    /// <summary>
    /// True when the plan has a blocker but can still do something. The blocker
    /// explains why the collapse was limited to the work layer; it is not a
    /// refusal.
    /// </summary>
    public bool HasLimitation => HasMode && Blocker != MergeBlocker.None;

    /// <summary>Why the collapse was limited, in a sentence the card displays.</summary>
    public string LimitationText => Blocker switch
    {
        MergeBlocker.DistinctEditions => MergeCopy.LimitedDistinctEditions,
        MergeBlocker.AchievementsOnBothSides => MergeCopy.LimitedAchievementsOnBothSides,
        MergeBlocker.ConflictingUpdateEvents => MergeCopy.LimitedConflictingUpdateEvents,
        _ => string.Empty,
    };

    /// <summary>True when the plan can do nothing at all.</summary>
    public bool IsBlocked => Mode == MergeMode.NothingToDo;

    // ── Chrome the view binds to ─────────────────────────────────────────────

    /// <summary>The card's own label: what this will do, or that it cannot.</summary>
    public string SectionLabel => IsBlocked ? MergeCopy.BlockedLabel : MergeCopy.ApplySectionLabel;

    /// <summary>Small uppercase label beside the mode description.</summary>
    public string ModeLabel => MergeCopy.ModeLabel;

    /// <summary>The per-pair apply control's label.</summary>
    public string ApplyButtonText => MergeCopy.ApplyButton;

    /// <summary>
    /// Automation name so a screen reader hears which pair the button applies,
    /// not "Apply this pair" repeated down the list (section 8).
    /// </summary>
    public string ApplyAutomationName =>
        string.Create(CultureInfo.CurrentCulture, $"{MergeCopy.ApplyButton}. {SurvivorLine}");

    /// <summary>The refusal sentence, in plain language, on a plan that can do nothing at all.</summary>
    public string RefusalText => RefusalFor(Blocker);

    /// <summary>The refusal sentence for one blocker, shared with the apply report.</summary>
    public static string RefusalFor(MergeBlocker blocker) => blocker switch
    {
        MergeBlocker.CandidateNotFound => MergeCopy.RefusedCandidateNotFound,
        MergeBlocker.CandidateNotConfirmed => MergeCopy.RefusedCandidateNotConfirmed,
        MergeBlocker.AlreadyApplied => MergeCopy.RefusedAlreadyApplied,
        MergeBlocker.DistinctEditions => MergeCopy.RefusedDistinctEditions,
        MergeBlocker.AchievementsOnBothSides => MergeCopy.RefusedAchievementsOnBothSides,
        MergeBlocker.ConflictingUpdateEvents => MergeCopy.RefusedConflictingUpdateEvents,
        MergeBlocker.PreferredSurvivorNotInPair => MergeCopy.RefusedPreferredSurvivorNotInPair,
        MergeBlocker.SurvivorCannotHoldIgdbId => MergeCopy.RefusedSurvivorCannotHoldIgdbId,
        _ => MergeCopy.RefusedAlreadyApplied,
    };

    // A release id is not a name, but when a side has no title on record it is
    // the only thing on screen that tells two untitled rows apart.
    internal static string ReleaseLabel(long? releaseId) => releaseId is { } id
        ? string.Create(CultureInfo.InvariantCulture, $"release {id}")
        : "the other entry";
}
