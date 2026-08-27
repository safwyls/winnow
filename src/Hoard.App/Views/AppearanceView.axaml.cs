using Avalonia.Controls;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the Appearance screen. There is deliberately none of it
/// beyond <c>InitializeComponent</c>: everything here is either static copy or
/// bound state on <see cref="ViewModels.AppearanceViewModel"/>, and picking a
/// theme is a command (§5.1).
/// </summary>
public partial class AppearanceView : UserControl
{
    public AppearanceView()
    {
        InitializeComponent();
    }

#if DEBUG
    /// <summary>
    /// Scrolls the screen to an absolute offset. Debug only, and it exists for
    /// the same reason <c>FilterPanelView.ScrollTo</c> does: YOUR THEMES sits
    /// under a row of five theme cards and is below the fold on an 820px
    /// window, so a screenshot cannot show the folder, the contrast report or
    /// the validation output without a scroll — and injected input is not
    /// trustworthy here (SetForegroundWindow fails silently on this machine).
    /// Same convention as --open-appearance: a flag that lands the window on the
    /// state to be reviewed rather than a synthetic click that may or may not
    /// have landed.
    /// </summary>
    public void ScrollTo(double y)
        => ScreenScroll.Offset = ScreenScroll.Offset.WithY(y);
#endif
}
