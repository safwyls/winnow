using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// "What you've told the feed" — the inspection surface, and the reason this
/// loop is not a black box.
///
/// <para><b>Why it exists at all.</b> A recommender that quietly accumulates
/// dismissals is one the user cannot argue with: games stop appearing and there
/// is nothing to point at. §6b's answer is that verdicts are append-and-revoke
/// rows, and this screen is the other half of that answer — the whole history,
/// including what was taken back. Dismissed → undone → dismissed again is two
/// rows and a stamp, and all three facts are on screen, because a revocation
/// the interface hides is a revocation the user has to take on trust.</para>
///
/// <para><b>Lapsed rows are shown too.</b> A snooze that ran out needed no write
/// (§6b: "active" is computed at read time), so the only evidence it ever
/// happened is its row — and "why did this come back" is exactly the question
/// this surface has to be able to answer.</para>
///
/// <para><b>Undo lives here as well as on the card.</b> The card's receipt is
/// the immediate route back and covers the misclick; this is the one that works
/// a week later, and it is the only route back for a verdict given on a day the
/// feed no longer shows.</para>
///
/// <para><b>Titles come from the library's own tiles</b>
/// (<see cref="IGameTileSource.TileForRelease"/>), so a game is named here
/// exactly as it is named on the wall. A release the library no longer holds
/// keeps its row and says so — dropping it would hide a verdict the user gave,
/// which is the failure this whole screen exists to prevent.</para>
/// </summary>
public partial class FeedHistoryViewModel : ObservableObject
{
    /// <summary>
    /// Both parameters optional for the reason the rest of this screen's are: an
    /// unwired host costs the list rather than the window, and the empty state
    /// is a sentence either way.
    /// </summary>
    public FeedHistoryViewModel(IFeedService? feed = null, IGameTileSource? tiles = null)
    {
        _feed = feed;
        _tiles = tiles;
    }

    private readonly IFeedService? _feed;
    private readonly IGameTileSource? _tiles;

    /// <summary>Newest first, which is the order the repository returns and the order the question is asked in.</summary>
    public ObservableCollection<FeedHistoryEntryViewModel> Entries { get; } = [];

    /// <summary>The screen's own name, and it names the act rather than the table.</summary>
    public string Title => "What you've told the feed";

    /// <summary>
    /// How many verdicts are on record, in Plex Mono with tabular figures like
    /// every number in the app. Counts every row, revoked and lapsed included —
    /// this is a history, and a history that only counted the parts still in
    /// force would be the summary of it rather than the thing itself.
    /// </summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = "0";

    /// <summary>True once there is at least one row — the header states the number only when there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMessage))]
    public partial bool HasEntries { get; set; }

    /// <summary>
    /// The one sentence shown when the list is empty. It is a direction rather
    /// than a mood (§7): it says where the controls are, because someone opening
    /// this screen before ever using them has asked a reasonable question.
    /// </summary>
    public string Message =>
        "Nothing yet. Every card carries “Not interested” and “Not now”, and whatever you tell it lands here — including the ones you take back.";

    public bool ShowMessage => !HasEntries;

    /// <summary>
    /// Re-reads the whole history. Cheap by construction — one row per verdict
    /// ever given, against a log that grows by a click at a time — and it still
    /// goes through the service, which puts the read on a background thread with
    /// everything else that touches SQLite.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        if (_feed is null)
        {
            return;
        }

        IReadOnlyList<FeedVerdictRecord> rows;
        try
        {
            rows = await _feed.GetHistoryAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // The service is contracted not to throw. If it does, the honest
            // state is the one the screen already has rather than a claim that
            // the user has never said anything.
            return;
        }

        Entries.Clear();
        foreach (var row in rows)
        {
            Entries.Add(new FeedHistoryEntryViewModel(row, _tiles?.TileForRelease(row.ReleaseId)?.Title, Revoke));
        }

