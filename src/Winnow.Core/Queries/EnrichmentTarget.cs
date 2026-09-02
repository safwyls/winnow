namespace Winnow.Core.Queries;

/// <summary>
/// A work that still needs enrichment, paired with its external lookup id and
/// flags for which metadata columns are already populated.
/// Uses init properties (not positional params) for Dapper Int64 compatibility.
/// </summary>
public sealed record EnrichmentTarget
{
    /// <summary>The work to fill in.</summary>
    public required long WorkId { get; init; }

    /// <summary>The paired release. Name promotion updates both work and release together.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>External id provider, e.g. <c>steam</c>.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's id, e.g. the Steam appid.</summary>
    public required string ProviderId { get; init; }

    /// <summary>True when the stored name is a machine-minted placeholder that may be renamed.</summary>
    public bool NameIsProvisional { get; init; }

    /// <summary>Whether <c>works.igdb_id</c> is already set.</summary>
    public bool HasIgdbId { get; init; }

    /// <summary>Whether <c>works.first_release_year</c> is already set.</summary>
    public bool HasFirstReleaseYear { get; init; }

    /// <summary>Whether <c>works.summary</c> is already set.</summary>
    public bool HasSummary { get; init; }

    /// <summary>Whether <c>works.cover_url</c> is already set.</summary>
    public bool HasCoverUrl { get; init; }

    /// <summary>Whether <c>works.publisher</c> is already set (migration 0005).</summary>
    public bool HasPublisher { get; init; }

    /// <summary>Whether <c>works.steam_app_type</c> is already set (migration 0006).</summary>
    public bool HasSteamAppType { get; init; }

    /// <summary>Whether <c>works.epic_categories</c> is already set (migration 0009).</summary>
    public bool HasEpicCategories { get; init; }

    /// <summary>
    /// Whether <c>works.igdb_game_type</c> is already set (migration 0022).
    /// False on every work enriched before the relation fields were requested,
    /// which is what brings them back for one more pass.
    /// </summary>
    public bool HasIgdbGameType { get; init; }

    /// <summary>The stored title (release name, falling back to work name).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>True when only metadata is outstanding (name is already real).</summary>
    public bool IsMetadataOnly => !NameIsProvisional;

    /// <summary>
    /// How many of the five visible metadata columns are still missing (0-5).
    /// Mirrors the query's ORDER BY (emptiest-first). Excludes <c>steam_app_type</c>.
    /// </summary>
    public int MissingColumns
        => (HasIgdbId ? 0 : 1)
         + (HasFirstReleaseYear ? 0 : 1)
         + (HasSummary ? 0 : 1)
         + (HasCoverUrl ? 0 : 1)
         + (HasPublisher ? 0 : 1);
}
