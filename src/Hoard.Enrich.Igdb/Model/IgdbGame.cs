namespace Hoard.Enrich.Igdb.Model;

/// <summary>
/// An IGDB game as Hoard cares about it. Maps onto <c>works</c> (§6): id, name,
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
    /// IGDB <c>game_modes</c> names — "Single player", "Co-operative",
    /// "Massively Multiplayer Online (MMO)". Folded onto Hoard's own vocabulary
    /// by <c>GameModes.FromIgdbName</c>, because Steam answers the same question
    /// with category ids and the filter has to ask it once.
    ///
    /// <para><b>An init property rather than a positional parameter, on
    /// purpose.</b> This record IS the cached payload shape — the IGDB cache
    /// stores the projection, not the raw response — so every one of the 865
    /// entries already on the author's disk predates this field. An absent
    /// property keeps its initializer, so those entries still deserialize and
    /// still carry their genres; a new positional parameter would have made them
    /// unreadable and silently thrown away metadata a machine without Twitch
    /// credentials could never fetch again.</para>
    ///
    /// <para>Consequently this is empty on every already-cached game until its
    /// entry expires and is re-fetched. That is the correct trade: Steam's player
    /// categories already answer the same question for the same games, from a
    /// cache that is also already on disk.</para>
    /// </summary>
    public IReadOnlyList<string> GameModes { get; init; } = NoStrings;

    /// <summary>
    /// IGDB <c>player_perspectives</c> names — "First person", "Bird view /
    /// Isometric". Same free ride on the same request, and the same
    /// backwards-compatibility rule as <see cref="GameModes"/>.
    /// </summary>
    public IReadOnlyList<string> PlayerPerspectives { get; init; } = NoStrings;
}

/// <summary>
/// The result of the <c>external_games</c> hard join: one store id bound to one
/// IGDB game, with the display fields the same request already returned.
///
/// <para>§5.3 treats this as a hard join — it is an identifier match published
/// by IGDB itself, not a name similarity, so Resolve may auto-merge on it.</para>
///
/// <para><b>Was <c>IgdbSteamMatch</c>, with a <c>SteamAppId</c>.</b> The name
/// was accurate and that was the problem: the type described a query that could
/// only ever be asked about Steam, and it sat at the end of a chain — one
/// provider in the repository query, one source id in the Apicalypse builder —
/// that left every Epic and GOG row in the library with no metadata at all. The
/// <c>uid</c> here is whatever id the <c>external_game_source</c> it was fetched
/// under indexes: a Steam appid under source 1, a GOG product id under source 5.
/// Which source produced it is the caller's context, not a field, because a
/// match is only ever handed back to the batch that asked for it.</para>
/// </summary>
/// <param name="Uid">The store id IGDB published this row against.</param>
public sealed record IgdbExternalMatch(
    string Uid,
    long IgdbId,
    string? Name,
    string? CoverUrl,
    int? FirstReleaseYear,
    string? Summary);
