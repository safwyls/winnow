using Winnow.Core.Domain;

namespace Winnow.Core.Repositories;

/// <summary>
/// Storage for feed verdicts (dismiss/snooze/undo), surfacing log, and
/// launch-endorsement joins. Written by the App layer; read by the recommender.
/// </summary>
public interface IFeedFeedbackRepository
{
    /// <summary>Appends a verdict (Id ignored) and returns the assigned id.</summary>
    Task<long> RecordVerdictAsync(FeedVerdict verdict, CancellationToken ct = default);

    /// <summary>Revokes active verdicts of a given kind on a release by stamping RevokedAt. Returns rows revoked.</summary>
    Task<int> RevokeVerdictsAsync(
        long releaseId, string kind, DateTime revokedAtUtc, CancellationToken ct = default);

    /// <summary>Verdicts active at the given instant (not revoked, not expired). Computed at read time.</summary>
    Task<IReadOnlyList<FeedVerdict>> GetActiveVerdictsAsync(
        DateTime asOfUtc, CancellationToken ct = default);

    /// <summary>All verdicts ever given (including revoked/lapsed), newest first.</summary>
    Task<IReadOnlyList<FeedVerdict>> GetAllVerdictsAsync(CancellationToken ct = default);

    /// <summary>Records what the feed surfaced today. Idempotent per (release, day).</summary>
    Task RecordSurfacedAsync(
        IReadOnlyList<FeedSurfacing> surfacings, CancellationToken ct = default);

    /// <summary>Surfacings on or after the given day, oldest first.</summary>
    Task<IReadOnlyList<FeedSurfacing>> GetSurfacedSinceAsync(
        DateOnly since, CancellationToken ct = default);

    /// <summary>
    /// Derived launch-endorsement join: sessions attributed to a launch within
    /// <paramref name="windowDays"/> of a surfacing. One row per qualifying session.
    /// </summary>
    Task<IReadOnlyList<FeedEndorsement>> GetEndorsementsAsync(
        int windowDays, CancellationToken ct = default);
}
