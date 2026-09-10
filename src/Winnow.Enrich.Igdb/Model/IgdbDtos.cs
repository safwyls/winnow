using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Core.Domain;

namespace Winnow.Enrich.Igdb.Model;

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

    /// <summary>
    /// Shapes a <c>platforms</c> expansion into display names: trimmed, blanks
    /// dropped, duplicates collapsed case-insensitively.
    ///
    /// <para>Shared by <see cref="IgdbGameDto"/> and
    /// <see cref="IgdbSearchGameDto"/> rather than written out in each. The
    /// wrong-game control draws a row matched by IGDB id beside rows found by
    /// title search, so two copies of this shaping would be two answers about
    /// one game.</para>
    /// </summary>
    internal static IReadOnlyList<string> PlatformNames(IReadOnlyList<IgdbNamedDto>? platforms)
        => platforms?
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? IgdbGame.NoStrings;

    /// <summary>
    /// Shapes an image array (<c>screenshots</c> or <c>artworks</c>) into
    /// validated ids: trimmed, blanks dropped, anything that is not an IGDB
    /// image id rejected, duplicates collapsed ordinally. IGDB's own order
    /// is preserved — the strip is drawn in the order IGDB returns, which is
    /// the order the publisher chose. Ordinal rather than case-insensitive
    /// because IGDB image ids are lowercase alphanumeric and two ids
    /// differing only in case would be two different assets.
    /// </summary>
    internal static IReadOnlyList<string> ImageIds(IReadOnlyList<IgdbImageDto>? images)
        => images?
            .Where(i => !string.IsNullOrWhiteSpace(i.ImageId))
            .Select(i => i.ImageId!.Trim())
            .Where(IsImageId)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? IgdbGame.NoStrings;

    internal static IReadOnlyList<GameImage> Images(IReadOnlyList<IgdbImageDto>? images)
        => images?
            .Where(i => !string.IsNullOrWhiteSpace(i.ImageId) && IsImageId(i.ImageId.Trim()))
            .DistinctBy(i => i.ImageId!.Trim(), StringComparer.Ordinal)
            .Select(i => new GameImage
            {
                ImageId = i.ImageId!.Trim(),
                Width = i.Width is > 0 ? i.Width : null,
                Height = i.Height is > 0 ? i.Height : null,
                AlphaChannel = i.AlphaChannel,
                Animated = i.Animated,
                ImageType = string.IsNullOrWhiteSpace(i.ImageType?.Name) ? null : i.ImageType.Name.Trim(),
            }).ToArray() ?? [];

    /// <summary>
    /// ASCII alphanumeric, 1–64 characters. Strict for the same reason
    /// <c>IgdbImageUrl.ImageId</c> is strict: an id that fails this check
    /// would become a 404 from the CDN, and a 404 becomes a 30-day
    /// negative marker in the cover cache.
    /// </summary>
    internal static bool IsImageId(string value)
    {
        if (value.Length is 0 or > 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a rating score. IGDB sends 0 for "no rating" rather than
    /// omitting the field, so zero is read as absence. That makes "a game
    /// with no rating data shows nothing rather than a zero" a property of
    /// the data instead of something the view has to remember.
    /// </summary>
    internal static double? Score(double? value)
        => value is > 0 and <= 100 ? value : null;

    /// <summary>
    /// Reads a rating count. Zero means "nobody has rated it" and is read
    /// as absence, same discipline as <see cref="Score"/>.
    /// </summary>
    internal static int? Count(int? value)
        => value is > 0 ? value : null;

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

/// <summary>
/// The shared wire shape of a <c>screenshots</c> or <c>artworks</c> row.
/// Artwork also carries an expanded <c>image_type</c> name.
/// </summary>
internal sealed class IgdbImageDto
{
    public long Id { get; init; }

    public string? ImageId { get; init; }

    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool? AlphaChannel { get; init; }
    public bool? Animated { get; init; }

    [JsonConverter(typeof(ExpandableNamedConverter))]
    public IgdbNamedDto? ImageType { get; init; }
}

internal sealed class IgdbInvolvedCompanyDto
{
    public bool Publisher { get; init; }

    public bool Developer { get; init; }

    public IgdbNamedDto? Company { get; init; }
}

/// <summary>
/// One <c>game_types</c> row. Its label field is <c>type</c>, not
/// <c>name</c>; this is the one place IGDB breaks its own naming convention,
/// and the reason the query asks for <c>game_type.type</c> rather than
/// <c>game_type.name</c>. Asking for <c>name</c> returns empty rather than
/// failing loudly.
/// </summary>
internal sealed class IgdbGameTypeDto
{
    public long Id { get; init; }

    public string? Type { get; init; }
}

/// <summary>
/// Reads an Apicalypse reference field that may arrive as a bare id (when
/// unexpanded) or as an object with an <c>id</c> property (when expanded),
/// and yields the id either way. Both shapes must be handled because a body
/// that threw on the wrong one would take a whole batch down with it.
/// </summary>
internal sealed class ReferenceIdConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.GetInt64();
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartObject:
                using (var document = JsonDocument.ParseValue(ref reader))
                {
                    return document.RootElement.TryGetProperty("id", out var id)
                           && id.ValueKind == JsonValueKind.Number
                           && id.TryGetInt64(out var value)
                        ? value
                        : null;
                }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}

/// <summary>
/// Reads <c>game_type</c>, which arrives as an expanded object under the
/// shipped query and would arrive as a bare id if the expansion were ever
/// dropped. Both shapes are handled for the same reason
/// <see cref="ReferenceIdConverter"/> handles both: a deserialization failure
/// takes the whole batch down.
/// </summary>
internal sealed class ExpandableGameTypeConverter : JsonConverter<IgdbGameTypeDto?>
{
    public override IgdbGameTypeDto? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => new IgdbGameTypeDto { Id = reader.GetInt64() },
            JsonTokenType.Null => null,
            _ => JsonSerializer.Deserialize<IgdbGameTypeDto>(ref reader, IgdbJson.Options),
        };

    public override void Write(Utf8JsonWriter writer, IgdbGameTypeDto? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, IgdbJson.Options);
}

