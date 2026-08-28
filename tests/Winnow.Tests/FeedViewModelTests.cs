using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Feed's view model — M8's half of the recommender.
///
/// <para><b>Nothing here scores anything.</b> The screen talks to
/// <see cref="IFeedService"/>, which is the App-layer seam that exists so this
/// is possible: every state — working, five shelves, a thin shelf, an omitted
/// one, a quiet library and a failed pass — is driven by a fake returning
/// values. A test that ran the real engine would need a thousand-row database
/// and would be asserting on <c>Winnow.Recommend</c>'s behaviour, which its own
/// 68 tests already own.</para>
///
/// <para><b>What is actually being defended here</b> is the one rule the
/// milestone cannot lose: every card states its reason, in the engine's own
/// words, in full. So the reason is asserted verbatim rather than by prefix, and
/// the run-splitting that sets its numbers in the data face is asserted to
/// reproduce the sentence exactly.</para>
///
/// <para>No Avalonia application, dispatcher or rendering is involved.</para>
/// </summary>
public sealed class FeedViewModelTests
{
    /// <summary>
    /// A real reason off the author's library, kept whole: the playtime, the
    /// year, the quoted patch title and the taste clause. It is the longest
    /// shape the patched shelf produces short of a mode-mismatch row, and it is
    /// what the card has to be able to say without trimming.
    ///
    /// <para>The shelf ids below are literals rather than
    /// <c>Winnow.Recommend.ShelfIds</c>, which is the seam's own argument made in
    /// the test: these are the ids the App layer receives across
    /// <see cref="IFeedService"/>, and a test reaching for the scoring module's
    /// constants would be asserting the boundary does not exist.</para>
    /// </summary>
    private const string PatchedReason =
        "You put 2.8 hours into this in 2021 and it has had an update since, most recently \"PATCH NOTES - S06.05.02\". Survival is where your hours go, and this is one.";

    // ── The five shelves ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_five_shelves_arrive_in_the_engines_order_with_their_own_pitches()
    {
        var tiles = new FakeTileSource();
        var feed = new FeedViewModel(
            new FakeFeedService(FullFeed(tiles)),
            tiles);

        await feed.LoadCommand.ExecuteAsync(null);

        Assert.Equal(
            [
                "patched_while_away",
                "worth_another_look",
                "ready_to_play",
                "barely_touched",
                "on_your_taste",
            ],
            feed.Shelves.Select(s => s.Id));

        // The shelf's own one-line pitch is the engine's, not a rewrite of it.
        Assert.Equal(
            "Major updates landed after you stopped playing.",
            feed.Shelves[0].Blurb);

        Assert.True(feed.ShowShelves);
        Assert.False(feed.ShowMessage);
        Assert.Null(feed.Message);
    }

    [Fact]
    public async Task Every_card_carries_the_engines_sentence_verbatim()
    {
        var tiles = new FakeTileSource();
        var feed = new FeedViewModel(new FakeFeedService(FullFeed(tiles)), tiles);

        await feed.LoadCommand.ExecuteAsync(null);

        var card = feed.Shelves[0].Cards[0];

        // Not truncated, not summarised, not moved into a tooltip. The whole
        // sentence, including the quoted patch title at the end of it.
        Assert.Equal(PatchedReason, card.Reason);

        // And the split that lets the card set its numbers in Plex Mono is
        // lossless — concatenating the runs is the sentence again.
        Assert.Equal(PatchedReason, string.Concat(card.ReasonRuns.Select(r => r.Text)));
    }

    // ── A thin shelf, and an omitted one ─────────────────────────────────────

