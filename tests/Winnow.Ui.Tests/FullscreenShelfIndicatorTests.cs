using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenShelfIndicatorTests
{
    [AvaloniaFact]
    public void Shelf_targets_select_by_mouse_without_taking_controller_focus()
    {
        var selected = -1;
        var indicator = new FullscreenShelfIndicator(["Patched", "Forgotten", "Unplayed"], 1, index => selected = index);
        var window = new Window { Width = 200, Height = 400, Content = indicator };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var buttons = indicator.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(5, buttons.Length);
            Assert.All(buttons, button => Assert.False(button.Focusable));
            Assert.Equal("Current shelf", AutomationProperties.GetItemStatus(buttons[2]));
            Assert.Equal("Unplayed, shelf 3 of 3", AutomationProperties.GetName(buttons[3]));
            foreach (var (button, expected) in new[] { (buttons[0], 0), (buttons[3], 2), (buttons[4], 2) })
            {
                var center = button.TranslatePoint(new Point(22, 22), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                Assert.Equal(expected, selected);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, false, true)]
    [InlineData(5, true, false)]
    public void Endpoint_arrows_are_disabled_and_all_shelf_dots_remain_visible(int selected, bool previous, bool next)
    {
        var indicator = new FullscreenShelfIndicator(["One", "Two", "Three", "Four", "Five", "Six"], selected, _ => { });
        var window = new Window { Width = 100, Height = 400, Content = indicator };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var buttons = indicator.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(8, buttons.Length);
            Assert.Equal(previous, buttons[0].IsEnabled);
            Assert.Equal(next, buttons[^1].IsEnabled);
            Assert.All(buttons, button =>
            {
                Assert.True(button.IsVisible);
                Assert.Equal(44, button.Bounds.Height);
            });
            Assert.Equal(380, indicator.Bounds.Height);
        }
        finally { window.Close(); }
    }
}
