using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>Saved post-session notes for the game currently open in the details modal.</summary>
public sealed partial class GameJournalViewModel : ObservableObject
{
    private readonly ISessionRepository _sessions;

    public GameJournalViewModel(
        IEnumerable<SessionJournalEntry> entries,
        bool promptEnabled,
        ISessionRepository sessions)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _sessions = sessions;
        PromptEnabled = promptEnabled;
        Entries = new ObservableCollection<JournalEntryViewModel>(
            entries
                .OrderByDescending(entry => entry.SessionAt)
                .ThenByDescending(entry => entry.SessionId)
                .Select(entry => new JournalEntryViewModel(entry, this)));
    }

    public ObservableCollection<JournalEntryViewModel> Entries { get; }

    public bool PromptEnabled { get; }

    public bool HasEntries => Entries.Count > 0;

    /// <summary>The two empty states say whether a future note can appear on its own.</summary>
    public string EmptyText => PromptEnabled
        ? GameDetailsCopy.JournalEmptyPromptOn
        : GameDetailsCopy.JournalEmptyPromptOff;

    internal ISessionRepository Sessions => _sessions;

    internal void ApplySnapshot(IReadOnlyList<SessionJournalEntry> entries)
    {
        var existing = Entries.ToDictionary(entry => entry.SessionId);
        var ordered = entries.OrderByDescending(entry => entry.SessionAt).ThenByDescending(entry => entry.SessionId)
            .Select(entry =>
            {
                if (!existing.TryGetValue(entry.SessionId, out var current)) return new JournalEntryViewModel(entry, this);
                current.RefreshSaved(entry);
                return current;
            }).ToList();
        // An external deletion must not discard a draft or detach a write already in flight.
        ordered.AddRange(Entries.Where(entry => (entry.IsEditing || entry.IsSaving) && !ordered.Contains(entry)));
        foreach (var removed in Entries.Except(ordered).ToArray()) Entries.Remove(removed);
        for (var i = 0; i < ordered.Count; i++)
        {
            var from = Entries.IndexOf(ordered[i]);
            if (from < 0) Entries.Insert(i, ordered[i]);
            else if (from != i) Entries.Move(from, i);
        }
        OnPropertyChanged(nameof(HasEntries));
    }

    internal async Task DeleteAsync(JournalEntryViewModel entry, CancellationToken ct)
    {
        await _sessions.DeleteNoteAsync(entry.SessionId, ct);
        Entries.Remove(entry);
        OnPropertyChanged(nameof(HasEntries));
    }
}

/// <summary>One session note, with an inline draft so cancelling an edit leaves the saved text alone.</summary>
public sealed partial class JournalEntryViewModel : ObservableObject
{
    private readonly GameJournalViewModel? _journal;
    private readonly ISessionRepository _sessions;

    internal JournalEntryViewModel(SessionJournalEntry entry, GameJournalViewModel journal)
        : this(entry.SessionId, new SessionNote { SessionId = entry.SessionId, Note = entry.Note, Rating = entry.Rating }, journal.Sessions)
    {
        _journal = journal;
        DateText = UpdateEventViewModel.LocalDateText(entry.SessionAt);
    }

    /// <summary>Creates a session-note draft when the parent surface already presents the session's date.</summary>
    public JournalEntryViewModel(long sessionId, SessionNote? original, ISessionRepository sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
        SessionId = sessionId;
        Note = original?.Note;
        Rating = original?.Rating;
    }

    public long SessionId { get; }

    public string DateText { get; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string? Note { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRating), nameof(RatingText))]
    public partial int? Rating { get; set; }

    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    public bool HasRating => Rating is >= 1 and <= 5;

    public string RatingText => HasRating ? $"{Rating} / 5" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRead), nameof(ShowEditor))]
    public partial bool IsEditing { get; set; }

    public bool ShowRead => !IsEditing;

    public bool ShowEditor => IsEditing;

    [ObservableProperty]
    public partial string DraftNote { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDraftRated1), nameof(IsDraftRated2), nameof(IsDraftRated3),
        nameof(IsDraftRated4), nameof(IsDraftRated5), nameof(DraftRatingText))]
    public partial int DraftRating { get; set; }

    public bool IsDraftRated1 => DraftRating >= 1;
    public bool IsDraftRated2 => DraftRating >= 2;
    public bool IsDraftRated3 => DraftRating >= 3;
    public bool IsDraftRated4 => DraftRating >= 4;
    public bool IsDraftRated5 => DraftRating >= 5;
    public string DraftRatingText => DraftRating is >= 1 and <= 5 ? $"Rating: {DraftRating} / 5" : "No rating";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial bool IsSaving { get; set; }

    public bool CanEdit => !IsSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; set; }

    public bool HasProblem => Problem is not null;

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    public string AutomationName => DateText.Length == 0 ? "Journal entry" : $"Journal entry from {DateText}";

    internal void RefreshSaved(SessionJournalEntry entry)
    {
        if (IsSaving) return;
        Note = entry.Note;
        Rating = entry.Rating;
        if (!IsEditing) { DraftNote = Note ?? string.Empty; DraftRating = Rating ?? 0; }
    }

    [RelayCommand]
    private void Edit()
    {
        if (IsSaving)
        {
            return;
        }

        DraftNote = Note ?? string.Empty;
        DraftRating = Rating ?? 0;
        Problem = null;
        IsConfirmingDelete = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        if (!IsSaving)
        {
            IsEditing = false;
            Problem = null;
        }
    }

    [RelayCommand]
    private void Rate(string? value)
    {
        if (IsSaving || !int.TryParse(value, out var rating) || rating is < 1 or > 5)
        {
            return;
        }

        DraftRating = DraftRating == rating ? 0 : rating;
    }

    [RelayCommand]
    private void ClearRating() { if (!IsSaving) DraftRating = 0; }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (IsSaving)
        {
            return;
        }

        var note = string.IsNullOrWhiteSpace(DraftNote) ? null : DraftNote.Trim();
        int? rating = DraftRating is >= 1 and <= 5 ? DraftRating : null;
        if (note is null && rating is null)
        {
            Problem = GameDetailsCopy.JournalEmptyEditProblem;
            return;
        }

        IsSaving = true;
        Problem = null;
        try
        {
            await _sessions.SetNoteAsync(new SessionNote { SessionId = SessionId, Note = note, Rating = rating }, ct);
            Note = note;
            Rating = rating;
            IsEditing = false;
        }
        catch
        {
            Problem = GameDetailsCopy.JournalSaveProblem;
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void RequestDelete()
    {
        if (!IsSaving)
        {
            IsConfirmingDelete = true;
            Problem = null;
        }
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken ct)
    {
        if (IsSaving || !IsConfirmingDelete)
        {
            return;
        }

        IsSaving = true;
        Problem = null;
        try
        {
            if (_journal is not null) await _journal.DeleteAsync(this, ct);
            else await _sessions.DeleteNoteAsync(SessionId, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Problem = GameDetailsCopy.JournalDeleteProblem;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
