using Hoard.App.ViewModels;
using Hoard.Core.Domain;
using Hoard.Core.Repositories;
using Hoard.Data.Repositories;
using Hoard.Resolve.Matching;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The merge confirm queue's view model (design-system §6, §5.3 step 3).
///
/// <para>These run against a real migrated SQLite file and the real
/// repositories, because the two facts most worth pinning are both facts about
/// what reaches the database: that <c>Same game</c> and <c>Different games</c>
/// write the right terminal status, and that a rejected pair never comes back.
/// No Avalonia application, dispatcher or rendering is involved — the view
/// model is constructed directly and every assertion is on its properties.</para>
/// </summary>
public sealed class MergeQueueViewModelTests
{
    // ── Ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_is_ordered_by_score_descending()
    {
        using var fixture = new MergeQueueFixture();

        // Inserted worst-first, so an unordered read would come back backwards.
        var weakest = await fixture.QueuePairAsync(
            new SeedSide("Deus Ex: Human Revolution", 2011, "Square Enix"),
            new SeedSide("Deus Ex: Human Revolution - Director's Cut", 2013, "Square Enix"));
        var middle = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        var strongest = await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(
            [strongest, middle, weakest],
            queue.Candidates.Select(c => c.Id));

        // And the scores really are descending, not merely in insertion order.
        var scores = queue.Candidates.Select(c => c.Score).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public async Task Strongest_pair_is_selected_first()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        var strongest = await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(strongest, queue.SelectedCandidate?.Id);
        Assert.True(queue.SelectedCandidate?.IsSelected);
    }

    // ── Answering ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Same_game_writes_confirmed_and_the_pair_leaves_the_queue()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.SameGameCommand.ExecuteAsync(queue.Candidates[0]);

        Assert.Empty(queue.Candidates);
        Assert.Equal(MergeCandidateStatuses.Confirmed, await fixture.StatusOfAsync(id));

