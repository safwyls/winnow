using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.Services;

namespace Hoard.App.ViewModels;

/// <summary>
/// The Feed — the app's landing view, and the reason to launch games through
/// Hoard at all (ROADMAP §2: the launcher collects the history, the history
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
    }

    /// <summary>Rails in presentation order — the engine's claim order, strongest story first.</summary>
    public System.Collections.ObjectModel.ObservableCollection<FeedShelfViewModel> Shelves { get; } = [];

    /// <summary>The screen's own name. Directive rather than clever: it says what the screen is for.</summary>
    public string Title => "Where to start";

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

    public bool ShowShelves => !IsLoading && Message is null;

    public bool ShowMessage => Message is not null;

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
            Message = "Hoard couldn't work out a feed just now. Nothing in your library has changed — All games is in the rail.";
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
                    cards.Add(new FeedCardViewModel(tile, item.Reason));
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
            ? "Nothing to score yet. Hoard reads your library first, and the feed follows it."
            : "Nothing to put in front of you today. Everything Hoard scored is either played out, in rotation, or too recent to call forgotten.";
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
                "Scored from playtime and patch history alone so far. It sharpens as Hoard watches you play.",
            FeedConfidence.Settling =>
                "Hoard has started recording your sessions. The picks sharpen from here.",
            _ => null,
        },
    };
}
