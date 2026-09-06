namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the screenshot strip inside ABOUT. A horizontal row of thumbnails
/// from IGDB; picking one opens the lightbox overlay, which gives the shot the
/// whole window. A game with no screenshots draws nothing at all.
/// </summary>
public static class GameScreenshotsCopy
{
    /// <summary>The line under the strip, attributing the images and stating the count.</summary>
    public static string Caption(int count) =>
        count == 1 ? "1 screenshot from IGDB" : $"{count} screenshots from IGDB";

    /// <summary>Accessible name for the thumbnail strip as a list.</summary>
    public const string ListAutomationName = "Screenshots";

    /// <summary>Accessible name for one thumbnail. Position and total are spelled
    /// into the string because Avalonia's PositionInSet and SizeOfSet are wired to
    /// nothing.</summary>
    public static string ThumbnailAutomationName(int position, int count) =>
        $"Screenshot {position} of {count}";

    /// <summary>Tooltip on a thumbnail, stating what pressing it does and
    /// the shot's position in the strip.</summary>
    public static string ThumbnailTooltip(int position, int count) =>
        $"View screenshot {position} of {count}";
}
