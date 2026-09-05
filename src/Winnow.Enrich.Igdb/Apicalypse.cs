using System.Globalization;
using System.Text;

namespace Winnow.Enrich.Igdb;

/// <summary>
/// Builds Apicalypse query bodies (§4.4). Apicalypse is posted as
/// <c>text/plain</c>, not as query parameters or JSON; each clause is
/// semicolon-terminated.
///
/// <para>The <c>where field = (a,b,c)</c> form is what makes batching possible:
/// it means "any of", so a whole library's Steam appids fit in one request up
/// to the documented <c>limit</c> ceiling of 500 rows.</para>
/// </summary>
public static class Apicalypse
{
    /// <summary>The documented maximum for <c>limit</c>.</summary>
    public const int MaxLimit = 500;

    /// <summary>Content type IGDB expects for a query body.</summary>
    public const string ContentType = "text/plain";

    /// <summary>
    /// Rejects values that would break out of a quoted Apicalypse string.
    ///
    /// <para>Every id that reaches here is numeric today — Steam appids and GOG
    /// product ids both — so in practice nothing is ever rejected. But these ids
    /// arrive from parsed VDF files, a base64 blob and an SQLite database
    /// written by three different launchers, and a query language assembled by
    /// string concatenation gets a validator on principle rather than on
    /// evidence.</para>
    /// </summary>
    public static bool IsSafeStringValue(string value)
        => !string.IsNullOrWhiteSpace(value)
           && value.All(static c => !char.IsControl(c) && c is not ('"' or '\\' or ';'));

    /// <summary>Quotes and joins string values into an "any of" list: <c>("440","570")</c>.</summary>
    public static string StringList(IEnumerable<string> values)
    {
        var builder = new StringBuilder("(");
        var first = true;
        foreach (var value in values)
        {
            if (!IsSafeStringValue(value))
            {
                throw new ArgumentException(
                    $"Value '{value}' cannot be embedded in an Apicalypse query.", nameof(values));
            }

            if (!first)
            {
                builder.Append(',');
            }

            builder.Append('"').Append(value).Append('"');
            first = false;
        }

        return builder.Append(')').ToString();
    }

