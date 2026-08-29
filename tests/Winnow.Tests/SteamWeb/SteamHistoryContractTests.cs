using System.Text.Json;
using Winnow.Enrich.SteamWeb.Model;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Shape assertions for M5's two endpoints against the canned fixtures. Same
/// role as <see cref="SteamWebContractTests"/>: when a recapture breaks one of
/// these, Valve changed the contract and the parser has started silently
/// returning less than it used to.
/// </summary>
public class SteamHistoryContractTests
{
    /// <summary>
    /// The envelope verified live on 2026-08-28:
    /// <c>response.stats.{account_id, year, playtime_stats.{total_stats, games[]}}</c>.
    /// </summary>
    [Fact]
    public void The_year_in_review_envelope_is_response_stats_playtime_stats()
    {
        using var document = JsonDocument.Parse(SteamWebFixtures.YearInReview2024());

        var stats = document.RootElement.GetProperty("response").GetProperty("stats");
        Assert.Equal(SteamWebFixtures.FixtureAccountId, stats.GetProperty("account_id").GetUInt32());
        Assert.Equal(2024, stats.GetProperty("year").GetInt32());

        var playtime = stats.GetProperty("playtime_stats");
        Assert.Equal(JsonValueKind.Object, playtime.GetProperty("total_stats").ValueKind);
        Assert.Equal(JsonValueKind.Array, playtime.GetProperty("games").ValueKind);
    }

    /// <summary>
    /// The account id is the field the whole import is gated on: it is the
    /// only thing in either response that says WHOSE history this is. The API
    /// key, not the <c>steamid</c> parameter, is what Steam answers for.
    /// </summary>
    [Fact]
    public void The_account_id_is_read_and_carried_out_of_the_parser()
    {
        var payload = SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.YearInReview2024());

