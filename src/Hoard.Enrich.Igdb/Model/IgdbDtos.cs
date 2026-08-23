using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoard.Enrich.Igdb.Model;

/// <summary>
/// Wire shapes for the two endpoints this client uses. Deliberately separate
/// from <see cref="IgdbGame"/>: the wire model is IGDB's to change, the domain
/// model is ours.
/// </summary>
internal static class IgdbJson
{
    /// <summary>
    /// IGDB fields are snake_case. <see cref="JsonNamingPolicy.SnakeCaseLower"/>
    /// covers all of them, so no property needs an explicit attribute.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Builds a display cover url from whatever the response carried.
    ///
    /// <para>IGDB returns <c>cover.url</c> already sized <c>t_thumb</c> and
    /// protocol-relative (<c>//images.igdb.com/…</c>), which is unusable in a UI
    /// as-is. <c>image_id</c> is the durable handle, so when present the url is
    /// rebuilt at <c>t_cover_big</c>; otherwise the returned url is patched.</para>
    /// </summary>
    internal static string? CoverUrl(IgdbCoverDto? cover)
    {
        if (cover is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(cover.ImageId))
        {
            return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{cover.ImageId}.jpg";
        }

        if (string.IsNullOrWhiteSpace(cover.Url))
        {
            return null;
        }

        var url = cover.Url.StartsWith("//", StringComparison.Ordinal) ? "https:" + cover.Url : cover.Url;
        return url.Replace("/t_thumb/", "/t_cover_big/", StringComparison.Ordinal);
    }

    /// <summary>first_release_date is Unix seconds, UTC. Null and 0 both mean "unknown".</summary>
    internal static int? ReleaseYear(long? firstReleaseDate)
        => firstReleaseDate is null or 0
            ? null
            : DateTimeOffset.FromUnixTimeSeconds(firstReleaseDate.Value).UtcDateTime.Year;
}

internal sealed class IgdbCoverDto
{
    public string? ImageId { get; init; }

    public string? Url { get; init; }
}

internal sealed class IgdbNamedDto
{
    public long Id { get; init; }

    public string? Name { get; init; }
}

internal sealed class IgdbInvolvedCompanyDto
{
    public bool Publisher { get; init; }

    public bool Developer { get; init; }

    public IgdbNamedDto? Company { get; init; }
}

internal sealed class IgdbGameDto
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public string? Summary { get; init; }

    public long? FirstReleaseDate { get; init; }

    public IgdbCoverDto? Cover { get; init; }

    public IReadOnlyList<IgdbNamedDto>? Genres { get; init; }

    public IReadOnlyList<IgdbNamedDto>? Themes { get; init; }

    public IReadOnlyList<IgdbInvolvedCompanyDto>? InvolvedCompanies { get; init; }

    internal IgdbGame ToDomain() => new(
        Id,
        Name ?? string.Empty,
        IgdbJson.CoverUrl(Cover),
        IgdbJson.ReleaseYear(FirstReleaseDate),
        Summary,
        Names(Genres),
        Names(Themes),
        InvolvedCompanies?
            .Where(c => c.Publisher && !string.IsNullOrWhiteSpace(c.Company?.Name))
            .Select(c => c.Company!.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? IgdbGame.NoStrings);

    private static IReadOnlyList<string> Names(IReadOnlyList<IgdbNamedDto>? items)
        => items?
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => i.Name!)
            .ToArray() ?? IgdbGame.NoStrings;
}

/// <summary>
/// One <c>external_games</c> row. <c>game</c> arrives as a nested object because
/// the query expands <c>game.name</c> and friends; when only the scalar id comes
/// back the row is unusable and dropped.
/// </summary>
internal sealed class IgdbExternalGameDto
{
    public long Id { get; init; }

    public string? Uid { get; init; }

    [JsonConverter(typeof(ExpandableGameConverter))]
    public IgdbGameDto? Game { get; init; }

    /// <summary>
    /// Apicalypse returns a reference field as a bare id when it is not
    /// expanded and as an object when it is. The query always expands, but a
    /// bare number must not throw and take the whole batch down with it — an
    /// id-only row simply has no display fields.
    /// </summary>
    private sealed class ExpandableGameConverter : JsonConverter<IgdbGameDto?>
    {
        public override IgdbGameDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType switch
            {
                JsonTokenType.Number => new IgdbGameDto { Id = reader.GetInt64() },
                JsonTokenType.Null => null,
                _ => JsonSerializer.Deserialize<IgdbGameDto>(ref reader, IgdbJson.Options),
            };

        public override void Write(Utf8JsonWriter writer, IgdbGameDto? value, JsonSerializerOptions options)
            => JsonSerializer.Serialize(writer, value, IgdbJson.Options);
    }
}
