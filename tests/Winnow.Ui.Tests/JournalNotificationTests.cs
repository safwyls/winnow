using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class JournalNotificationTests
{
    [AvaloniaTheory]
    [InlineData(false, JournalNotificationDelivery.Submitted)]
    [InlineData(true, JournalNotificationDelivery.Submitted)]
    [InlineData(false, JournalNotificationDelivery.Unavailable)]
    [InlineData(true, JournalNotificationDelivery.Unavailable)]
    [InlineData(false, JournalNotificationDelivery.Suppressed)]
    [InlineData(true, JournalNotificationDelivery.Suppressed)]
    [InlineData(false, JournalNotificationDelivery.Failed)]
    [InlineData(true, JournalNotificationDelivery.Failed)]
    public async Task Activation_or_fallback_uses_the_finished_session_and_only_save_writes(bool fullscreen, JournalNotificationDelivery delivery)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var sessions = new SessionRepository(db.Factory);
        var first = await sessions.InsertAsync(new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow.AddHours(-1),
            EndedAt = DateTime.UtcNow, DurationSeconds = 3600, DetectionMethod = DetectionMethods.ProcessWatch });
        var second = await sessions.InsertAsync(new Session { OwnershipId = 2, StartedAt = DateTime.UtcNow.AddHours(-1),
            EndedAt = DateTime.UtcNow, DurationSeconds = 3600, DetectionMethod = DetectionMethods.ProcessWatch });
        using var journal = new SessionJournalService(sessions);
        var notifications = new Notifications { Delivery = delivery };
        using var prompt = new JournalPromptViewModel(journal, post: action => action(), notifications: notifications);
        using var library = new LibraryViewModel(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory), journal: prompt);
        await library.LoadCommand.ExecuteAsync(null);
        var feed = new FeedViewModel(new PreviewFeedService(), library);
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(library, preview.MergeQueue, preview.Stores, preview.Appearance, feed, preview.AccountStats, preview.LibrarySettings);
        using var context = new FullscreenContext(library, feed, shell);
        using var tv = new FullscreenView(context);
        var window = fullscreen ? new Window { Width = 1920, Height = 1080, Content = tv }
            : new MainWindow { Width = 1200, Height = 900 };
        var activations = 0;
        prompt.ActivationRequested += () => activations++;
        try
        {
            // Startup belongs to separate tests. Attach the preloaded library after
            // opening so MainWindow's async startup cannot outlive this database.
            window.Show();
            if (!fullscreen) window.DataContext = shell;
            Dispatcher.UIThread.RunJobs();
            var ended = new EndedSession(first, 1, 3600);
            prompt.Offer(ended, "Finished game");
            prompt.Offer(ended, "Duplicate");
            Assert.Equal(1, notifications.Shows);
            Assert.Null(await sessions.GetNoteAsync(first));
            Assert.Null(await sessions.GetNoteAsync(second));
            if (delivery == JournalNotificationDelivery.Submitted)
            {
                Assert.False(prompt.IsOpen);
                notifications.Activate!();
                Assert.Equal(1, activations);
                notifications.Activate!();
                Assert.Equal(1, activations);
            }
            else Assert.Equal(0, activations);
            Dispatcher.UIThread.RunJobs();
            Assert.True(prompt.IsOpen);
            var field = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), field => field.IsEffectivelyVisible &&
                AutomationProperties.GetName(field) == (fullscreen ? "Journal note" : "How was it?"));
            field.Text = "Keep this exact session";
            prompt.RateCommand.Execute("4");
            prompt.Offer(new(second, 2, 3600), "Other game");
            Assert.Equal("Finished game", prompt.Title);
            var save = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible &&
                (fullscreen ? Equals(button.Content, "Save") : ReferenceEquals(button.Command, prompt.SaveCommand)));
            Assert.True(save.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await prompt.PendingSave;
            Dispatcher.UIThread.RunJobs();
            var note = Assert.IsType<SessionNote>(await sessions.GetNoteAsync(first));
            Assert.Equal("Keep this exact session", note.Note);
            Assert.Equal(4, note.Rating);
            Assert.Null(await sessions.GetNoteAsync(second));
            Assert.False(prompt.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Ignored_superseded_or_dismissed_notifications_do_not_reopen_an_old_session()
    {
        var notifications = new Notifications();
        using var prompt = new JournalPromptViewModel(post: action => action(), notifications: notifications);
        prompt.Offer(new(1, 1, 60), "First");
        var stale = notifications.Activate!;
        Assert.False(prompt.IsOpen);
        prompt.Offer(new(2, 2, 60), "Second");
        stale();
        Assert.False(prompt.IsOpen);
        notifications.Unavailable!();
        Assert.True(prompt.IsOpen);
        Assert.Equal("Second", prompt.Title);
        prompt.DismissCommand.Execute(null);
        notifications.Activate!();
        Assert.False(prompt.IsOpen);
    }

    [AvaloniaFact]
    public void A_headless_or_missing_native_window_reports_unavailable_without_an_application_error()
    {
        using var adapter = new WindowsJournalNotification();
        Assert.Equal(JournalNotificationDelivery.Unavailable, adapter.Show("Example", () => Assert.Fail(), () => Assert.Fail()));
        var window = new Window();
        adapter.Attach(window);
        Assert.Equal(JournalNotificationDelivery.Unavailable, adapter.Show("Example", () => Assert.Fail(), () => Assert.Fail()));
    }

    private sealed class Notifications : IJournalNotification
    {
        public JournalNotificationDelivery Delivery { get; init; } = JournalNotificationDelivery.Submitted;
        public int Shows { get; private set; }
        public Action? Activate { get; private set; }
        public Action? Unavailable { get; private set; }
        public JournalNotificationDelivery Show(string title, Action activate, Action unavailable)
        { Shows++; Activate = activate; Unavailable = unavailable; return Delivery; }
        public void Dismiss() { }
    }
}
