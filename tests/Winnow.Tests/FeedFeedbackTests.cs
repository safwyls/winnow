using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Recommend;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The feed's feedback loop, from the two controls on a card to the row they
/// leave behind and the two routes back from it.
///
/// <para><b>Nothing here scores anything and nothing here opens a window.</b>
/// The view-model half runs against <see cref="FakeFeedService"/>, which keeps
/// verdicts with migration 0011's own semantics — append, revoke by stamp, never
/// delete — so the assertions are about the SCREEN. The service half runs a real
/// <see cref="FeedService"/> against a fake engine and a fake store, which is
/// where the §6b contract (load the sets, apply, compute, record what was shown)
/// can actually be observed.</para>
///
/// <para><b>The rule these defend</b> is that steering the model stays
/// inspectable and reversible. A dismissal that cannot be undone at the point of
/// the act is one people stop using; a history that hides what was taken back is
/// the black box the charter forbids.</para>
/// </summary>
public sealed class FeedFeedbackTests
{
    private const string Reason =
        "You put 2.8 hours into this in 2021 and it has had an update since.";

    // ── Two controls, two intents ────────────────────────────────────────────

    [Fact]
    public async Task Not_interested_stores_the_durable_verdict_and_the_card_says_so()
    {
        var (feed, service) = await ScreenAsync();
        var card = feed.Shelves[0].Cards[0];

        Assert.True(card.ShowActions);
        Assert.False(card.IsSetAside);

        await card.NotInterestedCommand.ExecuteAsync(null);

        var stored = Assert.Single(service.Verdicts);
        Assert.Equal(FeedVerdictKind.NotInterested, stored.Kind);
        Assert.Equal(card.Tile.ReleaseId, stored.ReleaseId);

        // A dismissal never expires — that is what makes it different from the
        // control beside it.
        Assert.Null(stored.ExpiresAt);

        // And the action line has become its own receipt: the controls are gone,
        // the card is not.
        Assert.True(card.IsSetAside);
        Assert.False(card.ShowActions);
        Assert.Equal("Off the feed.", card.SetAsideNote);
        Assert.False(card.HasSetAsideDate);
        Assert.Null(card.Problem);
    }

    [Fact]
    public async Task Not_now_stores_a_snooze_that_states_the_day_it_ends()
    {
        var (feed, service) = await ScreenAsync();
        var card = feed.Shelves[0].Cards[0];

        await card.NotNowCommand.ExecuteAsync(null);

        var stored = Assert.Single(service.Verdicts);
        Assert.Equal(FeedVerdictKind.Snoozed, stored.Kind);

        // The schema's pairing, from the screen's side: a snooze ALWAYS carries
        // an expiry, because one without is a dismissal wearing another name.
        Assert.NotNull(stored.ExpiresAt);

        // And the card states the day rather than "in a while" — the date is a
        // number, so it is a run of its own for the data face.
        Assert.True(card.IsSetAside);
        Assert.Equal("Back on", card.SetAsideNote);
        Assert.True(card.HasSetAsideDate);
        Assert.Equal(
            service.Now.AddDays(30).ToLocalTime().ToString("d MMM yyyy"),
            card.SetAsideDate);
    }

    [Fact]
    public async Task The_two_controls_are_kept_apart_all_the_way_down()
    {
        var (feed, service) = await ScreenAsync(cards: 2);

        await feed.Shelves[0].Cards[0].NotInterestedCommand.ExecuteAsync(null);
        await feed.Shelves[0].Cards[1].NotNowCommand.ExecuteAsync(null);

        // Two rows, two kinds. Collapsing them into one control would have lost
        // the difference here, permanently, at the only moment the user knew it.
        Assert.Equal(2, service.Verdicts.Count);
        Assert.Contains(service.Verdicts, v => v.Kind == FeedVerdictKind.NotInterested);
        Assert.Contains(service.Verdicts, v => v.Kind == FeedVerdictKind.Snoozed);

        // And each card carries the verdict IT was given, so its undo revokes
        // the right kind.
        Assert.Equal(FeedVerdictKind.NotInterested, feed.Shelves[0].Cards[0].Verdict);
        Assert.Equal(FeedVerdictKind.Snoozed, feed.Shelves[0].Cards[1].Verdict);
    }

