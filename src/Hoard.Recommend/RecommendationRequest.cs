using Hoard.Core.Queries;

namespace Hoard.Recommend;

/// <summary>
/// Everything one feed computation needs from the caller. The three id sets
/// are the user's voice in the model — the engine stores nothing (a computed
/// feed must be droppable at any moment), so intent that has to persist
/// (dismissals, snoozes, what was already shown) lives with the caller and
/// arrives here on every request.
/// </summary>
public sealed record RecommendationRequest
{
    private static readonly IReadOnlySet<long> EmptySet = new HashSet<long>();

    /// <summary>
    /// The clock, injected: dormancy, fresh-play and the default shuffle seed
    /// are all computed against this instant, which is what makes a feed
    /// reproducible in a test and identical across the items of one request.
    /// </summary>
    public required DateTime AsOfUtc { get; init; }

    /// <summary>How many items to return. The shortlist probed for history is 3× this, capped by tuning.</summary>
    public int MaxResults { get; init; } = 20;

    /// <summary>
    /// Items per shelf for <see cref="IRecommendationEngine.GetShelvesAsync"/>.
    /// 10: a rail the eye can actually sweep — beyond it a shelf stops being a
    /// pitch and becomes another list.
    /// </summary>
    public int MaxPerShelf { get; init; } = 6;

    /// <summary>
    /// §6.1's numbers — refund line, retired floor, stale window. Passed
    /// through to the bucket query and reused by the commitment curve, so the
    /// feed and the library rail can never disagree about what "bounced" means.
    /// </summary>
    public BucketThresholds Thresholds { get; init; } = BucketThresholds.Default;

    /// <summary>Every weight and threshold of the model. See <see cref="RecommendationTuning"/>.</summary>
    public RecommendationTuning Tuning { get; init; } = RecommendationTuning.Default;

    /// <summary>
    /// Releases the user has permanently dismissed — their explicit "you were
    /// right, I'm done with this" verdict. Hard-excluded before scoring; a
    /// recommender that argues with an explicit verdict is nagging.
    /// </summary>
    public IReadOnlySet<long> NotInterestedReleaseIds { get; init; } = EmptySet;

    /// <summary>Releases temporarily set aside ("not now"). Same exclusion, caller decides when it lapses.</summary>
    public IReadOnlySet<long> SnoozedReleaseIds { get; init; } = EmptySet;

    /// <summary>
    /// Releases the feed surfaced recently, per the caller's own bookkeeping.
    /// Demoted, not excluded — the anti-"same five games forever" mechanism.
    /// </summary>
    public IReadOnlySet<long> RecentlySurfacedReleaseIds { get; init; } = EmptySet;

    /// <summary>
    /// Seed for the deterministic near-tie shuffle. Null (the default) derives
    /// it from <see cref="AsOfUtc"/>'s DATE, so the feed rotates daily but is
    /// stable within a day — refreshing the view must not deal a new hand.
    /// Fix it in tests for full determinism.
    /// </summary>
    public int? ShuffleSeed { get; init; }
}
