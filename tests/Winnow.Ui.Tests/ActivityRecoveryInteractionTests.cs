using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ActivityRecoveryInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_reads_show_a_retry_action_and_retry_the_same_page(bool append)
    {
        var calls = new List<ActivityCursor?>();
        var fail = true;
        var reader = new Reader((ids, start, cursor, _) =>
        {
            calls.Add(cursor);
            if ((!append || cursor is not null) && fail) { fail = false; throw new IOException("injected"); }
            return Task.FromResult(Page(ids.First(), start, cursor is null ? 1 : 2, next: append && cursor is null));
        });
        using var fixture = new Fixture(reader);
        await fixture.LoadAsync();
        var window = Show(fixture.Page);
        try
        {
            await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            if (append)
            {
                Click(fixture.Page, "Load more"); await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, fixture.Page.SelectedSessionId);
            }
            Assert.Contains(fixture.Page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Couldn't read your activity. Try again.");
            Click(fixture.Page, "Try again"); await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(fixture.Page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Couldn't read", StringComparison.Ordinal) == true);
            Assert.Equal(calls[^2], calls[^1]);
            Assert.Equal(append ? 2 : 1, SessionButtons(fixture.Page).Length);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Finishing_a_read_preserves_the_section_tab_chosen_while_waiting()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new Reader(async (ids, start, _, _) => { entered.TrySetResult(); await release.Task; return Page(ids.First(), start, 1); });
        using var fixture = new Fixture(reader);
        await fixture.LoadAsync();
        var window = Show(fixture.Page);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(FindButton(fixture.Page, "Updates").Focus());
            release.TrySetResult(); await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.Equal("Updates", AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement())));
        }
        finally { release.TrySetResult(); window.Close(); await fixture.Page.PendingRefresh; }
    }

    [AvaloniaFact]
    public async Task Saving_a_note_on_an_older_loaded_page_keeps_its_selection_and_does_not_refetch_page_one()
    {
        var calls = 0;
        var reader = new Reader((ids, start, cursor, _) =>
        {
            calls++;
            return Task.FromResult(Page(ids.First(), start, cursor is null ? 1 : 2, next: cursor is null));
        });
        using var fixture = new Fixture(reader);
        await fixture.LoadAsync();
        await fixture.Sessions.InsertAsync(new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow, DetectionMethod = "manual" });
        await fixture.Sessions.InsertAsync(new Session { OwnershipId = 1, StartedAt = DateTime.UtcNow, DetectionMethod = "manual" });
        var window = Show(fixture.Page);
        FullscreenSessionNotePage? editor = null;
        fixture.Context.PageRequested += page => { editor = Assert.IsType<FullscreenSessionNotePage>(page); window.Content = editor; };
        fixture.Context.BackRequested += () => window.Content = fixture.Page;
        try
        {
            await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Click(fixture.Page, "Load more"); await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.True(SessionButtons(fixture.Page).Last().Focus());
            Assert.Equal(2, fixture.Page.SelectedSessionId);
            fixture.Page.Handle(GamepadButtons.Play); Dispatcher.UIThread.RunJobs();
            Assert.NotNull(editor);
            var field = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>());
            field.Text = "Continue from the old session.";
            Click(editor, "Save");
            var entry = Assert.IsType<JournalEntryViewModel>(editor.DataContext);
            await entry.SaveCommand.ExecutionTask!; Dispatcher.UIThread.RunJobs();
            await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.Same(fixture.Page, window.Content);
            Assert.Equal(2, calls);
            Assert.Equal(2, fixture.Page.SelectedSessionId);
            Assert.Equal("Continue from the old session.", (await fixture.Sessions.GetNoteAsync(2))!.Note);
            Assert.Contains(fixture.Page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Continue from the old session.");
            Assert.Contains(fixture.Page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Journal entry");
        }
        finally { editor?.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_library_refresh_while_the_note_editor_is_open_keeps_its_unsaved_text()
    {
        var reader = new Reader((ids, start, _, _) => Task.FromResult(Page(ids.First(), start, 1)));
        using var fixture = new Fixture(reader);
        await fixture.LoadAsync();
        var window = Show(fixture.Page);
        FullscreenSessionNotePage? editor = null;
        fixture.Context.PageRequested += page => { editor = Assert.IsType<FullscreenSessionNotePage>(page); window.Content = editor; };
        try
        {
            await fixture.Page.PendingRefresh; Dispatcher.UIThread.RunJobs();
            Assert.True(SessionButtons(fixture.Page).Single().Focus()); fixture.Page.Handle(GamepadButtons.Play); Dispatcher.UIThread.RunJobs();
            var field = Assert.Single(editor!.GetVisualDescendants().OfType<TextBox>());
            field.Text = "An unfinished thought";
            await fixture.Library.LoadCommand.ExecuteAsync(null); Dispatcher.UIThread.RunJobs();
            Assert.Same(editor, window.Content);
            Assert.Equal("An unfinished thought", field.Text);
        }
        finally { editor?.Dispose(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Account_summary_reports_loading_and_retry_without_losing_focus_or_publishing_after_disposal(bool dispose)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var stats = new StatsReader(async () =>
        {
            if (++calls == 1)
            {
                entered.TrySetResult(); await release.Task;
                if (!dispose) throw new IOException("injected");
            }
            return new AccountStats { Source = "steam", TransactionCount = 1 };
        });
        using var fixture = new Fixture(new Reader((_, _, _, _) => Task.FromResult(new ActivityPage([], null))), stats);
        using var summary = new FullscreenLibrarySummaryPage(fixture.Context);
        var window = Show(summary);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(summary.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Reading your account statistics…");
            Assert.True(FindButton(summary, "Back").Focus());
            var originalContent = summary.Content;
            if (dispose) summary.Dispose();
            release.TrySetResult(); await summary.PendingRefresh; Dispatcher.UIThread.RunJobs();
            if (dispose) Assert.Same(originalContent, summary.Content);
            else
            {
                Assert.Contains(summary.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Couldn't read account statistics. Try again.");
                Assert.Equal("Back", AutomationProperties.GetName(Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement())));
                Click(summary, "Try again"); await summary.PendingRefresh; Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, calls);
                Assert.DoesNotContain(summary.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Couldn't read", StringComparison.Ordinal) == true);
                Assert.Contains(summary.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == new AccountStatsViewModel(stats).SpendHeading);
                var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
                Assert.True(focused.IsEffectivelyEnabled);
                Assert.Contains(summary, focused.GetVisualAncestors());
            }
        }
        finally { release.TrySetResult(); window.Close(); await summary.PendingRefresh; }
    }

    private static ActivityPage Page(long owner, DateTime start, long id, bool next = false) => new([
        new(owner, "steam", start.AddDays(1), new Session { Id = id, OwnershipId = owner, StartedAt = start.AddDays(1), DurationSeconds = id * 60, DetectionMethod = "manual" }, null, null)
    ], next ? new(start.AddDays(1), id) : null);
    private static Window Show(Control page) { var window = new Window { Width = 1920, Height = 1080, Content = page }; window.Show(); Dispatcher.UIThread.RunJobs(); return window; }
    private static Button FindButton(Control page, string name) => Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == name);
    private static void Click(Control page, string name) { var button = FindButton(page, name); Assert.True(button.Focus()); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static Button[] SessionButtons(Control page) => page.GetVisualDescendants().OfType<Button>().Where(button => button.MinHeight == 116).ToArray();

    private sealed class Fixture : IDisposable
    {
        private readonly TempDatabase _database = new();
        private readonly ServiceProvider _services;
        public LibraryViewModel Library { get; }
        public SessionRepository Sessions { get; }
        public FullscreenContext Context { get; }
        public FullscreenActivityPage Page { get; }
        public Fixture(IActivityRepository reader, IAccountStatsRepository? stats = null)
        {
            LibraryReadFixtures.Seed(_database, 3);
            Sessions = new(_database.Factory);
            Library = new(new LibraryQueryRepository(_database.Factory), new OwnershipRepository(_database.Factory), new ReleaseRepository(_database.Factory), new WorkRepository(_database.Factory), new UpdateEventRepository(_database.Factory));
            var registrations = new ServiceCollection().AddSingleton(reader).AddSingleton<ISessionRepository>(Sessions);
            if (stats is not null) registrations.AddSingleton(stats);
            _services = registrations.BuildServiceProvider();
            Context = new(Library, new FeedViewModel(new PreviewFeedService(), Library), PreviewData.Shell, _services);
            Page = new(Context);
        }
        public Task LoadAsync() => Library.LoadCommand.ExecuteAsync(null);
        public void Dispose() { Page.Dispose(); Context.Dispose(); Context.Feed.Dispose(); Library.Dispose(); _services.Dispose(); _database.Dispose(); }
    }
    private sealed class Reader(Func<IReadOnlyCollection<long>, DateTime, ActivityCursor?, CancellationToken, Task<ActivityPage>> read) : IActivityRepository
    {
        public Task<ActivityPage> GetPageAsync(IReadOnlyCollection<long> ownershipIds, DateTime fromUtc, DateTime untilUtc, ActivitySection section, ActivityCursor? after = null, int pageSize = 50, CancellationToken ct = default)
            => read(ownershipIds, fromUtc, after, ct);
    }
    private sealed class StatsReader(Func<Task<AccountStats>> read) : IAccountStatsRepository
    {
        public Task<AccountStats> GetAsync(string source, CancellationToken ct = default) => read();
    }
}
