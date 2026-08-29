namespace Winnow.Core.Queries;

/// <summary>
/// Metadata for one work from an enrichment source. Each nullable field means
/// "the source said nothing" when null. Applied via one-way promotion
/// (<see cref="Repositories.IWorkRepository.ApplyEnrichmentAsync"/>).
/// </summary>
/// <param name="WorkId">The work to fill in.</param>
/// <param name="Name">Canonical title, or null. Only applied while the stored name is provisional.</param>
/// <param name="IgdbId">IGDB game id. Only applied when the work has none.</param>
/// <param name="FirstReleaseYear">Feeds the matcher's +/-1-year signal.</param>
/// <param name="Summary">IGDB prose summary.</param>
/// <param name="CoverUrl">Absolute cover URL.</param>
/// <param name="Publisher">Primary publisher name (migration 0005).</param>
/// <param name="SteamAppType">Valve's <c>common.type</c> verbatim (migration 0006).</param>
/// <param name="EpicCategories">Epic's comma-joined <c>categories[].path</c> list (migration 0009).</param>
public sealed record WorkEnrichment(
    long WorkId,
    string? Name = null,
    long? IgdbId = null,
    int? FirstReleaseYear = null,
    string? Summary = null,
    string? CoverUrl = null,
    string? Publisher = null,
    string? SteamAppType = null,
    string? EpicCategories = null)
{
    /// <summary>True when every field is null/blank, so there is nothing to write.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name)
        && IgdbId is null
        && FirstReleaseYear is null
        && string.IsNullOrWhiteSpace(Summary)
        && string.IsNullOrWhiteSpace(CoverUrl)
        && string.IsNullOrWhiteSpace(Publisher)
        && string.IsNullOrWhiteSpace(SteamAppType)
        && string.IsNullOrWhiteSpace(EpicCategories);
}