/// <summary>
/// One <c>age_rating_categories</c> row. Its label field is <c>rating</c>
/// ("The rating name"), which is a text string — not an enum and not the
/// same field as <see cref="IgdbAgeRatingDto.Rating"/>.
/// </summary>
internal sealed class IgdbAgeRatingCategoryDto
{
    public long Id { get; init; }

    public string? Rating { get; init; }
}

/// <summary>
/// Reads <c>organization</c>, which arrives as an expanded object under the
/// shipped query and would arrive as a bare id if the expansion were ever
/// dropped. Both shapes are handled so a deserialization failure cannot take
/// a whole batch down.
/// </summary>
internal sealed class ExpandableNamedConverter : JsonConverter<IgdbNamedDto?>
{
    public override IgdbNamedDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => new IgdbNamedDto { Id = reader.GetInt64() },
            JsonTokenType.Null => null,
            _ => JsonSerializer.Deserialize<IgdbNamedDto>(ref reader, IgdbJson.Options),
        };

    public override void Write(Utf8JsonWriter writer, IgdbNamedDto? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, IgdbJson.Options);
}

/// <summary>
/// Reads <c>rating_category</c>, which arrives as an expanded object under the
/// shipped query and would arrive as a bare id if the expansion were ever
/// dropped. Both shapes are handled for the same reason
/// <see cref="ExpandableNamedConverter"/> handles both.
/// </summary>
internal sealed class ExpandableAgeRatingCategoryConverter : JsonConverter<IgdbAgeRatingCategoryDto?>
{
    public override IgdbAgeRatingCategoryDto? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => new IgdbAgeRatingCategoryDto { Id = reader.GetInt64() },
            JsonTokenType.Null => null,
            _ => JsonSerializer.Deserialize<IgdbAgeRatingCategoryDto>(ref reader, IgdbJson.Options),
        };

    public override void Write(
        Utf8JsonWriter writer, IgdbAgeRatingCategoryDto? value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, IgdbJson.Options);
}

/// <summary>
/// One <c>age_ratings</c> row on a game. Carries both IGDB's deprecated numeric
/// enums (<see cref="Category"/>, <see cref="Rating"/>) and the current
/// reference fields (<see cref="Organization"/>, <see cref="RatingCategory"/>).
/// </summary>
internal sealed class IgdbAgeRatingDto
{
    public long Id { get; init; }

    /// <summary>
    /// IGDB's deprecated board enum (1 ESRB, 2 PEGI, 3 CERO, 4 USK, 5 GRAC,
    /// 6 CLASS_IND, 7 ACB). Kept because it is the only rating-board numbering
    /// IGDB publishes a value table for.
    /// </summary>
    public int? Category { get; init; }

    /// <summary>
    /// IGDB's deprecated rating enum, values 1-39. The board is implied by
    /// the value, so <see cref="Category"/> is not needed to read it. This is
    /// the primary reading when present.
    /// </summary>
    public int? Rating { get; init; }

    [JsonConverter(typeof(ExpandableNamedConverter))]
    public IgdbNamedDto? Organization { get; init; }

    [JsonConverter(typeof(ExpandableAgeRatingCategoryConverter))]
    public IgdbAgeRatingCategoryDto? RatingCategory { get; init; }
}

/// <summary>
/// The wire shape of one game from the age-ratings query. Carries only
/// the <c>age_ratings</c> expansion — no name, no cover, no genres — because
/// this query is deliberately separate from <see cref="Apicalypse.Games"/>.
/// </summary>
internal sealed class IgdbAgeRatingsGameDto
{
    public long Id { get; init; }

    public IReadOnlyList<IgdbAgeRatingDto>? AgeRatings { get; init; }
}

/// <summary>
/// The wire shape of one game from the search query, deliberately
/// separate from <see cref="IgdbGameDto"/> for the same reason
/// <see cref="IgdbAgeRatingsGameDto"/> is: a separate query gets a
/// separate wire shape. A row with no id or no name yields null from
/// <see cref="ToDomain"/> and is dropped rather than becoming a nameless
/// candidate.
/// </summary>
internal sealed class IgdbSearchGameDto
{
    public long Id { get; init; }

