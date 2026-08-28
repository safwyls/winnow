using Hoard.Core.Domain;
using Hoard.Core.Repositories;

namespace Hoard.Recommend;

/// <summary>
/// The read-side bridge of the feedback loop: turns what the feedback store
/// holds (verdicts, the surfacing log, launch-attributed sessions) into the
/// four id sets a <see cref="RecommendationRequest"/> carries.
///
/// <para><b>Why this lives here and not in the engine.</b> The engine's
/// contract is that intent arrives on the request and nothing persists inside
/// the module — a computed feed must be droppable at any moment. That contract
/// survives feedback: the store is truth (what the user said, what the feed
/// showed), this class is a pure read over it, and the engine still sees only
/// sets. It also does not live in the App layer, because how stored feedback
/// becomes scoring input is recommendation policy (the windows are
/// <see cref="RecommendationTuning"/> parameters), not presentation.</para>
///
/// <para><b>This class never writes.</b> Recording verdicts and surfacings is
/// the caller's act (FeedService, on user clicks and after each feed).
/// Hoard.Recommend reads through repository interfaces and returns scored
/// results — feedback changes nothing about that.</para>
/// </summary>
public sealed record FeedbackSets
{
    private static readonly IReadOnlySet<long> EmptySet = new HashSet<long>();

    /// <summary>Releases under an active "not interested" — hard-excluded, work-wide.</summary>
    public IReadOnlySet<long> NotInterestedReleaseIds { get; init; } = EmptySet;

    /// <summary>Releases under an unexpired snooze — same exclusion; expiry re-admits with no write.</summary>
    public IReadOnlySet<long> SnoozedReleaseIds { get; init; } = EmptySet;

    /// <summary>Releases surfaced in the window before (never including) today — demoted, not excluded.</summary>
    public IReadOnlySet<long> RecentlySurfacedReleaseIds { get; init; } = EmptySet;

    /// <summary>Releases the user launched off the feed — allowed to testify to the taste profile.</summary>
    public IReadOnlySet<long> EndorsedReleaseIds { get; init; } = EmptySet;

    /// <summary>Nothing stored yet — the sets a first launch runs on.</summary>
    public static FeedbackSets Empty { get; } = new();

    /// <summary>
    /// Reads the store and computes the sets as of one instant — the same
    /// instant the feed will be computed at, so "active", "recent" and
    /// "today" cannot disagree between the two reads.
    /// </summary>
    public static async Task<FeedbackSets> LoadAsync(
        IFeedFeedbackRepository feedback,
        DateTime asOfUtc,
        RecommendationTuning tuning,
        CancellationToken ct = default)
    {
        var notInterested = new HashSet<long>();
        var snoozed = new HashSet<long>();
        foreach (var verdict in await feedback.GetActiveVerdictsAsync(asOfUtc, ct))
        {
            // GetActiveVerdictsAsync already applied revocation and expiry;
            // routing by kind is all that is left. An unknown kind (a future
            // migration's) is deliberately dropped rather than guessed at —
            // a verdict must never silently mean something else.
            if (verdict.Kind == FeedVerdictKinds.NotInterested)
            {
                notInterested.Add(verdict.ReleaseId);
            }
            else if (verdict.Kind == FeedVerdictKinds.Snoozed)
            {
                snoozed.Add(verdict.ReleaseId);
            }
        }

        // ── The recently-surfaced window, excluding today ──────────────────
        // Today's own surfacings MUST stay out of the set: the feed is stable
        // within a day by design (the shuffle seed is the date), and a set
        // that included this morning's picks would penalise them on the
        // afternoon's refresh — dealing the new hand the day-seed exists to
        // prevent. So the window is the SurfacedWindowDays days strictly
        // before today: shown yesterday counts, shown an hour ago does not.
        var today = DateOnly.FromDateTime(asOfUtc);
        var since = today.AddDays(-tuning.SurfacedWindowDays);
        var recentlySurfaced = new HashSet<long>();
        foreach (var surfacing in await feedback.GetSurfacedSinceAsync(since, ct))
        {
            if (surfacing.SurfacedOn < today)
            {
                recentlySurfaced.Add(surfacing.ReleaseId);
            }
        }

        var endorsed = new HashSet<long>();
        foreach (var endorsement in await feedback.GetEndorsementsAsync(
                     tuning.EndorsementWindowDays, ct))
        {
            endorsed.Add(endorsement.ReleaseId);
        }

        return new FeedbackSets
        {
            NotInterestedReleaseIds = notInterested,
            SnoozedReleaseIds = snoozed,
            RecentlySurfacedReleaseIds = recentlySurfaced,
            EndorsedReleaseIds = endorsed,
        };
    }

    /// <summary>Stamps the sets onto a request, leaving everything else as the caller built it.</summary>
    public RecommendationRequest Apply(RecommendationRequest request) => request with
    {
        NotInterestedReleaseIds = NotInterestedReleaseIds,
        SnoozedReleaseIds = SnoozedReleaseIds,
        RecentlySurfacedReleaseIds = RecentlySurfacedReleaseIds,
        EndorsedReleaseIds = EndorsedReleaseIds,
    };

    /// <summary>
    /// The surfacing rows one computed shelf feed should append to the log —
    /// every item of every shelf, stamped with the feed's own day. The caller
    /// records these via <see cref="IFeedFeedbackRepository.RecordSurfacedAsync"/>
    /// right after rendering; the (release, day) primary key makes a same-day
    /// repeat a no-op.
    /// </summary>
    public static IReadOnlyList<FeedSurfacing> SurfacingsOf(ShelfFeed feed, DateTime asOfUtc)
    {
        var day = DateOnly.FromDateTime(asOfUtc);
        var rows = new List<FeedSurfacing>();
        foreach (var shelf in feed.Shelves)
        {
            foreach (var item in shelf.Items)
            {
                rows.Add(new FeedSurfacing
                {
                    ReleaseId = item.ReleaseId,
                    SurfacedOn = day,
                    ShelfId = shelf.Id,
                });
            }
        }

        return rows;
    }
}
