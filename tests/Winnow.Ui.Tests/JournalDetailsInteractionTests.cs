using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Core.Queries;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class JournalDetailsInteractionTests
{
    [AvaloniaFact]
    public async Task Details_modal_edits_then_deletes_a_journal_entry()
    {
        var now = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        var repository = new JournalRepository();
        var journal = new GameJournalViewModel(
        [
            new SessionJournalEntry
            {
                SessionId = 42,
                OwnershipId = 1,
                SessionAt = now.AddDays(-1),
                Note = "Looking for the key.",
                Rating = 3,
            },
        ], promptEnabled: true, repository);
        var tile = TileFixture.Tile(now, ownershipId: 1, bucket: LibraryBuckets.Bounced, title: "Bluebird");
        var model = new GameDetailsViewModel(tile, "Started", [], now, journal: journal) { SelectedTabIndex = 3 };
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 640, Content = view };
        window.Show();
        Flush();

        try
        {
            var deleting = Assert.Single(journal.Entries);
            var heading = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "JOURNAL");
            Assert.True(heading.IsEffectivelyVisible);
            var edit = Button(view, "Edit");
            Assert.Equal(new Thickness(9, 4), edit.Padding);
            Assert.Equal(new Thickness(9, 4), Button(view, "Delete").Padding);
            Click(edit, window);
            Flush();

            var note = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), box =>
                AutomationProperties.GetName(box) == "Journal note");
            Assert.True(note.IsEffectivelyVisible);
            note.Text = "Found the key behind the waterfall.";
            model.SelectedTabIndex = 4;
            Flush();
            model.SelectedTabIndex = 3;
            Flush();
            Assert.Equal("Found the key behind the waterfall.", journal.Entries[0].DraftNote);
            Assert.True(journal.Entries[0].IsEditing);
            Click(Button(view, "Save"), window);
            await journal.Entries[0].SaveCommand.ExecutionTask!;
            Flush();

            Assert.Equal("Found the key behind the waterfall.", journal.Entries[0].Note);
            Assert.Equal("Found the key behind the waterfall.", Assert.Single(repository.Saved).Note);

            Click(Button(view, "Delete"), window);
            Flush();
            Click(Button(view, "Delete"), window);
            await deleting.DeleteCommand.ExecutionTask!;
            Flush();

            Assert.Empty(journal.Entries);
            Assert.Equal([42L], repository.Deleted);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Details_modal_names_the_prompt_off_empty_state()
    {
        var now = DateTime.UtcNow;
        var journal = new GameJournalViewModel([], promptEnabled: false, new JournalRepository());
        var model = new GameDetailsViewModel(
            TileFixture.Tile(now, ownershipId: 1, bucket: LibraryBuckets.NeverPlayed, title: "Bluebird"),
            "Never played", [], now, journal: journal) { SelectedTabIndex = 3 };
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 640, Content = view };
        window.Show();
        Flush();
        try
        {
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.IsEffectivelyVisible && text.Text!.Contains("prompts are off"));
        }
        finally
        {
            window.Close();
        }
    }

    private static Button Button(Control root, string label)
        => Assert.Single(root.GetVisualDescendants().OfType<Button>(), button =>
            button.IsEffectivelyVisible && string.Equals(button.Content as string, label, StringComparison.Ordinal));

    private static void Click(Control control, Window window)
    {
        control.BringIntoView();
        Flush();
        var point = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    internal sealed class JournalRepository : ISessionRepository
    {
        public List<SessionNote> Saved { get; } = [];
        public List<long> Deleted { get; } = [];

        public Task<Session?> FindOpenMonitoredAsync(long ownershipId, IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<Session> SaveMonitoredAsync(Session session, IReadOnlyList<MonitoredProcessIdentity> processes, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<long> InsertAsync(Session session, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<Session?> GetAsync(long id, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Session>>([]);
        public Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
        {
            Saved.Add(note);
            return Task.CompletedTask;
        }
        public Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default)
            => Task.FromResult<SessionNote?>(null);
        public Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(
            long ownershipId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionJournalEntry>>([]);
        public Task DeleteNoteAsync(long sessionId, CancellationToken ct = default)
        {
            Deleted.Add(sessionId);
            return Task.CompletedTask;
        }
    }
}
