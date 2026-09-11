using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SessionRecoveryParityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Recovered_sitting_keeps_one_note_and_one_activity_record(bool fullscreen)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var sessions = new SessionRepository(db.Factory);
        // The fullscreen page's week follows the wall clock; keep this sitting in today's week.
        var started = DateTime.UtcNow;
        var process = new MonitoredProcessIdentity(100, started, "game");
        var checkpoint = await sessions.SaveMonitoredAsync(new Session
        {
            OwnershipId = 1, StartedAt = started, DetectionMethod = DetectionMethods.ProcessWatch, MonitorKey = "sitting",
        }, [process]);
        await sessions.SetNoteAsync(new SessionNote { SessionId = checkpoint.Id, Note = "Same sitting, same note", Rating = 4 });
        sessions = new SessionRepository(db.Factory);
        var recovered = Assert.IsType<Session>(await sessions.FindOpenMonitoredAsync(1, [process]));
        var owners = new OwnershipRepository(db.Factory);
        var updates = new UpdateEventRepository(db.Factory);
        var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), owners,
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), updates);
        await library.LoadCommand.ExecuteAsync(null);
        using var services = new ServiceCollection().AddSingleton<IOwnershipRepository>(owners)
            .AddSingleton<ISessionRepository>(sessions).AddSingleton<IUpdateEventRepository>(updates)
            .AddSingleton<IActivityRepository>(new ActivityRepository(db.Factory)).BuildServiceProvider();
        using var context = new FullscreenContext(library,
            new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        var journal = new GameJournalViewModel(await sessions.GetJournalEntriesByOwnershipAsync(1), true, sessions);
        var tracker = new ActivityTrackerViewModel([], await sessions.GetByOwnershipAsync(1),
            null, null, 0, started.AddMinutes(3)) { IsTrackedSessions = true };
        FullscreenActivityPage? page = fullscreen ? new FullscreenActivityPage(context) : null;
        var desktop = new StackPanel();
        desktop.Children.Add(new ActivityTrackerView { DataContext = tracker });
        desktop.Children.Add(new GameJournalView { DataContext = journal });
        var window = new Window { Width = 1920, Height = 1080, Content = (Control?)page ?? desktop };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (page is not null)
            {
                await page.PendingRefresh; Dispatcher.UIThread.RunJobs(); page.FocusInitial();
                Assert.Equal(checkpoint.Id, page.SelectedSessionId);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Duration not recorded", StringComparison.Ordinal) == true);
            }
            else
            {
                Assert.Empty(tracker.Series.Bars);
                Assert.Equal(checkpoint.Id, Assert.Single(journal.Entries).SessionId);
                Assert.Contains(desktop.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Same sitting, same note");
            }

            await sessions.SaveMonitoredAsync(recovered with { EndedAt = started.AddMinutes(10) }, [process]);
            var completed = Assert.Single(await sessions.GetByOwnershipAsync(1));
            Assert.Equal(checkpoint.Id, completed.Id);
            Assert.Equal(checkpoint.Id, Assert.Single(await sessions.GetJournalEntriesByOwnershipAsync(1)).SessionId);
            Assert.Equal(1, (await new LibraryHistoryStatsRepository(db.Factory).GetAsync()).SessionCount);
            if (page is not null)
            {
                await library.LoadCommand.ExecuteAsync(null);
                await page.PendingRefresh; Dispatcher.UIThread.RunJobs();
                Assert.Equal(checkpoint.Id, page.SelectedSessionId);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("0 h 10 m", StringComparison.Ordinal) == true);
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Same sitting, same note");
            }
            else
            {
                tracker.ApplySnapshot([], [completed], null, null, 0, started.AddMinutes(11), "");
                journal.ApplySnapshot(await sessions.GetJournalEntriesByOwnershipAsync(1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1d / 6, Assert.Single(tracker.Series.Bars).Hours, 8);
                Assert.Equal(checkpoint.Id, Assert.Single(journal.Entries).SessionId);
                Assert.Contains(desktop.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Same sitting, same note");
            }
        }
        finally { window.Close(); page?.Dispose(); }
    }
}
