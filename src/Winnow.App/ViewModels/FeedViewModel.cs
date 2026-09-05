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

    /// <summary>
    /// How often a standing receipt's clock is advanced. Thirty times a second
    /// is what makes the arc read as a sweep rather than as a series of jumps.
    /// </summary>
    private static readonly TimeSpan SmoothTick = TimeSpan.FromMilliseconds(33);

    /// <summary>
    /// The same clock under reduced motion (§8). The indicator stays and stays
    /// accurate — it is determinate, so it is information rather than
    /// decoration and withholding it would cost the reader the one statement of
    /// how long the undo has left. What goes is the continuous movement: it
    /// steps once a second, three times in all.
    /// </summary>
    private static readonly TimeSpan SteppedTick = TimeSpan.FromSeconds(1);

    private readonly IFeedService _feed;
    private readonly IGameTileSource? _tiles;
    private readonly TimeProvider _clock;
    private readonly Action<Action> _post;

    private ITimer? _ticker;
    private long _tickedAt;

    /// <summary>
    /// Which scoring pass the cards on screen belong to. Every card is stamped
    /// with it, and a swap checks the stamp: a reload wins, and a replacement
    /// computed by a pass this one superseded must not land on top of it.
    /// </summary>
    private long _generation;

    /// <summary>An invalidation that arrived during a load, waiting to be answered by the next one.</summary>
    private bool _reloadPending;

    /// <summary>A backfill is reading; another was asked for while it read.</summary>
    private bool _backfilling;
    private bool _backfillPending;

    /// <summary>
    /// Every release this pass has already put on screen or queued behind a
    /// shelf. A backfill reads the whole feed again and keeps only what this set
    /// does not already account for, which is what stops it offering a game the
    /// reader is looking at or has just answered.
    /// </summary>
    private readonly HashSet<long> _spent = [];

    /// <param name="tiles">Optional; without it the screen reports the library as unloaded.</param>
    /// <param name="clock">The receipt countdown's clock. Injected so a test states the time.</param>
    /// <param name="post">
    /// How a tick reaches the UI thread. Defaults to the Avalonia dispatcher;
    /// tests drive <see cref="Tick"/> themselves and no dispatcher has to exist.
    /// </param>
    public FeedViewModel(
        IFeedService feed,
        IGameTileSource? tiles = null,
        TimeProvider? clock = null,
        Action<Action>? post = null)
    {
        _feed = feed;
        _tiles = tiles;
        _clock = clock ?? TimeProvider.System;
        _post = post ?? (action => Avalonia.Threading.Dispatcher.UIThread.Post(action));

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
        do
        {
            // Claimed before the read, not after. An invalidation that arrives
            // while this pass is in flight is about state this pass has already
            // read, so it can only be answered by another pass — dropping it
            // leaves the feed stale until something else happens to ask.
            _reloadPending = false;

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
        while (_reloadPending);
    }

    /// <summary>
    /// Asks for a fresh pass, coalescing against one already running. The
    /// command refuses to run twice over, so a request arriving mid-load is
    /// queued rather than dropped and <see cref="LoadAsync"/> replays it.
    /// </summary>
    private void RequestReload()
    {
        if (LoadCommand.IsRunning)
        {
            _reloadPending = true;
            return;
        }

        _ = LoadCommand.ExecuteAsync(null);
    }

    private void Apply(FeedSnapshot snapshot)
    {
        // A failed re-score behind existing shelves is silently ignored.
        if (snapshot.Failed && Shelves.Count > 0)
        {
            return;
        }

        // A new pass supersedes every card and every reserve the last one
        // computed, swaps in flight from it included.
        var generation = ++_generation;
        _backfillPending = false;
        _spent.Clear();

        // Each card owns its cover state, so the cards going off the screen are
        // the only thing that may drop it.
        foreach (var shelf in Shelves)
        {
            foreach (var card in shelf.Cards)
            {
                Detach(card);
                card.Dispose();
            }
        }

        Shelves.Clear();
        StopTicker();

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
                    cards.Add(NewCard(tile, item.Reason, generation));
                }
            }

            if (cards.Count == 0)
            {
                continue;
            }

            // A reserve item with no tile is dropped here for the same reason a
            // visible one is, and here rather than at the swap: a receipt that
            // offers a replacement has to have one.
            var reserve = shelf.Reserve
                .Where(item => _tiles?.TileForOwnership(item.OwnershipId) is not null)
                .ToList();

            // Everything this pass accounted for, shown or held, so a backfill
            // reading the feed again can tell what is new from what is already
            // spoken for.
            foreach (var item in shelf.Items.Concat(reserve))
            {
                _spent.Add(item.ReleaseId);
            }

            var built = new FeedShelfViewModel(shelf.Id, shelf.Title, shelf.Blurb, cards, reserve);
            Offer(built);
            Shelves.Add(built);
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

    /// <summary>Builds one card, stamped with its pass and wired to this screen.</summary>
    private FeedCardViewModel NewCard(GameTileViewModel tile, string reason, long generation)
    {
        var card = new FeedCardViewModel(tile, reason, _feed) { Generation = generation };

        card.VerdictChanged += OnCardVerdictChanged;
        return card;
    }

    /// <summary>
    /// Drops the subscription on a card leaving the screen. Its cover lease is
    /// the card's own to release; this is the other half, and missing it leaks a
    /// dead card's handler into every later verdict.
    /// </summary>
    private void Detach(FeedCardViewModel card) => card.VerdictChanged -= OnCardVerdictChanged;

    /// <summary>Tells a shelf's cards whether the shelf can still answer a dismissal.</summary>
    private static void Offer(FeedShelfViewModel shelf)
    {
        var available = shelf.HasReserve;
        foreach (var card in shelf.Cards)
        {
            card.CanReplace = available;
        }
    }

    /// <summary>Re-reads history after a card verdict changes.</summary>
    private void OnCardVerdictChanged(object? sender, EventArgs e)
    {
        _ = History.LoadCommand.ExecuteAsync(null);

        // The verdict has just put a receipt on the clock, or an undo has just
        // taken one off it.
        StartTicker();
    }

    /// <summary>
    /// Advances every standing receipt and swaps out the ones whose five
    /// seconds have run. Driven by one ticker for the whole feed rather than a
    /// timer per card: several receipts can be counting at once, and the cards
    /// have to be walked anyway to find them.
    /// </summary>
    /// <param name="elapsed">Time since the last tick.</param>
    internal void Tick(TimeSpan elapsed)
    {
        List<FeedCardViewModel>? expired = null;

        foreach (var card in Live())
        {
            if (card.Tick(elapsed))
            {
                (expired ??= []).Add(card);
            }
        }

        if (expired is null)
        {
            return;
        }

        foreach (var card in expired)
        {
            Swap(card);
        }

        RequestBackfill();
    }

    /// <summary>
    /// Starts the one ticker the feed has, if anything is counting and it is not
    /// already running. Called wherever a countdown can begin: a verdict, and a
    /// backfill handing a shelf something to offer that it did not have before.
    /// </summary>
    private void StartTicker()
    {
        if (_ticker is not null || !IsCountingDown())
        {
            return;
        }

        _tickedAt = _clock.GetTimestamp();
        _ticker = _clock.CreateTimer(_ => _post(OnTick), null, TickInterval, TickInterval);
    }

    /// <summary>
    /// Advances by the time that actually passed rather than by the interval
    /// asked for. A timer that was late — and one on a loaded UI thread will be
    /// — would otherwise leave the receipt on screen past its three seconds and
    /// the arc short of its end.
    /// </summary>
    private void OnTick()
    {
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_tickedAt, now);
        _tickedAt = now;

        Tick(elapsed);

        if (!IsCountingDown())
        {
            StopTicker();
        }
    }

    private void StopTicker()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    /// <summary>How often the clock is advanced, which is where §8's reduced-motion rule lands.</summary>
    internal TimeSpan TickInterval => Stepped() ? SteppedTick : SmoothTick;

    /// <summary>
    /// Whether the reader has asked for reduced motion. Read off a live card,
    /// because every tile shares one <c>DormancyRamp</c> and the answer is the
    /// same for all of them.
    /// </summary>
    private bool Stepped()
    {
        foreach (var shelf in Shelves)
        {
            foreach (var card in shelf.Cards)
            {
                return card.Tile.SnapDormancy;
            }
        }

        return false;
    }

    /// <summary>Whether anything on screen is still counting, so the ticker knows to keep running.</summary>
    internal bool IsCountingDown()
    {
        foreach (var shelf in Shelves)
        {
            foreach (var card in shelf.Cards)
            {
                if (card.IsCountingDown)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Internal so the generation guard can be driven directly by a test.</summary>
    internal bool Swap(FeedCardViewModel outgoing)
    {
        // A card from a superseded pass is not on any shelf, and the reserve it
        // would have drawn from went with the pass that computed it.
        if (outgoing.Generation != _generation)
        {
            return false;
        }

        foreach (var shelf in Shelves)
        {
            var index = shelf.Cards.IndexOf(outgoing);
            if (index < 0)
            {
                continue;
            }

            if (NextReplacement(shelf) is not { } next)
            {
                // Nothing left to promote. The receipt keeps its place and its
                // clock stops, because a countdown running out on nothing would
                // be stating a replacement the shelf does not have.
                Offer(shelf);
                RequestBackfill();
                return false;
            }

            // One index assigned, not the collection cleared: this realises one
            // container and re-measures one shelf.
            shelf.Cards[index] = NewCard(next.Tile, next.Item.Reason, _generation);

            Detach(outgoing);
            outgoing.Dispose();

            Offer(shelf);

            // At swap time, because this is the first moment anybody has seen
            // it. Recording it with the pass would have charged a card nobody
            // looked at the demotion for having been looked at.
            _ = _feed.RecordSurfacedAsync(next.Item.ReleaseId, shelf.Id);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Takes the next reserve item this shelf can actually put on screen, or
    /// nothing. Two things disqualify one. The library may no longer hold a
    /// tile for it, and there would be no cover to draw. Or a card already on
    /// the shelf is saying the sentence it would arrive with: one shelf shares
    /// one reason ledger, and a deep shelf can reach the end of its phrasings
    /// before it reaches the end of its cards, so a swap is the one moment
    /// where two cards that were never on screen together could end up side by
    /// side saying the same thing.
    /// </summary>
    private (FeedItem Item, GameTileViewModel Tile)? NextReplacement(FeedShelfViewModel shelf)
    {
        while (shelf.Reserve.Count > 0)
        {
            var item = shelf.Reserve.Dequeue();

            if (_tiles?.TileForOwnership(item.OwnershipId) is not { } tile)
            {
                continue;
            }

            if (Spoken(shelf, item.Reason))
            {
                continue;
            }

            return (item, tile);
        }

        return null;
    }

    private static bool Spoken(FeedShelfViewModel shelf, string reason)
    {
        foreach (var card in shelf.Cards)
        {
            if (string.Equals(card.Reason, reason, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tops the shelves' queues back up in the background, after a swap has
    /// taken one out of them.
    ///
    /// <para>A backfill is not a reload. It scores the feed again and then
    /// throws away everything except the reserves, merging those into the
    /// queues already standing behind the shelves; no shelf is rebuilt and no
    /// card on screen moves. That is what makes it safe to run under a receipt,
    /// where a reload was not — a reload replaces the card the receipt is on,
    /// and with it the undo the reader was reaching for.</para>
    ///
    /// <para>The pass returns fresh games rather than the same ones because the
    /// dismissal that emptied the slot is already stored: the answered game is
    /// hard-excluded from the next pass, every shelf shifts up by one, and what
    /// arrives at the bottom is a game no queue has held. Anything that is on
    /// screen, already queued, or already spent this pass is dropped on the way
    /// in, so a shifted pass cannot re-offer what the reader is looking at.</para>
    ///
    /// <para>One in flight and one waiting: a reader answering a run of cards
    /// asks for this on every swap, and a queue of reads would all return the
    /// same answer at increasing cost.</para>
    /// </summary>
    private void RequestBackfill()
    {
        if (_backfilling)
        {
            _backfillPending = true;
            return;
        }

        Backfilling = BackfillAsync();
    }

    /// <summary>
    /// The backfill in flight, or a completed task. Internal so a test can wait
    /// for a read that nothing on screen is waiting for.
    /// </summary>
    internal Task Backfilling { get; private set; } = Task.CompletedTask;

    private async Task BackfillAsync()
    {
        _backfilling = true;
        try
        {
            do
            {
                _backfillPending = false;

                var generation = _generation;

                FeedSnapshot snapshot;
                try
                {
                    snapshot = await _feed.GetShelvesAsync();
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // A queue that stays short is a receipt that keeps its
                    // place. Nothing on screen depends on this landing.
                    return;
                }

                // A reload has replaced the shelves this was computed for, and
                // brought its own reserves with it.
                if (snapshot.Failed || generation != _generation)
                {
                    return;
                }

                Merge(snapshot);
            }
            while (_backfillPending);
        }
        finally
        {
            _backfilling = false;
        }
    }

    /// <summary>Adds a pass's reserves to the queues already standing, and nothing else from it.</summary>
    private void Merge(FeedSnapshot snapshot)
    {
        foreach (var shelf in Shelves)
        {
            var incoming = snapshot.Shelves.FirstOrDefault(s => s.Id == shelf.Id);
            if (incoming is null)
            {
                continue;
            }

            // Its visible slice is deliberately included: those items are the
            // shelf's best remaining candidates now that the answered ones are
            // excluded, and this shelf is not going to redraw itself to show
            // them. Whether one belongs in the queue is decided by _spent, the
            // same way a held item is.
            foreach (var item in incoming.Items.Concat(incoming.Reserve))
            {
                if (!_spent.Add(item.ReleaseId))
                {
                    continue;
                }

                if (_tiles?.TileForOwnership(item.OwnershipId) is null)
                {
                    continue;
                }

                shelf.Reserve.Enqueue(item);
            }

            Offer(shelf);
        }

        // A shelf that had nothing to offer has something now, so a receipt
        // that was standing without a clock has one.
        StartTicker();
    }

    /// <summary>Every card on screen, as a snapshot — swapping mutates the shelves being walked.</summary>
    private List<FeedCardViewModel> Live()
    {
        var cards = new List<FeedCardViewModel>();
        foreach (var shelf in Shelves)
        {
            cards.AddRange(shelf.Cards);
        }

        return cards;
    }

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

    /// <summary>Re-scores on library reload; queued behind a pass already running.</summary>
    private void OnTilesChanged(object? sender, EventArgs e) => RequestReload();

    /// <summary>Maps confidence tier to a note about improving picks; null once established.</summary>
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