    public string? Name { get; init; }

    public long? FirstReleaseDate { get; init; }

    public IgdbCoverDto? Cover { get; init; }

    public IReadOnlyList<IgdbNamedDto>? Platforms { get; init; }

    internal IgdbSearchResult? ToDomain()
        => Id <= 0 || string.IsNullOrWhiteSpace(Name)
            ? null
            : new IgdbSearchResult(
                Id,
                Name.Trim(),
                IgdbJson.CoverUrl(Cover),
                IgdbJson.ReleaseYear(FirstReleaseDate),
                IgdbJson.PlatformNames(Platforms));
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

    public IReadOnlyList<IgdbNamedDto>? GameModes { get; init; }

    public IReadOnlyList<IgdbNamedDto>? PlayerPerspectives { get; init; }

    /// <summary>
    /// <c>platforms</c>, expanded to names. Only the wrong-game control's
    /// candidate rows read these; they are on the shared query because an
    /// id-matched row must show what a title-search row shows.
    /// </summary>
    public IReadOnlyList<IgdbNamedDto>? Platforms { get; init; }

    public IReadOnlyList<IgdbInvolvedCompanyDto>? InvolvedCompanies { get; init; }

    /// <summary>
    /// <c>game_type</c>, the replacement for the deprecated <c>category</c>
    /// field. Fifteen values today: main_game, dlc_addon, expansion, bundle,
    /// standalone_expansion, mod, episode, season, remake, remaster,
    /// expanded_game, port, fork, pack, update. More will be added.
    /// </summary>
    [JsonConverter(typeof(ExpandableGameTypeConverter))]
    public IgdbGameTypeDto? GameType { get; init; }

    /// <summary>
    /// <c>parent_game</c>: the main game when this entry is DLC, an expansion,
    /// or part of a bundle. Arrives as a bare id under the shipped query.
    /// </summary>
    [JsonConverter(typeof(ReferenceIdConverter))]
    public long? ParentGame { get; init; }

    /// <summary>
    /// <c>version_parent</c>: the game this is a version of. A remaster, remake
    /// or port names its original through this field. Arrives as a bare id
    /// under the shipped query.
    /// </summary>
    [JsonConverter(typeof(ReferenceIdConverter))]
    public long? VersionParent { get; init; }

    /// <summary><c>version_title</c>, e.g. "Game of the Year Edition". Present only on a version entry.</summary>
    public string? VersionTitle { get; init; }

    /// <summary><c>screenshots</c>, the publisher-ordered image array distinct from cover art.</summary>
    public IReadOnlyList<IgdbImageDto>? Screenshots { get; init; }

    /// <summary><c>artworks</c>, promotional art distinct from both covers and screenshots.</summary>
    public IReadOnlyList<IgdbImageDto>? Artworks { get; init; }

    /// <summary>IGDB's own user-body rating, 0–100. Zero means "no rating" and is read as null by <see cref="IgdbJson.Score"/>.</summary>
    public double? Rating { get; init; }

    /// <summary>How many IGDB users rated this game. Zero means "nobody" and is read as null by <see cref="IgdbJson.Count"/>.</summary>
    public int? RatingCount { get; init; }

    /// <summary>IGDB's aggregation of external critics, 0–100. Zero means "no data" and is read as null by <see cref="IgdbJson.Score"/>.</summary>
    public double? AggregatedRating { get; init; }

    /// <summary>How many external critic sources IGDB aggregated. Zero means "none" and is read as null by <see cref="IgdbJson.Count"/>.</summary>
    public int? AggregatedRatingCount { get; init; }

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
            .ToArray() ?? IgdbGame.NoStrings)
    {
        // `this.` is not decoration: inside an object initializer the bare name
        // would still resolve to the DTO's property, but reading it back six
        // months from now should not require knowing that.
        GameModes = Names(this.GameModes),
        PlayerPerspectives = Names(this.PlayerPerspectives),
        Platforms = IgdbJson.PlatformNames(this.Platforms),
        GameType = string.IsNullOrWhiteSpace(this.GameType?.Type) ? null : this.GameType.Type,
        ParentGameId = ParentGame is > 0 ? ParentGame : null,
        VersionParentId = VersionParent is > 0 ? VersionParent : null,
        VersionTitle = string.IsNullOrWhiteSpace(this.VersionTitle) ? null : this.VersionTitle,
        ScreenshotImageIds = IgdbJson.ImageIds(this.Screenshots),
        ArtworkImageIds = IgdbJson.ImageIds(this.Artworks),
        ScreenshotImages = IgdbJson.Images(this.Screenshots),
        ArtworkImages = IgdbJson.Images(this.Artworks),
        UserRating = IgdbJson.Score(this.Rating),
        UserRatingCount = IgdbJson.Count(this.RatingCount),
        CriticRating = IgdbJson.Score(this.AggregatedRating),
        CriticRatingCount = IgdbJson.Count(this.AggregatedRatingCount),
    };

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
