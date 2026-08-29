using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// Post-session journal prompt (§5.2): an in-window card offering a free-text note
/// and optional rating after a game exits. Opt-in only (§9 pitfall 7). Self-dismisses
/// after <see cref="Patience"/> if untouched; a second session replaces the card only
/// when nothing has been typed or rated.
/// </summary>
public partial class JournalPromptViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long an untouched card waits before removing itself. Two minutes: long
    /// enough that a user who wandered off mid-game still finds it, short enough
    /// that it is not still there next time they open the app.
    /// </summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private readonly SessionJournalService? _journal;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;

    private ITimer? _timer;
    private long _sessionId;
    private bool _disposed;

    public JournalPromptViewModel(
        SessionJournalService? journal = null,
        Func<long, string?>? titleFor = null,
        TimeProvider? clock = null,
        Action<Action>? post = null)
    {
        _journal = journal;
        TitleFor = titleFor;
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

        if (_journal is not null)
        {
            _journal.SessionEnded += OnSessionEnded;
        }
    }

    /// <summary>
    /// Ownership id → the game's name, as the loaded library knows it. Settable
    /// rather than a constructor argument because the only thing that can answer
    /// it is <see cref="LibraryViewModel"/>, which is downstream of this: the
    /// composition root builds this, the library takes it and fills this in.
    ///
    /// <para>The library is asked rather than the database because it already
    /// holds the answer in memory for every tile on screen, and because a prompt
    /// naming a game the user cannot see in their own library would be a puzzle
    /// rather than a question.</para>
    /// </summary>
    public Func<long, string?>? TitleFor { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>"Portal 2" — the game that just ended.</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    /// <summary>"47m", "2h 10m". How long the sitting was, in the app's own voice.</summary>
    [ObservableProperty]
    public partial string DurationText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    public partial string Note { get; set; } = string.Empty;

    /// <summary>1–5, or 0 for "not rated" — which is the state it opens in and a valid answer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent), nameof(IsRated1), nameof(IsRated2),
        nameof(IsRated3), nameof(IsRated4), nameof(IsRated5))]
    public partial int Rating { get; set; }

    /// <summary>
    /// Whether the user has actually put anything in. Guards the replacement
    /// rule, and it is also what "dismiss costs nothing" means concretely.
    /// </summary>
    public bool HasContent => Rating is >= 1 and <= 5 || Note.Trim().Length > 0;

    public bool IsRated1 => Rating >= 1;

    public bool IsRated2 => Rating >= 2;

    public bool IsRated3 => Rating >= 3;

    public bool IsRated4 => Rating >= 4;

    public bool IsRated5 => Rating >= 5;

    /// <summary>
    /// Whether a write is in flight. The card stays open, and Save, Dismiss, the
    /// note box and the rating dots all lock — a Note typed over an in-flight
    /// save could be wiped by the Close() that follows it landing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    public partial bool IsSaving { get; set; }

    /// <summary>
    /// Set when a write did not land. The card stays open with the note and
    /// rating exactly as typed, because an unwritten note may not disappear —
    /// the same Save button is the retry.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Problem { get; set; }

    public bool HasError => Problem is not null;

    public string SaveLabel => IsSaving ? "Saving…" : "Save";

    /// <summary>
    /// The in-flight command, for a test to await. Tracks the whole outcome —
    /// write, then close-or-error — never just the raw call, so awaiting it
    /// only resolves once the card has actually settled.
    /// </summary>
    public Task PendingSave => SaveCommand.ExecutionTask ?? Task.CompletedTask;

    /// <summary>
    /// Opens the card. Public so a test — and only a test — can drive it without
    /// a watcher, a database and a finished game.
    /// </summary>
    public void Open(EndedSession ended, string title)
    {
        if (IsOpen && HasContent)
        {
            // Someone is mid-sentence. The newer session keeps its row and loses
            // its prompt, which is the cheaper of the two losses.
            return;
        }

        _sessionId = ended.SessionId;
        Title = title;
        DurationText = DurationOf(ended.DurationSeconds);
        Note = string.Empty;
        Rating = 0;
        Problem = null;
        IsOpen = true;

        Arm();
    }

    /// <summary>
    /// Star <paramref name="value"/>, or clearing it by pressing the one already
    /// set — a rating given by accident has to be retractable, or the card has
    /// become a thing you cannot leave without answering.
    /// </summary>
    /// <param name="value">
    /// The dot's number, as XAML hands it over: a string. A
    /// <c>RelayCommand&lt;int&gt;</c> would throw at runtime on
    /// <c>CommandParameter="3"</c> — Avalonia passes the literal through
    /// unconverted and the toolkit refuses a mistyped argument rather than
    /// coercing it — and the alternative is five typed literals in the markup
    /// to save one TryParse here.
    /// </param>
    [RelayCommand]
    private void Rate(string? value)
    {
        if (IsSaving || !int.TryParse(value, out var star) || star is < 1 or > 5)
        {
            return;
        }

        Rating = Rating == star ? 0 : star;
        Arm();
    }

    /// <summary>
    /// Writes and closes only once the write lands. Writing nothing is allowed
    /// and writes nothing: a Save pressed on an empty card is a dismissal that
    /// took the long way round. A write that fails leaves the card exactly as
    /// it was — open, with the note and rating intact — instead of closing over
    /// a note nobody wrote down.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (IsSaving)
        {
            return;
        }

        if (_journal is null || !HasContent)
        {
            Close();
            return;
        }

        IsSaving = true;
        Problem = null;
        try
        {
            await _journal.SaveAsync(
                _sessionId,
                Note,
                Rating is >= 1 and <= 5 ? Rating : null,
                ct);
            Close();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Problem = "Couldn't save. Your note is still here — try again.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>
    /// The ×, and Escape. Not an answer — an absence of one. Locked out mid-save
    /// so a click cannot close the card out from under a write that is about to
    /// either land (fine either way) or fail (which must find the card still
    /// open to show it).
    /// </summary>
    [RelayCommand]
    private void Dismiss()
    {
        if (IsSaving)
        {
            return;
        }

        Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_journal is not null)
        {
            _journal.SessionEnded -= OnSessionEnded;
        }

        _timer?.Dispose();
        _timer = null;
    }

    private void Close()
    {
        IsOpen = false;
        Note = string.Empty;
        Rating = 0;
        Problem = null;
        _sessionId = 0;
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Restarts the self-dismissal countdown. Called on every interaction, so
    /// the timer can only ever fire on a card nobody has touched — a note being
    /// typed slowly must not vanish out from under the cursor.
    /// </summary>
    private void Arm()
    {
        _timer?.Dispose();
        _timer = _clock.CreateTimer(
            _ => _post(() =>
            {
                if (IsOpen && !HasContent)
                {
                    Close();
                }
            }),
            null,
            Patience,
            Timeout.InfiniteTimeSpan);
    }

    partial void OnNoteChanged(string value) => Arm();

    /// <summary>
    /// The service already filtered for "completed, and the preference is on", so
    /// arriving here means the card should open. Raised on the watcher's tick
    /// thread; everything below the marshal is UI state.
    /// </summary>
    private void OnSessionEnded(object? sender, EndedSession ended)
        => _post(() =>
        {
            var title = TitleFor?.Invoke(ended.OwnershipId);
            if (string.IsNullOrWhiteSpace(title))
            {
                // The game is not in the loaded library — a non-game entry with
                // the filter on, or a row added since the last load. Naming it
                // "this game" would be worse than staying quiet, because a note
                // is only worth writing against a title you recognise.
                return;
            }

            Open(ended, title);
        });

    /// <summary>
    /// Same vocabulary the tiles use, so "47m" means the same thing in the card
    /// as it does on the cover behind it.
    /// </summary>
    private static string DurationOf(long seconds)
    {
        var minutes = Math.Max(0, seconds) / 60;
        if (minutes < 60)
        {
            return $"{minutes}m";
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
    }
}
