using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class GameJournalViewModelTests
{
    [Fact]
    public async Task Journal_edits_and_deletes_the_saved_entry()
    {
        var repository = new JournalRepository();
        var entry = new SessionJournalEntry
        {
            SessionId = 7,
            OwnershipId = 3,
            SessionAt = new DateTime(2026, 8, 22, 21, 0, 0, DateTimeKind.Utc),
            Note = "First route.",
            Rating = 2,
        };
        var journal = new GameJournalViewModel([entry], promptEnabled: true, repository);
        var row = Assert.Single(journal.Entries);

        row.EditCommand.Execute(null);
        row.DraftNote = "Found the shortcut.";
        row.RateCommand.Execute("4");
        await row.SaveCommand.ExecuteAsync(null);

        Assert.False(row.IsEditing);
        Assert.Equal("Found the shortcut.", row.Note);
        Assert.Equal(4, row.Rating);
        var saved = Assert.Single(repository.Saved);
        Assert.Equal(7, saved.SessionId);
        Assert.Equal("Found the shortcut.", saved.Note);
        Assert.Equal(4, saved.Rating);

        row.RequestDeleteCommand.Execute(null);
        Assert.True(row.IsConfirmingDelete);
        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(journal.Entries);
        Assert.Equal([7L], repository.Deleted);
    }

    [Fact]
    public void Empty_journal_names_whether_the_prompt_is_off()
    {
        var repository = new JournalRepository();

        Assert.Contains("No notes yet", new GameJournalViewModel([], true, repository).EmptyText);
        Assert.Contains("prompts are off", new GameJournalViewModel([], false, repository).EmptyText);
    }

    private sealed class JournalRepository : ISessionRepository
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