        HasEntries = Entries.Count > 0;
        CountText = Entries.Count.ToString("N0");
    }

    /// <summary>
    /// Raised after a row here is taken back, carrying the release it was on, so
    /// the Feed can put any card still showing that receipt back to its
    /// unanswered state. Two surfaces over one row must not disagree about it.
    /// </summary>
    public event EventHandler<long>? VerdictRevoked;

    /// <summary>
    /// The undo behind every active row. Reloads afterwards rather than mutating
    /// the row in place: a revocation is a new fact about a stored row, and
    /// re-reading is how this screen stays a view of the store rather than a
    /// second copy of it that can drift.
    /// </summary>
    private async Task Revoke(FeedHistoryEntryViewModel entry, CancellationToken ct)
    {
        if (_feed is null)
        {
            return;
        }

        try
        {
            await _feed.RevokeVerdictAsync(entry.ReleaseId, entry.Kind, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return;
        }

        VerdictRevoked?.Invoke(this, entry.ReleaseId);
        await LoadAsync(ct);
    }
}

/// <summary>
/// One stored verdict, as a line somebody can read: which game, which of the two
/// things they said, and where it stands now.
///
/// <para><b>The status is a sentence and a date, split.</b> The date sets in the
/// data face with tabular figures (§3) and the words beside it stay prose, which
/// is the same rule the feed's reason sentences follow — this app's numbers live
/// inside sentences on these screens and are still numbers.</para>
/// </summary>
public sealed class FeedHistoryEntryViewModel
{
    private readonly Func<FeedHistoryEntryViewModel, CancellationToken, Task> _revoke;

    internal FeedHistoryEntryViewModel(
        FeedVerdictRecord record,
        string? title,
        Func<FeedHistoryEntryViewModel, CancellationToken, Task> revoke)
    {
        ReleaseId = record.ReleaseId;
        Kind = record.Kind;
        Status = record.Status;
        _revoke = revoke;

        // A verdict outlives the library it was given in: a consolidated demo or
        // a hidden non-game entry has no tile any more. The row stays and says
        // so, because hiding a verdict the user gave is the one thing an
        // inspection surface may not do.
        Title = title ?? "A game that is no longer in your library";

        KindLabel = record.Kind == FeedVerdictKind.Snoozed ? "NOT NOW" : "NOT INTERESTED";

        (StatusNote, var stamp) = record.Status switch
        {
            // Taken back. The date is when they took it back, which is the fact
            // the row is now about.
            FeedVerdictStatus.Undone => ("Undone on", record.RevokedAt),

            // Ran out by itself. No write happened; the row is the only evidence.
            FeedVerdictStatus.Lapsed => ("Lapsed on", record.ExpiresAt),

            // Still standing. A snooze states the day it ends, a dismissal the
            // day it started — two different facts, worded as two.
            _ when record.Kind == FeedVerdictKind.Snoozed => ("Back on", record.ExpiresAt),
            _ => ("Off the feed since", record.CreatedAt),
        };

        StatusDate = stamp is { } value ? value.ToLocalTime().ToString("d MMM yyyy") : string.Empty;
    }

    public long ReleaseId { get; }

    public FeedVerdictKind Kind { get; }

    public FeedVerdictStatus Status { get; }

    /// <summary>The game, named exactly as the library names it.</summary>
    public string Title { get; }

    /// <summary>Which of the two things the user said, in the app's own caps.</summary>
    public string KindLabel { get; }

    /// <summary>The words of the status line.</summary>
    public string StatusNote { get; }

    /// <summary>Its date, alone, so the view can set it in the data face.</summary>
    public string StatusDate { get; }

    public bool HasStatusDate => StatusDate.Length > 0;

    /// <summary>
    /// Only a standing verdict can be taken back. An undone one already has been,
    /// and a lapsed snooze undid itself — offering a button for either would be
    /// the screen inviting an act that does nothing.
    /// </summary>
    public bool CanUndo => Status == FeedVerdictStatus.Active;

    /// <summary>Takes this verdict back. See <see cref="FeedHistoryViewModel"/>'s own remarks.</summary>
    public IAsyncRelayCommand UndoCommand => _undoCommand ??=
        new AsyncRelayCommand(ct => _revoke(this, ct));

    private AsyncRelayCommand? _undoCommand;
}
