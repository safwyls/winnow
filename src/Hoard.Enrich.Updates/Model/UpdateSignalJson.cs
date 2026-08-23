using System.Globalization;
using System.Text.Json;

namespace Hoard.Enrich.Updates.Model;

/// <summary>
/// Projections from the two response shapes this module reads. Both are total:
/// anything unexpected yields null, never an exception, because a shape change
/// at either host must degrade to "no signal" rather than break a background
/// pass (§5.1).
///
/// <para>Both hosts stringify their integers — steamcmd.net sends
/// <c>"timeupdated": "1787446656"</c> and <c>"appid": "570"</c> as strings while
/// <c>GetNewsForApp</c> sends <c>"date": 1734718461</c> as a number — so every
/// numeric read here accepts either form.</para>
/// </summary>
internal static class UpdateSignalJson
{
    /// <summary>
    /// The newest patch note in a <c>GetNewsForApp</c> body, or null when the
    /// envelope is unrecognisable. An envelope that parses but carries no items
    /// yields <see cref="NewsFetch.NoItems"/> — the caller distinguishes the two
    /// by the boolean.
    /// </summary>
    /// <param name="recognised">
    /// True when the body was a well-formed <c>appnews</c> envelope, whatever it
    /// contained. False means the contract changed and the answer is worthless.
    /// </param>
    internal static SteamNewsItem? TryReadNewestNewsItem(string body, out bool recognised)
    {
        recognised = false;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("appnews", out var appnews)
                || appnews.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            recognised = true;

            var total = appnews.TryGetProperty("count", out var count) ? ReadInt32(count) ?? 0 : 0;

            if (!appnews.TryGetProperty("newsitems", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            // The request asks for count=1 and the endpoint returns newest
            // first, but "newest" is asserted here rather than assumed: an
            // ordering change would otherwise silently pin the high-water mark
            // to whichever item happened to be first.
            SteamNewsItem? newest = null;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var date = item.TryGetProperty("date", out var dateElement) ? ReadInt64(dateElement) : null;
                if (date is not { } unixSeconds)
                {
                    // No timestamp means nothing to compare a high-water mark
                    // against, which is the only thing this item is for.
                    continue;
                }

                var gid = ReadString(item, "gid");
                if (string.IsNullOrEmpty(gid))
                {
                    continue;
                }

                var candidate = new SteamNewsItem(
                    Gid: gid,
                    Title: ReadString(item, "title"),
                    Url: ReadString(item, "url"),
                    PublishedAt: DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime,
                    TotalMatching: total,
                    RawJson: item.GetRawText());

                if (newest is null || candidate.PublishedAt > newest.PublishedAt)
                {
                    newest = candidate;
                }
            }

            return newest;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>public</c> branch from a steamcmd.net <c>/v1/info/{appid}</c> body.
    /// </summary>
    /// <param name="present">
    /// True when the response carried a non-empty object for this appid. False is
    /// the verified "missing app" shape — <c>{"data":{"999999999":{}}}</c> at
    /// HTTP 200 — and is a legitimate answer, not a failure.
    /// </param>
    internal static BuildBranch? TryReadPublicBranch(string appId, string body, out bool present)
    {
        present = false;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!data.TryGetProperty(appId, out var app) || app.ValueKind != JsonValueKind.Object)
            {
                // The service answered about some other appid, or about none.
                // Not "this app has no data" — that arrives as a present but
                // empty object — so leave `present` false and let the caller
                // treat it as unanswered rather than caching a wrong negative.
                return null;
            }

            // The verified missing-app shape: the key exists, the object is
            // empty. This is the branch the spike insists on — never HTTP status.
            var hasAnyProperty = app.EnumerateObject().MoveNext();
            if (!hasAnyProperty)
            {
                present = true;
                return null;
            }

            present = true;

            // Non-public branches are ignored on purpose: 620 carries beta,
            // demo_viewer and previous_release; 413150 carries compatibility and
            // three legacy pins. None of them is what a user is running.
            if (!app.TryGetProperty("depots", out var depots)
                || depots.ValueKind != JsonValueKind.Object
                || !depots.TryGetProperty("branches", out var branches)
                || branches.ValueKind != JsonValueKind.Object
                || !branches.TryGetProperty("public", out var publicBranch)
                || publicBranch.ValueKind != JsonValueKind.Object)
            {
                // A real app with no public branch — a server tool, say. The
                // response was valid, so this is an answer of "no build signal".
                return null;
            }

            var updated = publicBranch.TryGetProperty("timeupdated", out var timeUpdated)
                ? ReadInt64(timeUpdated)
                : null;
            if (updated is not { } updatedUnix || updatedUnix <= 0)
            {
                return null;
            }

            var buildUpdated = publicBranch.TryGetProperty("timebuildupdated", out var timeBuildUpdated)
                ? ReadInt64(timeBuildUpdated)
                : null;

            // The change number lives beside `depots`, not inside the branch, and
            // is worth keeping: it is the only handle on "did anything at all
            // change" if the heuristic is ever retuned (§4.5).
            var changeNumber = app.TryGetProperty("_change_number", out var change)
                ? ReadInt64(change)
                : null;

            var raw = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["appid"] = appId,
                ["_change_number"] = changeNumber,
                ["public"] = JsonSerializer.Deserialize<JsonElement>(publicBranch.GetRawText()),
            });

            return new BuildBranch(
                BuildId: ReadString(publicBranch, "buildid"),
                UpdatedAt: DateTimeOffset.FromUnixTimeSeconds(updatedUnix).UtcDateTime,
                BuildUpdatedAt: buildUpdated is { } b && b > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(b).UtcDateTime
                    : null,
                RawJson: raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement parent, string name)
        => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads a JSON number or a stringified number; null for anything else.</summary>
    private static long? ReadInt64(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt64(out var number) ? number : null,
        JsonValueKind.String => long.TryParse(
            element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null,
        _ => null,
    };

    private static int? ReadInt32(JsonElement element)
        => ReadInt64(element) is { } value && value is >= int.MinValue and <= int.MaxValue
            ? (int)value
            : null;
}
