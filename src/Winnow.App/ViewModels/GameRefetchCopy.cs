namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the refetch-metadata row in the details modal's More menu and
/// its status line in Band 3. Pressing the row re-asks IGDB and the Steam
/// store about this one game. The status line is absent entirely when nothing
/// has been asked.
/// </summary>
public static class GameRefetchCopy
{
    /// <summary>Face of the menu row. One constant name in every state.</summary>
    public const string MenuLabel = "Refetch metadata";

    /// <summary>Tooltip on the menu row.</summary>
    public const string MenuTooltip = "Re-ask IGDB and the Steam store about this game";

    /// <summary>Status while the requests are in flight.</summary>
    public const string Running = "Refetching…";

    /// <summary>Status when the refetch wrote new metadata.</summary>
    public const string Updated = "Metadata updated.";

    /// <summary>Status when the sources answered with the same data Winnow already
    /// held. Not a failure.</summary>
    public const string NothingNew = "Checked. Nothing new from the sources.";

    /// <summary>Status when IGDB credentials are not configured.</summary>
    public const string NotConfigured = "IGDB credentials are not set up.";

    /// <summary>Status when a source could not be reached. Attention, not blame.</summary>
    public const string Unreachable = "A source could not be reached.";

    /// <summary>Status when the game has neither an IGDB id nor a Steam appid.</summary>
    public const string NoSourceToAsk = "This game has no IGDB id or Steam appid to look up.";

    /// <summary>Status when the game is no longer in the library.</summary>
    public const string WorkNotFound = "This game is no longer in your library.";

    /// <summary>Status when a second refetch is attempted inside the five-minute
    /// cooldown. Says when the next attempt is allowed, in whole minutes rounded
    /// up, or in seconds when under a minute.</summary>
    public static string TooSoon(TimeSpan retryAfter)
    {
        if (retryAfter.TotalMinutes < 1)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            return seconds == 1
                ? "Try again in 1 second."
                : $"Try again in {seconds} seconds.";
        }

        var minutes = (int)Math.Ceiling(retryAfter.TotalMinutes);
        return minutes == 1
            ? "Try again in 1 minute."
            : $"Try again in {minutes} minutes.";
    }
}
