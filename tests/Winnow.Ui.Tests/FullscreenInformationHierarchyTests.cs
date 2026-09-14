using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenInformationHierarchyTests
{
    [AvaloniaTheory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void Reading_has_a_bounded_measure_and_keeps_navigation_outside_scrolling_text(int width, int height)
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var prose = string.Join("\n\n", Enumerable.Repeat("A journal entry about returning to a familiar game. The quieter moments made this session memorable, and there is still another path to explore.", 24));
        using var page = new FullscreenDetailsReadingPage(context, "A journey worth returning to", prose);
        var window = new Window { Width = width, Height = height, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            var scroll = Assert.Single(page.GetVisualDescendants().OfType<ScrollViewer>());
            var text = Assert.IsType<TextBlock>(scroll.Content);
            Assert.True(text.Bounds.Width <= 1200);
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var back = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => Avalonia.Automation.AutomationProperties.GetName(button) == "Back");
            Assert.Same(back, window.FocusManager!.GetFocusedElement());
            page.Handle(GamepadButtons.Down); Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
            Assert.Same(back, window.FocusManager.GetFocusedElement());
            page.Handle(GamepadButtons.Up); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroll.Offset.Y);
            Capture(window, $"fullscreen-information-reading-{width}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Activity_rows_separate_titles_dates_and_notes_while_retaining_selection()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 4);
        var owners = new OwnershipRepository(db.Factory);
        var sessions = new SessionRepository(db.Factory);
        var updates = new UpdateEventRepository(db.Factory);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), owners, new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), updates);
        await library.LoadCommand.ExecuteAsync(null);
        var id = await sessions.InsertAsync(new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow, DurationSeconds = 5400, DetectionMethod = "manual" });
        await sessions.SetNoteAsync(new SessionNote { SessionId = id, Note = "Found a new path through the old ruins. Next time, return to the gate above the river.", Rating = 4 });
        using var services = new ServiceCollection().AddSingleton<IOwnershipRepository>(owners)
            .AddSingleton<IActivityRepository>(new ActivityRepository(db.Factory)).AddSingleton<ISessionRepository>(sessions).BuildServiceProvider();
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        using var page = new FullscreenActivityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); await page.PendingRefresh; Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            Assert.Equal(id, page.SelectedSessionId);
            var row = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => Avalonia.Automation.AutomationProperties.GetAutomationId(button) == $"activity-session-{id}");
            var title = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), block => block.FontSize == 32 && block.Text == library.AllTiles.Single(tile => tile.OwnershipIds.Contains(1)).Title);
            Assert.Equal(FontWeight.Bold, title.FontWeight);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "YOUR NOTE");
            Capture(window, "fullscreen-information-activity");
            page.Handle(GamepadButtons.PageNext); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "No updates this week");
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }
}