    // ── Undo, at the point of the act ────────────────────────────────────────

    [Fact]
    public async Task Undo_on_the_card_revokes_and_puts_both_controls_back()
    {
        var (feed, service) = await ScreenAsync();
        var card = feed.Shelves[0].Cards[0];

        await card.NotInterestedCommand.ExecuteAsync(null);
        Assert.True(card.IsSetAside);

        await card.UndoCommand.ExecuteAsync(null);

        // The card is exactly as it was: the sentence never went anywhere, and
        // both controls are on offer again.
        Assert.False(card.IsSetAside);
        Assert.True(card.ShowActions);
        Assert.Null(card.Verdict);
        Assert.Equal(Reason, card.Reason);

        // Undo is a stamp, not a delete. The row survives — reversibility must
        // not cost the history that makes the loop inspectable.
        var row = Assert.Single(service.Verdicts);
        Assert.Equal(FeedVerdictStatus.Undone, row.Status);
        Assert.NotNull(row.RevokedAt);
    }

    [Fact]
    public async Task Dismissing_again_after_an_undo_is_two_rows_and_a_stamp()
    {
        var (feed, service) = await ScreenAsync();
        var card = feed.Shelves[0].Cards[0];

        await card.NotInterestedCommand.ExecuteAsync(null);
        await card.UndoCommand.ExecuteAsync(null);
        await card.NotInterestedCommand.ExecuteAsync(null);

        // §6b's own example, asserted: the history holds both attempts, the
        // first carrying its revocation, and only the second binding.
        await feed.History.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, feed.History.Entries.Count);
        Assert.Equal(1, feed.History.Entries.Count(e => e.Status == FeedVerdictStatus.Active));
        Assert.Equal(1, feed.History.Entries.Count(e => e.Status == FeedVerdictStatus.Undone));
        Assert.Equal("2", feed.History.CountText);
    }

    [Fact]
    public async Task A_write_that_did_not_land_shows_no_receipt()
    {
        var (feed, service) = await ScreenAsync();
        var card = feed.Shelves[0].Cards[0];

        service.WritesFail = true;
        await card.NotInterestedCommand.ExecuteAsync(null);

        // The card must not claim a dismissal the store does not hold: the game
        // would come back tomorrow in front of somebody who believes they
        // already answered for it.
        Assert.False(card.IsSetAside);
        Assert.True(card.ShowActions);
        Assert.Null(card.Verdict);
        Assert.Empty(service.Verdicts);

        Assert.True(card.HasProblem);
        Assert.Equal("Couldn't save that — nothing changed.", card.Problem);

        // And the way out is simply pressing again.
        service.WritesFail = false;
        await card.NotInterestedCommand.ExecuteAsync(null);

        Assert.True(card.IsSetAside);
        Assert.Null(card.Problem);
        Assert.Single(service.Verdicts);
    }

    [Fact]
    public async Task A_service_that_throws_on_a_verdict_is_a_sentence_and_not_a_crash()
    {
        var tiles = new FakeTileSource();
        var card = new FeedCardViewModel(Tile(tiles, 1), Reason, new ThrowingFeedService());

        await card.NotInterestedCommand.ExecuteAsync(null);

        Assert.False(card.IsSetAside);
        Assert.True(card.HasProblem);
    }

    [Fact]
    public void With_no_store_behind_it_the_controls_are_not_offered_at_all()
    {
        var tiles = new FakeTileSource();
        var card = new FeedCardViewModel(Tile(tiles, 1), Reason);

        // Offering a control and swallowing the click is worse than not
        // offering it. An unwired host costs the loop, never the screen.
        Assert.False(card.CanGiveFeedback);
        Assert.True(card.ShowActions);
    }

    // ── The inspection surface ───────────────────────────────────────────────

    [Fact]
    public async Task The_history_shows_everything_and_names_it_from_the_library()
    {
        var (feed, service) = await ScreenAsync(cards: 2);

        await feed.Shelves[0].Cards[0].NotInterestedCommand.ExecuteAsync(null);
        await feed.Shelves[0].Cards[1].NotNowCommand.ExecuteAsync(null);
        await feed.History.LoadCommand.ExecuteAsync(null);

        Assert.True(feed.History.HasEntries);
        Assert.Equal(2, feed.History.Entries.Count);

        // Newest first, and each row is named exactly as the library names it —
        // a second projection of the title is how two screens start disagreeing.
        var newest = feed.History.Entries[0];
        Assert.Equal("Deep Rock Galactic 2", newest.Title);
        Assert.Equal("NOT NOW", newest.KindLabel);
        Assert.Equal("Back on", newest.StatusNote);
        Assert.True(newest.HasStatusDate);

        var older = feed.History.Entries[1];
        Assert.Equal("NOT INTERESTED", older.KindLabel);
        Assert.Equal("Off the feed since", older.StatusNote);
    }

    [Fact]
    public async Task Only_a_standing_verdict_offers_an_undo()
    {
        var (feed, service) = await ScreenAsync();

        // A lapsed snooze: no write ever happened, so its row is the only
        // evidence it existed — and "why did this come back" is a question this
        // screen has to answer.
        service.Seed(new FeedVerdictRecord(
            ReleaseId: 4_242,
            FeedVerdictKind.Snoozed,
            CreatedAt: service.Now.AddDays(-60),
            ExpiresAt: service.Now.AddDays(-30),
            RevokedAt: null,
            FeedVerdictStatus.Lapsed));

        await feed.Shelves[0].Cards[0].NotInterestedCommand.ExecuteAsync(null);
        await feed.Shelves[0].Cards[0].UndoCommand.ExecuteAsync(null);
        await feed.History.LoadCommand.ExecuteAsync(null);

        var lapsed = Assert.Single(feed.History.Entries, e => e.Status == FeedVerdictStatus.Lapsed);
        var undone = Assert.Single(feed.History.Entries, e => e.Status == FeedVerdictStatus.Undone);

        Assert.False(lapsed.CanUndo);
        Assert.False(undone.CanUndo);
        Assert.Equal("Lapsed on", lapsed.StatusNote);
        Assert.Equal("Undone on", undone.StatusNote);

        // A verdict outlives the library it was given in, and the row says so
        // rather than vanishing — hiding one is the one thing this surface may
        // not do.
        Assert.Equal("A game that is no longer in your library", lapsed.Title);
    }

    [Fact]
    public async Task Undo_from_the_history_puts_a_card_still_showing_a_receipt_back()
    {
        var (feed, service) = await ScreenAsync();
        var card = feed.Shelves[0].Cards[0];

        await card.NotInterestedCommand.ExecuteAsync(null);
        await feed.ToggleHistoryCommand.ExecuteAsync(null);

        var row = Assert.Single(feed.History.Entries);
        Assert.True(row.CanUndo);

        await row.UndoCommand.ExecuteAsync(null);

        // Two surfaces over one stored row, and they may not disagree about it.
        Assert.False(card.IsSetAside);
        Assert.True(card.ShowActions);
        Assert.Null(card.Verdict);

        // The list re-read rather than editing itself, so the row now shows what
        // the store shows.
        Assert.Equal(FeedVerdictStatus.Undone, Assert.Single(feed.History.Entries).Status);
    }

    [Fact]
    public async Task The_history_takes_the_body_and_gives_it_back()
    {
        var (feed, _) = await ScreenAsync();

        Assert.True(feed.ShowShelves);
        Assert.False(feed.ShowHistory);
        Assert.Equal("What you've told the feed", feed.HistoryLabel);

        await feed.ToggleHistoryCommand.ExecuteAsync(null);

        // Exactly one of the three body states at a time.
        Assert.True(feed.ShowHistory);
        Assert.False(feed.ShowShelves);
        Assert.False(feed.ShowMessage);
        Assert.False(feed.ShowCandidates);
        Assert.Equal("Back to the feed", feed.HistoryLabel);

        feed.CloseHistoryCommand.Execute(null);

        Assert.False(feed.ShowHistory);
        Assert.True(feed.ShowShelves);
        Assert.True(feed.ShowCandidates);
    }

    [Fact]
    public async Task The_header_states_the_count_only_once_there_is_one()
    {
        var (feed, _) = await ScreenAsync();

        Assert.False(feed.History.HasEntries);
        Assert.False(feed.ShowHistoryCount);
        Assert.True(feed.History.ShowMessage);
        Assert.Contains("Nothing yet", feed.History.Message);

        // No manual reload: the card's own write is what moves the header, or
        // the number is one the interface is wrong about until somebody opens
        // the screen that would have corrected it.
        await feed.Shelves[0].Cards[0].NotInterestedCommand.ExecuteAsync(null);

        Assert.True(feed.ShowHistoryCount);
        Assert.Equal("1", feed.History.CountText);
        Assert.False(feed.History.ShowMessage);

        // Never twice at once: the surface's own header states it while it is up.
        await feed.ToggleHistoryCommand.ExecuteAsync(null);
        Assert.False(feed.ShowHistoryCount);
    }

    // ── The service: §6b's five steps ────────────────────────────────────────

    [Fact]
    public async Task A_feed_load_reads_the_verdicts_and_logs_what_it_showed()
    {
        var store = new FakeFeedbackStore();
        store.Verdicts.Add(new FeedVerdict
        {
            Id = 1,
            ReleaseId = 11,
            Kind = FeedVerdictKinds.NotInterested,
            CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        store.Verdicts.Add(new FeedVerdict
        {
            Id = 2,
            ReleaseId = 12,
            Kind = FeedVerdictKinds.Snoozed,
            CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        store.Surfacings.Add(new FeedSurfacing
        {
            ReleaseId = 13,
            SurfacedOn = new DateOnly(2026, 8, 26),
            ShelfId = "ready_to_play",
        });

        var engine = new FakeEngine();
        var service = new FeedService(engine, store, Clock);

        var snapshot = await service.GetShelvesAsync();

        // Step 1 and 2: the stored facts reached the engine ON THE REQUEST. The
        // engine still stores nothing — it is handed sets.
        Assert.NotNull(engine.LastRequest);
        Assert.Equal([11L], engine.LastRequest!.NotInterestedReleaseIds);
        Assert.Equal([12L], engine.LastRequest.SnoozedReleaseIds);
        Assert.Equal([13L], engine.LastRequest.RecentlySurfacedReleaseIds);

        // Step 4: everything the feed showed is now in the log, stamped with the
        // feed's own day.
        Assert.Equal(2, store.Surfacings.Count(s => s.SurfacedOn == new DateOnly(2026, 8, 27)));
        Assert.Contains(store.Surfacings, s => s.ReleaseId == 101 && s.ShelfId == "patched_while_away");

        Assert.False(snapshot.Failed);
        Assert.Equal(2, snapshot.Shelves.Sum(s => s.Items.Count));
    }

    [Fact]
    public async Task A_surfacing_write_that_fails_costs_rotation_and_not_the_feed()
    {
        var store = new FakeFeedbackStore { RecordSurfacedThrows = true };
        var service = new FeedService(new FakeEngine(), store, Clock);

        var snapshot = await service.GetShelvesAsync();

        // The log is the engine's cross-day memory, so losing a day of it means
        // tomorrow repeats some of today's picks. It must never mean five
        // shelves of real recommendations become an apology.
        Assert.False(snapshot.Failed);
        Assert.Equal(2, snapshot.Shelves.Sum(s => s.Items.Count));
        Assert.Equal(997, snapshot.CandidateCount);
        Assert.Empty(store.Surfacings);
    }

    [Fact]
    public async Task A_host_with_no_feedback_store_still_computes_a_feed()
    {
        var engine = new FakeEngine();
        var snapshot = await new FeedService(engine, feedback: null, Clock).GetShelvesAsync();

        Assert.False(snapshot.Failed);
        Assert.Empty(engine.LastRequest!.NotInterestedReleaseIds);

        // And the two controls simply are not offered — see the card test above.
        var outcome = await new FeedService(engine, feedback: null, Clock)
            .RecordVerdictAsync(1, FeedVerdictKind.NotInterested);
        Assert.False(outcome.Saved);
    }

    [Fact]
    public async Task The_service_pairs_expiry_with_kind_the_way_the_schema_does()
    {
        var store = new FakeFeedbackStore();
        var service = new FeedService(new FakeEngine(), store, Clock);

        await service.RecordVerdictAsync(11, FeedVerdictKind.NotInterested);
        await service.RecordVerdictAsync(12, FeedVerdictKind.Snoozed);

        var dismissal = Assert.Single(store.Verdicts, v => v.ReleaseId == 11);
        var snooze = Assert.Single(store.Verdicts, v => v.ReleaseId == 12);

        // 0011's CHECK, honoured before it can fire: a dismissal must carry no
        // expiry and a snooze must carry one.
        Assert.Equal(FeedVerdictKinds.NotInterested, dismissal.Kind);
        Assert.Null(dismissal.ExpiresAt);

        Assert.Equal(FeedVerdictKinds.Snoozed, snooze.Kind);
        Assert.Equal(Now + FeedVerdictKinds.DefaultSnooze, snooze.ExpiresAt);
    }

    [Fact]
    public async Task A_read_that_throws_is_an_empty_history_and_not_an_exception()
    {
        var store = new FakeFeedbackStore { GetAllThrows = true };
        var service = new FeedService(new FakeEngine(), store, Clock);

        Assert.Empty(await service.GetHistoryAsync());
        Assert.False((await service.RecordVerdictAsync(1, FeedVerdictKind.Snoozed)).Saved);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);

    private static TimeProvider Clock { get; } = new FixedClock(Now);

    /// <summary>
    /// A loaded Feed screen with <paramref name="cards"/> cards on one shelf,
    /// each wired to the same fake store — the state every card assertion above
    /// starts from.
    /// </summary>
    private static async Task<(FeedViewModel Feed, FakeFeedService Service)> ScreenAsync(int cards = 1)
    {
        var tiles = new FakeTileSource();
        var items = new List<FeedItem>();
        for (var i = 1; i <= cards; i++)
        {
            var title = $"Deep Rock Galactic {i}";
            tiles.Add(i, title);
            items.Add(new FeedItem(i, i, title, Reason));
        }

        var service = new FakeFeedService(new FeedSnapshot(
            [new FeedShelf("patched_while_away", "Patched while you were away", "Pitch.", items)],
            CandidateCount: 997,
            FeedConfidence.Settling,
            Failed: false))
        {
            Now = Now,
        };

        var feed = new FeedViewModel(service, tiles);
        await feed.LoadCommand.ExecuteAsync(null);
        return (feed, service);
    }

    private static GameTileViewModel Tile(FakeTileSource tiles, long id)
    {
        tiles.Add(id, $"Deep Rock Galactic {id}");
        return tiles.TileForOwnership(id)!;
    }
}

/// <summary>A clock that does not move, so a snooze's expiry is a constant.</summary>
internal sealed class FixedClock(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}

/// <summary>
/// An engine that records the request it was handed and returns a fixed shelf
/// feed. What it is for is the request: §6b's contract is that the stored
/// feedback arrives as id sets on the way IN, and this is where that can be
/// seen.
/// </summary>
internal sealed class FakeEngine : IRecommendationEngine
{
    public RecommendationRequest? LastRequest { get; private set; }

    public Task<RecommendationFeed> GetFeedAsync(
        RecommendationRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("the Feed screen asks for shelves");

    public Task<ShelfFeed> GetShelvesAsync(RecommendationRequest request, CancellationToken ct = default)
    {
        LastRequest = request;

        return Task.FromResult(new ShelfFeed
        {
            Shelves =
            [
                new RecommendationShelf
                {
                    Id = "patched_while_away",
                    Title = "Patched while you were away",
                    Blurb = "Major updates landed after you stopped playing.",
                    Items = [Item(101, "Abiotic Factor")],
                },
                new RecommendationShelf
                {
                    Id = "ready_to_play",
                    Title = "Installed and waiting",
                    Blurb = "Already on your disk, nothing sunk.",
                    Items = [Item(102, "Fez")],
                },
            ],
            Tier = DataTier.Settling,
            CandidateCount = 997,
        });
    }

    private static Recommendation Item(long id, string title) => new()
    {
        OwnershipId = id,
        ReleaseId = id,
        WorkId = id,
        Title = title,
        Store = ExternalIdProviders.Steam,
        Bucket = LibraryBuckets.StaleButPatched,
        Score = 0.5,
        Reason = "A reason.",
        Signals = [],
    };
}

/// <summary>
/// The feedback store, in memory, with 0011's semantics and a switch for each
/// way it can fail. Verdicts are appended and revoked by stamp; surfacings are
/// idempotent per (release, day), the way the primary key makes them.
/// </summary>
internal sealed class FakeFeedbackStore : IFeedFeedbackRepository
{
    public List<FeedVerdict> Verdicts { get; } = [];

    public List<FeedSurfacing> Surfacings { get; } = [];

    public bool RecordSurfacedThrows { get; init; }

    public bool GetAllThrows { get; init; }

    public Task<long> RecordVerdictAsync(FeedVerdict verdict, CancellationToken ct = default)
    {
        if (GetAllThrows)
        {
            throw new InvalidOperationException("the database is locked");
        }

        var id = Verdicts.Count + 1;
        Verdicts.Add(verdict with { Id = id });
        return Task.FromResult((long)id);
    }

    public Task<int> RevokeVerdictsAsync(
        long releaseId, string kind, DateTime revokedAtUtc, CancellationToken ct = default)
    {
        var revoked = 0;
        for (var i = 0; i < Verdicts.Count; i++)
        {
            var row = Verdicts[i];
            if (row.ReleaseId == releaseId && row.Kind == kind && row.IsActiveAt(revokedAtUtc))
            {
                Verdicts[i] = row with { RevokedAt = revokedAtUtc };
                revoked++;
            }
        }

        return Task.FromResult(revoked);
    }

    public Task<IReadOnlyList<FeedVerdict>> GetActiveVerdictsAsync(
        DateTime asOfUtc, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeedVerdict>>(
            Verdicts.Where(v => v.IsActiveAt(asOfUtc)).ToList());

    public Task<IReadOnlyList<FeedVerdict>> GetAllVerdictsAsync(CancellationToken ct = default)
        => GetAllThrows
            ? throw new InvalidOperationException("the database is locked")
            : Task.FromResult<IReadOnlyList<FeedVerdict>>(
                Verdicts.OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).ToList());

    public Task RecordSurfacedAsync(
        IReadOnlyList<FeedSurfacing> surfacings, CancellationToken ct = default)
    {
        if (RecordSurfacedThrows)
        {
            throw new InvalidOperationException("the database is locked");
        }

        foreach (var row in surfacings)
        {
            // INSERT OR IGNORE against (release_id, surfaced_on).
            if (!Surfacings.Any(s => s.ReleaseId == row.ReleaseId && s.SurfacedOn == row.SurfacedOn))
            {
                Surfacings.Add(row);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FeedSurfacing>> GetSurfacedSinceAsync(
        DateOnly since, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeedSurfacing>>(
            Surfacings.Where(s => s.SurfacedOn >= since).OrderBy(s => s.SurfacedOn).ToList());

    public Task<IReadOnlyList<FeedEndorsement>> GetEndorsementsAsync(
        int windowDays, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FeedEndorsement>>([]);
}
