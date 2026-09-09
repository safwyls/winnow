using System.Collections.Specialized;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// A verdict leaves a receipt in the card's place — that is where the
/// zero-friction undo lives, and a snooze's return date is only readable while
/// something is on screen to state it. The receipt counts down for three seconds
/// and is then replaced by the next card from the shelf's reserve; the clock is
/// held while the pointer is over the card or something in it has focus, because
/// a three-second window on an undo is a time limit and the undo is a Tab stop.
/// When a swap empties the queue, a backfill scores the feed again and merges
/// fresh reserves into the live queues without touching any card on screen.
///
/// <para>These cover the seam (what the service holds back and what it logs as
/// shown) and the screen (the countdown, the hold, the swap, what is released
/// with the card that leaves, and the backfill that tops the queue back
/// up).</para>
/// </summary>
public sealed class FeedReserveTests
{
    private static readonly DateTime Now = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    private const string Reason = "Never opened since it joined your library.";

    // ── The seam: what one pass shows and what it holds ──────────────────────

    [Fact]
    public async Task The_service_asks_one_pass_for_more_than_it_shows()
    {
        var engine = new DeepEngine(items: 10);
        await new FeedService(engine, new FakeFeedbackStore(), new FixedClock(Now)).GetShelvesAsync();

        Assert.NotNull(engine.LastRequest);

        // Both halves stated. The engine needs the surface size to fill the
        // visible slices before any reserve and to size the reason ledger's
        // variety caps — see RecommendationRequest.VisiblePerShelf.
        Assert.Equal(6, engine.LastRequest!.VisiblePerShelf);
        Assert.True(engine.LastRequest.MaxPerShelf > engine.LastRequest.VisiblePerShelf);
    }

    [Fact]
    public async Task A_shelf_shows_six_and_holds_the_rest()
    {
        var engine = new DeepEngine(items: 10);
        var snapshot = await new FeedService(engine, new FakeFeedbackStore(), new FixedClock(Now))
            .GetShelvesAsync();

        var shelf = Assert.Single(snapshot.Shelves);

        Assert.Equal(6, shelf.Items.Count);
        Assert.Equal(4, shelf.Reserve.Count);

        // The held ones are the ones past the slice, in the order the pass put
        // them in, and none of them is also on screen.
        Assert.Equal([7L, 8L, 9L, 10L], shelf.Reserve.Select(i => i.ReleaseId));
        Assert.Empty(shelf.Items.Select(i => i.ReleaseId).Intersect(shelf.Reserve.Select(i => i.ReleaseId)));
    }

    [Fact]
    public async Task A_shelf_shallower_than_the_slice_holds_nothing_back()
    {
        var engine = new DeepEngine(items: 4);
        var snapshot = await new FeedService(engine, new FakeFeedbackStore(), new FixedClock(Now))
            .GetShelvesAsync();

        var shelf = Assert.Single(snapshot.Shelves);

        Assert.Equal(4, shelf.Items.Count);
        Assert.Empty(shelf.Reserve);
    }

    [Fact]
    public async Task Generating_cards_and_reserves_records_no_impressions()
    {
        var store = new FakeFeedbackStore();
        await new FeedService(new DeepEngine(items: 10), store, new FixedClock(Now)).GetShelvesAsync();

        // A held card has been seen by nobody. Logging it here would earn it the
        // recently-surfaced demotion tomorrow, and would put it inside the
        // endorsement window, for an impression that never happened.
        Assert.Empty(store.Surfacings);
    }

    [Fact]
    public async Task A_card_promoted_out_of_the_reserve_logs_itself_when_it_appears()
    {
        var store = new FakeFeedbackStore();
        var service = new FeedService(new DeepEngine(items: 10), store, new FixedClock(Now));

        await service.RecordSurfacedAsync(7, "patched_while_away");

        var row = Assert.Single(store.Surfacings);
        Assert.Equal(7, row.ReleaseId);
        Assert.Equal("patched_while_away", row.ShelfId);
        Assert.Equal(DateOnly.FromDateTime(Now), row.SurfacedOn);
    }

