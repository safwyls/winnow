using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenSectionHintTests
{
    [AvaloniaTheory]
    [InlineData("Library", 1d)]
    [InlineData("Library", 1.4d)]
    [InlineData("Activity", 1d)]
    [InlineData("Activity", 1.4d)]
    [InlineData("Settings", 1d)]
    [InlineData("Settings", 1.4d)]
    public async Task Trigger_hints_remain_visible_around_sections_when_navigating(string section, double scale)
    {
        using var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        await library.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell) { TextScale = scale };
        using var shell = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = shell };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            FullscreenPage page = section switch
            {
                "Library" => new FullscreenBrowsePage(context, false),
                "Activity" => new FullscreenActivityPage(context),
                _ => new FullscreenSettingsPage(context)
            };
            context.Push(page); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Empty(page.RightHints);
            Check();
            Assert.True(page.Handle(GamepadButtons.PageNext)); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Check();
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(directory, $"section-hints-{section}-{scale * 100:0}.png"));
            }
            void Check()
            {
                var row = page.GetVisualDescendants().OfType<Grid>().Single(x => x.Name == "FullscreenSectionNavigation");
                var left = row.GetVisualDescendants().OfType<Control>().Single(x => x.Name == "SectionPreviousTrigger");
                var right = row.GetVisualDescendants().OfType<Control>().Single(x => x.Name == "SectionNextTrigger");
                Assert.True(left.IsEffectivelyVisible && right.IsEffectivelyVisible);
                Assert.False(left.Focusable || right.Focusable);
                Assert.True(left.Bounds.Right < right.Bounds.Left);
                Assert.True(right.TranslatePoint(default, page)!.Value.X + right.Bounds.Width <= page.Bounds.Width + 1);
            }
        }
        finally { window.Close(); }
    }
}
