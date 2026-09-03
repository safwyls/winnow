using System.Globalization;
using System.Text;

namespace Winnow.Tests.SteamWeb;

/// <summary>
/// Canned <c>GetOwnedGames</c> bodies. Shapes follow what was captured live on
/// 2026-08-24 and pinned in <c>tests/fixtures/steam-web/</c>; nothing here opens
/// a socket.
/// </summary>
public static class SteamWebFixtures
{
    /// <summary>The verbatim (sanitized) capture the contract test pins.</summary>
    public static string CapturedResponse()
        => File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "fixtures", "steam-web", "getownedgames-v1.json"));

    /// <summary>Appids in the captured fixture, in the order Steam returned them.</summary>
    public static readonly string[] CapturedAppIds =
        ["10", "20", "30", "1203620", "933480", "976620", "804270"];

    /// <summary>
    /// A response built from the given entries, with the same envelope Steam
    /// uses: <c>{"response":{"game_count":N,"games":[…]}}</c>.
    /// </summary>
    public static string OwnedGames(params OwnedGameFixture[] games)
    {
        var builder = new StringBuilder("{\"response\":{\"game_count\":")
            .Append(games.Length.ToString(CultureInfo.InvariantCulture))
            .Append(",\"games\":[");

        for (var i = 0; i < games.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(games[i].ToJson());
        }

        return builder.Append("]}}").ToString();
    }

    /// <summary>
    /// The bare envelope Steam returns for a profile it will not disclose.
    /// Verified live 2026-08-24 against a second account on the same machine:
    /// HTTP 200, 15 bytes, exactly this.
    /// </summary>
    public const string UndisclosedProfile = "{\"response\":{}}";

    /// <summary>An account that genuinely owns nothing: an explicit zero count.</summary>
    public const string EmptyLibrary = "{\"response\":{\"game_count\":0}}";

    /// <summary>
    /// The steam3 account id every M5 fixture is written for, and the SteamID64
    /// derived from it. Fake, per the fixture README's sanitization rule.
    /// </summary>
    public const uint FixtureAccountId = 11111111;

    /// <summary>Cumulative minutes appid 1203620 stands at in the last-played fixture.</summary>
    public const long EnshroudedAnchorMinutes = 817;

    /// <summary>Cumulative minutes appid 933480 stands at in the last-played fixture.</summary>
    public const long EnderalAnchorMinutes = 100;

    /// <summary><c>ClientGetLastPlayedTimes</c>, as pinned in <c>tests/fixtures/steam-web/</c>.</summary>
    public static string LastPlayedTimes() => Fixture("clientgetlastplayedtimes-v1.json");

    /// <summary>Year in Review 2024 — the live envelope, with per-game monthly breakdowns.</summary>
    public static string YearInReview2024() => Fixture("getuseryearinreview-2024-v1.json");

    /// <summary>Year in Review 2025 — one game, one month, so a two-year merge is testable.</summary>
    public static string YearInReview2025() => Fixture("getuseryearinreview-2025-v1.json");

    /// <summary>
    /// Year in Review in the shape the spike's proto describes: months at the
    /// <c>playtime_stats</c> level, each carrying a per-appid array.
    /// </summary>
    public static string YearInReviewProtoMonths() => Fixture("getuseryearinreview-protomonths-v1.json");

    /// <summary>
    /// The bare envelope. For Year in Review this covers both "the account ran
    /// no Steam Replay that year" and "not your account", which are
    /// indistinguishable on the wire.
    /// </summary>
    public const string EmptyYearInReview = "{\"response\":{}}";

    /// <summary>A Year in Review for a DIFFERENT account than the one asked about.</summary>
    public const string ForeignYearInReview =
        "{\"response\":{\"stats\":{\"account_id\":22222222,\"year\":2024,\"playtime_stats\":{\"games\":"
        + "[{\"appid\":1203620,\"stats\":{\"total_playtime_seconds\":6000},\"rtime_first_played\":1704067200,"
        + "\"months\":[{\"rtime_month\":1704067200,\"stats\":{\"total_playtime_seconds\":6000}}]}]}}}}";

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "steam-web", name));

    /// <summary>One entry, with the optional fields optional exactly as Steam has them.</summary>
    public sealed record OwnedGameFixture(
        long AppId,
        string? Name = null,
        long PlaytimeForever = 0,
        long? PlaytimeTwoWeeks = null,
        long RtimeLastPlayed = 0,
        string? IconHash = "abc123")
    {
        public string ToJson()
        {
            var builder = new StringBuilder("{\"appid\":")
                .Append(AppId.ToString(CultureInfo.InvariantCulture));

            if (Name is not null)
            {
                builder.Append(",\"name\":\"").Append(Name.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }

            // Steam omits playtime_2weeks entirely when it is zero.
            if (PlaytimeTwoWeeks is { } recent)
            {
                builder.Append(",\"playtime_2weeks\":").Append(recent.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(",\"playtime_forever\":").Append(PlaytimeForever.ToString(CultureInfo.InvariantCulture));

            if (IconHash is not null)
            {
                builder.Append(",\"img_icon_url\":\"").Append(IconHash).Append('"');
            }

            // rtime_last_played is PRESENT and zero on never-played games, not absent.
            return builder
                .Append(",\"rtime_last_played\":")
                .Append(RtimeLastPlayed.ToString(CultureInfo.InvariantCulture))
                .Append('}')
                .ToString();
        }
    }
}
