using System.Globalization;
using System.Text.Json;
using Winnow.Core.Domain;

namespace Winnow.Enrich.SteamWeb.Model;

/// <summary>
/// Reads the two M5 history endpoints. Hand-rolled over
/// <see cref="JsonDocument"/> for the same reason
/// <see cref="SteamWebJson"/> is: the parser has to be able to say <b>"this was
/// not an answer"</b>, and a deserialiser that maps a missing array to an empty
/// list has already thrown that distinction away. For a backfill the difference
/// is the whole design: an answered-but-empty year is recorded as done and
/// never fetched again, while an unanswered one must be retried.
/// </summary>
public static class SteamHistoryJson
{
    /// <summary>
    /// Reads a Year in Review body, or returns null when the body was not
    /// an answer: unparseable, or missing the <c>response.stats</c> envelope
    /// entirely (the <c>{"response":{}}</c> Steam sends for a year, or an
    /// account, it will not disclose).
    ///
    /// <para>Envelope verified live 2026-08-28:
    /// <c>response.stats.{account_id, year, playtime_stats.{total_stats, games[]}}</c>,
    /// with each game carrying <c>appid</c>, <c>stats</c>,
    /// <c>rtime_first_played</c> and its own monthly breakdown.</para>
    ///
    /// <para>The monthly axis is read from BOTH shapes the sources describe,
    /// because they disagree and only one was observed end to end. The proto in
    /// <c>docs/spikes/steam-gdpr-export.md</c> puts <c>months[]</c> at the
    /// <c>playtime_stats</c> level with a nested per-month <c>appid[]</c> array;
    /// the live probe found the per-game object carrying its own months. Reading
    /// both costs one extra loop and means a Valve-side change of shape degrades
    /// to fewer points rather than to none.</para>
    /// </summary>
    public static SteamYearInReviewPayload? TryReadYearInReview(string? body)
    {
        if (TryParse(body) is not { } document)
        {
            return null;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("stats", out var stats)
                || stats.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accountId = TryReadInt64(stats, "account_id") is { } id and > 0 and <= uint.MaxValue
                ? (uint)id
                : (uint?)null;

            var fallbackYear = TryReadInt64(stats, "year") is { } y and > 1990 and < 3000 ? (int)y : 0;

            if (!stats.TryGetProperty("playtime_stats", out var playtime)
                || playtime.ValueKind != JsonValueKind.Object)
            {
                // The envelope is there and names the account, but carries no
                // playtime block. That is an answer (the year holds nothing),
                // not a failure.
                return new SteamYearInReviewPayload(accountId, []);
            }

            // appid -> accumulating entry, so the two month shapes and the game
            // list all fold into one record per appid.
            var games = new Dictionary<string, GameBuilder>(StringComparer.Ordinal);

            if (playtime.TryGetProperty("games", out var gameList) && gameList.ValueKind == JsonValueKind.Array)
            {
                foreach (var game in gameList.EnumerateArray())
                {
                    ReadGame(game, fallbackYear, games);
                }
            }

            if (playtime.TryGetProperty("months", out var monthList) && monthList.ValueKind == JsonValueKind.Array)
            {
                foreach (var month in monthList.EnumerateArray())
                {
                    ReadTopLevelMonth(month, fallbackYear, games);
                }
            }

            var result = games.Values
                .Select(static b => b.Build())
                .OrderBy(static g => ParseAppIdForOrdering(g.AppId))
                .ThenBy(static g => g.AppId, StringComparer.Ordinal)
                .ToArray();

            return new SteamYearInReviewPayload(accountId, result);
        }
    }

