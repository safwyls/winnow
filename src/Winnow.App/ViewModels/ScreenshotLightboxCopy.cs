namespace Winnow.App.ViewModels;

/// <summary>
/// Copy for the screenshot lightbox, the full-window overlay a thumbnail in
/// ABOUT opens. It dims everything behind it, gives the shot the whole frame,
/// and carries a close control and left/right navigation.
/// </summary>
public static class ScreenshotLightboxCopy
{
    /// <summary>
    /// Accessible name for the overlay itself. It names the surface as a dialog
    /// and states which shot of how many is showing, because Avalonia 11.3.20
    /// has no <c>AutomationProperties.IsDialog</c> and <c>PositionInSet</c> /
    /// <c>SizeOfSet</c> are read by nothing (design-system.md §8).
    /// </summary>
    public static string DialogAutomationName(int position, int count) =>
        $"Screenshot dialog, {position} of {count}";

    /// <summary>
    /// The line under the image, stating the position and the count. It is a
    /// bound <c>TextBlock</c> marked as a live region, so navigating
    /// re-announces it; changing the overlay's own <c>Name</c> raises no UIA
    /// event (design-system.md §8, §10.3).
    /// </summary>
    public static string Caption(int position, int count) =>
        $"{position} of {count}";

    /// <summary>Accessible name for the close control.</summary>
    public const string CloseAutomationName = "Close";

    /// <summary>
    /// Tooltip on the close control. Escape closes the lightbox and leaves the
    /// details modal up, so naming the key here is correct.
    /// </summary>
    public const string CloseTooltip = "Close (Escape)";

    /// <summary>
    /// Accessible name for the previous-shot control. Navigation wraps, so from
    /// the first shot it goes to the last.
    /// </summary>
    public const string PreviousAutomationName = "Previous screenshot";

    /// <summary>Tooltip on the previous-shot control.</summary>
    public const string PreviousTooltip = "Previous (Left arrow)";

    /// <summary>
    /// Accessible name for the next-shot control. Navigation wraps, so from the
    /// last shot it goes to the first.
    /// </summary>
    public const string NextAutomationName = "Next screenshot";

    /// <summary>Tooltip on the next-shot control.</summary>
    public const string NextTooltip = "Next (Right arrow)";
}
