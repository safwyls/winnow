using System.Globalization;
using System.Text.Json;
using Winnow.Core.Domain;

namespace Winnow.Enrich.SteamWeb.Model;

/// <summary>
/// Reads the <c>GetOwnedGames</c> envelope. Hand-rolled over
/// <see cref="JsonDocument"/> rather than attribute-mapped DTOs for one reason:
/// the parser has to be able to say <b>"this was not an answer"</b>, and a
/// deserialiser that maps a missing <c>games</c> array to an empty list has
/// already thrown that distinction away.
///
/// <para>Shapes below were captured live on 2026-08-24 against the user's own
/// account and against a second account on the same machine.</para>
/// </summary>
public static class SteamWebJson
{
    /// <summary>
    /// The games in an owned-games response, or <b>null when the body was not an
    /// answer</b> — unparseable, the wrong shape, or the bare
    /// <c>{"response":{}}</c> envelope.
    ///
    /// <para>That last case is real and verified: querying a second account on
    /// the same machine returned exactly <c>{"response":{}}</c> in 15 bytes with
    /// a 200 status. Steam sends it when it will not disclose a profile's
    /// library. Reading it as "owns nothing" would cache a wipe of the user's
    /// library for a whole TTL, so it is classified as unanswered.</para>
    ///
    /// <para>An explicit <c>"game_count": 0</c> <i>is</i> an answer, and yields
    /// an empty list.</para>
    /// </summary>
    public static IReadOnlyList<SteamOwnedGame>? TryReadOwnedGames(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!response.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
            {
                // No array. Only an explicit zero count makes this an answer.
                return response.TryGetProperty("game_count", out var count)
                    && TryReadInt64(count) == 0
                        ? []
                        : null;
            }

            var result = new List<SteamOwnedGame>(games.GetArrayLength());
            foreach (var element in games.EnumerateArray())
            {
                if (TryReadGame(element) is { } game)
                {
                    result.Add(game);
                }
            }

            // Deterministic order, so a cached payload and a fresh one project
            // identically and a diff between two runs is a real change.
            result.Sort(static (a, b) =>
            {
                var byId = ParseAppIdForOrdering(a.AppId).CompareTo(ParseAppIdForOrdering(b.AppId));
                return byId != 0 ? byId : string.CompareOrdinal(a.AppId, b.AppId);
            });

            return result;
        }
    }

    private static SteamOwnedGame? TryReadGame(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("appid", out var appIdElement)
            || TryReadInt64(appIdElement) is not { } appId
            || appId <= 0)
        {
            // An entry with no usable appid cannot be correlated with anything
            // and is dropped rather than allowed to poison the batch.
            return null;
        }

        var name = element.TryGetProperty("name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(nameElement.GetString())
                ? nameElement.GetString()!.Trim()
                : null;

        var icon = element.TryGetProperty("img_icon_url", out var iconElement)
            && iconElement.ValueKind == JsonValueKind.String
            && iconElement.GetString() is { Length: > 0 } hash
                ? hash
                : null;

        // rtime_last_played carries the same placeholders the local files do —
        // 0 on a never-launched game, 86400 on one last played before Steam
        // tracked timestamps. SteamTime is the single rule both readers apply,
        // so both answer null here. Converting 86400 into a literal 1970-01-02
        // made this source disagree with the local scan about the same game, and
        // the two then appended a fresh play_record apiece on every sync.
        var lastPlayed = element.TryGetProperty("rtime_last_played", out var rtime)
            ? SteamTime.FromEpochSeconds(TryReadInt64(rtime))
            : null;

        return new SteamOwnedGame(
            AppId: appId.ToString(CultureInfo.InvariantCulture),
            Title: name,
            PlaytimeForeverMinutes: ReadMinutes(element, "playtime_forever"),
            PlaytimeTwoWeeksMinutes: ReadMinutes(element, "playtime_2weeks"),
            LastPlayedUtc: lastPlayed,
            IconHash: icon);
    }

    private static long ReadMinutes(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && TryReadInt64(value) is { } minutes && minutes > 0
            ? minutes
            : 0;

    /// <summary>
    /// Reads an integer whether Steam encoded it as a number or as a string.
    /// The store fixtures already show Valve mixing the two within one object
    /// (<c>final_price_in_cents</c> is a string while <c>weight</c> is a number),
    /// so nothing here assumes which form a field will arrive in.
    /// </summary>
    private static long? TryReadInt64(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var number) => number,
            JsonValueKind.Number when element.TryGetDouble(out var real) => (long)real,
            JsonValueKind.String when long.TryParse(
                element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

    private static long ParseAppIdForOrdering(string appId)
        => long.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : long.MaxValue;
}
