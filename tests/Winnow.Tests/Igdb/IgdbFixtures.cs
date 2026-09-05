using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Winnow.Enrich.Igdb.Credentials;

namespace Winnow.Tests.Igdb;

/// <summary>
/// Canned Twitch and IGDB payloads, shaped like the real ones documented at
/// api-docs.igdb.com: <c>external_games</c> rows carry <c>uid</c> plus an
/// expanded <c>game</c> object, covers carry <c>image_id</c> and a
/// protocol-relative <c>t_thumb</c> url, dates are Unix seconds.
///
/// <para>The generators read the Apicalypse body they are answering, so a
/// response only ever contains rows the query actually asked for — which is
/// what makes the batching assertions meaningful rather than circular.</para>
/// </summary>
public static class IgdbFixtures
{
    private static readonly Regex QuotedValues = new("\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex NumberListValues = new(@"where\s+id\s*=\s*\(([^)]*)\)", RegexOptions.Compiled);
    private static readonly Regex OffsetClause = new(@"offset\s+(\d+)\s*;", RegexOptions.Compiled);
    private static readonly Regex LimitClause = new(@"limit\s+(\d+)\s*;", RegexOptions.Compiled);

    /// <summary>A Twitch client-credentials response. <c>expires_in</c> is Twitch's real ~60-day figure.</summary>
    public static string TokenResponse(string accessToken, long expiresIn = 5_184_000)
        => $$"""
             {"access_token":"{{accessToken}}","expires_in":{{expiresIn}},"token_type":"bearer"}
             """;

    /// <summary>Steam appids named in a <c>where uid = (…)</c> clause, in order.</summary>
    public static IReadOnlyList<string> RequestedUids(string apicalypseBody)
    {
        var whereClause = ClauseAfter(apicalypseBody, "uid = (");
        return whereClause is null
            ? []
            : QuotedValues.Matches(whereClause).Select(m => m.Groups[1].Value).ToArray();
    }

