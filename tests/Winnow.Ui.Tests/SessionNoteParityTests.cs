using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SessionNoteParityTests
{
    [AvaloniaTheory]
    [InlineData("desktop", false)]
    [InlineData("details", false)]
    [InlineData("activity", false)]
    [InlineData("desktop", true)]
    [InlineData("details", true)]
    [InlineData("activity", true)]
    public async Task Each_editor_rejects_empty_entries_and_trims_successful_notes(string surface, bool existing)
    {
        using var editor = new Editor(surface, existing);
        Assert.Equal("Journal note", AutomationProperties.GetName(editor.Field));
        Assert.Equal(existing ? 4 : 0, editor.Entry.DraftRating);
        if (surface != "desktop")
            Assert.Contains(editor.Window.GetVisualDescendants().OfType<TextBlock>(), block =>
                block.Text == (existing ? "Rating: 4 / 5" : "No rating"));
        editor.Field.Text = "  \r\n ";
        editor.Entry.ClearRatingCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        editor.Save();
        await editor.Entry.SaveCommand.ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(GameDetailsCopy.JournalEmptyEditProblem, editor.Entry.Problem);
        Assert.Equal(0, editor.Repository.Attempts);
        Assert.True(editor.Entry.IsEditing);
        Assert.Equal(0, editor.Backs);
        Assert.Contains(editor.Window.GetVisualDescendants().OfType<TextBlock>(), block =>
            block.Text == GameDetailsCopy.JournalEmptyEditProblem && block.IsEffectivelyVisible);

        editor.Field.Text = "  Remember the other route. \r\n ";
        editor.Entry.RateCommand.Execute("5");
        Dispatcher.UIThread.RunJobs();
        editor.Save();
        await editor.Entry.SaveCommand.ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
        var saved = Assert.Single(editor.Repository.Saved);
        Assert.Equal("Remember the other route.", saved.Note);
        Assert.Equal(5, saved.Rating);
        Assert.Equal(saved.Note, editor.Entry.Note);
        Assert.False(editor.Entry.IsEditing);
        Assert.Null(editor.Entry.Problem);
        Assert.Equal(surface == "desktop" ? 0 : 1, editor.Backs);
    }

    [AvaloniaTheory]
    [InlineData("desktop")]
    [InlineData("details")]
    [InlineData("activity")]
    public async Task Failed_saves_keep_the_draft_and_pending_retries_block_conflicting_edits(string surface)
    {
        using var editor = new Editor(surface, true);
        editor.Repository.Write = _ => throw new IOException("fixture write failed");
        editor.Field.Text = "  Keep my draft.  ";
        Dispatcher.UIThread.RunJobs();
        editor.Save();
        await editor.Entry.SaveCommand.ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(GameDetailsCopy.JournalSaveProblem, editor.Entry.Problem);
        Assert.Equal("  Keep my draft.  ", editor.Entry.DraftNote);
        Assert.Equal("Original note", editor.Entry.Note);
        Assert.True(editor.Entry.IsEditing);
        Assert.Equal(0, editor.Backs);
        Assert.Empty(editor.Repository.Saved);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Repository.Write = _ => gate.Task;
        try
        {
            editor.Save();
            var pending = editor.Entry.SaveCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.Entry.IsSaving);
            Assert.False(editor.Field.IsEffectivelyEnabled);
            Assert.All(editor.Window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible),
                button => Assert.False(button.IsEffectivelyEnabled));
            editor.Page?.Handle(GamepadButtons.Back);
            editor.Entry.CancelEditCommand.Execute(null);
            editor.Entry.RateCommand.Execute("2");
            await editor.Entry.SaveCommand.ExecuteAsync(null);
            Assert.Equal(2, editor.Repository.Attempts);
            Assert.Equal(4, editor.Entry.DraftRating);
            Assert.True(editor.Entry.IsEditing);
            Assert.Equal(0, editor.Backs);
            gate.SetResult();
            await pending;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Keep my draft.", Assert.Single(editor.Repository.Saved).Note);
            Assert.Equal(surface == "desktop" ? 0 : 1, editor.Backs);
        }
        finally { gate.TrySetResult(); }
    }

    [AvaloniaTheory]
    [InlineData("details")]
    [InlineData("activity")]
    public async Task A_save_finishing_after_the_editor_is_disposed_cannot_navigate(string surface)
    {
        using var editor = new Editor(surface, true);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        editor.Repository.Write = _ => gate.Task;
        try
        {
            editor.Save();
            var pending = editor.Entry.SaveCommand.ExecutionTask!;
            editor.Page!.Dispose();
            gate.SetResult();
            await pending;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(editor.Repository.Saved);
            Assert.Equal(0, editor.Backs);
            Assert.Equal(0, editor.Callbacks);
        }
        finally { gate.TrySetResult(); }
    }

    private sealed class Editor : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly FullscreenContext _context;
        public Editor(string surface, bool existing)
        {
            _services = new ServiceCollection().AddSingleton<ISessionRepository>(Repository).BuildServiceProvider();
            _context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, _services);
            _context.BackRequested += () => Backs++;
            var journal = new GameJournalViewModel([new SessionJournalEntry
            {
                SessionId = 7, OwnershipId = 3, SessionAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
                Note = existing ? "Original note" : null, Rating = existing ? 4 : null
            }], true, Repository);
            Entry = journal.Entries[0];
            Control content;
            if (surface == "activity")
            {
                Page = new FullscreenSessionNotePage(_context, 7, "A real session", existing ?
                    new SessionNote { SessionId = 7, Note = "Original note", Rating = 4 } : null, _ => Callbacks++);
                Entry = Assert.IsType<JournalEntryViewModel>(Page.DataContext);
                content = Page;
            }
            else if (surface == "details") content = Page = new FullscreenDetailsJournalPage(_context, Entry);
            else { Entry.EditCommand.Execute(null); content = new GameJournalView { DataContext = journal }; }
            Window = new Window { Width = 1920, Height = 1080, Content = content };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Field = Assert.Single(Window.GetVisualDescendants().OfType<TextBox>());
        }
        public JournalRepository Repository { get; } = new();
        public Window Window { get; }
        public FullscreenPage? Page { get; }
        public JournalEntryViewModel Entry { get; }
        public TextBox Field { get; }
        public int Backs { get; private set; }
        public int Callbacks { get; private set; }
        public void Save()
        {
            Dispatcher.UIThread.RunJobs();
            var save = Assert.Single(Window.GetVisualDescendants().OfType<Button>(), button =>
                button.IsEffectivelyVisible && Equals(button.Content, "Save"));
            Assert.True(save.Focus());
            if (Page is not null) Page.Handle(GamepadButtons.Accept);
            else
            {
                Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            }
        }
        public void Dispose() { Window.Close(); Page?.Dispose(); _context.Dispose(); _services.Dispose(); }
    }

    private sealed class JournalRepository : ISessionRepository
    {
        public int Attempts { get; private set; }
        public List<SessionNote> Saved { get; } = [];
        public Func<SessionNote, Task> Write { get; set; } = _ => Task.CompletedTask;
        public async Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
        { Attempts++; await Write(note); Saved.Add(note); }
        public Task<long> InsertAsync(Session session, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<Session?> GetAsync(long id, CancellationToken ct = default) => Task.FromResult<Session?>(null);
        public Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Session>>([]);
        public Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default) => Task.FromResult<SessionNote?>(null);
        public Task<IReadOnlyList<SessionJournalEntry>> GetJournalEntriesByOwnershipAsync(long ownershipId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionJournalEntry>>([]);
        public Task DeleteNoteAsync(long sessionId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
