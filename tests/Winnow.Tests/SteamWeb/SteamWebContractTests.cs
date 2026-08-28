using System.Text.Json;
using Winnow.Enrich.SteamWeb.Model;
using Xunit;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Pins the exact <c>GetOwnedGames</c> shape Winnow's parser depends on against
/// the bytes captured on 2026-08-24 (<c>tests/fixtures/steam-web/README.md</c>).
///
/// <para><b>This test is the early-warning system.</b> The client soft-fails by
/// design: a shape change produces an unanswered result rather than an
/// exception, which means a silent stop. When someone recaptures the fixture and
/// these assertions break, that is the signal that Valve changed the contract —
/// before the silence reaches a user.</para>
/// </summary>
public class SteamWebContractTests
{
    private static string Body => SteamWebFixtures.CapturedResponse();

    [Fact]
    public void The_envelope_is_response_then_games()
    {
        using var document = JsonDocument.Parse(Body);

        var response = document.RootElement.GetProperty("response");
        Assert.Equal(JsonValueKind.Array, response.GetProperty("games").ValueKind);
        Assert.Equal(
            response.GetProperty("games").GetArrayLength(), response.GetProperty("game_count").GetInt32());
    }

    [Fact]
    public void Every_entry_carries_an_appid_and_a_name()
    {
        using var document = JsonDocument.Parse(Body);

        foreach (var game in document.RootElement.GetProperty("response").GetProperty("games").EnumerateArray())
        {
            Assert.True(game.TryGetProperty("appid", out var appid));
            Assert.True(appid.GetInt64() > 0);

            // include_appinfo=1 is what makes `name` present at all. If this
            // assertion ever fails on a fresh capture, the parameter stopped
            // working — and every title in the library silently becomes null.
            Assert.True(game.TryGetProperty("name", out var name));
            Assert.False(string.IsNullOrWhiteSpace(name.GetString()));
        }
    }

    [Fact]
    public void Rtime_last_played_is_present_and_zero_rather_than_absent_on_never_played_games()
    {
        using var document = JsonDocument.Parse(Body);

        var neverPlayed = document.RootElement
            .GetProperty("response").GetProperty("games")
            .EnumerateArray()
            .First(g => g.GetProperty("appid").GetInt64() == 20);

        Assert.True(neverPlayed.TryGetProperty("rtime_last_played", out var rtime));
        Assert.Equal(0, rtime.GetInt64());

        // Zero is the "never" sentinel. The parser must map it to null, not to
        // 1970-01-01, or every unplayed game sorts as ancient rather than unseen.
        var parsed = SteamWebJson.TryReadOwnedGames(Body);
        Assert.NotNull(parsed);
        Assert.Null(parsed.Single(g => g.AppId == "20").LastPlayedUtc);
    }

    [Fact]
    public void Playtime_2weeks_is_omitted_rather_than_zero()
    {
        using var document = JsonDocument.Parse(Body);

        var games = document.RootElement.GetProperty("response").GetProperty("games").EnumerateArray().ToArray();

        // Exactly one entry in the capture has recent playtime; the rest omit the
        // field entirely. Absent and zero are therefore indistinguishable, and
        // both must read as 0.
        Assert.Single(games, g => g.TryGetProperty("playtime_2weeks", out _));

        var parsed = SteamWebJson.TryReadOwnedGames(Body);
        Assert.NotNull(parsed);
        Assert.Equal(473, parsed.Single(g => g.AppId == "1203620").PlaytimeTwoWeeksMinutes);
        Assert.Equal(0, parsed.Single(g => g.AppId == "10").PlaytimeTwoWeeksMinutes);
    }

    /// <summary>
    /// The per-platform splits do not sum to <c>playtime_forever</c>: appid
    /// 933480 reports 100 minutes total against 12 + 0 + 6 + 0 = 18 across
    /// platforms. Anything deriving a total from the splits would be wrong, so
    /// this pins that <c>playtime_forever</c> is the only figure projected.
    /// </summary>
    [Fact]
    public void Playtime_forever_is_not_the_sum_of_the_per_platform_splits()
    {
        using var document = JsonDocument.Parse(Body);

        var game = document.RootElement
            .GetProperty("response").GetProperty("games")
            .EnumerateArray()
            .First(g => g.GetProperty("appid").GetInt64() == 933480);

        var total = game.GetProperty("playtime_forever").GetInt64();
        var split = game.GetProperty("playtime_windows_forever").GetInt64()
            + game.GetProperty("playtime_mac_forever").GetInt64()
            + game.GetProperty("playtime_linux_forever").GetInt64()
            + game.GetProperty("playtime_deck_forever").GetInt64();

        Assert.NotEqual(total, split);

        var parsed = SteamWebJson.TryReadOwnedGames(Body);
        Assert.NotNull(parsed);
        Assert.Equal(total, parsed.Single(g => g.AppId == "933480").PlaytimeForeverMinutes);
    }

    /// <summary>
    /// Both Enderal releases are in the capture because they are two of the seven
    /// titles that <b>vanish</b> when <c>skip_unvetted_apps=false</c> is omitted
    /// (841 vs 834, measured live 2026-08-24). §4.2's trap, pinned.
    /// </summary>
    [Fact]
    public void The_unvetted_apps_that_only_appear_with_the_flag_are_in_the_capture()
    {
        var parsed = SteamWebJson.TryReadOwnedGames(Body);

        Assert.NotNull(parsed);
        Assert.Contains(parsed, g => g.AppId == "933480");
        Assert.Contains(parsed, g => g.AppId == "976620");
    }

    [Fact]
    public void An_empty_img_icon_url_is_a_present_blank_string()
    {
        var parsed = SteamWebJson.TryReadOwnedGames(Body);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Single(g => g.AppId == "804270").IconHash);
    }

    [Fact]
    public void The_capture_parses_to_the_expected_appids()
    {
        var parsed = SteamWebJson.TryReadOwnedGames(Body);

        Assert.NotNull(parsed);
        Assert.Equal(
            SteamWebFixtures.CapturedAppIds.OrderBy(long.Parse).ToArray(),
            parsed.Select(g => g.AppId).ToArray());
    }
}
