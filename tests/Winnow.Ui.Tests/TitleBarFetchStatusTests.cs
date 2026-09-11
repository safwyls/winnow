using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class TitleBarFetchStatusTests
{
    [AvaloniaFact]
    public void Fetch_progress_fits_caption_updates_and_survives_fullscreen_round_trip()
    {
        var shell = PreviewData.Shell;
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 688 };
        try
        {
            shell.Fetch.Clear();
            window.Show();
            var status = window.FindControl<Border>("TitleBarFetchStatus")!;
            var caption = window.FindControl<Border>("TitleBar")!;
            Assert.False(status.IsVisible);

            shell.Fetch.Begin(997);
            Dispatcher.UIThread.RunJobs();
            Assert.True(status.IsEffectivelyVisible);
            Assert.Contains(caption, status.GetVisualAncestors());
            Assert.False(status.IsHitTestVisible);
            Assert.Equal("Fetching details, 997 titles left", AutomationProperties.GetName(status));
            Assert.InRange(status.Bounds.Height, 1, caption.Bounds.Height);
            Assert.True(status.Bounds.Width >= status.DesiredSize.Width - status.Margin.Left - status.Margin.Right);

            window.ToggleFullscreen();
            Assert.False(status.IsEffectivelyVisible);
            shell.Fetch.Report(1);
            window.ToggleFullscreen();
            Dispatcher.UIThread.RunJobs();
            Assert.True(status.IsEffectivelyVisible);
            Assert.Equal("Fetching details, 1 title left", AutomationProperties.GetItemStatus(status));
            Assert.Contains(status.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "title left");

            shell.Fetch.Report(0);
            Dispatcher.UIThread.RunJobs();
            Assert.False(status.IsVisible);
        }
        finally
        {
            shell.Fetch.Clear();
            window.ExitFromTray();
        }
    }
}
