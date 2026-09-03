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
    /// <summary>
    /// Valve's numeric <c>StoreItem.type</c> (migration 0022). Init property
    /// rather than a positional parameter so every existing construction site
    /// keeps compiling.
    /// </summary>
    public int? SteamStoreType { get; init; }

    /// <summary><c>related_items.parent_appid</c>, or PICS <c>common.parent</c> (migration 0022).</summary>
    public string? SteamParentAppId { get; init; }

    /// <summary>The IGDB <c>game_type.type</c> label (migration 0022).</summary>
    public string? IgdbGameType { get; init; }

    /// <summary>IGDB <c>parent_game</c> (migration 0022).</summary>
    public long? IgdbParentId { get; init; }

    /// <summary>IGDB <c>version_parent</c> (migration 0022).</summary>
    public long? IgdbVersionParentId { get; init; }

    /// <summary>True when every field is null/blank, so there is nothing to write.</summary>
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name)
        && IgdbId is null
        && FirstReleaseYear is null
        && string.IsNullOrWhiteSpace(Summary)
        && string.IsNullOrWhiteSpace(CoverUrl)
        && string.IsNullOrWhiteSpace(Publisher)
        && string.IsNullOrWhiteSpace(SteamAppType)
        && string.IsNullOrWhiteSpace(EpicCategories)
        && SteamStoreType is null
        && string.IsNullOrWhiteSpace(SteamParentAppId)
        && string.IsNullOrWhiteSpace(IgdbGameType)
        && IgdbParentId is null
        && IgdbVersionParentId is null;
}