    /// <summary>IGDB ids named in a <c>where id = (…)</c> clause, in order.</summary>
    public static IReadOnlyList<long> RequestedIds(string apicalypseBody)
    {
        var match = NumberListValues.Match(apicalypseBody);
        return match.Success
            ? match.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => long.Parse(v, CultureInfo.InvariantCulture))
                .ToArray()
            : [];
    }

    public static int Offset(string apicalypseBody)
    {
        var match = OffsetClause.Match(apicalypseBody);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
    }

    public static int Limit(string apicalypseBody)
    {
        var match = LimitClause.Match(apicalypseBody);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 10;
    }

    /// <summary>The IGDB id this fixture assigns to a Steam appid, so tests can assert the join.</summary>
    public static long IgdbIdForAppId(string appId)
        => 100_000 + long.Parse(appId, CultureInfo.InvariantCulture);

    /// <summary>
    /// An <c>external_games</c> response answering <paramref name="body"/>,
    /// honouring its <c>limit</c> and <c>offset</c> so paging behaves as it
    /// would against the real API.
    /// </summary>
    /// <param name="unknownAppIds">Appids IGDB has no record of; omitted from the response.</param>
    public static string ExternalGames(string body, ISet<string>? unknownAppIds = null)
    {
        var uids = RequestedUids(body)
            .Where(uid => unknownAppIds is null || !unknownAppIds.Contains(uid))
            .Skip(Offset(body))
            .Take(Limit(body))
            .ToArray();

        // Serialised from objects rather than hand-written JSON: the shapes are
        // nested three deep and a stray brace in a string literal would be a
        // fixture bug masquerading as a parser bug.
        var rows = uids.Select((uid, index) => new
        {
            id = index + 1,
            uid,
            game = GameObject(IgdbIdForAppId(uid), "Game " + uid, "co" + uid, includeRelations: false),
        });

        return JsonSerializer.Serialize(rows, SerializerOptions);
    }

    /// <summary>A <c>games</c> response answering <paramref name="body"/>.</summary>
    public static string Games(string body)
    {
        var ids = RequestedIds(body).Skip(Offset(body)).Take(Limit(body)).ToArray();
        var rows = ids.Select(id => GameObject(id, "Game " + id, "co" + id, includeRelations: true));

        return JsonSerializer.Serialize(rows, SerializerOptions);
    }

    private static readonly Regex SearchClause = new("search\\s+\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the term from a <c>search "…"</c> clause, or null when
    /// the body carries no search clause. Used by the test responder to
    /// tell a search request from a metadata lookup on the same endpoint.
    /// </summary>
    public static string? SearchedTerm(string apicalypseBody)
    {
        var match = SearchClause.Match(apicalypseBody);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// The IGDB id this fixture assigns to the <paramref name="rank"/>th
    /// search hit. Deterministic from the rank alone, so tests can assert
    /// the result without knowing the term.
    /// </summary>
    public static long IgdbIdForSearchHit(int rank) => 500_000 + rank;

    /// <summary>
    /// A <c>games</c> response answering a search body:
    /// <paramref name="matches"/> hits, honouring the query's
    /// <c>limit</c>, each carrying the fields the search query asks for
    /// and nothing else. The fixture answers the term the query actually
    /// asked for, which is what keeps the search assertions non-circular.
    /// </summary>
    public static string SearchGames(string body, int matches = 3)
    {
        var term = SearchedTerm(body);
        if (term is null)
        {
            return "[]";
        }

        var rows = Enumerable.Range(1, Math.Min(matches, Limit(body)))
            .Select(rank => new
            {
                id = IgdbIdForSearchHit(rank),
                name = rank == 1 ? term : $"{term} {rank}",
                first_release_date = 1_224_460_800L,
                cover = new
                {
                    id = 9,
                    image_id = "cosearch" + rank.ToString(CultureInfo.InvariantCulture),
                    url = "//images.igdb.com/igdb/image/upload/t_thumb/cosearch"
                          + rank.ToString(CultureInfo.InvariantCulture) + ".jpg",
                },
                platforms = new[]
                {
                    new { id = 6, name = "PC (Microsoft Windows)" },
                    new { id = 48, name = "PlayStation 4" },
                },
            });

        return JsonSerializer.Serialize(rows, SerializerOptions);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static object GameObject(long id, string name, string imageId, bool includeRelations) => new
    {
        id,
        name,
        summary = "A canned summary.",

        // 2008-10-20, Unix seconds — IGDB's format for first_release_date.
        first_release_date = 1_224_460_800L,
        cover = new
        {
            id = 9,
            image_id = imageId,

            // Protocol-relative and thumbnail-sized, exactly as IGDB returns it.
            url = $"//images.igdb.com/igdb/image/upload/t_thumb/{imageId}.jpg",
        },
        genres = includeRelations
            ? new[] { new { id = 5, name = "Shooter" }, new { id = 31, name = "Adventure" } }
            : null,
        themes = includeRelations ? new[] { new { id = 1, name = "Action" } } : null,
        // Real IGDB platform ids and names, and deliberately the same pair
        // SearchGames returns: a test comparing an id-matched row against a
        // title-search row is only meaningful if both fixtures speak of the
        // same platforms.
        platforms = includeRelations
            ? new[]
            {
                new { id = 6, name = "PC (Microsoft Windows)" },
                new { id = 48, name = "PlayStation 4" },
            }
            : null,
        involved_companies = includeRelations
            ? new[]
            {
                new { id = 1, publisher = false, developer = true, company = new { id = 10, name = "Some Studio" } },
                new { id = 2, publisher = true, developer = false, company = new { id = 11, name = "Valve" } },
            }
            : null,

        // game_type's label field is `type`, not `name`;
        // the one place IGDB breaks its own convention, and the reason the
        // query asks for game_type.type. `category` is deprecated and is
        // deliberately absent from both the query and this fixture.
        game_type = includeRelations ? new { id = 0, type = "main_game" } : null,

        // Unexpanded reference fields, which is how Apicalypse returns them
        // when the query names them without a dotted path: bare ids.
        parent_game = (long?)null,
        version_parent = (long?)null,
        version_title = (string?)null,
    };

    private static string? ClauseAfter(string body, string marker)
    {
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = body.IndexOf(')', start);
        return end < 0 ? null : body[start..end];
    }

    /// <summary>
    /// A reversible stand-in for DPAPI, so the protection tests assert the
    /// <i>shape</i> of protection (that nothing readable is written and that it
    /// round-trips) without depending on a real Windows user profile. Base64
    /// is not encryption and is not pretending to be.
    /// </summary>
    public sealed class ReversibleProtector : IIgdbSecretProtector
    {
        public bool IsAvailable => true;

        public string Name => "test:reversible";

        public string? Protect(string plaintext)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public string? Unprotect(string? protectedBase64)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(protectedBase64 ?? ""));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
