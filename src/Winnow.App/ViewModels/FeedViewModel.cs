using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>
/// The Feed — the app's landing view, and the reason to launch games through
/// Winnow at all (ROADMAP §2: the launcher collects the history, the history
/// makes the feed good, the feed is what brings you back).
///
/// <para><b>Five shelves, each stating its own reason, each card stating its
/// own.</b> The engine decides membership, order and prose; this class decides
/// nothing about which game is where. What it owns is the four states a screen
/// like this has to be able to be in — working, ready, quiet, broken — and the
/// rule that none of them is ever a blank pane.</para>
///
/// <para><b>Scoring is not on the startup path.</b> The pass takes ~500ms over a
/// thousand games (see <see cref="IFeedService"/> for the measurement), so the
/// window opens, the library loads, and the feed is asked for afterwards; until
/// it answers this screen says what it is doing and points at the rail. §5.1
/// pitfall 3 — an expensive read on a user-facing path — is the one this
/// milestone was most likely to walk into.</para>
///
/// <para><b>It never takes the app down.</b> A recommendation is derived and
/// droppable by definition, so a feed that cannot be computed is a sentence and
/// a "Try again", never an exception out of a constructor and never an empty
/// window. The library is one rail click away in every one of these states.</para>
/// </summary>
public partial class FeedViewModel : ObservableObject
{
    /// <summary>
    /// The waiting sentence, in one place because it is written twice — once as
    /// the state the screen opens in, and once by the reload that puts it back.
    /// </summary>
    private const string WorkingMessage =
        "Working out where to start. It takes a moment over a library this size — All games is in the rail if you'd rather browse.";

    private readonly IFeedService _feed;
    private readonly IGameTileSource? _tiles;

