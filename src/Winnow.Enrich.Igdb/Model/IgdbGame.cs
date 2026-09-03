namespace Winnow.Enrich.Igdb.Model;

/// <summary>
/// An IGDB game as Winnow cares about it. Maps onto <c>works</c> (§6): id, name,
/// first release year, summary, cover url — plus the weak genre/theme signals
/// §4.3 flags as the fallback when Steam user tags are unavailable.
/// </summary>
/// <param name="IgdbId">IGDB's game id — the value stored in <c>works.igdb_id</c>.</param>
/// <param name="Name">Canonical name. Replaces a provisional <c>App &lt;appid&gt;</c> title.</param>
/// <param name="CoverUrl">Absolute https cover url, already upsized past IGDB's thumbnail default.</param>
/// <param name="FirstReleaseYear">Year of first release across all platforms, or null when IGDB has no date.</param>
/// <param name="Summary">IGDB's prose summary.</param>
/// <param name="Genres">IGDB genre names.</param>
/// <param name="Themes">IGDB theme names.</param>
/// <param name="Publishers">Companies flagged as publisher on the game's involved_companies rows.</param>
public sealed record IgdbGame(
    long IgdbId,
    string Name,
    string? CoverUrl,
    int? FirstReleaseYear,
    string? Summary,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Themes,
    IReadOnlyList<string> Publishers)
{
    public static readonly IReadOnlyList<string> NoStrings = Array.Empty<string>();

    /// <summary>
    /// IGDB <c>game_modes</c> names. Init property (not positional) so
    /// pre-existing cached entries still deserialize.
    /// </summary>
    public IReadOnlyList<string> GameModes { get; init; } = NoStrings;

    /// <summary>
    /// IGDB <c>player_perspectives</c> names — "First person", "Bird view /
    /// Isometric". Same free ride on the same request, and the same
    /// backwards-compatibility rule as <see cref="GameModes"/>.
    /// </summary>
    public IReadOnlyList<string> PlayerPerspectives { get; init; } = NoStrings;

    /// <summary>
    /// The <c>game_types.type</c> label. Fifteen values today: main_game,
    /// dlc_addon, expansion, bundle, standalone_expansion, mod, episode, season,
    /// remake, remaster, expanded_game, port, fork, pack, update. Null when
    /// IGDB gave none, which under the current query means the cached payload
    /// predates the field (payload version 1).
    /// </summary>
    public string? GameType { get; init; }

    /// <summary>IGDB <c>parent_game</c> id. The main game when this is DLC, an expansion, or part of a bundle.</summary>
    public long? ParentGameId { get; init; }

    /// <summary>IGDB <c>version_parent</c> id. The original game when this is a remaster, remake or port.</summary>
    public long? VersionParentId { get; init; }

    /// <summary>IGDB <c>version_title</c>, e.g. "Game of the Year Edition". Null when IGDB gave none.</summary>
    public string? VersionTitle { get; init; }

    /// <summary>
    /// The single parent IGDB names for this game, whichever field carried it.
    /// <see cref="ParentGameId"/> wins over <see cref="VersionParentId"/> when
    /// both are present, because an edition of an expansion belongs under the
    /// expansion, not under the game the expansion extends.
    /// </summary>
    public long? RelationParentId => ParentGameId ?? VersionParentId;
}

/// <summary>
/// The result of the <c>external_games</c> hard join: one store id bound to one
/// IGDB game, with display fields from the same request.
/// </summary>
/// <param name="Uid">The store id IGDB published this row against.</param>
public sealed record IgdbExternalMatch(
    string Uid,
    long IgdbId,
    string? Name,
    string? CoverUrl,
    int? FirstReleaseYear,
    string? Summary);
