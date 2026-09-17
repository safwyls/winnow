using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Winnow.App.Views;

public partial class PluginTabStrip : UserControl
{
    public PluginTabStrip()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => UpdateOverflow();
        PluginTabScroll.ScrollChanged += (_, _) => UpdateOverflow();
        PluginTabs.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(RevealSelection, DispatcherPriority.Loaded);
        PluginTabs.AddHandler(KeyDownEvent, OnTabsKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => Dispatcher.UIThread.Post(RevealSelection, DispatcherPriority.Loaded);
    }

    private void UpdateOverflow()
    {
        // Compare against the whole strip, not the smaller viewport after arrows appear.
        var overflow = PluginTabScroll.Extent.Width > Bounds.Width + 1;
        PreviousPluginTabs.IsVisible = NextPluginTabs.IsVisible = overflow;
        PreviousPluginTabs.IsEnabled = PluginTabScroll.Offset.X > 1;
        NextPluginTabs.IsEnabled = PluginTabScroll.Offset.X < PluginTabScroll.Extent.Width - PluginTabScroll.Viewport.Width - 1;
    }

    private void RevealSelection() => PluginTabs.GetVisualDescendants().OfType<TabItem>()
        .FirstOrDefault(tab => tab.IsSelected)?.BringIntoView();

    private void OnTabsKeyDown(object? sender, KeyEventArgs e)
    {
        var index = e.Key switch
        {
            Key.Left => Math.Max(0, PluginTabs.SelectedIndex - 1),
            Key.Right => Math.Min(PluginTabs.ItemCount - 1, PluginTabs.SelectedIndex + 1),
            Key.Home => 0,
            Key.End => PluginTabs.ItemCount - 1,
            _ => -1
        };
        if (index < 0) return;
        PluginTabs.SelectedIndex = index;
        PluginTabs.ContainerFromIndex(index)?.Focus();
        RevealSelection();
        e.Handled = true;
    }

    private void OnPrevious(object? sender, RoutedEventArgs e) => Scroll(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => Scroll(1);
    private void Scroll(int direction)
    {
        var maximum = Math.Max(0, PluginTabScroll.Extent.Width - PluginTabScroll.Viewport.Width);
        PluginTabScroll.Offset = new Vector(Math.Clamp(PluginTabScroll.Offset.X + direction * PluginTabScroll.Viewport.Width * .75, 0, maximum), 0);
    }
}
