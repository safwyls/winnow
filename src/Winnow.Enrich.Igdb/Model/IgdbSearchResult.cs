namespace Winnow.Enrich.Igdb.Model;

/// <summary>
/// One candidate in a search-by-title result list: name, cover, first
/// release year and platforms — exactly the four facts that distinguish
/// Prey (2006, Xbox 360) from Prey (2017, PS4/PC), which is the failure
/// this whole capability exists to fix. <see cref="Platforms"/> is empty
/// rather than null when IGDB named none.
/// </summary>
public sealed record IgdbSearchResult(
    long IgdbId,
    string Name,
    string? CoverUrl,
    int? FirstReleaseYear,
    IReadOnlyList<string> Platforms);
