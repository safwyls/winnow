using System.Globalization;
using System.Text.Json;

namespace Hoard.Tests.Updates;

/// <summary>
/// Response bodies for the update-signal tests: the captured live ones, and
/// generators for the shapes a test needs to vary (a patch note at a chosen
/// date, a build push at a chosen date).
/// </summary>
public static class UpdateFixtures
{
    /// <summary>Stardew Valley — the spike's worked correlation example.</summary>
    public const string StardewAppId = "413150";

    /// <summary>Portal 2 — build and announcement on the same day.</summary>
    public const string PortalAppId = "620";

    /// <summary>Dota 2 — a fresh depot push with no patch note behind it.</summary>
    public const string DotaAppId = "570";

    /// <summary>Spacewar. One of the appids verified to answer 403: no news feed.</summary>
    public const string NoFeedAppId = "480";

    private static readonly string FixtureRoot =
        Path.Combine(AppContext.BaseDirectory, "fixtures", "update-signals");

    /// <summary>The captured `tags=patchnotes` response for Stardew Valley.</summary>
    public static string NewsResponse()
        => File.ReadAllText(Path.Combine(FixtureRoot, "getnewsforapp-patchnotes-413150.json"));

    /// <summary>The captured response for an app with a feed but nothing tagged `patchnotes`.</summary>
    public static string NewsNoMatchesResponse()
        => File.ReadAllText(Path.Combine(FixtureRoot, "getnewsforapp-nomatches-790.json"));

    /// <summary>The captured steamcmd.net response for Stardew Valley, five branches and all.</summary>
    public static string BuildInfoResponse()
        => File.ReadAllText(Path.Combine(FixtureRoot, "steamcmd-info-413150.json"));

    /// <summary>
    /// The captured response for an appid that does not exist: HTTP 200 with an
    /// empty inner object. The shape that must never be read as a parse failure.
    /// </summary>
    public static string BuildInfoMissingResponse()
        => File.ReadAllText(Path.Combine(FixtureRoot, "steamcmd-info-missing.json"));

    /// <summary>A `GetNewsForApp` body carrying one patch note at a chosen instant.</summary>
    public static string News(
        string appId, DateTime publishedAt, string gid, string title = "Patch notes", int totalMatching = 34)
    {
        var date = new DateTimeOffset(DateTime.SpecifyKind(publishedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return $$$"""
            {"appnews":{"appid":{{{appId}}},"newsitems":[{
              "gid":"{{{gid}}}",
              "title":{{{JsonSerializer.Serialize(title)}}},
              "url":"https://steamstore-a.akamaihd.net/news/externalpost/steam_community_announcements/{{{gid}}}",
              "is_external_url":true,
              "author":"fixture",
              "contents":"H",
              "feedlabel":"Community Announcements",
              "date":{{{date.ToString(CultureInfo.InvariantCulture)}}},
              "feedname":"steam_community_announcements",
              "feed_type":1,
              "appid":{{{appId}}},
              "tags":["patchnotes"]
            }],"count":{{{totalMatching.ToString(CultureInfo.InvariantCulture)}}}}}
            """;
    }

    /// <summary>A `GetNewsForApp` body for an app with a feed and no matching items.</summary>
    public static string NewsEmpty(string appId)
        => $$$"""{"appnews":{"appid":{{{appId}}},"newsitems":[],"count":0}}""";

    /// <summary>
    /// A steamcmd.net body whose `public` branch flipped at a chosen instant.
    /// Non-public branches are included so every test exercises the "read
    /// `public` only" rule rather than a simplified shape.
    /// </summary>
    public static string BuildInfo(string appId, DateTime updatedAt, string buildId = "16826371")
    {
        var updated = new DateTimeOffset(DateTime.SpecifyKind(updatedAt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var built = updated - 1026;
        return $$$"""
            {"data":{"{{{appId}}}":{
              "_change_number":38253266,
              "appid":"{{{appId}}}",
              "common":{"name":"Fixture"},
              "depots":{"branches":{
                "public":{"buildid":"{{{buildId}}}","timebuildupdated":"{{{built}}}","timeupdated":"{{{updated}}}"},
                "previous_version":{"buildid":"1","timeupdated":"1000000000"},
                "beta":{"buildid":"2","timeupdated":"1500000000"}
              }}
            }},"status":"success"}
            """;
    }

    /// <summary>The verified missing-app body: HTTP 200, empty inner object.</summary>
    public static string BuildInfoMissing(string appId)
        => $$$"""{"data": {"{{{appId}}}": {}}, "status": "success"}""";
}
