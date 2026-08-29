using Winnow.Enrich.GamesDb.Model;

namespace Winnow.Enrich.GamesDb;

/// <summary>
/// Cross-store identity graph: resolves a store id to the same game on other
/// platforms, by exact identifier rather than title.
/// </summary>
public interface IGameIdentityGraph
{
    /// <summary>
    /// Resolves one store id, or returns null when the graph has no release
    /// under it or cannot be reached.
    /// </summary>
    /// <param name="platform">A <see cref="GamesDbPlatforms"/> value.</param>
    /// <param name="externalId">
    /// That platform's id. For Epic this is the manifest's <c>AppName</c>, not
    /// the catalog item id — the catalog item id 404s.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    Task<GamesDbGame?> ResolveAsync(string platform, string externalId, CancellationToken ct = default);
}