        // And it stays gone across a reload — the row is no longer pending.
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.Candidates);
    }

    [Fact]
    public async Task Different_games_writes_rejected_and_the_pair_leaves_the_queue()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.DifferentGamesCommand.ExecuteAsync(queue.Candidates[0]);

        Assert.Empty(queue.Candidates);
        Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(id));
    }

    /// <summary>
    /// §5.3's memory rule, from the queue's side: the resolver refuses to
    /// re-queue a pair that already has a row in any status, and this is the
    /// screen that puts it in the terminal one. If a rejection could be undone
    /// by the next scan, the user would be asked the same question forever and
    /// would stop reading it.
    /// </summary>
    [Fact]
    public async Task A_rejected_pair_stays_rejected_when_the_resolver_runs_again()
    {
        using var fixture = new MergeQueueFixture();
        var (left, right) = await fixture.CreatePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        var id = await fixture.QueueScoredPairAsync(left, right);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.DifferentGamesCommand.ExecuteAsync(queue.Candidates[0]);

        // A later scan finds the same pair and asks whether it is already known.
        var existing = await fixture.Candidates.FindByPairAsync(left.ReleaseId, right.ReleaseId);
        Assert.Equal(MergeCandidateStatuses.Rejected, existing?.Status);

        // Mirrored orientation must find the same row, or a re-scan would insert
        // its twin and resurrect the question.
        var mirrored = await fixture.Candidates.FindByPairAsync(right.ReleaseId, left.ReleaseId);
        Assert.Equal(id, mirrored?.Id);

        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.Candidates);
    }

    [Fact]
    public async Task Answering_twice_writes_only_the_first_answer()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Candidates[0];

        await queue.DifferentGamesCommand.ExecuteAsync(card);
        await queue.SameGameCommand.ExecuteAsync(card);

        Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(id));
    }

    [Fact]
    public async Task Answering_moves_the_cursor_to_the_pair_that_took_its_place()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var second = queue.Candidates[1];
        await queue.SameGameCommand.ExecuteAsync(queue.Candidates[0]);

        Assert.Same(second, queue.SelectedCandidate);
        Assert.True(second.IsSelected);
    }

    // ── Keyboard navigation (§8) ─────────────────────────────────────────────

    [Fact]
    public async Task Selection_moves_by_card_and_clamps_at_the_ends()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, queue.MoveSelection(1));
        Assert.Equal(1, queue.MoveSelection(1));
        Assert.Equal(0, queue.MoveSelection(-1));
        Assert.Equal(0, queue.MoveSelection(-1));

        // Exactly one card is marked selected at a time.
        Assert.Single(queue.Candidates, c => c.IsSelected);
    }

    [Fact]
    public void Moving_selection_on_an_empty_queue_is_a_no_op()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.Equal(-1, queue.MoveSelection(1));
        Assert.Null(queue.SelectedCandidate);
    }

    // ── The signal breakdown ─────────────────────────────────────────────────

    /// <summary>
    /// The breakdown is the product, not diagnostics: it is the only thing on
    /// screen that answers "why does the app think these are the same game".
    /// </summary>
    [Fact]
    public async Task Signal_breakdown_decodes_from_the_stored_payload()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Candidates[0];

        // The three diffs §6 names by hand.
        Assert.Equal("0.00", card.TitleDistanceText);   // 1 - similarity
        Assert.Equal("Δ1", card.YearDeltaText);
        Assert.Equal("SAME", card.PublisherMatchText);
        Assert.Equal("0.87", card.ScoreText);
        Assert.True(card.IsPriority);

        Assert.Equal(
            ["TITLE DISTANCE", "YEAR DELTA", "PUBLISHER", "COVER", "EDITION"],
            card.Signals.Select(s => s.Label));

        var year = card.Signals.Single(s => s.Label == "YEAR DELTA");
        Assert.True(year.Fired);
        Assert.Equal("Δ1", year.ValueText);
        Assert.Equal("+0.15", year.ContributionText);
        Assert.True(year.IsForMatch);
        Assert.Contains("2015 vs 2016", year.Detail, StringComparison.Ordinal);

        // One side is a content bundle: evidence against, and small.
        var edition = card.Signals.Single(s => s.Label == "EDITION");
        Assert.Equal("DIFFERENT", edition.ValueText);
        Assert.Equal("-0.05", edition.ContributionText);
        Assert.True(edition.IsAgainstMatch);
    }

    /// <summary>
    /// The trap case (§5.3): identical titles, no corroboration. A signal that
    /// could not be evaluated must read as "we don't know", never as agreement —
    /// that distinction is the entire difference between Prey and Prey.
    /// </summary>
    [Fact]
    public async Task Signals_that_did_not_fire_read_as_unknown_and_contribute_nothing()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Candidates[0];

        Assert.Equal("0.65", card.ScoreText);
        Assert.False(card.IsPriority);
        Assert.Equal("—", card.YearDeltaText);
        Assert.Equal("—", card.PublisherMatchText);

        var year = card.Signals.Single(s => s.Label == "YEAR DELTA");
        Assert.False(year.Fired);
        Assert.Equal("—", year.ValueText);
        Assert.Equal(" 0.00", year.ContributionText);
        Assert.False(year.IsForMatch);
        Assert.False(year.IsAgainstMatch);

        var publisher = card.Signals.Single(s => s.Label == "PUBLISHER");
        Assert.False(publisher.Fired);
        Assert.Equal("—", publisher.ValueText);

        // Both sides still name themselves, and the release ids are what tells
        // two identically titled records apart on screen.
        Assert.Equal("Prey", card.Left.Title);
        Assert.Equal("Prey", card.Right.Title);
        Assert.NotEqual(card.Left.ReleaseText, card.Right.ReleaseText);
        Assert.Equal("2017", card.Left.YearText);
        Assert.Equal("—", card.Right.YearText);
        Assert.Equal("publisher unknown", card.Right.PublisherText);
    }

    [Fact]
    public async Task Sides_line_up_with_the_row_columns_even_when_the_payload_is_mirrored()
    {
        using var fixture = new MergeQueueFixture();
        var (left, right) = await fixture.CreatePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        // Payload written in the opposite orientation to the row's columns.
        var mirrored = new SoftMatcher().Score(fixture.Subject(right), fixture.Subject(left));
        await fixture.Candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = left.ReleaseId,
            RightReleaseId = right.ReleaseId,
            Score = mirrored.Score,
            SignalsJson = SoftMatchSignalsJson.Serialize(mirrored),
            Status = MergeCandidateStatuses.Pending,
        });

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Candidates[0];

        Assert.Equal(left.ReleaseId, card.Left.ReleaseId);
        Assert.Equal(2017, card.Left.Year);
        Assert.Equal(right.ReleaseId, card.Right.ReleaseId);
        Assert.Null(card.Right.Year);
    }

    [Fact]
    public async Task A_row_with_no_recorded_payload_falls_back_to_the_release_titles()
    {
        using var fixture = new MergeQueueFixture();
        var (left, right) = await fixture.CreatePairAsync(
            new SeedSide("Bastion", 2011, "Supergiant Games"),
            new SeedSide("Bastion", 2011, "Supergiant Games"));

        await fixture.Candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = left.ReleaseId,
            RightReleaseId = right.ReleaseId,
            Score = 0.9,
            SignalsJson = null,
            Status = MergeCandidateStatuses.Pending,
        });

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Candidates[0];

        Assert.False(card.HasSignals);
        Assert.Equal("—", card.TitleDistanceText);
        Assert.Equal("—", card.YearDeltaText);
        Assert.Equal("Bastion", card.Left.Title);
        Assert.Equal("Bastion", card.Right.Title);
    }

    // ── Empty state (§7) ─────────────────────────────────────────────────────

    [Fact]
    public void An_unloaded_queue_shows_neither_cards_nor_an_empty_state()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.False(queue.ShowEmpty);
        Assert.False(queue.HasPending);
        Assert.Empty(queue.Candidates);
    }

    /// <summary>
    /// Zero pending rows has two causes and only one of them is a fact about
    /// the user's library. Before a sweep has ever completed, the screen must
    /// not claim the library is unambiguous — nothing has looked.
    /// </summary>
    [Fact]
    public async Task An_empty_queue_before_any_sweep_says_the_comparison_has_not_run()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.ShowEmpty);
        Assert.False(queue.HasPending);
        Assert.False(queue.HasCompletedSweep);
        Assert.Equal("0", queue.PendingCountText);
        Assert.Equal(0.4, queue.RowOpacity);

        // Directions, not moods: it says what is about to happen and what fills
        // the queue. It states nothing about the library, because nothing has
        // yet looked at the library.
        Assert.Equal(
            "Nothing to review yet. Hoard hasn't finished comparing your library for records that "
            + "might be the same game — that runs in the background after a scan. Anything it can't "
            + "call lands here, and nothing merges until you say so.",
            queue.EmptyMessage);
    }

    [Fact]
    public async Task An_empty_queue_after_a_sweep_says_the_comparison_found_nothing()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.ResolveState.SetLastSoftMatchSweepAsync(DateTimeOffset.UtcNow);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.ShowEmpty);
        Assert.True(queue.HasCompletedSweep);

        Assert.Equal(
            "Nothing to review. Hoard compared every record in your library and found no two it "
            + "couldn't tell apart. Anything ambiguous lands here, and nothing merges until you say so.",
            queue.EmptyMessage);
    }

    /// <summary>
    /// With no state repository in the container the screen cannot know, and
    /// "cannot know" must read as "has not run". The one thing it must never do
    /// is announce a clean library on the strength of a query it did not make.
    /// </summary>
    [Fact]
    public async Task Without_a_state_repository_the_weaker_claim_is_the_one_made()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.ResolveState.SetLastSoftMatchSweepAsync(DateTimeOffset.UtcNow);

        var queue = fixture.CreateViewModel(withResolveState: false);
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.False(queue.HasCompletedSweep);
        Assert.StartsWith("Nothing to review yet.", queue.EmptyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_the_last_pair_returns_the_queue_to_its_empty_state()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.True(queue.HasPending);
        Assert.Equal(1.0, queue.RowOpacity);

        await queue.DifferentGamesCommand.ExecuteAsync(queue.Candidates[0]);

        Assert.True(queue.ShowEmpty);
        Assert.False(queue.HasPending);
        Assert.Null(queue.SelectedCandidate);
    }

    [Fact]
    public async Task The_count_is_the_number_of_pending_pairs()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, queue.PendingCount);
        Assert.Equal("2", queue.PendingCountText);
        Assert.True(queue.HasPending);
        Assert.False(queue.ShowEmpty);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    /// <summary>A release as a store feed would describe it, before it is scored.</summary>
    private sealed record SeedSide(string Title, int? Year, string? Publisher);

    /// <summary>A seeded release: the row id plus the metadata it was seeded with.</summary>
    private sealed record SeededRelease(long ReleaseId, SeedSide Side);

    private sealed class MergeQueueFixture : IDisposable
    {
        private readonly TempDatabase _db = new();
        private int _appId = 100000;

        public MergeQueueFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Candidates = new MergeCandidateRepository(_db.Factory);
            ResolveState = new ResolveStateRepository(_db.Factory);
        }

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public IResolveStateRepository ResolveState { get; }

        /// <summary>No cover cache: the queue must compose on procedural art alone.</summary>
        public MergeQueueViewModel CreateViewModel(bool withResolveState = true)
            => new(Candidates, Releases, Works, null, withResolveState ? ResolveState : null);

        public MatchSubject Subject(SeededRelease release)
            => new()
            {
                ReleaseId = release.ReleaseId,
                Title = release.Side.Title,
                ReleaseYear = release.Side.Year,
                Publisher = release.Side.Publisher,
            };

        public async Task<(SeededRelease Left, SeededRelease Right)> CreatePairAsync(
            SeedSide left, SeedSide right)
            => (await CreateReleaseAsync(left), await CreateReleaseAsync(right));

        /// <summary>Creates both releases and queues them, exactly as the resolver would.</summary>
        public async Task<long> QueuePairAsync(SeedSide left, SeedSide right)
        {
            var (leftRelease, rightRelease) = await CreatePairAsync(left, right);
            return await QueueScoredPairAsync(leftRelease, rightRelease);
        }

        /// <summary>
        /// Scores with the real matcher and writes the real payload, so the view
        /// model is decoding what the resolver actually produces rather than a
        /// hand-written approximation of it.
        /// </summary>
        public async Task<long> QueueScoredPairAsync(SeededRelease left, SeededRelease right)
        {
            var score = new SoftMatcher().Score(Subject(left), Subject(right));
            Assert.True(score.ShouldQueue, $"Fixture pair scored {score.Score:F2} and would not be queued.");

            var id = await Candidates.InsertAsync(new MergeCandidate
            {
                LeftReleaseId = left.ReleaseId,
                RightReleaseId = right.ReleaseId,
                Score = score.Score,
                SignalsJson = SoftMatchSignalsJson.Serialize(score),
                Status = MergeCandidateStatuses.Pending,
            });

            _pairs[id] = (left.ReleaseId, right.ReleaseId);
            return id;
        }

        /// <summary>
        /// Reads a candidate's status back. <c>FindByPairAsync</c> is the only
        /// read that returns a row in any status — which is exactly the lookup
        /// the resolver uses to keep an answered pair out of the queue.
        /// </summary>
        public async Task<string?> StatusOfAsync(long candidateId)
        {
            var (left, right) = _pairs[candidateId];
            return (await Candidates.FindByPairAsync(left, right))?.Status;
        }

        private readonly Dictionary<long, (long Left, long Right)> _pairs = [];

        private async Task<SeededRelease> CreateReleaseAsync(SeedSide side)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = side.Title,
                FirstReleaseYear = side.Year,
            });

            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = side.Title,
                Platform = "windows",
            });

            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(System.Globalization.CultureInfo.InvariantCulture),
            });

            return new SeededRelease(releaseId, side);
        }

        public void Dispose() => _db.Dispose();
    }
}