    /// <summary>
    /// <paramref name="tiles"/> is optional so an unwired host costs the covers
    /// and the buttons rather than the window — but with no source there are no
    /// cards to draw, and the screen says the library has not loaded rather than
    /// claiming the feed is empty.
    /// </summary>
    public FeedViewModel(IFeedService feed, IGameTileSource? tiles = null)
    {
        _feed = feed;
        _tiles = tiles;

        // The cards ARE the library's tiles, so a library reload leaves this
        // screen holding the previous generation of them — the titles and the
        // art from before enrichment fixed them. Re-scored rather than re-mapped
        // because the reload can move bucket membership too, and a shelf built
        // from stale buckets is a wrong shelf rather than a stale one.
        if (_tiles is not null)
        {
            _tiles.TilesChanged += OnTilesChanged;
        }

        // The inspection surface. It is built here rather than injected because
        // it is a state of this screen and not a peer of it — see IsHistoryOpen
        // for why it does not get a rail row.
        History = new FeedHistoryViewModel(feed, tiles);

        // A verdict taken back on the history screen has to reach any card still
        // showing its receipt. Two surfaces over one stored row, and they must
        // not disagree about it.
        History.VerdictRevoked += OnVerdictRevoked;

        // The header's count is the history's, so it has to move when that does.
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

    /// <summary>
    /// Whether the inspection surface is up in place of the shelves.
    ///
    /// <para><b>Why it is a state of this screen and not a rail row.</b> The
    /// rail's SETTINGS section holds STORES and APPEARANCE and was written to
    /// grow, and this was the obvious place to put it. Three things say
    /// otherwise. It is not a preference — it configures nothing; it is the
    /// record of acts performed on one screen, and filing it under app settings
    /// files an audit trail as a knob. Its whole value is a number that grows,
    /// and that section's own note forbids counts on its rows ("a settings row
    /// would only ever say a constant"). And §12.2's rule — the rail never
    /// leaves the user on a screen their click did not describe — cuts against a
    /// SETTINGS row that lands them inside the Feed. So it hangs off the Feed's
    /// own header, one click from the cards it is about and from the receipts
    /// that are the other route back.</para>
    ///
    /// <para>It is a state rather than a popup for §12.3's reason: Avalonia's
    /// focus adorner does not render inside a popup, so a flyout here would need
    /// every ring hand-drawn, while in the window's own tree §8's ring and a
    /// linear tab order both come free.</para>
    /// </summary>
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

    /// <summary>
    /// Whether the header states how many verdicts are on record. Not while the
    /// screen is open — the surface's own header says it two lines below, and
    /// one number twice is the interface disagreeing with itself if either ever
    /// lags.
    /// </summary>
    public bool ShowHistoryCount => !IsHistoryOpen && History.HasEntries;

    /// <summary>
    /// True while the scoring pass is out. Starts true, because the app opens on
    /// this screen and the first thing it is honestly doing is working.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShelves), nameof(ShowMessage))]
    public partial bool IsLoading { get; set; } = true;

    /// <summary>
    /// The one sentence shown when there are no rails to show — working, quiet,
    /// or broken. Null exactly when the shelves are up.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShelves), nameof(ShowMessage))]
    public partial string? Message { get; set; } = WorkingMessage;

    /// <summary>
    /// True when the last pass failed, which is the only state offering a retry.
    /// A quiet feed is not something to try again at.
    /// </summary>
    [ObservableProperty]
    public partial bool CanRetry { get; set; }

    /// <summary>
    /// How many owned games the last pass actually scored, in Plex Mono with
    /// tabular figures like every number in the app.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCandidates))]
    public partial string CandidateCountText { get; set; } = "0";

    /// <summary>Whether the header may state the number at all — it must not say "0 games scored" while loading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCandidates))]
    public partial bool HasCandidates { get; set; }

    /// <summary>
    /// The confidence line, or null once the library has enough history that
    /// saying anything would be an apology for nothing. Driven by the tier the
    /// engine reports rather than by install age.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfidenceNote))]
    public partial string? ConfidenceNote { get; set; }

    public bool HasConfidenceNote => !string.IsNullOrEmpty(ConfidenceNote);

    public bool ShowShelves => !IsLoading && Message is null && !IsHistoryOpen;

    public bool ShowMessage => Message is not null && !IsHistoryOpen;

    /// <summary>The inspection surface takes the body, so exactly one of these three is ever true.</summary>
    public bool ShowHistory => IsHistoryOpen;

    /// <summary>
    /// The header's count line. It states what today's feed was scored from, so
    /// it goes quiet on the history surface rather than describing a screen the
    /// user is not looking at.
    /// </summary>
    public bool ShowCandidates => HasCandidates && !IsHistoryOpen;

    /// <summary>
    /// Computes and renders today's feed. Safe to call repeatedly; the engine
    /// seeds its shuffle from the date, so a second call inside a day deals the
    /// same hand rather than a new one.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        // A pass behind a screen that already has shelves keeps them up. It
        // takes half a second, and blanking a surface somebody is reading to put
        // the same five shelves back is a flicker with nothing in it.
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
            // The service is contracted not to throw; this is the belt on top of
            // the braces, because the one thing this screen may not do is take
            // the window with it.
            snapshot = FeedSnapshot.Unavailable;
        }

        Apply(snapshot);
        IsLoading = false;

