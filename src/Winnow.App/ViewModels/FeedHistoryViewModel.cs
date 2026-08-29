using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// Verdict history screen (§6b): shows every feed response the user has given,
/// including revoked and lapsed rows, with undo. Titles come from
/// <see cref="IGameTileSource.TileForRelease"/>.
/// </summary>
public partial class FeedHistoryViewModel : ObservableObject
{
    /// <summary>Both parameters optional; without them the screen shows its empty state.</summary>
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

    /// <summary>Total verdict count (all rows, including revoked and lapsed). Plex Mono, tnum.</summary>
    [ObservableProperty]
    public partial string CountText { get; set; } = "0";

    /// <summary>True once there is at least one row — the header states the number only when there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMessage))]
    public partial bool HasEntries { get; set; }

    /// <summary>Empty-state message directing the user to the feed controls (§7).</summary>
    public string Message =>
        "Nothing yet. Your feed responses will appear here.";

    public bool ShowMessage => !HasEntries;

    /// <summary>Reloads the full verdict history from the service.</summary>
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

    /// <summary>Raised after a verdict is revoked, carrying the release id for feed synchronisation.</summary>
    public event EventHandler<long>? VerdictRevoked;

    /// <summary>Revokes a verdict and reloads the list from the store.</summary>
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
/// One verdict row: game title, kind (snoozed/dismissed), and current status.
/// The date is split out for Plex Mono rendering (§3).
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

    /// <summary>Only active verdicts can be undone; undone and lapsed rows already are.</summary>
    public bool CanUndo => Status == FeedVerdictStatus.Active;

    /// <summary>Takes this verdict back. See <see cref="FeedHistoryViewModel"/>'s own remarks.</summary>
    public IAsyncRelayCommand UndoCommand => _undoCommand ??=
        new AsyncRelayCommand(ct => _revoke(this, ct));

    private AsyncRelayCommand? _undoCommand;
}
