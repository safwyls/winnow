using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Views.Fullscreen;
using Winnow.App.ViewModels;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenActivityTests
{
    [AvaloniaFact]
    public async Task Empty_sections_explain_their_content_and_horizontal_navigation_changes_weeks_outside_tabs()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 4);
        using var services = new ServiceCollection().AddSingleton<IOwnershipRepository>(new OwnershipRepository(db.Factory))
            .AddSingleton<ISessionRepository>(new SessionRepository(db.Factory)).BuildServiceProvider();
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        using var page = new FullscreenActivityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); await page.PendingRefresh; Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No sessions this week");
            page.Handle(GamepadButtons.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, page.WeekOffset);
            page.Handle(GamepadButtons.Right); page.Handle(GamepadButtons.Right); Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, page.WeekOffset);
            page.Handle(GamepadButtons.Up); Dispatcher.UIThread.RunJobs();
            page.Handle(GamepadButtons.Right);
            Assert.Equal(0, page.WeekOffset);
            page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Journal")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No journal entries this week");
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No sessions this week");
            Assert.Equal("LT / RT  Change week", page.RightHints);
            Assert.DoesNotContain("LT", page.Hints);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Library_reload_refreshes_notes_preserves_session_and_removes_hidden_games()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 4);
        var owners = new OwnershipRepository(db.Factory);
        var sessions = new SessionRepository(db.Factory);
        var hidden = new HiddenGameRepository(db.Factory);
        var updates = new UpdateEventRepository(db.Factory);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), owners, new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), updates, hidden: hidden);
        await library.LoadCommand.ExecuteAsync(null);
        await sessions.InsertAsync(new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow, DetectionMethod = "manual" });
        var selectedId = await sessions.InsertAsync(new Session { OwnershipId = 2, StartedAt = DateTime.UtcNow.AddMinutes(-1), DetectionMethod = "manual" });
        using var services = new ServiceCollection().AddSingleton<IOwnershipRepository>(owners).AddSingleton<ISessionRepository>(sessions).AddSingleton<IUpdateEventRepository>(updates).BuildServiceProvider();
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        var page = new FullscreenActivityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            page.FocusInitial(); page.Handle(GamepadButtons.Down);
            Assert.Equal(selectedId, page.SelectedSessionId);
            await sessions.SetNoteAsync(new SessionNote { SessionId = selectedId, Note = "A newly saved note" });
            await library.LoadCommand.ExecuteAsync(null); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.Equal(selectedId, page.SelectedSessionId);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "A newly saved note");
            await hidden.HideAsync(2);
            await library.LoadCommand.ExecuteAsync(null); await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(selectedId, page.SelectedSessionId);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Game 2" || text.Text == "A newly saved note");
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Session_editor_saves_to_the_same_journal_repository_and_keeps_existing_rating()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 4);
        var owners = new OwnershipRepository(db.Factory);
        var repository = new SessionRepository(db.Factory);
        var owner = (await owners.GetAllAsync())[0];
        var id = await repository.InsertAsync(new Session { OwnershipId = owner.Id, StartedAt = DateTime.UtcNow, DetectionMethod = "manual" });
        await repository.SetNoteAsync(new SessionNote { SessionId = id, Note = "Old note", Rating = 4 });
        using var services = new ServiceCollection().AddSingleton<ISessionRepository>(repository).BuildServiceProvider();
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        SessionNote? saved = null;
        var page = new FullscreenSessionNotePage(context, id, "A real session", await repository.GetNoteAsync(id), note => saved = note);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Single(page.GetVisualDescendants().OfType<TextBox>()).Text = "Try the other route next time.";
            page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && saved is null; i++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.NotNull(saved);
            var actual = await repository.GetNoteAsync(id);
            Assert.Equal("Try the other route next time.", actual!.Note);
            Assert.Equal(4, actual.Rating);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public void Missing_activity_repository_is_an_unavailable_state_not_an_empty_history_claim()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var page = new FullscreenActivityPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), block => block.Text?.Contains("Activity is unavailable", StringComparison.Ordinal) == true);
        }
        finally { window.Close(); page.Dispose(); }
    }
}
