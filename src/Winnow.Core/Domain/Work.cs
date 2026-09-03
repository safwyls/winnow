namespace Winnow.Core.Domain;

/// <summary>
/// Canonical identity layer 1 of 4: the game as a concept ("Skyrim").
/// Never collapse <see cref="Release"/> into this — Skyrim SE is not Skyrim.
/// </summary>
public sealed record Work
{
    public long Id { get; init; }
    public long? IgdbId { get; init; }
    public required string Name { get; init; }

    /// <summary>True when <see cref="Name"/> is a machine-minted placeholder (e.g. "App 1203620"), not a real title.</summary>
    public bool NameIsProvisional { get; init; }
    public string? SortName { get; init; }
    public int? FirstReleaseYear { get; init; }
    public string? Summary { get; init; }
    public string? CoverUrl { get; init; }

    /// <summary>Primary publisher name, or null when unknown (migration 0005). One name, not a list.</summary>
    public string? Publisher { get; init; }

    /// <summary>Valve's <c>common.type</c> verbatim (migration 0006), or null when unread. Compare case-insensitively.</summary>
    public string? SteamAppType { get; init; }

    /// <summary>Epic's comma-joined <c>categories[].path</c> list (migration 0009), or null when unread. Classified via <see cref="Queries.EpicGameFilter"/>.</summary>
    public string? EpicCategories { get; init; }

    /// <summary>Valve's numeric <c>StoreItem.type</c> (migration 0022), or null when unread. 0 is a real value meaning game.</summary>
    public int? SteamStoreType { get; init; }

    /// <summary>The appid Steam names as this app's parent (migration 0022), or null. TEXT, matching external_ids encoding.</summary>
    public string? SteamParentAppId { get; init; }

    /// <summary>The IGDB <c>game_type.type</c> label (migration 0022), or null when unread.</summary>
    public string? IgdbGameType { get; init; }

    /// <summary>IGDB <c>parent_game</c> (migration 0022), or null.</summary>
    public long? IgdbParentId { get; init; }

    /// <summary>IGDB <c>version_parent</c> (migration 0022), or null.</summary>
    public long? IgdbVersionParentId { get; init; }
}