    [Fact]
    public async Task A_thin_shelf_is_a_finished_shelf_and_states_its_own_count()
    {
        var tiles = new FakeTileSource();

        // Six items is what `ready_to_play` actually holds on the real library.
        var snapshot = Snapshot(
            Shelf("ready_to_play", "Installed and waiting", "Already on your disk, nothing sunk.",
                Enumerable.Range(1, 6).Select(i => Item(tiles, i, $"Reason {i}.")).ToArray()));

        var feed = new FeedViewModel(new FakeFeedService(snapshot), tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        var shelf = Assert.Single(feed.Shelves);
        Assert.Equal(6, shelf.Cards.Count);

        // The count is what makes six read as an answer rather than as a
        // half-loaded ten.
        Assert.Equal("6", shelf.CountText);
        Assert.True(feed.ShowShelves);
    }

    [Fact]
    public async Task An_omitted_shelf_is_absent_rather_than_empty()
    {
        var tiles = new FakeTileSource();

        // The engine omits a shelf with nothing to say, so the screen must never
        // be holding a place for one.
        var snapshot = Snapshot(
            Shelf("patched_while_away", "Patched while you were away", "Pitch.",
                Item(tiles, 1, PatchedReason)),
            Shelf("on_your_taste", "Never opened, right up your alley", "Pitch.",
                Item(tiles, 2, "Never opened since it joined your library.")));

        var feed = new FeedViewModel(new FakeFeedService(snapshot), tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, feed.Shelves.Count);
        Assert.DoesNotContain(feed.Shelves, s => s.Id == "worth_another_look");
        Assert.DoesNotContain(feed.Shelves, s => s.Cards.Count == 0);
    }

    [Fact]
    public async Task A_card_whose_tile_the_library_does_not_hold_is_dropped_not_faked()
    {
        var tiles = new FakeTileSource();
        var known = Item(tiles, 1, PatchedReason);
        var unknown = new FeedItem(9_999, 9_999, "A game the library has not loaded", "Some reason.");

        var feed = new FeedViewModel(
            new FakeFeedService(Snapshot(
                Shelf("patched_while_away", "Patched while you were away", "Pitch.", known, unknown))),
            tiles);

        await feed.LoadCommand.ExecuteAsync(null);

        var shelf = Assert.Single(feed.Shelves);
        var card = Assert.Single(shelf.Cards);
        Assert.Equal("Deep Rock Galactic 1", card.Tile.Title);
    }

    // ── The states that are not shelves ──────────────────────────────────────

    [Fact]
    public void The_screen_opens_working_rather_than_empty()
    {
        var feed = new FeedViewModel(new FakeFeedService(FeedSnapshot.Unavailable));

        // Before anything has been asked for: the honest state is "working", and
        // it must not be a blank pane, a spinner, or a claim about the library.
        Assert.True(feed.IsLoading);
        Assert.True(feed.ShowMessage);
        Assert.False(feed.ShowShelves);
        Assert.NotNull(feed.Message);
        Assert.Contains("Working out where to start", feed.Message!);

        // And it must not state a count it does not have yet.
        Assert.False(feed.HasCandidates);
        Assert.False(feed.CanRetry);
    }

    [Fact]
    public async Task The_working_state_holds_until_the_pass_answers()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(FullFeed(tiles)) { Gate = new TaskCompletionSource() };
        var feed = new FeedViewModel(service, tiles);

        var loading = feed.LoadCommand.ExecuteAsync(null);

        Assert.True(feed.IsLoading);
        Assert.False(feed.ShowShelves);
        Assert.Empty(feed.Shelves);

        service.Gate!.SetResult();
        await loading;

        Assert.False(feed.IsLoading);
        Assert.True(feed.ShowShelves);
        Assert.Equal(5, feed.Shelves.Count);
    }

    [Fact]
    public async Task A_failed_pass_says_so_offers_a_retry_and_blanks_nothing()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(FeedSnapshot.Unavailable);
        var feed = new FeedViewModel(service, tiles);

        await feed.LoadCommand.ExecuteAsync(null);

        Assert.False(feed.IsLoading);
        Assert.True(feed.ShowMessage);
        Assert.False(feed.ShowShelves);
        Assert.True(feed.CanRetry);

        // It must never be worded as a fact about the library — the library is
        // fine, and the copy says where it is.
        Assert.NotNull(feed.Message);
        Assert.Contains("couldn't work out a feed", feed.Message!);
        Assert.Contains("All games is in the rail", feed.Message!);

        // And retrying is a real route out of it.
        service.Next = FullFeed(tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        Assert.Equal(5, feed.Shelves.Count);
        Assert.False(feed.CanRetry);
        Assert.Null(feed.Message);
    }

