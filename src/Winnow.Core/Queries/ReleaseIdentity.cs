using Winnow.Core.Identity;

namespace Winnow.Core.Queries;

/// <summary>
/// Joined release + work projection for the soft matcher (§5.3 step 2).
/// One row per release, carrying the title, year, publisher, and app-type
/// metadata needed for matching and non-game filtering.
/// Uses init properties (not positional params) for Dapper Int64 compatibility.
/// </summary>
public sealed record ReleaseIdentity
{
    /// <summary><c>releases.id</c>.</summary>
    public required long ReleaseId { get; init; }

    /// <summary>Its work id. Two releases of the same work are never compared.</summary>
    public required long WorkId { get; init; }

    /// <summary><c>releases.name</c> -- the primary match target (carries the edition).</summary>
    public required string ReleaseName { get; init; }

    /// <summary>The work's title, used when the release row has nothing usable.</summary>
    public required string WorkName { get; init; }

    /// <summary><c>works.first_release_year</c>, or null. Feeds the +/-1-year signal.</summary>
    public int? FirstReleaseYear { get; init; }

    /// <summary><c>works.publisher</c>, or null. Feeds the publisher match signal.</summary>
    public string? Publisher { get; init; }

    /// <summary>True when the name is a placeholder (<c>App 1203620</c>). Excluded from matching.</summary>
    public bool NameIsProvisional { get; init; }

    /// <summary>
    /// <c>works.steam_app_type</c> — Valve's <c>common.type</c> (<c>game</c>, <c>tool</c>,
    /// <c>music</c>, …), or null when nothing has probed this app yet. Null is the norm.
    /// </summary>
    public string? SteamAppType { get; init; }

    /// <summary>
    /// <c>works.epic_categories</c> — Epic's comma-joined category paths, or null when the
    /// catalog has not been read. Null is the norm.
    /// </summary>
    public string? EpicCategories { get; init; }

    /// <summary>
    /// True when at least one ownership row points at this release. The
    /// expansion scan needs it because <see cref="DemoConsolidation"/> is
    /// defined over owned releases: a base game the user does not own cannot
    /// hide anything.
    /// </summary>
    public bool IsOwned { get; init; }

    /// <summary><c>works.steam_store_type</c> (migration 0022). Valve's numeric <c>StoreItem.type</c>.</summary>
    public int? SteamStoreType { get; init; }

    /// <summary><c>works.steam_parent_app_id</c> (migration 0022). The appid Steam names as this app's parent.</summary>
    public string? SteamParentAppId { get; init; }

    /// <summary><c>works.igdb_game_type</c> (migration 0022). The IGDB <c>game_type</c> label.</summary>
    public string? IgdbGameType { get; init; }

    /// <summary><c>works.igdb_parent_id</c> (migration 0022). IGDB <c>parent_game</c>.</summary>
    public long? IgdbParentId { get; init; }

    /// <summary><c>works.igdb_version_parent_id</c> (migration 0022). IGDB <c>version_parent</c>.</summary>
    public long? IgdbVersionParentId { get; init; }

    /// <summary><c>works.igdb_id</c>, so an IGDB parent id can be joined to a work in the library.</summary>
    public long? IgdbId { get; init; }

    /// <summary>
    /// The Steam appid this release is known by, or null for a non-Steam
    /// release. Needed to resolve a parent appid to a work.
    /// </summary>
    public string? SteamAppId { get; init; }

    /// <summary>The raw storefront facts assembled from all relation columns on this work.</summary>
    public StorefrontFacts StorefrontFacts => new()
    {
        SteamStoreType = SteamStoreType,
        SteamParentAppId = SteamParentAppId,
        SteamAppType = SteamAppType,
        IgdbGameType = IgdbGameType,
        IgdbParentId = IgdbParentId,
        IgdbVersionParentId = IgdbVersionParentId,
    };

    /// <summary>Release name, falling back to work name.</summary>
    public string MatchTitle =>
        string.IsNullOrWhiteSpace(ReleaseName) ? WorkName : ReleaseName;

    /// <summary>
    /// True when either storefront's classification says this row is not a game — a
    /// dedicated server, an engine build, a marketplace asset pack. Unclassified rows
    /// (the normal case for a library nothing has probed) are games.
    /// </summary>
    public bool IsNonGame => NonGameEntries.IsNonGame(SteamAppType, EpicCategories);
}