        // The header's entry point states how many verdicts are on record, so
        // the count has to exist before anyone opens the screen. One read of a
        // table that grows a click at a time, on the same background path the
        // scoring pass just came off — and it is awaited rather than dropped so
        // the number can never be a load behind what the cards are showing.
        await History.LoadCommand.ExecuteAsync(null);
    }

    private void Apply(FeedSnapshot snapshot)
    {
        // A pass that failed behind a working feed changes nothing. The shelves
        // on screen are still true, and replacing them with an apology would be
        // the app charging the user for its own retry.
        if (snapshot.Failed && Shelves.Count > 0)
        {
            return;
        }

        Shelves.Clear();

        CandidateCountText = snapshot.CandidateCount.ToString("N0");
        HasCandidates = !snapshot.Failed && snapshot.CandidateCount > 0;
        ConfidenceNote = NoteFor(snapshot.Confidence, snapshot.Failed);

        if (snapshot.Failed)
        {
            CanRetry = true;
            Message = "Winnow couldn't work out a feed just now. Nothing in your library has changed — All games is in the rail.";
            return;
        }

        foreach (var shelf in snapshot.Shelves)
        {
            var cards = new List<FeedCardViewModel>(shelf.Items.Count);
            foreach (var item in shelf.Items)
            {
                // A card with no tile has no cover, no install state and no
                // honest Play button, so it is dropped rather than drawn as a
                // stub. See IGameTileSource for why this is rare and why it is
                // not an error.
                if (_tiles?.TileForOwnership(item.OwnershipId) is { } tile)
                {
                    var card = new FeedCardViewModel(tile, item.Reason, _feed);

                    // The header's count is a live claim about stored rows, so
                    // it moves when one is written from a card. Cheap: the
                    // history is one row per verdict ever given, and the read
                    // goes through the service like every other.
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

        // Two different silences, and they are not interchangeable. Saying
        // "you've played everything" to someone whose library simply has not
        // loaded is the app being wrong about the one thing it is for — which is
        // why the empty case tests the library as well as the scored pool.
        Message = _tiles is { HasTiles: false } || snapshot.CandidateCount == 0
            ? "Nothing to score yet. Winnow reads your library first, and the feed follows it."
            : "Nothing to put in front of you today. Everything Winnow scored is either played out, in rotation, or too recent to call forgotten.";
    }

    /// <summary>
    /// Opens or closes the inspection surface. Loading is on the way IN only:
    /// the list is a view of stored rows, and re-reading it on the way out would
    /// be work done for a screen nobody is looking at.
    /// </summary>
    [RelayCommand]
    private async Task ToggleHistoryAsync()
    {
        IsHistoryOpen = !IsHistoryOpen;

        if (IsHistoryOpen)
        {
            await History.LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Back to the shelves. Its own command rather than a second call to the
    /// toggle, because Escape has to mean "close" and never "open" — a window
    /// key that opens a screen when the screen is already shut is how a user
    /// ends up somewhere they did not ask to be.
    /// </summary>
    [RelayCommand]
    private void CloseHistory() => IsHistoryOpen = false;

    /// <summary>
    /// A card stored or revoked a verdict. Re-reads the history so the header's
    /// count — and the list itself, if somebody opens it next — agrees with the
    /// store rather than with the last time anyone looked.
    /// </summary>
    private void OnCardVerdictChanged(object? sender, EventArgs e)
        => _ = History.LoadCommand.ExecuteAsync(null);

    /// <summary>
    /// A verdict was taken back on the history screen. Any card still showing
    /// its receipt goes back to offering both controls — the store has one row
    /// and the two surfaces may not disagree about what it says.
    /// </summary>
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

    /// <summary>
    /// The library reloaded under the feed. Skipped while a pass is already out
    /// — two scoring passes racing to write the same shelves would be half a
    /// second of work to arrive at the answer the first one is already
    /// fetching.
    /// </summary>
    private void OnTilesChanged(object? sender, EventArgs e)
    {
        if (!LoadCommand.IsRunning)
        {
            _ = LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// §6 of the engine's doc: the tier rides on the feed so the UI can
    /// calibrate its confidence rather than either going blank or overclaiming.
    /// Established says nothing — a feed that has earned its confidence should
    /// stop apologising.
    /// </summary>
    private static string? NoteFor(FeedConfidence confidence, bool failed) => failed switch
    {
        true => null,
        false => confidence switch
        {
            FeedConfidence.EarlyDays =>
                "Scored from playtime and patch history alone so far. It sharpens as Winnow watches you play.",
            FeedConfidence.Settling =>
                "Winnow has started recording your sessions. The picks sharpen from here.",
            _ => null,
        },
    };
}