    [Fact]
    public async Task A_service_that_throws_is_a_sentence_and_not_a_crash()
    {
        var feed = new FeedViewModel(new ThrowingFeedService(), new FakeTileSource());

        await feed.LoadCommand.ExecuteAsync(null);

        Assert.True(feed.CanRetry);
        Assert.NotNull(feed.Message);
        Assert.Empty(feed.Shelves);
    }

    [Fact]
    public async Task A_quiet_feed_and_an_unloaded_library_are_different_sentences()
    {
        var tiles = new FakeTileSource();
        tiles.Add(1, "Deep Rock Galactic 1");

        // Scored a thousand games and none of them earned a shelf: quiet.
        var quiet = new FeedViewModel(
            new FakeFeedService(new FeedSnapshot([], 997, FeedConfidence.Settling, Failed: false)),
            tiles);
        await quiet.LoadCommand.ExecuteAsync(null);
        Assert.Contains("Nothing to put in front of you today", quiet.Message!);

        // Nothing scored at all: the library has not been read yet, which is a
        // completely different claim and must not be worded as the first one.
        var cold = new FeedViewModel(
            new FakeFeedService(new FeedSnapshot([], 0, FeedConfidence.EarlyDays, Failed: false)),
            new FakeTileSource(empty: true));
        await cold.LoadCommand.ExecuteAsync(null);
        Assert.Contains("Nothing to score yet", cold.Message!);
    }

    // ── A library reload under the feed ──────────────────────────────────────

    [Fact]
    public async Task A_library_reload_rescores_the_feed_without_blanking_it()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(FullFeed(tiles));
        var feed = new FeedViewModel(service, tiles);

        await feed.LoadCommand.ExecuteAsync(null);
        Assert.Equal(1, service.Calls);

        // Enrichment renamed works and landed covers behind a library the user
        // was already browsing; every tile object the cards hold is superseded.
        tiles.Reload();

        Assert.Equal(2, service.Calls);

