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
