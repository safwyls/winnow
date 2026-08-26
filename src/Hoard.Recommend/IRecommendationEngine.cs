namespace Hoard.Recommend;

/// <summary>
/// The module's one entry point: owned-but-unplayed games, ranked and
/// explained. Read-only over the library — implementations must never write,
/// and a computed feed must be droppable at any moment with no data loss
/// (charter: derived, never truth).
/// </summary>
public interface IRecommendationEngine
{
    /// <summary>
    /// Computes a fresh feed for the request. Deterministic for identical
    /// inputs: same database state, same request (seed included) — same feed.
    /// </summary>
    Task<RecommendationFeed> GetFeedAsync(RecommendationRequest request, CancellationToken ct = default);
}