        // And the shelves never went away while it happened: a pass behind a
        // screen that already has shelves must not flash the working state.
        Assert.True(feed.ShowShelves);
        Assert.Null(feed.Message);
        Assert.Equal(5, feed.Shelves.Count);
    }

    [Fact]
    public async Task A_refresh_that_fails_leaves_the_shelves_that_are_already_true()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(FullFeed(tiles));
        var feed = new FeedViewModel(service, tiles);

        await feed.LoadCommand.ExecuteAsync(null);

        service.Next = FeedSnapshot.Unavailable;
        tiles.Reload();

        // The games on screen are still real games with still-true reasons.
        // Replacing them with an apology would charge the user for a retry they
        // did not ask for.
        Assert.Equal(5, feed.Shelves.Count);
        Assert.True(feed.ShowShelves);
        Assert.Null(feed.Message);
        Assert.False(feed.CanRetry);
    }

    // ── Confidence, and the number in the header ─────────────────────────────

    [Fact]
    public async Task The_tier_calibrates_the_copy_and_goes_quiet_once_it_is_earned()
    {
        var tiles = new FakeTileSource();

        var cold = new FeedViewModel(new FakeFeedService(FullFeed(tiles, FeedConfidence.EarlyDays)), tiles);
        await cold.LoadCommand.ExecuteAsync(null);
        Assert.True(cold.HasConfidenceNote);
        Assert.Contains("sharpens", cold.ConfidenceNote!);

        var settled = new FeedViewModel(new FakeFeedService(FullFeed(tiles, FeedConfidence.Established)), tiles);
        await settled.LoadCommand.ExecuteAsync(null);
        Assert.False(settled.HasConfidenceNote);
        Assert.Null(settled.ConfidenceNote);
    }

    [Fact]
    public async Task The_candidate_count_is_stated_only_once_there_is_one()
    {
        var tiles = new FakeTileSource();
        var feed = new FeedViewModel(new FakeFeedService(FullFeed(tiles)), tiles);

        await feed.LoadCommand.ExecuteAsync(null);

        Assert.True(feed.HasCandidates);
        Assert.Equal("997", feed.CandidateCountText);
    }

    // ── The reason's numbers ─────────────────────────────────────────────────

    [Fact]
    public void Every_word_carrying_a_digit_is_data_and_the_rest_is_prose()
    {
        var runs = ReasonText.Split(PatchedReason);

        // Lossless first: the sentence survives the split unchanged.
        Assert.Equal(PatchedReason, string.Concat(runs.Select(r => r.Text)));

        var data = runs.Where(r => r.IsData).Select(r => r.Text).ToList();

        // The playtime, the year, and the version inside the quoted patch title
        // — a version string is data all the way through, which is why the rule
        // is per word rather than per digit.
        Assert.Equal(["2.8", "2021", "S06.05.02"], data);

        // The quotation marks belong to the sentence, not to the number they
        // happen to touch.
        Assert.Contains(runs, r => !r.IsData && r.Text.Contains('"'));
    }

    [Fact]
    public void A_sentence_with_no_numbers_stays_one_run_of_prose()
    {
        var run = Assert.Single(ReasonText.Split("Never opened since it joined your library."));

        Assert.False(run.IsData);
        Assert.Equal("Never opened since it joined your library.", run.Text);
    }

    [Fact]
    public void An_empty_reason_splits_to_nothing_rather_than_to_an_empty_run()
        => Assert.Empty(ReasonText.Split(null));

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static FeedSnapshot FullFeed(
        FakeTileSource tiles, FeedConfidence confidence = FeedConfidence.Settling)
        => new(
            [
                Shelf("patched_while_away", "Patched while you were away",
                    "Major updates landed after you stopped playing.",
                    Item(tiles, 1, PatchedReason)),
                Shelf("worth_another_look", "Worth another look",
                    "You committed real hours past the refund line, then drifted off mid-story.",
                    Item(tiles, 2, "You put 2.5 hours in — past the refund line — then let it go — that was 2022.")),
                Shelf("ready_to_play", "Installed and waiting",
                    "Already on your disk with nothing sunk.",
                    Item(tiles, 3, "Never opened since it joined your library. It's installed and ready to launch.")),
                Shelf("barely_touched", "Barely gave it a chance",
                    "Under 2 hours in — you opened the door and never walked through.",
                    Item(tiles, 4, "You tried it for 104 minutes and never went back — that was 2017.")),
                Shelf("on_your_taste", "Never opened, right up your alley",
                    "Sitting sealed in your library, and it matches where your hours actually go.",
                    Item(tiles, 5, "Never opened since it joined your library. Sandbox is where your hours go, and this is one.")),
            ],
            CandidateCount: 997,
            confidence,
            Failed: false);

    private static FeedSnapshot Snapshot(params FeedShelf[] shelves)
        => new(shelves, CandidateCount: 997, FeedConfidence.Settling, Failed: false);

    private static FeedShelf Shelf(string id, string title, string blurb, params FeedItem[] items)
        => new(id, title, blurb, items);

    /// <summary>Registers a tile with the fake library and returns the feed item that points at it.</summary>
    private static FeedItem Item(FakeTileSource tiles, long ownershipId, string reason)
    {
        var title = $"Deep Rock Galactic {ownershipId}";
        tiles.Add(ownershipId, title);
        return new FeedItem(ownershipId, ownershipId, title, reason);
    }
}

/// <summary>
/// A feed service that returns what a test hands it. <see cref="Gate"/> holds the
/// answer back so the working state can be observed while a real pass would be
/// out — the state the app spends its first half-second in.
///
/// <para>It also stands in for the feedback store, with the same
/// append-and-revoke semantics migration 0011 gives the real one: a verdict is a
/// row, undo is a stamp on it, and nothing is ever deleted. That is what lets
/// the history assertions below be about the SCREEN rather than about SQLite.</para>
/// </summary>
internal sealed class FakeFeedService : IFeedService
{
    private readonly List<FeedVerdictRecord> _verdicts = [];

    public FakeFeedService(FeedSnapshot next) => Next = next;

    public FeedSnapshot Next { get; set; }

    public TaskCompletionSource? Gate { get; init; }

    public int Calls { get; private set; }

    /// <summary>Makes every write fail, the way a locked or missing database would.</summary>
    public bool WritesFail { get; set; }

    /// <summary>The clock the expiry is computed from, so a snooze's date is assertable.</summary>
    public DateTime Now { get; set; } = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Every verdict ever recorded here, newest first — the real repository's order.</summary>
    public IReadOnlyList<FeedVerdictRecord> Verdicts => _verdicts;

