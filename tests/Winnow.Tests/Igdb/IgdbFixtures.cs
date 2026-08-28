using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        involved_companies = includeRelations
            ? new[]
            {
                new { id = 1, publisher = false, developer = true, company = new { id = 10, name = "Some Studio" } },
                new { id = 2, publisher = true, developer = false, company = new { id = 11, name = "Valve" } },
            }
            : null,
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
}
