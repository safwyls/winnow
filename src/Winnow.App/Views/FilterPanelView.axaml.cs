using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>
/// The filter panel. All behaviour is in
/// <see cref="ViewModels.Filters.FilterPanelViewModel"/> — the panel has no
/// state of its own, and deliberately no animation: a column that slides costs
/// the grid a reflow per frame, and §8's reduced-motion rule is a rule about
/// the animations that earn their place, not an invitation to add one here.
/// </summary>
public partial class FilterPanelView : UserControl
{
    public FilterPanelView() => InitializeComponent();

#if DEBUG
    /// <summary>
    /// Scrolls the panel to an absolute offset. Debug only, and it exists for
    /// one reason: the groups near the bottom of the column — FEATURES and
    /// CONTROLLER — are below the fold on an 820px window, so a screenshot of
    /// the panel cannot show them working without a scroll, and injected input
    /// is not trustworthy here (SetForegroundWindow fails silently on this
    /// machine). Same convention as --open-filters and --open-queue: a flag
    /// that lands the window on the state to be reviewed, rather than a
    /// synthetic click that may or may not have landed.
    /// </summary>
    public void ScrollTo(double y)
        => PanelScroll.Offset = PanelScroll.Offset.WithY(y);
#endif
}