    public async Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
    {
        Calls++;
        if (Gate is not null)
        {
            await Gate.Task.WaitAsync(ct);
        }

        return Next;
    }

    public Task<FeedVerdictOutcome> RecordVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
    {
        if (WritesFail)
        {
            return Task.FromResult(FeedVerdictOutcome.NotSaved);
        }

        var expires = kind == FeedVerdictKind.Snoozed ? Now.AddDays(30) : (DateTime?)null;

        // Newest first, like GetAllVerdictsAsync.
        _verdicts.Insert(0, new FeedVerdictRecord(
            releaseId, kind, Now, expires, RevokedAt: null, FeedVerdictStatus.Active));

        return Task.FromResult(new FeedVerdictOutcome(Saved: true, expires));
    }

    public Task<bool> RevokeVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
    {
        if (WritesFail)
        {
            return Task.FromResult(false);
        }

        var revoked = false;
        for (var i = 0; i < _verdicts.Count; i++)
        {
            var row = _verdicts[i];
            if (row.ReleaseId != releaseId || row.Kind != kind || row.Status != FeedVerdictStatus.Active)
            {
                continue;
            }

            // A stamp, never a deletion — the row survives as history.
            _verdicts[i] = row with { RevokedAt = Now, Status = FeedVerdictStatus.Undone };
            revoked = true;
        }

        return Task.FromResult(revoked);
    }

    public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>(_verdicts.ToList());

    /// <summary>Puts a row in that this session did not write — a lapsed snooze, an old dismissal.</summary>
    public void Seed(FeedVerdictRecord record) => _verdicts.Insert(0, record);
}

/// <summary>The service contract says it never throws; this one does, to prove the screen survives it.</summary>
internal sealed class ThrowingFeedService : IFeedService
{
    public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("the database went away mid-pass");

    public Task<FeedVerdictOutcome> RecordVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
        => throw new InvalidOperationException("the database went away mid-write");

    public Task<bool> RevokeVerdictAsync(
        long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
        => throw new InvalidOperationException("the database went away mid-write");

    public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
        => throw new InvalidOperationException("the database went away mid-read");
}

/// <summary>
/// The library's tiles, without a library or a database behind them. Tiles are
/// built directly because <see cref="GameTileViewModel"/>'s constructor is the
/// same one <c>LibraryViewModel</c> uses — what a test needs from it here is an
/// identity and a title, not cover art.
/// </summary>
internal sealed class FakeTileSource : IGameTileSource
{
    private readonly Dictionary<long, GameTileViewModel> _tiles = [];
    private readonly bool _empty;

    public FakeTileSource(bool empty = false) => _empty = empty;

    public event EventHandler? TilesChanged;

    public bool HasTiles => !_empty && _tiles.Count > 0;

    /// <summary>Stands in for a library reload: every tile object is replaced.</summary>
    public void Reload() => TilesChanged?.Invoke(this, EventArgs.Empty);

    public void Add(long ownershipId, string title)
        => _tiles[ownershipId] = new GameTileViewModel(
            ownershipId: ownershipId,
            releaseId: ownershipId,
            title: title,
            store: "steam",
            bucket: LibraryBuckets.StaleButPatched,
            playtimeMinutes: 168,
            lastPlayedUtc: new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            nowUtc: new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc),
            hasUnread: true);

    public GameTileViewModel? TileForOwnership(long ownershipId)
        => _empty ? null : _tiles.GetValueOrDefault(ownershipId);

    /// <summary>
    /// The fixtures above give a tile the same id for both keys, so a release
    /// lookup is the ownership lookup — which is what the inspection screen
    /// needs to put a title against a stored verdict.
    /// </summary>
    public GameTileViewModel? TileForRelease(long releaseId)
    {
        if (_empty)
        {
            return null;
        }

        foreach (var tile in _tiles.Values)
        {
            if (tile.ReleaseId == releaseId)
            {
                return tile;
            }
        }

        return null;
    }
}

/// <summary>
/// A Feed for tests that need one only because <see cref="MainWindowViewModel"/>
/// requires it. Nothing is scored, which is the state every such test wants.
/// </summary>
internal static class DetachedFeed
{
    public static FeedViewModel Create()
        => new(new FakeFeedService(FeedSnapshot.Unavailable));
}