    /// <summary>Joins numeric values into an "any of" list: <c>(7346,1020)</c>.</summary>
    public static string NumberList(IEnumerable<long> values)
        => "(" + string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture))) + ")";

    /// <summary>
    /// The <c>external_games</c> query for a given <paramref name="sourceId"/>
    /// (Steam = 1, GOG = 5, Epic = 26). Expands <c>game.*</c> selectively to
    /// return display fields in a single request.
    /// </summary>
    public static string ExternalGames(IEnumerable<string> uids, int sourceId, int limit, int offset)
        => $"""
            fields uid,game,game.name,game.summary,game.first_release_date,game.cover.image_id,game.cover.url;
            where external_game_source = {sourceId.ToString(CultureInfo.InvariantCulture)} & uid = {StringList(uids)};
            limit {Clamp(limit).ToString(CultureInfo.InvariantCulture)};
            offset {offset.ToString(CultureInfo.InvariantCulture)};
            """;

    /// <summary>
    /// The <c>games</c> query for full metadata.
    ///
    /// <para><c>involved_companies.publisher</c> is a boolean flag on the join
    /// row, so the company name has to be expanded alongside it and filtered
    /// client-side; Apicalypse cannot filter on a nested field of an expanded
    /// array.</para>
    ///
    /// <para>Editions — Skyrim vs. Special Edition vs. Anniversary — are
    /// deliberately absent. §4.4 names <c>game_versions</c> as the right
    /// abstraction for the Release layer and tells us not to reinvent it; that
    /// is a later milestone, not this one, and nothing here should grow an
    /// ad-hoc edition guess in the meantime.</para>
    ///
    /// <para><c>game_modes</c> and <c>player_perspectives</c> are the library
    /// filter's descriptors (migration 0007). They cost NOTHING to ask for: an
    /// Apicalypse <c>fields</c> clause is one request whatever it lists, so these
    /// ride along on the same call that was already fetching name, year and
    /// publisher. They were left out originally for the reason 0005 records —
    /// nothing consumed them and §6 had no column — and are added now that
    /// something does.</para>
    ///
    /// <para><c>platforms.name</c> rides the same free ride, and is what tells
    /// Prey (2006, Xbox 360) apart from Prey (2017, PS4/PC) in the wrong-game
    /// control's candidate rows. It belongs on this query rather than on a third
    /// one because a candidate found by id and a candidate found by title are
    /// drawn side by side, and an id row that showed a year and no platforms was
    /// the defect that put it here.</para>
    /// </summary>
    public static string Games(IEnumerable<long> igdbIds, int limit, int offset)
        => $"""
            fields name,summary,first_release_date,cover.image_id,cover.url,genres.name,themes.name,game_modes.name,player_perspectives.name,platforms.name,involved_companies.publisher,involved_companies.company.name,game_type.type,parent_game,version_parent,version_title;
            where id = {NumberList(igdbIds)};
            limit {Clamp(limit).ToString(CultureInfo.InvariantCulture)};
            offset {offset.ToString(CultureInfo.InvariantCulture)};
            """;

    /// <summary>
    /// The <c>games</c> query for age ratings — separate from <see cref="Games"/>
    /// because it names deprecated fields (<c>age_ratings.category</c>,
    /// <c>age_ratings.rating</c>). A field IGDB finally removes would 400 the
    /// whole body; on the shared query that single 400 would cost name, cover art,
    /// genres, themes, game modes, perspectives and publisher for the entire
    /// library. On its own query it costs maturity alone.
    /// </summary>
    public static string AgeRatings(IEnumerable<long> igdbIds, int limit, int offset)
        => AgeRatingsQuery(
            "fields age_ratings.category,age_ratings.rating,"
            + "age_ratings.organization.name,age_ratings.rating_category.rating;",
            igdbIds,
            limit,
            offset);

    /// <summary>
    /// The fallback query when <see cref="AgeRatings"/> is rejected: only the
    /// current reference fields (<c>organization.name</c>,
    /// <c>rating_category.rating</c>), no deprecated enums. Converts "IGDB
    /// removed a deprecated field" from total loss into label-based mapping.
    /// </summary>
    public static string AgeRatingsWithoutDeprecatedFields(IEnumerable<long> igdbIds, int limit, int offset)
        => AgeRatingsQuery(
            "fields age_ratings.organization.name,age_ratings.rating_category.rating;",
            igdbIds,
            limit,
            offset);

    /// <summary>
    /// Default cap on search results. High enough to find the right Prey
    /// among similarly named games, low enough not to spend the 4 req/s
    /// budget paging a relevance-ranked tail nobody asked for.
    /// </summary>
    public const int DefaultSearchLimit = 20;

    /// <summary>
    /// Sanitizes a user-typed title into a term safe for the quoted
    /// <c>search "…"</c> clause. Returns null when nothing searchable
    /// remains.
    ///
    /// <para>Unlike <see cref="IsSafeStringValue"/>, which rejects unsafe
    /// input, this method replaces the dangerous characters — the double
    /// quote, the backslash, the semicolon and control characters — with
    /// spaces and collapses the whitespace. Rejecting is right for a
    /// machine-generated store id that must never be mangled; a search
    /// term is free text a person typed, and a title containing a quote
    /// should still search rather than silently fail.</para>
    /// </summary>
    public static string? SearchTerm(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var builder = new StringBuilder(title.Length);
        foreach (var c in title)
        {
            builder.Append(char.IsControl(c) || c is '"' or '\\' or ';' ? ' ' : c);
        }

        var cleaned = string.Join(
            ' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// The <c>games</c> query for a title search: the <c>search "…"</c>
    /// clause on the same endpoint, asking for name, cover, year and
    /// platforms. Rides its own query body rather than widening
    /// <see cref="Games"/>, for the same isolation reason
    /// <see cref="AgeRatings"/> is separate: a 400 on this query costs
    /// the search alone, not the shared metadata for the entire library.
    ///
    /// <para>The field list is a subset of <see cref="Games"/>'s, not a
    /// different set of fields. <see cref="Games"/> asks for
    /// <c>platforms.name</c> too, which is what lets a candidate matched
    /// by id and a candidate found by title show the same facts.</para>
    /// </summary>
    public static string SearchGames(string term, int limit)
        => $"""
            fields name,cover.image_id,cover.url,first_release_date,platforms.name;
            search "{term}";
            limit {Clamp(limit).ToString(CultureInfo.InvariantCulture)};
            """;

    private static string AgeRatingsQuery(string fields, IEnumerable<long> igdbIds, int limit, int offset)
        => $"""
            {fields}
            where id = {NumberList(igdbIds)};
            limit {Clamp(limit).ToString(CultureInfo.InvariantCulture)};
            offset {offset.ToString(CultureInfo.InvariantCulture)};
            """;

    private static int Clamp(int limit) => Math.Clamp(limit, 1, MaxLimit);
}
