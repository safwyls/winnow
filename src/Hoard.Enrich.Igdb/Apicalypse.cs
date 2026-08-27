using System.Globalization;
using System.Text;

namespace Hoard.Enrich.Igdb;

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
    /// The <c>external_games</c> query: the high-precision store id → IGDB id
    /// join described in §4.4 as "the backbone of entity resolution".
    ///
    /// <para>Filters on <c>external_game_source</c>. The older
    /// <c>external_games.category</c> enum still carries the same value for
    /// Steam but is marked deprecated in the current IGDB docs, so the new field
    /// is the one queried.</para>
    ///
    /// <para><b><paramref name="sourceId"/> is a parameter, not the constant 1.</b>
    /// IGDB enumerates Steam as 1, GOG as 5 and the Epic Games Store as 26
    /// (<c>GET /v4/external_game_sources</c>, re-verified live against the
    /// project's credentials). This query was hardcoded to Steam's id and the
    /// call site was hardcoded to Steam's provider, so a GOG product id — which
    /// IGDB stores verbatim under source 5, byte-identical to the
    /// <c>gog_&lt;id&gt;</c> releaseKey suffix — was never once asked about.
    /// See <c>docs/spikes/epic-gog-local-files.md</c> section 19 for the
    /// per-source coverage measurements.</para>
    ///
    /// <para>Expands <c>game.*</c> selectively so one request yields the id
    /// <i>and</i> the display fields Resolve needs, instead of a second round
    /// trip against <c>/games</c>.</para>
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
    /// </summary>
    public static string Games(IEnumerable<long> igdbIds, int limit, int offset)
        => $"""
            fields name,summary,first_release_date,cover.image_id,cover.url,genres.name,themes.name,game_modes.name,player_perspectives.name,involved_companies.publisher,involved_companies.company.name;
            where id = {NumberList(igdbIds)};
            limit {Clamp(limit).ToString(CultureInfo.InvariantCulture)};
            offset {offset.ToString(CultureInfo.InvariantCulture)};
            """;

    private static int Clamp(int limit) => Math.Clamp(limit, 1, MaxLimit);
}
