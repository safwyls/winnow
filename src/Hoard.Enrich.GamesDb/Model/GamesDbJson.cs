using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoard.Enrich.GamesDb.Model;

/// <summary>JSON settings and DTOs for the gamesdb response.</summary>
internal static class GamesDbJson
{
    /// <summary>
    /// snake_case, which is what the service emits (<c>game_id</c>,
    /// <c>platform_id</c>, <c>external_id</c>).
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>
/// The lookup response, cut down to the two facts Hoard wants.
///
/// <para>The full body is ~14 KB of titles, summaries, artwork URLs, genres and
/// popularity ranks. None of it is read: §4.4 makes IGDB the metadata backbone,
/// and a second opinion about a game's title and cover — from a service that
/// resolves games rather than editions — is exactly the kind of near-miss that
/// ends up merged into the wrong row. This is an <i>identity</i> graph here and
/// nothing else.</para>
/// </summary>
internal sealed class GamesDbLookupDto
{
    public string? GameId { get; init; }

    public GamesDbGameDto? Game { get; init; }
}

internal sealed class GamesDbGameDto
{
    public IReadOnlyList<GamesDbReleaseDto>? Releases { get; init; }
}

internal sealed class GamesDbReleaseDto
{
    public string? PlatformId { get; init; }

    public string? ExternalId { get; init; }
}
