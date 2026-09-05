namespace Winnow.Core.Queries;

/// <summary>
/// The user-visible metadata fields on a work (migration 0027). Each name
/// is deliberately identical to the <c>works</c> column it maps to, which
/// is what lets <c>WorkFieldSourceRepository.ColumnFor</c>
/// map a whitelisted constant to a SQL literal instead of ever interpolating
/// a caller's string into a query.
///
/// <para>Columns deliberately absent: <c>steam_app_type</c>,
/// <c>epic_categories</c>, <c>steam_store_type</c>, <c>steam_parent_app_id</c>,
/// <c>igdb_game_type</c>, <c>igdb_parent_id</c>, <c>igdb_version_parent_id</c>
/// — facts about a store entry rather than fields the editor exposes. And
/// <c>igdb_id</c> is identity, which is the pin's question, not a
/// field's.</para>
/// </summary>
public static class WorkFields
{
    public const string Name = "name";

    public const string FirstReleaseYear = "first_release_year";

    public const string Summary = "summary";

    public const string CoverUrl = "cover_url";

    public const string Publisher = "publisher";

    public const string BackgroundUrl = "background_url";

    /// <summary>Every tracked field, in the order the editor renders them.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Name,
        FirstReleaseYear,
        Summary,
        CoverUrl,
        Publisher,
        BackgroundUrl,
    ];

    /// <summary>Fields whose values are image references rather than text.</summary>
    public static readonly IReadOnlyList<string> Art = [CoverUrl, BackgroundUrl];

    public static bool IsKnown(string? field)
        => field is not null && All.Contains(field, StringComparer.Ordinal);

    public static bool IsArt(string? field)
        => field is not null && Art.Contains(field, StringComparer.Ordinal);

    /// <summary>
    /// True for <see cref="FirstReleaseYear"/>, the one non-text column. The
    /// caller range-checks rather than merely parsing, so a typo becomes a
    /// refusal instead of a stored absurdity.
    /// </summary>
    public static bool IsNumeric(string? field)
        => string.Equals(field, FirstReleaseYear, StringComparison.Ordinal);
}

/// <summary>
/// Who last wrote a field (migration 0027). Stored verbatim in
/// <c>work_field_sources.source</c>.
/// </summary>
public static class FieldSources
{
    public const string User = "user";

    public const string Igdb = "igdb";

    public const string Steam = "steam";

    public const string Epic = "epic";

    public const string Gog = "gog";

    public static bool IsUserOwned(string? source)
        => string.Equals(source, User, StringComparison.Ordinal);
}