    /// <summary>
    /// Reads a <c>ClientGetLastPlayedTimes</c> body, or returns null when the
    /// body was not an answer. An explicitly empty <c>games</c> array IS an
    /// answer and yields an empty list.
    /// </summary>
    public static IReadOnlyList<SteamLastPlayedGame>? TryReadLastPlayedTimes(string? body)
    {
        if (TryParse(body) is not { } document)
        {
            return null;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var response)
                || response.ValueKind != JsonValueKind.Object
                || !response.TryGetProperty("games", out var games)
                || games.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var result = new List<SteamLastPlayedGame>(games.GetArrayLength());
            foreach (var element in games.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object
                    || TryReadInt64(element, "appid") is not { } appId
                    || appId <= 0)
                {
                    continue;
                }

                result.Add(new SteamLastPlayedGame(
                    AppId: appId.ToString(CultureInfo.InvariantCulture),
                    PlaytimeForeverMinutes: NonNegative(TryReadInt64(element, "playtime_forever")),

                    // Both timestamps go through the shared placeholder rule.
                    // first_playtime is 0 on many entries and 0 means "not
                    // tracked": mapping it to 1970-01-01 would date every such
                    // game's first play to the Unix epoch and make the whole
                    // cold-start signal worse than having none.
                    LastPlayedUtc: SteamTime.FromEpochSeconds(TryReadInt64(element, "last_playtime")),
                    FirstPlayedUtc: SteamTime.FromEpochSeconds(TryReadInt64(element, "first_playtime")),
                    PlaytimeTwoWeeksMinutes: NonNegative(TryReadInt64(element, "playtime_2weeks"))));
            }

            result.Sort(static (a, b) =>
            {
                var byId = ParseAppIdForOrdering(a.AppId).CompareTo(ParseAppIdForOrdering(b.AppId));
                return byId != 0 ? byId : string.CompareOrdinal(a.AppId, b.AppId);
            });

