using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenSettingsHierarchyTests
{
    [AvaloniaTheory]
    [InlineData("library", 1)]
    [InlineData("library", 1.4)]
    [InlineData("credentials", 1)]
    [InlineData("credentials", 1.4)]
    [InlineData("artwork", 1)]
    [InlineData("artwork", 1.4)]
    public void Settings_groups_keep_text_within_their_reading_region_and_controls_reachable(string surface, double textScale)
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var originalScale = context.TextScale;
        context.TextScale = textScale;
        using var view = new FullscreenView(context);
        FullscreenPage page = surface switch
        {
            "library" => new FullscreenSettingsPage(context, "Library"),
            "credentials" => new FullscreenIgdbSettingsPage(context),
            _ => new FullscreenArtworkOrderPage(context)
        };
        context.Push(page);
        var window = new Window { Width = 2560, Height = 1440, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var reading = page.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(scroll => scroll.Content is StackPanel
                    && scroll.VerticalScrollBarVisibility != Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            Assert.Contains(reading.GetVisualDescendants().OfType<TextBlock>(),
                text => AutomationProperties.GetHeadingLevel(text) == 2);
            Assert.Contains(reading.GetVisualDescendants().OfType<Border>(),
                border => border.Height == 1 && border.Bounds.Width > 300);
            foreach (var text in reading.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible))
            {
                var point = text.TranslatePoint(default, reading)!.Value;
                Assert.True(point.X >= -1 && point.X + text.Bounds.Width <= reading.Bounds.Width + 1,
                    $"{text.Text} extends outside {surface}'s reading region.");
            }
            Capture(window, $"{surface}-{textScale:0.0}");
            foreach (var control in reading.GetVisualDescendants().OfType<Control>()
                         .Where(control => (control is Button or TextBox) && control.IsEffectivelyEnabled && control.IsEffectivelyVisible))
            {
                control.Focus();
                control.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                var point = control.TranslatePoint(default, reading)!.Value;
                Assert.True(point.Y >= -1 && point.Y + control.Bounds.Height <= reading.Bounds.Height + 1,
                    $"{AutomationProperties.GetName(control)} is clipped after focus.");
            }
        }
        finally { context.TextScale = originalScale; window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, $"fullscreen-settings-hierarchy-{name}.png"));
    }
}
