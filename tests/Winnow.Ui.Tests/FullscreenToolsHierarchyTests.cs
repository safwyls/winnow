using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenToolsHierarchyTests
{
    [AvaloniaTheory]
    [InlineData(2560)]
    [InlineData(1280)]
    public void Journal_groups_keep_the_editor_and_rating_actions_reachable(int width)
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var prompt = new JournalPromptViewModel
        {
            Title = "The Long Journey Home — Definitive Collector's Edition",
            DurationText = "2h 10m", Note = "Return to the mountain camp before exploring the next valley."
        };
        using var page = new FullscreenSessionJournalPage(context, prompt);
        var window = new Window { Width = width, Height = width * 9 / 16, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var column = page.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "FullscreenSessionJournal");
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            Assert.Equal(3, column.Children.OfType<Border>().Count());
            var labels = column.Children.OfType<TextBlock>().Where(t => AutomationProperties.GetHeadingLevel(t) == 2).Select(t => t.Text).ToArray();
            Assert.Equal(new[] { "YOUR LAST SESSION", "JOURNAL", "RATING" }, labels);
            page.FocusInitial();
            Assert.Equal("Edit note", AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement())));
            page.Handle(GamepadButtons.Down);
            Assert.Equal("1 / 5", AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager.GetFocusedElement())));
            page.Handle(GamepadButtons.Down);
            var save = Assert.IsType<Button>(window.FocusManager.GetFocusedElement());
            Assert.Equal("Save", AutomationProperties.GetName(save));
            Dispatcher.UIThread.RunJobs();
            var position = save.TranslatePoint(default, scroll)!.Value;
            Assert.True(position.Y >= -1 && position.Y + save.Bounds.Height <= scroll.Viewport.Height + 1);
            page.FocusInitial(); Dispatcher.UIThread.RunJobs();
            var path = Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(path);
                window.CaptureRenderedFrame()!.Save(Path.Combine(path, $"fullscreen-journal-hierarchy-{width}.png"));
            }
        }
        finally { window.Close(); }
    }
}