            return result;
        }
    }

    /// <summary>One entry in <c>playtime_stats.games[]</c>, with whatever months it carries.</summary>
    private static void ReadGame(JsonElement game, int fallbackYear, Dictionary<string, GameBuilder> games)
    {
        if (game.ValueKind != JsonValueKind.Object
            || TryReadInt64(game, "appid") is not { } appId
            || appId <= 0)
        {
            return;
        }

        var builder = Builder(games, appId.ToString(CultureInfo.InvariantCulture));

        if (game.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            builder.TotalSeconds = Math.Max(
                builder.TotalSeconds, NonNegative(TryReadInt64(stats, "total_playtime_seconds")));
            builder.TotalSessions = (int)Math.Max(
                builder.TotalSessions, NonNegative(TryReadInt64(stats, "total_sessions")));
        }

        if (SteamTime.FromEpochSeconds(TryReadInt64(game, "rtime_first_played")) is { } first)
        {
            // Earliest wins. Each year's response reports the first play Steam
            // knows of at that point, so across four fetched years the smallest
            // value is the one that means "first ever".
            builder.FirstPlayed = builder.FirstPlayed is { } known && known <= first ? known : first;
        }

        if (game.TryGetProperty("months", out var months) && months.ValueKind == JsonValueKind.Array)
        {
            foreach (var month in months.EnumerateArray())
            {
                if (ReadMonth(month, fallbackYear) is { } parsed)
                {
                    builder.Months.Add(parsed);
                }
            }
        }
    }

    /// <summary>
    /// One entry in the proto's <c>playtime_stats.months[]</c>: a month that
    /// carries its own per-appid array. The array's name in the proto is
    /// <c>appid</c> (singular) even though it is repeated, so both that and the
    /// likelier-looking <c>games</c> are accepted.
    /// </summary>
    private static void ReadTopLevelMonth(
        JsonElement month, int fallbackYear, Dictionary<string, GameBuilder> games)
    {
        if (month.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var when = ReadMonthKey(month, fallbackYear);
        if (when is not { } period)
        {
            return;
        }

        var perGame = month.TryGetProperty("appid", out var byAppId) && byAppId.ValueKind == JsonValueKind.Array
            ? byAppId
            : month.TryGetProperty("games", out var byGames) && byGames.ValueKind == JsonValueKind.Array
                ? byGames
                : default;

        if (perGame.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in perGame.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || TryReadInt64(entry, "appid") is not { } appId
                || appId <= 0)
            {
                continue;
            }

            var (seconds, sessions) = ReadStats(entry);
            Builder(games, appId.ToString(CultureInfo.InvariantCulture)).Months.Add(
                new SteamMonthlyPlaytime(period.Year, period.Month, seconds, sessions));
        }
    }

    /// <summary>One entry in a per-game <c>months[]</c> array.</summary>
    private static SteamMonthlyPlaytime? ReadMonth(JsonElement month, int fallbackYear)
    {
        if (month.ValueKind != JsonValueKind.Object || ReadMonthKey(month, fallbackYear) is not { } period)
        {
            return null;
        }

        var (seconds, sessions) = ReadStats(month);
        return new SteamMonthlyPlaytime(period.Year, period.Month, seconds, sessions);
    }

    /// <summary>
    /// The month a period object names. <c>rtime_month</c> is the proto's field
    /// and its <c>rtime_</c> prefix says epoch seconds, but a bare 1-12 index
    /// alongside the response's own <c>year</c> is the other plausible encoding
    /// and costs one comparison to accept. Anything below 13 is read as an index
    /// against <paramref name="fallbackYear"/>; anything above is read as a
    /// timestamp.
    /// </summary>
    private static (int Year, int Month)? ReadMonthKey(JsonElement period, int fallbackYear)
    {
        var raw = TryReadInt64(period, "rtime_month") ?? TryReadInt64(period, "month");
        if (raw is not { } value || value <= 0)
        {
            return null;
        }

        if (value <= 12)
        {
            var year = TryReadInt64(period, "year") is { } y and > 1990 and < 3000 ? (int)y : fallbackYear;
            return year > 0 ? (year, (int)value) : null;
        }

        if (SteamTime.FromEpochSeconds(value) is not { } stamp)
        {
            return null;
        }

        return (stamp.Year, stamp.Month);
    }

    /// <summary>
    /// Seconds and sessions from a <c>stats</c> sub-object, falling back to the
    /// same fields sitting directly on the entry.
    /// </summary>
    private static (long Seconds, int Sessions) ReadStats(JsonElement entry)
    {
        var source = entry.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object
            ? stats
            : entry;

        return (
            NonNegative(TryReadInt64(source, "total_playtime_seconds")),
            (int)NonNegative(TryReadInt64(source, "total_sessions")));
    }

    private static GameBuilder Builder(Dictionary<string, GameBuilder> games, string appId)
    {
        if (!games.TryGetValue(appId, out var builder))
        {
            builder = new GameBuilder(appId);
            games[appId] = builder;
        }

        return builder;
    }

    private static JsonDocument? TryParse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                return null;
            }

            return document;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long NonNegative(long? value) => value is { } v && v > 0 ? v : 0;

    /// <summary>
    /// Reads an integer whether Steam encoded it as a number or as a string.
    /// Valve mixes the two within one object elsewhere in this API surface, so
    /// nothing here assumes which form a field arrives in.
    /// </summary>
    private static long? TryReadInt64(JsonElement parent, string property)
        => parent.TryGetProperty(property, out var element) ? TryReadInt64(element) : null;

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

    private sealed class GameBuilder(string appId)
    {
        public List<SteamMonthlyPlaytime> Months { get; } = [];

        public long TotalSeconds { get; set; }

        public int TotalSessions { get; set; }

        public DateTime? FirstPlayed { get; set; }

        public SteamYearInReviewGame Build()
            => new(appId, Months, TotalSeconds, TotalSessions, FirstPlayed);
    }
}

/// <summary>
/// What a Year in Review body carried, separated from the request that produced
/// it so the parser never has to know which account was asked for.
/// </summary>
/// <param name="AccountId">
/// <c>response.stats.account_id</c>, or null when absent. The client compares
/// this against the account it asked about.
/// </param>
/// <param name="Games">Per-appid stats, ordered by appid.</param>
public readonly record struct SteamYearInReviewPayload(
    uint? AccountId, IReadOnlyList<SteamYearInReviewGame> Games);
