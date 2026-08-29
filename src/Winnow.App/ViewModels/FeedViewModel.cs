using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Feed — the app's landing view. Shelves are scored by
/// <see cref="IFeedService"/>; this class owns the four screen states
/// (working, ready, quiet, broken). Scoring runs after library load, not on
/// the startup path.
/// </summary>
public partial class FeedViewModel : ObservableObject
{
    private const string WorkingMessage =
        "Building the feed…";

    private readonly IFeedService _feed;
    private readonly IGameTileSource? _tiles;

    /// <param name="tiles">Optional; without it the screen reports the library as unloaded.</param>
    public FeedViewModel(IFeedService feed, IGameTileSource? tiles = null)
    {
        _feed = feed;
        _tiles = tiles;

        // Re-score on library reload so shelves reflect updated buckets.
        if (_tiles is not null)
        {
            _tiles.TilesChanged += OnTilesChanged;
        }

        History = new FeedHistoryViewModel(feed, tiles);

        // Sync cards when a verdict is revoked on the history screen.
        History.VerdictRevoked += OnVerdictRevoked;

        // Update header count when history changes.
        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FeedHistoryViewModel.HasEntries))
            {
                OnPropertyChanged(nameof(ShowHistoryCount));
            }
        };
    }

    /// <summary>Sections in presentation order — the engine's claim order, strongest story first.</summary>
    public System.Collections.ObjectModel.ObservableCollection<FeedShelfViewModel> Shelves { get; } = [];

    /// <summary>The screen's own name. Directive rather than clever: it says what the screen is for.</summary>
    public string Title => "Where to start";

    /// <summary>
    /// Everything the user has ever told the feed, and the undo for each of it.
    /// </summary>
    public FeedHistoryViewModel History { get; }

    /// <summary>Whether the history view is shown in place of the feed shelves.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowShelves), nameof(ShowMessage), nameof(ShowHistory), nameof(ShowCandidates),
        nameof(HistoryLabel), nameof(ShowHistoryCount))]
    public partial bool IsHistoryOpen { get; set; }

    /// <summary>
    /// The header control's own words. One control rather than two, and it says
    /// which way it goes: a toggle whose label never changes is a toggle whose
    /// state you have to infer from the screen behind it.
    /// </summary>
    public string HistoryLabel => IsHistoryOpen ? "Back to the feed" : "What you've told the feed";

    /// <summary>Show history count only when the history screen is closed.</summary>
    public bool ShowHistoryCount => !IsHistoryOpen && History.HasEntries;

    /// <summary>True while scoring. Starts true (the feed loads on startup).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShelves), nameof(ShowMessage))]
    public partial bool IsLoading { get; set; } = true;

    /// <summary>Status message; null when shelves are visible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShelves), nameof(ShowMessage))]
    public partial string? Message { get; set; } = WorkingMessage;

    /// <summary>True when the last pass failed; enables retry.</summary>
    [ObservableProperty]
    public partial bool CanRetry { get; set; }

    /// <summary>Number of games scored, formatted for display.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCandidates))]
    public partial string CandidateCountText { get; set; } = "0";

    /// <summary>Suppressed while loading so "0 games scored" doesn't flash.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCandidates))]
    public partial bool HasCandidates { get; set; }

    /// <summary>Confidence note; null once the feed is established.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfidenceNote))]
    public partial string? ConfidenceNote { get; set; }

    public bool HasConfidenceNote => !string.IsNullOrEmpty(ConfidenceNote);

    public bool ShowShelves => !IsLoading && Message is null && !IsHistoryOpen;

    public bool ShowMessage => Message is not null && !IsHistoryOpen;

    /// <summary>Mutually exclusive with ShowShelves and ShowMessage.</summary>
    public bool ShowHistory => IsHistoryOpen;

    /// <summary>Hidden on the history screen.</summary>
    public bool ShowCandidates => HasCandidates && !IsHistoryOpen;

    /// <summary>Scores and renders today's feed. Idempotent within a day.</summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // Keep existing shelves visible during a re-score.
        var quiet = Shelves.Count > 0;
        if (!quiet)
        {
            IsLoading = true;
            CanRetry = false;
            Message = WorkingMessage;
        }

        FeedSnapshot snapshot;
        try
        {
            snapshot = await _feed.GetShelvesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Belt-and-braces; must not take the window down.
            snapshot = FeedSnapshot.Unavailable;
        }

        Apply(snapshot);
        IsLoading = false;

        // Load history so the header count is ready before the screen opens.
        await History.LoadCommand.ExecuteAsync(null);
    }

    private void Apply(FeedSnapshot snapshot)
    {
        // A failed re-score behind existing shelves is silently ignored.
        if (snapshot.Failed && Shelves.Count > 0)
        {
            return;
        }

        // Each card owns its cover state, so the cards going off the screen are
        // the only thing that may drop it.
        foreach (var shelf in Shelves)
        {
            foreach (var card in shelf.Cards)
            {
                card.VerdictChanged -= OnCardVerdictChanged;
                card.Dispose();
            }
        }

        Shelves.Clear();

        CandidateCountText = snapshot.CandidateCount.ToString("N0");
        HasCandidates = !snapshot.Failed && snapshot.CandidateCount > 0;
        ConfidenceNote = NoteFor(snapshot.Confidence, snapshot.Failed);

        if (snapshot.Failed)
        {
            CanRetry = true;
            Message = "Couldn't build the feed. Try again, or browse All games.";
            return;
        }

        foreach (var shelf in snapshot.Shelves)
        {
            var cards = new List<FeedCardViewModel>(shelf.Items.Count);
            foreach (var item in shelf.Items)
            {
                // Drop items with no matching tile (no cover to draw).
                if (_tiles?.TileForOwnership(item.OwnershipId) is { } tile)
                {
                    var card = new FeedCardViewModel(tile, item.Reason, _feed);

                    card.VerdictChanged += OnCardVerdictChanged;
                    cards.Add(card);
                }
            }

            if (cards.Count > 0)
            {
                Shelves.Add(new FeedShelfViewModel(shelf.Id, shelf.Title, shelf.Blurb, cards));
            }
        }

        if (Shelves.Count > 0)
        {
            Message = null;
            return;
        }

        // Distinguish "library not loaded" from "nothing to suggest".
        Message = _tiles is { HasTiles: false } || snapshot.CandidateCount == 0
            ? "Nothing to score yet. The feed appears once your library has loaded."
            : "Nothing to suggest right now.";
    }

    /// <summary>Toggles the history view; loads on open.</summary>
    [RelayCommand]
    private async Task ToggleHistoryAsync()
    {
        IsHistoryOpen = !IsHistoryOpen;

        if (IsHistoryOpen)
        {
            await History.LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Closes the history view (Escape binding).</summary>
    [RelayCommand]
    private void CloseHistory() => IsHistoryOpen = false;

    /// <summary>Re-reads history after a card verdict changes.</summary>
    private void OnCardVerdictChanged(object? sender, EventArgs e)
        => _ = History.LoadCommand.ExecuteAsync(null);

    /// <summary>Restores cards whose verdict was revoked on the history screen.</summary>
    private void OnVerdictRevoked(object? sender, long releaseId)
    {
        foreach (var shelf in Shelves)
        {
            foreach (var card in shelf.Cards)
            {
                if (card.Tile.ReleaseId == releaseId)
                {
                    card.Restore();
                }
            }
        }
    }

    /// <summary>Re-scores on library reload; skipped if already running.</summary>
    private void OnTilesChanged(object? sender, EventArgs e)
    {
        if (!LoadCommand.IsRunning)
        {
            _ = LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>Maps confidence tier to a user-facing note; null once established.</summary>
    private static string? NoteFor(FeedConfidence confidence, bool failed) => failed switch
    {
        true => null,
        false => confidence switch
        {
            FeedConfidence.EarlyDays =>
                "Based on playtime and patch history. Improves as you play.",
            FeedConfidence.Settling =>
                "Session tracking active. Picks improve from here.",
            _ => null,
        },
    };
}
