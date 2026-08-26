using Avalonia.Controls;

namespace Hoard.App.Views;

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
}
