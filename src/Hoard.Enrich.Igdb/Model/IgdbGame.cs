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
}

/// <summary>
/// The result of the <c>external_games</c> hard join: one Steam appid bound to
/// one IGDB game, with the display fields the same request already returned.
///
/// <para>§5.3 treats this as a hard join — it is an identifier match published
/// by IGDB itself, not a name similarity, so Resolve may auto-merge on it.</para>
/// </summary>
public sealed record IgdbSteamMatch(
    string SteamAppId,
    long IgdbId,
    string? Name,
    string? CoverUrl,
    int? FirstReleaseYear,
    string? Summary);
