using Hoard.Core.Domain;

namespace Hoard.Core.Repositories;

/// <summary>
/// Storage for the feed's feedback loop: the verdicts the user gives
/// (dismiss / snooze / undo), the log of what the feed surfaced, and the
/// derived launch-endorsement join over the two.
///
/// <para><b>Who calls what.</b> The App layer (FeedService) is the writer: it
/// records verdicts when the user clicks, revokes them when the user undoes,
/// and records surfacings after each day's first feed computation.
/// <c>Hoard.Recommend</c> only ever reads (its charter forbids writes) —
/// <c>FeedbackSets</c> turns these rows into the id sets a
/// <c>RecommendationRequest</c> carries. The engine itself never sees this
/// interface.</para>
/// </summary>
public interface IFeedFeedbackRepository
{
    /// <summary>Appends a verdict (Id ignored) and returns the assigned id.</summary>
    Task<long> RecordVerdictAsync(FeedVerdict verdict, CancellationToken ct = default);

    /// <summary>
    /// Revokes every active verdict of one kind on one release by stamping
    /// <see cref="FeedVerdict.RevokedAt"/> — the undo, as a timestamp rather
    /// than a deletion. Returns how many rows were revoked (0 when nothing was
    /// active, which is not an error: the user may undo a snooze that already
    /// lapsed).
    /// </summary>
    Task<int> RevokeVerdictsAsync(
        long releaseId, string kind, DateTime revokedAtUtc, CancellationToken ct = default);

    /// <summary>
    /// The verdicts that bind at the given instant: not revoked, and (for
    /// snoozes) not yet expired. "Active" is computed here, at read time —
    /// never stored — so a lapsed snooze re-admits its game with no write.
    /// </summary>
    Task<IReadOnlyList<FeedVerdict>> GetActiveVerdictsAsync(
        DateTime asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Every verdict ever given, revoked and lapsed included, newest first —
    /// the inspection surface. A user must be able to see the whole of what
    /// they have told the system, or the loop is the black box the charter
    /// forbids.
    /// </summary>
    Task<IReadOnlyList<FeedVerdict>> GetAllVerdictsAsync(CancellationToken ct = default);

    /// <summary>
    /// Records what the feed surfaced today. Idempotent per (release, day):
    /// re-recording after a same-day refresh is a no-op, so a refresh can
    /// never inflate the log or shift the day's feed.
    /// </summary>
    Task RecordSurfacedAsync(
        IReadOnlyList<FeedSurfacing> surfacings, CancellationToken ct = default);

    /// <summary>Surfacings on or after the given day, oldest first.</summary>
    Task<IReadOnlyList<FeedSurfacing>> GetSurfacedSinceAsync(
        DateOnly since, CancellationToken ct = default);

    /// <summary>
    /// The launch-endorsement join, computed fresh on every call (derived,
    /// never stored): sessions with <c>attributed_by = 'launch'</c> whose
    /// start date falls within <paramref name="windowDays"/> days after a
    /// surfacing of the same release. One row per qualifying session; a
    /// session surfaced on several recent days counts once, against the
    /// latest qualifying surfacing.
    /// </summary>
    Task<IReadOnlyList<FeedEndorsement>> GetEndorsementsAsync(
        int windowDays, CancellationToken ct = default);
}
