namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the Band 2 play-history axis in the details modal. The axis runs
/// from the game's release year to today in two zones: a flat band for the
/// unmeasured total before monthly coverage, and per-month bars on the dormancy
/// ramp for the measured period. Everything drawn is the user's own hours.
/// </summary>
public static class PlayAxisCopy
{
    /// <summary>Accessible group name for the whole Band 2 history block.</summary>
    public const string AxisAutomationName = "Play history";

    /// <summary>Standing attribution under the axis. States that these are the
    /// user's own hours.</summary>
    public const string OwnHoursNote = "Your hours, month by month.";

    /// <summary>The sentence about the left zone, where the total is one figure
    /// rather than a monthly shape. <paramref name="amountText"/> and
    /// <paramref name="coverageStartText"/> arrive already formatted.</summary>
    public static string UnmeasuredNote(string amountText, string coverageStartText) =>
        $"{amountText} before {coverageStartText}, shown as one total. Winnow was not tracking months then.";

    /// <summary>One line naming the last session, the idle span, and how many
    /// updates landed since. A zero count reads as no updates recorded, never as
    /// nothing shipped (section 10.4).</summary>
    public static string LastSessionLine(string lastPlayedText, string idleText, int updateCount) =>
        updateCount switch
        {
            0 => $"Last played {lastPlayedText}, {idleText} ago. No updates recorded in that stretch.",
            1 => $"Last played {lastPlayedText}, {idleText} ago. 1 update landed since.",
            _ => $"Last played {lastPlayedText}, {idleText} ago. {updateCount} updates landed since.",
        };

    /// <summary>Tooltip on the axis control, naming the two zones.</summary>
    public const string AxisTooltip =
        "Flat band: total hours before monthly tracking. Bars: recorded hours per month.";
}