    [Fact]
    public async Task A_surfacing_write_that_fails_at_swap_time_costs_rotation_and_not_the_card()
    {
        var store = new FakeFeedbackStore { RecordSurfacedThrows = true };
        var service = new FeedService(new DeepEngine(items: 10), store, new FixedClock(Now));

        // The card is already on screen by the time this runs. It may not throw
        // back into the screen that put it there.
        await service.RecordSurfacedAsync(7, "patched_while_away");

        Assert.Empty(store.Surfacings);
    }

    // ── The screen: the receipt, and what takes its place ────────────────────

    [Fact]
    public async Task A_receipt_goes_on_the_clock_when_the_shelf_is_holding_a_replacement()
    {
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 1);

        var first = feed.Shelves[0].Cards[0];
        Assert.True(first.CanReplace);
        Assert.False(first.IsCountingDown);

        await first.NotInterestedCommand.ExecuteAsync(null);

        Assert.True(first.IsSetAside);
        Assert.True(first.IsCountingDown);
        Assert.Equal(0.0, first.CountdownProgress);
        Assert.True(feed.IsCountingDown());
    }

    [Fact]
    public async Task The_clock_runs_its_window_out_and_states_how_far_it_has_got()
    {
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 1);
        var card = feed.Shelves[0].Cards[0];

        await card.NotInterestedCommand.ExecuteAsync(null);

        // Halves of the window rather than a number of seconds: what is under
        // test is that the indicator is determinate, not how long the window is.
        feed.Tick(FeedCardViewModel.Countdown / 2);

        // Determinate, and it says so: half the window has run, and the arc the
        // view sweeps is half a turn.
        Assert.Equal(0.5, card.CountdownProgress, 3);
        Assert.Equal(180.0, card.CountdownSweep, 3);
        Assert.Same(card, feed.Shelves[0].Cards[0]);

        feed.Tick(FeedCardViewModel.Countdown / 2);

        Assert.NotSame(card, feed.Shelves[0].Cards[0]);
        Assert.Equal("Held 101", feed.Shelves[0].Cards[0].Tile.Title);
    }

    [Fact]
    public async Task The_clock_is_held_while_the_reader_is_on_the_card()
    {
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 1);
        var card = feed.Shelves[0].Cards[0];

        await card.NotInterestedCommand.ExecuteAsync(null);

        // A whole window of pointer, and none of it counts. The undo is a time
        // limit on the reader, and taking it away from somebody looking at it is
        // the one thing this must not do.
        card.IsPointerOver = true;
        feed.Tick(FeedCardViewModel.Countdown);

        Assert.Same(card, feed.Shelves[0].Cards[0]);
        Assert.Equal(0.0, card.CountdownProgress);

        // Undo is a Tab stop, so focus holds it for the same reason.
        card.IsPointerOver = false;
        card.IsFocusWithin = true;
        feed.Tick(FeedCardViewModel.Countdown);

        Assert.Same(card, feed.Shelves[0].Cards[0]);
        Assert.Equal(0.0, card.CountdownProgress);

        // And it resumes from where it was, not from where it would have been.
        card.IsFocusWithin = false;
        feed.Tick(FeedCardViewModel.Countdown);

        Assert.NotSame(card, feed.Shelves[0].Cards[0]);
    }

    [Fact]
    public async Task Reduced_motion_steps_the_clock_instead_of_sweeping_it()
    {
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 1, reducedMotion: true);
        var (smooth, _, _) = await ScreenAsync(cards: 2, reserve: 1);

        // The indicator is determinate, so it is information and stays; what
        // goes under §8 is the continuous movement. A handful of steps rather
        // than thirty a second.
        Assert.Equal(TimeSpan.FromSeconds(1), feed.TickInterval);
        Assert.True(smooth.TickInterval < TimeSpan.FromMilliseconds(50));

        // Still accurate, and still ends where it should.
        var card = feed.Shelves[0].Cards[0];
        await card.NotInterestedCommand.ExecuteAsync(null);

        feed.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(1.0 / FeedCardViewModel.Countdown.TotalSeconds, card.CountdownProgress, 3);

        feed.Tick(FeedCardViewModel.Countdown);
        Assert.NotSame(card, feed.Shelves[0].Cards[0]);
    }

    [Fact]
    public async Task The_receipt_running_out_puts_the_next_card_in_that_cards_place()
    {
        var (feed, service, _) = await ScreenAsync(cards: 2, reserve: 1);
        var shelf = feed.Shelves[0];

        var changes = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)shelf.Cards).CollectionChanged += (_, e) => changes.Add(e);

        var outgoing = shelf.Cards[0];
        await outgoing.NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        // In its place: one index assigned, the shelf not rebuilt. Clearing the
        // collection would re-realise every container in the feed and re-lease
        // every cover, for one card the reader answered.
        var change = Assert.Single(changes);
        Assert.Equal(NotifyCollectionChangedAction.Replace, change.Action);
        Assert.Equal(0, change.NewStartingIndex);

        Assert.Equal(2, shelf.Cards.Count);
        Assert.NotSame(outgoing, shelf.Cards[0]);
        Assert.Equal("Held 101", shelf.Cards[0].Tile.Title);
        Assert.False(shelf.Cards[0].IsSetAside);

        Assert.Empty(service.Surfaced);
        await feed.RecordViewportEntryAsync(shelf.Cards[0]);
        Assert.Equal([(101L, "ready_to_play")], service.Surfaced);
    }

    [Fact]
    public async Task The_verdict_survives_the_card_that_carried_it()
    {
        var (feed, service, _) = await ScreenAsync(cards: 2, reserve: 1);
        var outgoing = feed.Shelves[0].Cards[0];

        await outgoing.NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        // The replacement replaces the RECEIPT. The verdict stands, and the
        // history screen is where it can still be taken back.
        var verdict = Assert.Single(service.Verdicts);
        Assert.Equal(1, verdict.ReleaseId);
        Assert.Equal(FeedVerdictKind.NotInterested, verdict.Kind);
        Assert.Equal(FeedVerdictStatus.Active, verdict.Status);
    }

    [Fact]
    public async Task A_not_now_receipt_states_its_return_date_and_survives_the_press_that_made_it()
    {
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 2);
        var card = feed.Shelves[0].Cards[0];

        await card.NotNowCommand.ExecuteAsync(null);

        // Nothing may take this off the screen on the press that made it: the
        // date is the whole content of a "not now" receipt, and a card that
        // vanished under the finger would state it to nobody.
        Assert.Same(card, feed.Shelves[0].Cards[0]);
        Assert.True(card.IsSetAside);
        Assert.True(card.HasSetAsideDate);
        Assert.Equal("Back on", card.SetAsideNote);
    }

    [Fact]
    public async Task Taking_a_verdict_back_leaves_the_card_where_it_is()
    {
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 2);
        var card = feed.Shelves[0].Cards[0];

        await card.NotNowCommand.ExecuteAsync(null);
        Assert.True(card.IsCountingDown);

        await card.UndoCommand.ExecuteAsync(null);

        // The clock stops with the receipt it was counting, and the card stays
        // where it is however long the feed ticks afterwards.
        Assert.False(card.IsCountingDown);
        Assert.Equal(0.0, card.CountdownProgress);
        Assert.False(feed.IsCountingDown());

        feed.Tick(TimeSpan.FromSeconds(30));

        Assert.Same(card, feed.Shelves[0].Cards[0]);
        Assert.False(card.IsSetAside);
    }

    [Fact]
    public async Task A_replacement_never_arrives_saying_what_a_card_on_screen_is_saying()
    {
        var tiles = new FakeTileSource();

        // The shelf has run out of distinct sentences: the reserve's first card
        // says what the card being dismissed says, and its second says what the
        // card beside it says. Neither may be promoted — a swap is the one
        // moment two cards that were never scored side by side can end up
        // there.
        var snapshot = new FeedSnapshot(
            [
                new FeedShelf(
                    "on_your_taste",
                    "Never opened, right up your alley",
                    "Sitting sealed in your library.",
                    [Said(tiles, 1, "Shown 1", "Still sealed since the day it arrived."),
                     Said(tiles, 2, "Shown 2", "Never opened since it joined your library.")])
                {
                    Reserve =
                    [
                        Said(tiles, 101, "Held 1", "Still sealed since the day it arrived."),
                        Said(tiles, 102, "Held 2", "Never opened since it joined your library."),
                        Said(tiles, 103, "Held 3", "Sitting there unopened since you bought it."),
                    ],
                },
            ],
            CandidateCount: 997,
            FeedConfidence.Settling,
            Failed: false);

        var service = new FakeFeedService(snapshot);
        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        var shelf = feed.Shelves[0];
        var outgoing = shelf.Cards[0];
        await outgoing.NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        Assert.Equal("Held 3", shelf.Cards[0].Tile.Title);
        Assert.Empty(service.Surfaced);
        await feed.RecordViewportEntryAsync(shelf.Cards[0]);
        Assert.Equal([(103L, "on_your_taste")], service.Surfaced);
    }

    [Fact]
    public async Task Derelict_reserve_can_repeat_the_same_source_fact()
    {
        var tiles = new FakeTileSource();
        const string reason = "IGDB reports offline status (98% confidence).";
        var shelf = new FeedShelf("derelict", "Derelict", "Lifecycle evidence.",
            [Said(tiles, 1, "Closed one", reason), Said(tiles, 2, "Closed two", reason)])
        {
            Reserve = [Said(tiles, 3, "Closed three", reason)],
        };
        var service = new FakeFeedService(new FeedSnapshot([shelf], 0, FeedConfidence.EarlyDays, Failed: false));
        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);
        await feed.Shelves[0].Cards[0].NotInterestedCommand.ExecuteAsync(null);

        feed.Tick(FeedCardViewModel.Countdown);

        Assert.Equal("Closed three", feed.Shelves[0].Cards[0].Tile.Title);
    }

    [Fact]
    public async Task A_shelf_with_nothing_held_keeps_the_receipt_it_shipped_with()
    {
        var (feed, service, _) = await ScreenAsync(cards: 2, reserve: 0);
        var card = feed.Shelves[0].Cards[0];

        await card.NotInterestedCommand.ExecuteAsync(null);

        // No replacement, so no clock: a countdown running out on nothing would
        // be stating a replacement the shelf does not have.
        Assert.False(card.CanReplace);
        Assert.False(card.IsCountingDown);

        feed.Tick(TimeSpan.FromSeconds(30));

        Assert.Same(card, feed.Shelves[0].Cards[0]);
        Assert.True(card.IsSetAside);
        Assert.Empty(service.Surfaced);
    }

    // ── Generation scoping, release, and refill ──────────────────────────────

    [Fact]
    public async Task A_swap_from_a_pass_the_feed_has_replaced_is_discarded()
    {
        var (feed, service, tiles) = await ScreenAsync(cards: 2, reserve: 2);

        var stale = feed.Shelves[0].Cards[0];
        await stale.NotInterestedCommand.ExecuteAsync(null);

        // A library reload: a fresh pass, with its own cards and its own reserve.
        tiles.Reload();
        await feed.LoadCommand.ExecuteAsync(null);

        var live = feed.Shelves[0].Cards[0];
        Assert.NotSame(stale, live);

        Assert.False(feed.Swap(stale));
        Assert.Same(live, feed.Shelves[0].Cards[0]);
        Assert.Empty(service.Surfaced);
    }

    [Fact]
    public async Task The_card_that_leaves_gives_up_its_cover_lease()
    {
        var cache = new HitCache();
        var pool = new CoverLeasePool(cache);
        var (feed, _, _) = await ScreenAsync(cards: 2, reserve: 1, covers: pool, cache: cache);

        var shelf = feed.Shelves[0];
        var outgoing = shelf.Cards[0];

        // Art on screen: a memory hit settles inside Request, so no dispatcher
        // has to exist for this to be the state a real card would be in.
        outgoing.Cover.Request(160);
        Assert.True(outgoing.Cover.HasCover);
        Assert.Equal(1, pool.LiveSlots);

        await outgoing.NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        // The pool slot is the observable: a card swapped out without being
        // disposed would hold its slot for the life of the window.
        Assert.NotSame(outgoing, shelf.Cards[0]);
        Assert.Equal(0, pool.LiveSlots);
        Assert.False(outgoing.Cover.HasCover);
    }

    [Fact]
    public async Task The_card_that_leaves_stops_reaching_the_screen()
    {
        var (feed, service, _) = await ScreenAsync(cards: 2, reserve: 1);
        var outgoing = feed.Shelves[0].Cards[0];

        await outgoing.NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        var before = service.HistoryCalls;
        await outgoing.UndoCommand.ExecuteAsync(null);

        // The card is off the screen but still alive in the test's hand. Its
        // verdict event must no longer reach the screen, or every dismissal
        // leaves another dead subscriber behind it.
        Assert.Equal(before, service.HistoryCalls);
    }

    // ── Backfill: keeping the queues topped up behind the screen ─────────────

    [Fact]
    public async Task A_swap_tops_the_sections_queue_back_up_without_touching_the_screen()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(Snapshot(tiles, cards: 2, reserve: 1));
        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        var shelf = feed.Shelves[0];
        var survivor = shelf.Cards[1];

        // What the next pass returns once the answered game is excluded: the
        // shelf has shifted up and a game no queue has held is at the bottom.
        service.Next = Snapshot(tiles, cards: 2, reserve: 1, firstShown: 200, firstHeld: 300);

        await shelf.Cards[0].NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);
        await feed.Backfilling;

        // The queue is topped up, and nothing that was drawn moved: the card
        // beside the swap is the same object, and the shelf is the same length.
        Assert.True(shelf.HasReserve);
        Assert.Same(survivor, shelf.Cards[1]);
        Assert.Equal(2, shelf.Cards.Count);

        // And the reader can keep going.
        await shelf.Cards[1].NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        Assert.NotSame(survivor, shelf.Cards[1]);
    }

    [Fact]
    public async Task A_backfill_never_offers_a_game_that_is_on_screen_or_already_queued()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(Snapshot(tiles, cards: 2, reserve: 1));
        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        var shelf = feed.Shelves[0];

        // A pass that returns exactly what this one did. Every id is already on
        // screen, already queued, or already swapped in.
        service.Next = Snapshot(tiles, cards: 2, reserve: 1);

        await shelf.Cards[0].NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);
        await feed.Backfilling;

        // Nothing enqueued, because there was nothing new to enqueue. The
        // alternative is re-offering the reader the card they are looking at.
        Assert.False(shelf.HasReserve);
        Assert.False(shelf.Cards[0].CanReplace);
    }

    [Fact]
    public async Task Backfills_are_coalesced_to_one_reading_and_one_waiting()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(Snapshot(tiles, cards: 3, reserve: 3));
        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        var shelf = feed.Shelves[0];
        var before = service.Calls;

        // The reads are held open, and three swaps ask for three backfills
        // while they are.
        service.Gate = new TaskCompletionSource();
        service.Next = Snapshot(tiles, cards: 3, reserve: 3, firstShown: 200, firstHeld: 300);

        for (var i = 0; i < 3; i++)
        {
            await shelf.Cards[i].NotInterestedCommand.ExecuteAsync(null);
            feed.Tick(FeedCardViewModel.Countdown);
        }

        service.Gate!.SetResult();
        await feed.Backfilling;

        // One in flight and one behind it. A reader answering a run of cards
        // must not queue a read per card, all returning the same answer at
        // increasing cost.
        Assert.Equal(2, service.Calls - before);
    }

    [Fact]
    public async Task A_backfill_from_a_pass_the_feed_has_replaced_is_discarded()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(Snapshot(tiles, cards: 2, reserve: 1));
        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);

        // The swap asks for a backfill, and that read is held open.
        var held = new TaskCompletionSource();
        service.Gate = held;
        service.Sequence.Enqueue(Snapshot(tiles, cards: 2, reserve: 1, firstShown: 200, firstHeld: 300));

        await feed.Shelves[0].Cards[0].NotInterestedCommand.ExecuteAsync(null);
        feed.Tick(FeedCardViewModel.Countdown);

        // A library reload lands first, with shelves and queues of its own.
        service.Gate = null;
        service.Next = Snapshot(tiles, cards: 2, reserve: 1, firstShown: 500, firstHeld: 600);
        await feed.LoadCommand.ExecuteAsync(null);

        held.SetResult();
        await feed.Backfilling;

        // What the held read returns is about shelves that are no longer on
        // screen, so none of it may reach the queue that replaced them.
        Assert.Equal([600L], feed.Shelves[0].Reserve.Select(i => i.ReleaseId));
    }

    [Fact]
    public async Task An_invalidation_arriving_during_a_pass_is_replayed_rather_than_dropped()
    {
        var tiles = new FakeTileSource();
        var service = new FakeFeedService(Snapshot(tiles, cards: 2, reserve: 0))
        {
            Gate = new TaskCompletionSource(),
        };
        var feed = new FeedViewModel(service, tiles);

        var loading = feed.LoadCommand.ExecuteAsync(null);

        // The library finished importing while the pass was reading. The pass in
        // flight has already read the state this is about, so dropping the event
        // leaves the feed stale until something else happens to ask.
        tiles.Reload();

        service.Gate!.SetResult();
        await loading;

        Assert.Equal(2, service.Calls);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    /// <summary>A loaded screen: one shelf of <paramref name="cards"/> cards over <paramref name="reserve"/> held ones.</summary>
    private static async Task<(FeedViewModel Feed, FakeFeedService Service, FakeTileSource Tiles)> ScreenAsync(
        int cards,
        int reserve,
        ICoverLeases? covers = null,
        HitCache? cache = null,
        bool reducedMotion = false)
    {
        var tiles = new FakeTileSource();
        tiles.Ramp.ReducedMotion = reducedMotion;

        var service = new FakeFeedService(Snapshot(tiles, cards, reserve, covers, cache));
        var feed = new FeedViewModel(service, tiles);

        await feed.LoadCommand.ExecuteAsync(null);
        return (feed, service, tiles);
    }

    /// <param name="firstShown">
    /// Where the shown items' ids start. A backfill test hands the second pass a
    /// different range, which is what a real second pass returns once the
    /// answered games are excluded and every shelf has shifted up.
    /// </param>
    private static FeedSnapshot Snapshot(
        FakeTileSource tiles,
        int cards,
        int reserve,
        ICoverLeases? covers = null,
        HitCache? cache = null,
        long firstShown = 1,
        long firstHeld = 101)
    {
        var shown = new List<FeedItem>();
        for (var i = 0; i < cards; i++)
        {
            shown.Add(Item(tiles, firstShown + i, $"Shown {firstShown + i}", covers, cache));
        }

        var held = new List<FeedItem>();
        for (var i = 0; i < reserve; i++)
        {
            held.Add(Item(tiles, firstHeld + i, $"Held {firstHeld + i}", covers, cache));
        }

        return new FeedSnapshot(
            [
                new FeedShelf(
                    "ready_to_play",
                    "Installed and waiting",
                    "Already on your disk, nothing sunk.",
                    shown)
                {
                    Reserve = held,
                },
            ],
            CandidateCount: 997,
            FeedConfidence.Settling,
            Failed: false);
    }

    /// <summary>One item with a stated sentence, for the shelf that has run out of them.</summary>
    private static FeedItem Said(FakeTileSource tiles, long id, string title, string reason)
    {
        tiles.Add(id, title);
        return new FeedItem(id, id, title, reason);
    }

    private static FeedItem Item(
        FakeTileSource tiles, long id, string title, ICoverLeases? covers, HitCache? cache)
    {
        if (covers is null)
        {
            tiles.Add(id, title);
        }
        else
        {
            var key = CoverKey.Steam(id.ToString());
            cache?.Put(key, 160);
            tiles.Add(id, title, key, covers);
        }

        // A sentence of its own per card: one shelf shares one reason ledger,
        // and a card is never promoted into a shelf that is already saying what
        // it would say.
        return new FeedItem(id, id, title, $"{Reason} ({title})");
    }

    /// <summary>
    /// A cover cache that is always a memory hit, so a card's art settles inside
    /// <c>Request</c> and the lease pool's slot count is observable without a
    /// dispatcher. The payload has null layers; nothing here looks inside it.
    /// </summary>
    private sealed class HitCache : ICoverCache
    {
        private readonly Dictionary<(CoverKey Key, int Width), CoverArt> _memory = [];

        public void Put(CoverKey key, int width) => _memory[(key, width)] = new CoverArt(null!, null);

        public bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
            => _memory.TryGetValue((key, CoverImaging.SnapWidth(displayWidthPixels)), out art!);

        public Task<CoverArt?> GetAsync(
            CoverKey key, double displayWidthPixels, CoverLayers layers, CancellationToken ct = default)
            => Task.FromResult<CoverArt?>(
                TryGet(key, displayWidthPixels, layers, out var art) ? art : null);
    }

    /// <summary>An engine that returns one shelf of a stated depth, so the service's slice is visible.</summary>
    private sealed class DeepEngine(int items) : Winnow.Recommend.IRecommendationEngine
    {
        public Winnow.Recommend.RecommendationRequest? LastRequest { get; private set; }

        public Task<Winnow.Recommend.RecommendationFeed> GetFeedAsync(
            Winnow.Recommend.RecommendationRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("the Feed screen asks for shelves");

        public Task<Winnow.Recommend.ShelfFeed> GetShelvesAsync(
            Winnow.Recommend.RecommendationRequest request, CancellationToken ct = default)
        {
            LastRequest = request;

            return Task.FromResult(new Winnow.Recommend.ShelfFeed
            {
                Shelves =
                [
                    new Winnow.Recommend.RecommendationShelf
                    {
                        Id = "patched_while_away",
                        Title = "Patched while you were away",
                        Blurb = "Major updates landed after you stopped playing.",
                        Items = Enumerable.Range(1, items).Select(i => Item(i)).ToList(),
                    },
                ],
                Tier = Winnow.Recommend.DataTier.Settling,
                CandidateCount = 997,
            });
        }

        private static Winnow.Recommend.Recommendation Item(long id) => new()
        {
            OwnershipId = id,
            ReleaseId = id,
            WorkId = id,
            Title = $"Game {id}",
            Store = Winnow.Core.Domain.ExternalIdProviders.Steam,
            Bucket = Winnow.Core.Queries.LibraryBuckets.StaleButPatched,
            Score = 1.0 - (id / 100.0),
            Reason = "A reason.",
            Explanation = new Winnow.Recommend.RecommendationReason
            {
                Primary = Winnow.Recommend.ReasonSignal.PatchedSinceYouLeft,
                Evidence = new Winnow.Recommend.ReasonEvidence { ReleaseId = id, Title = $"Game {id}" },
            },
            Signals = [],
        };
    }
}
