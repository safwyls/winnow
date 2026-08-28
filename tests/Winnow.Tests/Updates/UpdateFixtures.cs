using System.Globalization;
using System.Text.Json;

namespace Winnow.Tests.Updates;

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

    /// <summary>
    /// Everwind Demo. Named by steamcmd.net and by nothing else: IGDB has no
    /// entry and <c>IStoreBrowseService/GetItems</c> returns nothing, so this
    /// appid showed as <c>App 4028270</c> in the author's library.
    /// </summary>
    public const string DemoAppId = "4028270";

    /// <summary>
    /// Monster Hunter Wilds Beta test. Exists, and answers <c>_missing_token</c>
    /// with no <c>common</c> block — the restricted shape.
    /// </summary>
    public const string RestrictedAppId = "3065170";

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

    /// <summary>
    /// The captured response for Everwind Demo: a full <c>common</c> block with
    /// <c>name</c>, <c>type: "Demo"</c> and <c>parent: "2253100"</c>.
    /// </summary>
    public static string DemoAppInfoResponse()
        => File.ReadAllText(Path.Combine(FixtureRoot, "steamcmd-info-demo-4028270.json"));

    /// <summary>
    /// The captured restricted response: HTTP 200, a NON-empty inner object
    /// carrying <c>"_missing_token": true</c> and <c>"public_only": "1"</c>, and
    /// no <c>common</c> or <c>depots</c> block at all.
    /// </summary>
    public static string RestrictedAppInfoResponse()
        => File.ReadAllText(Path.Combine(FixtureRoot, "steamcmd-info-restricted-3065170.json"));

    /// <summary>
    /// A steamcmd.net body carrying only a <c>common</c> block — the shape an
    /// app with no public branch answers with, and the one that proves the name
    /// survives even when the build projection finds nothing.
    /// </summary>
    public static string AppInfoOnly(string appId, string name, string type, string? parent = null)
    {
        var parentField = parent is null ? string.Empty : ",\"parent\":\"" + parent + "\"";

        return $$$"""
            {"data":{"{{{appId}}}":{
              "_change_number":38253266,
              "appid":"{{{appId}}}",
              "common":{"name":{{{JsonSerializer.Serialize(name)}}},"type":{{{JsonSerializer.Serialize(type)}}}{{{parentField}}}}
            }},"status":"success"}
            """;
    }

    /// <summary>The verified restricted body: HTTP 200, non-empty object, no <c>common</c>.</summary>
    public static string Restricted(string appId)
        => $$$"""
            {"data": {"{{{appId}}}": {"_change_number": 38298585, "_missing_token": true,
              "appid": "{{{appId}}}", "public_only": "1"}}, "status": "success"}
            """;

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