        Assert.NotNull(payload);
        Assert.Equal(SteamWebFixtures.FixtureAccountId, payload.Value.AccountId);
    }

    [Fact]
    public void Per_game_months_carry_seconds_not_minutes()
    {
        var payload = SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.YearInReview2024());

        Assert.NotNull(payload);
        var game = payload.Value.Games.Single(g => g.AppId == "1203620");

        Assert.Equal(
            [(2024, 1, 6000L), (2024, 2, 12000L), (2024, 5, 3000L)],
            game.Months.OrderBy(m => m.Ordinal).Select(m => (m.Year, m.Month, m.PlaytimeSeconds)));

        // The year total is the sum of its months here, but nothing derives one
        // from the other: GetOwnedGames already proved Valve's own totals and
        // splits need not agree.
        Assert.Equal(21000, game.TotalPlaytimeSeconds);
    }

    /// <summary>
    /// <c>rtime_month</c> carries epoch seconds, so the month is decoded from a
    /// timestamp rather than assumed to be an index. Reading 1704067200 as a
    /// bare month number would put January 2024's play in a month that does not
    /// exist.
    /// </summary>
    [Fact]
    public void Rtime_month_is_decoded_as_an_epoch_timestamp()
    {
        var payload = SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.YearInReview2024());

        Assert.NotNull(payload);
        var first = payload.Value.Games.Single(g => g.AppId == "1203620").Months.MinBy(m => m.Ordinal);

        Assert.NotNull(first);
        Assert.Equal(2024, first.Year);
        Assert.Equal(1, first.Month);
    }

    /// <summary>
    /// A zero <c>rtime_first_played</c> means "not tracked". Mapping it to
    /// 1970-01-01 would date the first play of every such game to the Unix epoch
    /// and make the cold-start signal worse than not having one.
    /// </summary>
    [Fact]
    public void A_zero_first_played_is_absent_rather_than_the_epoch()
    {
        var payload = SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.YearInReview2024());

        Assert.NotNull(payload);
        Assert.Null(payload.Value.Games.Single(g => g.AppId == "933480").FirstPlayedUtc);
        Assert.Equal(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            payload.Value.Games.Single(g => g.AppId == "1203620").FirstPlayedUtc);
    }

    /// <summary>A game listed for the year with no monthly breakdown is ordinary, not a parse failure.</summary>
    [Fact]
    public void A_game_with_an_empty_months_array_still_parses()
    {
        var payload = SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.YearInReview2024());

        Assert.NotNull(payload);
        Assert.Empty(payload.Value.Games.Single(g => g.AppId == "444440000").Months);
    }

    /// <summary>
    /// The other shape. The spike's proto puts <c>months[]</c> at the
    /// <c>playtime_stats</c> level with a per-month <c>appid[]</c> array, and
    /// the live probe found months on the per-game object instead. Only one was
    /// observed end to end, so both are read. A Valve-side change of placement
    /// then costs points rather than the whole import.
    /// </summary>
    [Fact]
    public void Months_at_the_playtime_stats_level_are_read_too()
    {
        var payload = SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.YearInReviewProtoMonths());

        Assert.NotNull(payload);
        var game = payload.Value.Games.Single(g => g.AppId == "1203620");

        Assert.Equal(
            [(2023, 1, 12000L), (2023, 8, 6000L)],
            game.Months.OrderBy(m => m.Ordinal).Select(m => (m.Year, m.Month, m.PlaytimeSeconds)));
    }

    /// <summary>
    /// The bare envelope is an ANSWER for this endpoint (the year holds
    /// nothing), but the parser cannot tell that from a body it could not
    /// read, so it declines and the client classifies. Keeping the two
    /// apart is what lets a year be marked complete without silently
    /// swallowing a failure.
    /// </summary>
    [Fact]
    public void The_bare_envelope_does_not_parse_as_a_year()
    {
        Assert.Null(SteamHistoryJson.TryReadYearInReview(SteamWebFixtures.EmptyYearInReview));
        Assert.Null(SteamHistoryJson.TryReadYearInReview("not json"));
        Assert.Null(SteamHistoryJson.TryReadYearInReview(null));
        Assert.Null(SteamHistoryJson.TryReadYearInReview(string.Empty));
    }

    /// <summary>
    /// The one that had to be verified live and contradicts nothing else Winnow
    /// reads: <c>first_playtime</c> is <b>0 on many entries</b>, and 0 means
    /// "not tracked". Three of the five fixture entries carry it.
    /// </summary>
    [Fact]
    public void First_playtime_is_zero_on_many_entries_and_zero_means_absent()
    {
        using var document = JsonDocument.Parse(SteamWebFixtures.LastPlayedTimes());

        var raw = document.RootElement.GetProperty("response").GetProperty("games")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(3, raw.Count(g => g.GetProperty("first_playtime").GetInt64() == 0));

        var parsed = SteamHistoryJson.TryReadLastPlayedTimes(SteamWebFixtures.LastPlayedTimes());
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed.Count(g => g.FirstPlayedUtc is not null));
        Assert.Null(parsed.Single(g => g.AppId == "10").FirstPlayedUtc);
        Assert.Equal(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            parsed.Single(g => g.AppId == "1203620").FirstPlayedUtc);
    }

    /// <summary>
    /// <c>playtime_forever</c> is the anchor the whole reconstruction stands on,
    /// so it is projected verbatim and never derived from the per-platform
    /// splits, which do not sum to it.
    /// </summary>
    [Fact]
    public void Playtime_forever_is_the_anchor_and_is_not_the_sum_of_the_splits()
    {
        using var document = JsonDocument.Parse(SteamWebFixtures.LastPlayedTimes());

        var game = document.RootElement.GetProperty("response").GetProperty("games")
            .EnumerateArray()
            .First(g => g.GetProperty("appid").GetInt64() == 933480);

        var split = game.GetProperty("playtime_windows_forever").GetInt64()
            + game.GetProperty("playtime_linux_forever").GetInt64()
            + game.GetProperty("playtime_mac_forever").GetInt64();
        Assert.NotEqual(game.GetProperty("playtime_forever").GetInt64(), split);

        var parsed = SteamHistoryJson.TryReadLastPlayedTimes(SteamWebFixtures.LastPlayedTimes());
        Assert.NotNull(parsed);
        Assert.Equal(
            SteamWebFixtures.EnderalAnchorMinutes, parsed.Single(g => g.AppId == "933480").PlaytimeForeverMinutes);
        Assert.Equal(
            SteamWebFixtures.EnshroudedAnchorMinutes,
            parsed.Single(g => g.AppId == "1203620").PlaytimeForeverMinutes);
    }

    /// <summary>A body with no games array is not "this account has played nothing".</summary>
    [Fact]
    public void A_last_played_body_with_no_games_array_does_not_parse()
    {
        Assert.Null(SteamHistoryJson.TryReadLastPlayedTimes("{\"response\":{}}"));
        Assert.Null(SteamHistoryJson.TryReadLastPlayedTimes("{}"));

        // An explicitly empty array IS an answer.
        Assert.Empty(SteamHistoryJson.TryReadLastPlayedTimes("{\"response\":{\"games\":[]}}")!);
    }
}
