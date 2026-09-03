using System.Globalization;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Merges screen's view model: one queue of proposal cards in five
/// sections, whose unit is a CARD and whose answer is a LINK.
///
/// <para>These run against a real migrated SQLite file and the real
/// repositories, because the facts most worth pinning are all facts about what
/// reaches the database: that answering writes links and rejections and never a
/// merge, that a rejected pair never comes back, and that linking, retracting
/// and linking again leaves the same rows behind every time. No Avalonia
/// application, dispatcher or rendering is involved; the view model is
/// constructed with an inline poster and a fake clock, and every assertion is
/// on its properties.</para>
/// </summary>
public sealed class MergeQueueViewModelTests
{
    private static readonly SeedSide Prey = new("Prey", 2017, "Bethesda Softworks");
    private static readonly SeedSide PreyUnknown = new("Prey", null, null);
    private static readonly SeedSide Witcher = new("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED");
    private static readonly SeedSide WitcherGoty =
        new("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED");
    private static readonly SeedSide Stanley = new("The Stanley Parable", 2013, "Galactic Cafe");
    private static readonly SeedSide CivIv = new("Sid Meier's Civilization IV", 2005, "2K");
    private static readonly SeedSide CivIvWarlords = new("Sid Meier's Civilization IV: Warlords", 2006, "2K");
    private static readonly SeedSide CivIvBeyond =
        new("Sid Meier's Civilization IV: Beyond the Sword", 2007, "2K");

    // ── Empty states ─────────────────────────────────────────────────────────

    [Fact]
    public void Before_load_every_section_is_empty_and_nothing_is_loaded()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.False(queue.IsLoaded);
        Assert.Equal(5, queue.Sections.Count);
        Assert.Equal(
            [
                MergeSectionKind.Stores, MergeSectionKind.Editions, MergeSectionKind.Expansions,
                MergeSectionKind.Parts, MergeSectionKind.Tests,
            ],
            queue.Sections.Select(section => section.Kind));
        Assert.All(queue.Sections, section => Assert.Empty(section.Cards));
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task An_empty_load_before_any_sweep_says_the_library_is_still_being_scanned()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.IsLoaded);
        Assert.False(queue.HasCompletedSweep);
        Assert.All(queue.Sections, section => Assert.Empty(section.Cards));
        Assert.Equal("nothing waiting", queue.PendingLine);

        // Only the same-game sections depend on the matcher; the scan-fed
        // sections have already been scanned by the time the load returns.
        Assert.Equal("Still scanning your library.", Section(queue, MergeSectionKind.Stores).EmptyText);
        Assert.Equal("Still scanning your library.", Section(queue, MergeSectionKind.Editions).EmptyText);
        Assert.Equal("Nothing left to decide here.", Section(queue, MergeSectionKind.Expansions).EmptyText);
    }

    [Fact]
    public async Task An_empty_load_after_a_sweep_says_there_is_nothing_to_decide()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.ResolveState.SetLastSoftMatchSweepAsync(DateTimeOffset.UtcNow);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.HasCompletedSweep);
        Assert.Equal("nothing waiting", queue.PendingLine);
        Assert.All(
            queue.Sections,
            section => Assert.Equal("Nothing left to decide here.", section.EmptyText));
    }

    // ── Sections and confidence ──────────────────────────────────────────────

    [Fact]
    public async Task A_pair_owned_on_two_stores_lands_in_ACROSS_STORES()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.Equal(MergeSectionKind.Stores, card.Section);
        Assert.Empty(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal("1 proposal · non-destructive", queue.PendingLine);
    }

    [Fact]
    public async Task A_pair_owned_on_one_store_lands_in_EDITIONS()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.Equal(MergeSectionKind.Editions, card.Section);
        Assert.Empty(Section(queue, MergeSectionKind.Stores).Cards);
    }

    /// <summary>
    /// The structural answer to the complaint that several proposals name the
    /// same game. Three store entries produce three pairwise proposals and
    /// exactly ONE card, so there is no second card left to go stale.
    /// </summary>
    [Fact]
    public async Task Three_stores_of_one_game_are_one_card_with_three_rows()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueCrossStoreTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.Equal(3, card.Rows.Count);
        Assert.Equal(3, card.CandidateIds.Count);
        Assert.Equal(1, queue.PendingCount);
        Assert.All(card.Rows, row => Assert.Equal("Prey", row.Title));

        // Three rows with one title still answer to three different names.
        Assert.Equal(3, card.Rows.Select(row => row.Label).Distinct().Count());
    }

    [Fact]
    public async Task A_priority_pair_with_identical_titles_is_an_exact_match()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.Equal(MergeConfidence.Exact, card.Confidence);
        Assert.Equal("EXACT MATCH", card.ConfidenceLabel);
        Assert.True(card.IsExact);
        Assert.StartsWith("Same title on ", card.Reason, StringComparison.Ordinal);
        Assert.Contains("Same publisher, same year.", card.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The matcher strips the edition and reports the titles identical, but
    /// the stores spell them differently, and whether a Game of the Year
    /// edition is the same game is the user's call. LIKELY says so, and the
    /// reason names the edition rather than claiming one title.
    /// </summary>
    [Fact]
    public async Task A_priority_pair_that_differs_only_by_edition_is_likely_and_says_so()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.Equal(MergeConfidence.Likely, card.Confidence);
        Assert.Equal("LIKELY", card.ConfidenceLabel);
        Assert.StartsWith(MergeCopy.ReasonSameTitleApartFromEdition, card.Reason, StringComparison.Ordinal);
        Assert.Contains("a year apart", card.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Prey (2006) and Prey (2017) guard. Identical titles with nothing
    /// corroborating them score below the band, and the card says it needs
    /// reading rather than dressing the pair up as a match.
    /// </summary>
    [Fact]
    public async Task A_pair_below_the_band_is_worth_a_look()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.Equal(MergeConfidence.WorthALook, card.Confidence);
        Assert.Equal("WORTH A LOOK", card.ConfidenceLabel);
        Assert.True(card.IsWorthALook);
    }

    [Fact]
    public async Task A_forced_review_band_arrives_worth_a_look_whatever_the_titles()
    {
        using var fixture = new MergeQueueFixture();
        var (left, right) = await fixture.CreatePairAsync(Prey, Prey);
        await fixture.QueueScoredPairAsync(left, right, SoftMatchBand.Review);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.Equal(MergeConfidence.WorthALook, card.Confidence);
    }

    // ── The header ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_header_defaults_to_the_ladders_primary()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueCrossStoreTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.Equal(0, card.HeaderIndex);
        Assert.Same(card.Rows[0], card.Header);
        Assert.True(card.Rows[0].IsHeader);
        Assert.Equal("HEADER", card.Rows[0].HeaderMark);
        Assert.Equal(card.Rows[0].WorkId, card.ParentWorkId);
        Assert.Equal(card.Rows[0].Title, card.HeaderTitle);
        Assert.False(card.IsTouched);

        foreach (var row in card.Rows.Skip(1))
        {
            Assert.False(row.IsHeader);
            Assert.Equal("NESTS UNDER", row.HeaderMark);
        }

        Assert.Equal(
            card.Rows.Skip(1).Select(row => row.WorkId).Order(),
            card.ChildWorkIds);
    }

    [Fact]
    public async Task Promoting_a_row_moves_the_header_and_the_direction_of_the_link()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        var displaced = card.Rows[0];
        var chosen = card.Rows[1];
        Assert.True(chosen.CanPromote);

        card.Promote(chosen);

        Assert.True(card.IsTouched);
        Assert.Equal(1, card.HeaderIndex);
        Assert.Same(chosen, card.Header);
        Assert.True(chosen.IsHeader);
        Assert.False(displaced.IsHeader);
        Assert.Equal(chosen.Title, card.HeaderTitle);
        Assert.Equal(chosen.WorkId, card.ParentWorkId);
        Assert.Equal([displaced.WorkId], card.ChildWorkIds);

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.Equal([(displaced.WorkId, chosen.WorkId)], await fixture.LiveLinksAsync());
    }

    [Fact]
    public async Task Promoting_a_row_from_another_card_is_refused()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var cards = Section(queue, MergeSectionKind.Editions).Cards;
        Assert.Equal(2, cards.Count);

        Assert.Throws<ArgumentException>(() => cards[0].Promote(cards[1].Rows[1]));
        Assert.Equal(0, cards[0].HeaderIndex);
        Assert.False(cards[0].IsTouched);
    }

    [Fact]
    public async Task Rows_on_an_expansion_card_cannot_be_promoted()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards);
        // No radio is drawn on these rows: CanPromote is what hides it.
        Assert.All(card.Rows, row => Assert.False(row.CanPromote));

        var before = card.Header;
        card.Promote(card.Rows[1]);

        Assert.Same(before, card.Header);
        Assert.Equal(0, card.HeaderIndex);
        Assert.False(card.IsTouched);
    }

    // ── Same game ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Same_game_writes_one_act_under_the_header_and_leaves_a_strip_in_place()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueueCrossStoreTripleAsync();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Equal(3, queue.PendingCount);

        var section = Section(queue, MergeSectionKind.Stores);
        var card = Assert.Single(section.Cards);
        var parent = card.ParentWorkId;
        var children = card.ChildWorkIds;

        await queue.SameGameCommand.ExecuteAsync(card);

        // One act, one transaction, three-way identity.
        Assert.Equal(1, fixture.ActCount());
        var act = Assert.Single(await fixture.Links.GetActsAsync());
        Assert.Equal(
            children.Select(child => (child, parent)),
            await fixture.LiveLinksAsync());

        // The card became a strip where it stood.
        Assert.Same(card, Assert.Single(section.Cards));
        Assert.True(card.IsResolved);
        Assert.False(card.IsPending);
        Assert.Equal(act.Id, card.ActId);
        Assert.Equal(0, section.PendingCount);
        Assert.Equal(2, queue.PendingCount);

        Assert.True(queue.IsDockOpen);
        Assert.Equal($"Rolled up under {card.HeaderTitle}.", queue.DockTitle);
        Assert.Equal("Rolled up under Prey.", queue.DockTitle);
        Assert.EndsWith("nothing was deleted.", queue.DockNote, StringComparison.Ordinal);
        Assert.StartsWith("2 entries nested", queue.DockNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answering_twice_writes_only_the_first_answer()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        await queue.SameGameCommand.ExecuteAsync(card);
        await queue.SameGameCommand.ExecuteAsync(card);
        await queue.DifferentGamesCommand.ExecuteAsync(card);

        Assert.Equal(1, fixture.ActCount());
        Assert.Single(await fixture.LiveLinksAsync());
        Assert.True(card.IsResolved);
    }

    /// <summary>
    /// The review path writes a link and destroys nothing. Every work, every
    /// store entry and every ownership row is still there afterwards, because
    /// a link is additive.
    /// </summary>
    [Fact]
    public async Task The_review_path_destroys_nothing()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueCrossStoreTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SameGameCommand.ExecuteAsync(Assert.Single(Section(queue, MergeSectionKind.Stores).Cards));

        using var conn = fixture.Factory.Open();
        Assert.Equal(3, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(3, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));
        Assert.Equal(3, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM ownerships;"));
        Assert.Equal(2, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM identity_links WHERE retracted_at IS NULL;"));
    }

    // ── Different games ──────────────────────────────────────────────────────

    [Fact]
    public async Task Different_games_rejects_every_proposal_on_the_card_and_removes_it()
    {
        using var fixture = new MergeQueueFixture();
        var (a, b, c) = await fixture.CreateTripleAsync();
        var ids = new[]
        {
            await fixture.QueueScoredPairAsync(a, b),
            await fixture.QueueScoredPairAsync(a, c),
            await fixture.QueueScoredPairAsync(b, c),
        };

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        var card = Assert.Single(section.Cards);

        await queue.DifferentGamesCommand.ExecuteAsync(card);

        Assert.Empty(section.Cards);
        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(await fixture.LiveLinksAsync());
        foreach (var id in ids)
        {
            Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(id));
        }

        Assert.True(queue.IsDockOpen);
        Assert.Equal("Left 1 group alone.", queue.DockTitle);
    }

    /// <summary>
    /// Consecutive dismissals share one dock card and one Undo, and Undo puts
    /// every card back at the index it left from with its proposals pending.
    /// </summary>
    [Fact]
    public async Task Consecutive_dismissals_share_one_dock_and_one_undo_restores_them_in_place()
    {
        using var fixture = new MergeQueueFixture();
        var ids = new[]
        {
            await fixture.QueuePairAsync(Prey, PreyUnknown),
            await fixture.QueuePairAsync(Witcher, WitcherGoty),
            await fixture.QueuePairAsync(Stanley, Stanley),
        };

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        var original = section.Cards.ToList();
        Assert.Equal(3, original.Count);

        await queue.DifferentGamesCommand.ExecuteAsync(original[0]);
        Assert.Equal("Left 1 group alone.", queue.DockTitle);

        await queue.DifferentGamesCommand.ExecuteAsync(original[1]);
        Assert.Equal("Left 2 groups alone.", queue.DockTitle);
        Assert.Same(original[2], Assert.Single(section.Cards));
        Assert.Equal(1, queue.PendingCount);

        var pendingNow = 0;
        foreach (var id in ids)
        {
            pendingNow += await fixture.StatusOfAsync(id) == MergeCandidateStatuses.Pending ? 1 : 0;
        }

        Assert.Equal(1, pendingNow);

        await queue.UndoCommand.ExecuteAsync(null);

        Assert.Equal(original, section.Cards);
        Assert.All(section.Cards, card => Assert.True(card.IsPending));
        Assert.Equal(3, queue.PendingCount);
        Assert.False(queue.IsDockOpen);
        foreach (var id in ids)
        {
            Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(id));
        }
    }

    [Fact]
    public async Task Different_games_on_an_expansion_card_writes_refusals_that_undo_removes()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);
        await fixture.CreateReleaseAsync(CivIvBeyond);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Expansions);
        var card = Assert.Single(section.Cards);
        Assert.Equal(2, card.RefusalPairs.Count);

        await queue.DifferentGamesCommand.ExecuteAsync(card);

        var refusals = await fixture.ExpansionRefusals.GetAllAsync();
        Assert.Equal(2, refusals.Count);
        Assert.All(refusals, refusal => Assert.Equal(card.ParentWorkId, refusal.BaseWorkId));
        Assert.Equal(card.ChildWorkIds, refusals.Select(refusal => refusal.ChildWorkId).Order());
        Assert.Empty(section.Cards);
        Assert.Empty(await fixture.LiveLinksAsync());

        await queue.UndoCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.ExpansionRefusals.GetAllAsync());
        Assert.Same(card, Assert.Single(section.Cards));
        Assert.True(card.IsPending);
    }

    // ── Undo and the dock ────────────────────────────────────────────────────

    [Fact]
    public async Task Undo_after_same_game_retracts_the_act_and_closes_the_dock()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        var card = Assert.Single(section.Cards);

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.Single(await fixture.LiveLinksAsync());
        Assert.True(queue.IsDockOpen);

        await queue.UndoCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.Same(card, Assert.Single(section.Cards));
        Assert.True(card.IsPending);
        Assert.Null(card.ActId);
        Assert.Equal(1, queue.PendingCount);
        Assert.False(queue.IsDockOpen);
    }

    /// <summary>
    /// The user's fifth complaint on the old screen: undo reported success and
    /// the pair then read as unmergeable. Four cycles must leave exactly what
    /// one leaves.
    /// </summary>
    [Fact]
    public async Task Link_undo_and_link_again_ends_where_linking_once_ends()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        IReadOnlyList<(long Child, long Parent)> afterFirst = [];
        for (var cycle = 0; cycle < 4; cycle++)
        {
            Assert.True(card.IsPending);
            await queue.SameGameCommand.ExecuteAsync(card);
            var links = await fixture.LiveLinksAsync();

            if (cycle == 0)
            {
                afterFirst = links;
                Assert.Single(links);
            }
            else
            {
                Assert.Equal(afterFirst, links);
            }

            await queue.UndoCommand.ExecuteAsync(null);
            Assert.Empty(await fixture.LiveLinksAsync());
        }

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.Equal(afterFirst, await fixture.LiveLinksAsync());
    }

    [Fact]
    public async Task The_dock_closes_by_itself_after_seven_seconds_and_undo_is_then_gone()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.True(queue.IsDockOpen);

        fixture.Clock.Advance(MergeQueueViewModel.DockFor - TimeSpan.FromSeconds(1));
        Assert.True(queue.IsDockOpen);

        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(queue.IsDockOpen);

        // The act stands: Undo after the dock has gone does nothing.
        await queue.UndoCommand.ExecuteAsync(null);
        Assert.Single(await fixture.LiveLinksAsync());
        Assert.True(card.IsResolved);
    }

    [Fact]
    public async Task Dismissing_the_dock_closes_it_early_and_forgets_the_undo()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.True(queue.IsDockOpen);

        queue.DismissDockCommand.Execute(null);
        Assert.False(queue.IsDockOpen);

        await queue.UndoCommand.ExecuteAsync(null);
        Assert.Single(await fixture.LiveLinksAsync());
        Assert.True(card.IsResolved);

        // And the timer that would have closed it is gone too: advancing the
        // clock past the window changes nothing.
        fixture.Clock.Advance(MergeQueueViewModel.DockFor + TimeSpan.FromSeconds(1));
        Assert.False(queue.IsDockOpen);
    }

    // ── Separate again ───────────────────────────────────────────────────────

    [Fact]
    public async Task Separate_again_on_a_card_answered_this_session_returns_it_to_pending_in_place()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        var original = section.Cards.ToList();
        var card = original[0];

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.True(card.IsResolved);
        Assert.False(card.IsFromHistory);

        await queue.SeparateCommand.ExecuteAsync(card);

        Assert.True(card.IsPending);
        Assert.Null(card.ActId);
        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.Equal(original, section.Cards);
        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public async Task A_standing_same_game_act_across_two_stores_loads_as_a_strip_in_ACROSS_STORES()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);
        var actId = await fixture.LinkAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var strip = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.True(strip.IsResolved);
        Assert.True(strip.IsFromHistory);
        Assert.Equal(actId, strip.ActId);
        Assert.Equal(IdentityLinkKinds.SameGame, strip.LinkKind);
        Assert.Equal(steam.WorkId, strip.ParentWorkId);
        Assert.Equal([epic.WorkId], strip.ChildWorkIds);

        // The proposal it answered is not asked again.
        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(Section(queue, MergeSectionKind.Editions).Cards);
    }

    [Fact]
    public async Task A_standing_same_game_act_on_one_store_loads_as_a_strip_in_EDITIONS()
    {
        using var fixture = new MergeQueueFixture();
        var (left, right) = await fixture.CreatePairAsync(Witcher, WitcherGoty);
        await fixture.QueueScoredPairAsync(left, right);
        var actId = await fixture.LinkAsync(left, right);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var strip = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.True(strip.IsFromHistory);
        Assert.Equal(actId, strip.ActId);
        Assert.Empty(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task A_standing_expansion_act_loads_as_a_strip_in_EXPANSIONS()
    {
        using var fixture = new MergeQueueFixture();
        var baseGame = await fixture.CreateReleaseAsync(CivIv);
        var pack = await fixture.CreateReleaseAsync(CivIvWarlords);
        var actId = await fixture.LinkAsync(baseGame, pack, IdentityLinkKinds.ExpansionOf);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var strip = Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards);
        Assert.True(strip.IsResolved);
        Assert.True(strip.IsFromHistory);
        Assert.Equal(actId, strip.ActId);
        Assert.Equal(IdentityLinkKinds.ExpansionOf, strip.LinkKind);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task A_standing_variant_act_loads_as_a_strip_in_TEST_BUILDS()
    {
        using var fixture = new MergeQueueFixture();
        var baseGame = await fixture.CreateReleaseAsync(CivIv);
        var demo = await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords Demo", 2006, "2K"));
        var actId = await fixture.LinkAsync(
            baseGame, demo, IdentityLinkKinds.VariantOf, RelationLabels.Demo);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var strip = Assert.Single(Section(queue, MergeSectionKind.Tests).Cards);
        Assert.True(strip.IsFromHistory);
        Assert.Equal(actId, strip.ActId);
        Assert.Equal(IdentityLinkKinds.VariantOf, strip.LinkKind);
        Assert.Equal(RelationLabels.Demo, strip.RelationLabel);
        Assert.Empty(Section(queue, MergeSectionKind.Expansions).Cards);
        Assert.Equal(0, queue.PendingCount);
    }

    /// <summary>
    /// A strip loaded from an earlier session carries no proposal ids, so
    /// separating it reloads the queue: the act is retracted and the proposals
    /// it answered are questions again.
    /// </summary>
    [Fact]
    public async Task Separate_again_on_a_strip_from_history_retracts_the_act_and_brings_the_proposal_back()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        var id = await fixture.QueueScoredPairAsync(steam, epic);
        await fixture.LinkAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Stores);
        var strip = Assert.Single(section.Cards);

        await queue.SeparateCommand.ExecuteAsync(strip);

        Assert.Empty(await fixture.LiveLinksAsync());
        var card = Assert.Single(section.Cards);
        Assert.True(card.IsPending);
        Assert.False(card.IsFromHistory);
        Assert.Equal([id], card.CandidateIds);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(id));
    }

    [Fact]
    public async Task Separate_again_on_an_expansion_strip_brings_the_scan_proposal_back()
    {
        using var fixture = new MergeQueueFixture();
        var baseGame = await fixture.CreateReleaseAsync(CivIv);
        var pack = await fixture.CreateReleaseAsync(CivIvWarlords);
        await fixture.LinkAsync(baseGame, pack, IdentityLinkKinds.ExpansionOf);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Expansions);

        await queue.SeparateCommand.ExecuteAsync(Assert.Single(section.Cards));

        Assert.Empty(await fixture.LiveLinksAsync());
        var card = Assert.Single(section.Cards);
        Assert.True(card.IsPending);
        Assert.Equal(IdentityLinkKinds.ExpansionOf, card.LinkKind);
        Assert.Single(card.RefusalPairs);
    }

    // ── The header's bulk paths ──────────────────────────────────────────────

    [Fact]
    public async Task Merge_selected_links_every_checked_card_under_its_own_header_and_one_undo_retracts_them_all()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Stanley, Stanley);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        var cards = section.Cards.ToList();
        Assert.Equal(3, cards.Count);

        Assert.Equal("Merge selected", queue.MergeSelectedLabel);
        Assert.False(queue.CanMergeSelected);

        cards[0].IsSelected = true;
        cards[2].IsSelected = true;
        cards[2].Promote(cards[2].Rows[1]);

        Assert.Equal(2, queue.SelectedCount);
        Assert.Equal("Merge 2 selected", queue.MergeSelectedLabel);
        Assert.True(queue.CanMergeSelected);

        await queue.MergeSelectedCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.ActCount());
        Assert.Equal(
            new[]
            {
                (cards[0].Rows[1].WorkId, cards[0].Rows[0].WorkId),
                (cards[2].Rows[0].WorkId, cards[2].Rows[1].WorkId),
            }.OrderBy(link => link.Item1).ThenBy(link => link.Item2),
            await fixture.LiveLinksAsync());

        Assert.True(cards[0].IsResolved);
        Assert.True(cards[1].IsPending);
        Assert.True(cards[2].IsResolved);
        Assert.Equal(1, queue.PendingCount);
        Assert.Equal(0, queue.SelectedCount);
        Assert.Equal("Rolled up 2 groups.", queue.DockTitle);

        await queue.UndoCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.All(cards, card => Assert.True(card.IsPending));
        Assert.Equal(3, queue.PendingCount);
    }

    [Fact]
    public async Task Accept_exact_links_only_exact_cross_store_cards_and_its_label_counts_live()
    {
        using var fixture = new MergeQueueFixture();

        // Two exact matches across stores.
        await fixture.QueueScoredPairAsync(
            await fixture.CreateReleaseAsync(Prey, store: "steam"),
            await fixture.CreateReleaseAsync(Prey, store: "epic"));
        await fixture.QueueScoredPairAsync(
            await fixture.CreateReleaseAsync(Stanley, store: "steam"),
            await fixture.CreateReleaseAsync(Stanley, store: "gog"));

        // An exact match on ONE store, and a likely match across stores.
        // Neither is the safe bulk path.
        await fixture.QueuePairAsync(Stanley, Stanley);
        await fixture.QueueScoredPairAsync(
            await fixture.CreateReleaseAsync(Witcher, store: "steam"),
            await fixture.CreateReleaseAsync(WitcherGoty, store: "gog"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var stores = Section(queue, MergeSectionKind.Stores);
        var editions = Section(queue, MergeSectionKind.Editions);
        Assert.Equal(3, stores.PendingCount);
        Assert.Equal(1, editions.PendingCount);

        Assert.Equal(2, queue.ExactCount);
        Assert.Equal("Accept 2 exact matches", queue.AcceptExactLabel);
        Assert.True(queue.CanAcceptExact);

        await queue.AcceptExactCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.ActCount());
        Assert.Equal(2, stores.Cards.Count(card => card.IsResolved && card.IsExact));
        var stillPending = Assert.Single(stores.Cards, card => card.IsPending);
        Assert.Equal(MergeConfidence.Likely, stillPending.Confidence);
        Assert.True(Assert.Single(editions.Cards).IsPending);
        Assert.Equal(2, queue.PendingCount);

        Assert.Equal(0, queue.ExactCount);
        Assert.Equal("No exact matches left", queue.AcceptExactLabel);
        Assert.False(queue.CanAcceptExact);
        Assert.Equal("Rolled up 2 exact matches.", queue.DockTitle);

        // A second press writes nothing.
        await queue.AcceptExactCommand.ExecuteAsync(null);
        Assert.Equal(2, fixture.ActCount());
    }

    [Fact]
    public async Task The_accept_exact_label_is_singular_at_one()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueScoredPairAsync(
            await fixture.CreateReleaseAsync(Prey, store: "steam"),
            await fixture.CreateReleaseAsync(Prey, store: "epic"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Accept 1 exact match", queue.AcceptExactLabel);
    }

    // ── Sort ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Strongest_match_orders_exact_before_likely_before_worth_a_look()
    {
        using var fixture = new MergeQueueFixture();

        // Inserted weakest-first, so an unordered read would come back backwards.
        await fixture.QueuePairAsync(Prey, PreyUnknown);
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Stanley, Stanley);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(MergeSort.StrongestMatch, queue.Sort);
        Assert.Equal(
            [MergeConfidence.Exact, MergeConfidence.Likely, MergeConfidence.WorthALook],
            Section(queue, MergeSectionKind.Editions).Cards.Select(card => card.Confidence));
    }

    [Fact]
    public async Task Playtime_at_stake_orders_by_summed_minutes_descending()
    {
        using var fixture = new MergeQueueFixture();
        var (preyLeft, preyRight) = await fixture.CreatePairAsync(Prey, PreyUnknown);
        var (witcherLeft, witcherRight) = await fixture.CreatePairAsync(Witcher, WitcherGoty);
        var (stanleyLeft, stanleyRight) = await fixture.CreatePairAsync(Stanley, Stanley);
        await fixture.QueueScoredPairAsync(preyLeft, preyRight);
        await fixture.QueueScoredPairAsync(witcherLeft, witcherRight);
        await fixture.QueueScoredPairAsync(stanleyLeft, stanleyRight);

        // Strongest match would put Stanley (exact) first and Prey last;
        // playtime says the opposite. The Witcher's hours are split across
        // its two entries, so the card's figure is a sum, not a maximum.
        await fixture.PlayedAsync(preyLeft, 600, MergeQueueFixture.Now.AddDays(-40));
        await fixture.PlayedAsync(witcherLeft, 30, MergeQueueFixture.Now.AddDays(-40));
        await fixture.PlayedAsync(witcherRight, 30, MergeQueueFixture.Now.AddDays(-40));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        Assert.Equal("The Stanley Parable", section.Cards[0].HeaderTitle);

        var option = queue.SortOptions.Single(o => o.Sort == MergeSort.PlaytimeAtStake);
        queue.SelectSortCommand.Execute(option);

        Assert.Equal(MergeSort.PlaytimeAtStake, queue.Sort);
        Assert.Equal([600, 60, 0], section.Cards.Select(card => card.TotalMinutes));
        Assert.Equal(
            ["Prey", "The Witcher 3: Wild Hunt", "The Stanley Parable"],
            section.Cards.Select(card => card.HeaderTitle));
        Assert.True(option.IsSelected);
        Assert.False(queue.SortOptions.Single(o => o.Sort == MergeSort.StrongestMatch).IsSelected);
    }

    [Fact]
    public async Task Title_orders_by_the_header_title()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Stanley, Stanley);
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        queue.SelectSortCommand.Execute(queue.SortOptions.Single(option => option.Sort == MergeSort.Title));

        Assert.Equal(
            ["Prey", "The Stanley Parable", "The Witcher 3: Wild Hunt"],
            Section(queue, MergeSectionKind.Editions).Cards.Select(card => card.HeaderTitle));
    }

    [Fact]
    public async Task Resolved_strips_sit_after_pending_cards_after_a_resort()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Stanley, Stanley);
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);

        // The exact match leads the strongest-match order, and by title it
        // would lead too ("The Stanley Parable" < "The Witcher 3"); as a strip
        // it sits last either way.
        var answered = section.Cards[0];
        Assert.Equal("The Stanley Parable", answered.HeaderTitle);
        await queue.SameGameCommand.ExecuteAsync(answered);

        queue.SelectSortCommand.Execute(queue.SortOptions.Single(option => option.Sort == MergeSort.Title));

        Assert.Equal(
            ["Prey", "The Witcher 3: Wild Hunt", "The Stanley Parable"],
            section.Cards.Select(card => card.HeaderTitle));
        Assert.Same(answered, section.Cards[^1]);
        Assert.True(section.Cards[^1].IsResolved);
        Assert.All(section.Cards.Take(2), card => Assert.True(card.IsPending));
    }

    [Fact]
    public async Task The_sort_label_names_the_current_order()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Sort · Strongest match", queue.SortLabel);

        queue.SelectSortCommand.Execute(queue.SortOptions.Single(option => option.Sort == MergeSort.PlaytimeAtStake));
        Assert.Equal("Sort · Playtime at stake", queue.SortLabel);

        queue.SelectSortCommand.Execute(queue.SortOptions.Single(option => option.Sort == MergeSort.Title));
        Assert.Equal("Sort · Title", queue.SortLabel);
    }

    // ── The kind filter ──────────────────────────────────────────────────────

    [Fact]
    public async Task Filtering_to_STORES_hides_every_other_section_and_counts_only_what_is_shown()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueScoredPairAsync(
            await fixture.CreateReleaseAsync(Prey, store: "steam"),
            await fixture.CreateReleaseAsync(Prey, store: "epic"));
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Stanley, Stanley);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Null(queue.Kind);
        Assert.False(queue.IsKindFiltered);
        Assert.Equal("3", queue.CutCountText);
        Assert.Equal("3 proposals · non-destructive", queue.PendingLine);
        Assert.All(queue.Sections, section => Assert.True(section.IsVisible));

        var stores = queue.KindOptions.Single(option => option.Kind == MergeSectionKind.Stores);
        Assert.Equal("STORES", stores.Label);
        queue.SelectKindCommand.Execute(stores);

        Assert.Equal(MergeSectionKind.Stores, queue.Kind);
        Assert.True(queue.IsKindFiltered);
        Assert.Equal("ACROSS STORES", queue.KindChipLabel);
        Assert.Equal("3 → 1", queue.CutCountText);
        Assert.Equal(3, queue.PendingCount);
        Assert.Equal(1, queue.ShownPendingCount);
        Assert.Equal("1 proposal · non-destructive", queue.PendingLine);
        Assert.True(stores.IsSelected);
        foreach (var section in queue.Sections)
        {
            Assert.Equal(section.Kind == MergeSectionKind.Stores, section.IsVisible);
        }

        queue.ClearKindCommand.Execute(null);

        Assert.Null(queue.Kind);
        Assert.Equal("3", queue.CutCountText);
        Assert.Equal("3 proposals · non-destructive", queue.PendingLine);
        Assert.Equal(string.Empty, queue.KindChipLabel);
        Assert.All(queue.Sections, section => Assert.True(section.IsVisible));
        Assert.True(queue.KindOptions[0].IsSelected);
        Assert.False(stores.IsSelected);
    }

    // ── Rows read the library's own read model ───────────────────────────────

    [Fact]
    public async Task A_played_row_reads_its_hours_and_idle_time_from_the_library()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);
        await fixture.PlayedAsync(steam, 300, MergeQueueFixture.Now.AddDays(-100));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        var played = card.Rows.Single(row => row.WorkId == steam.WorkId);
        var unplayed = card.Rows.Single(row => row.WorkId == epic.WorkId);

        Assert.Equal("5h", played.PlaytimeText);
        Assert.Equal("3mo", played.IdleText);
        Assert.Equal(300, played.PlaytimeMinutes);
        Assert.False(played.HasUnread);
        Assert.Contains("5h", played.DetailText, StringComparison.Ordinal);

        Assert.Equal("0h", unplayed.PlaytimeText);
        Assert.Equal("never", unplayed.IdleText);
        Assert.Contains("never opened", unplayed.DetailText, StringComparison.Ordinal);

        Assert.Equal(300, card.TotalMinutes);
        Assert.StartsWith("5h rolled up · 2 entries", card.RollupText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unplayed_card_rolls_up_zero_hours()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        Assert.All(card.Rows, row => Assert.Equal("0h", row.PlaytimeText));
        Assert.All(card.Rows, row => Assert.Equal("never", row.IdleText));
        Assert.Equal(0, card.TotalMinutes);
        Assert.Equal("0h rolled up · 2 entries", card.RollupText);
        Assert.Equal("2 entries · 0h · nested, nothing deleted", card.ResolvedMeta);
    }

    [Fact]
    public async Task A_pack_row_with_no_playtime_shows_an_em_dash_for_both()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards);
        var pack = Assert.Single(card.Rows, row => row.IsPack);
        Assert.Equal("—", pack.PlaytimeText);
        Assert.Equal("—", pack.IdleText);
        Assert.Contains("no separate playtime recorded", pack.DetailText, StringComparison.Ordinal);

        // The base is a game, and a game never opened says so.
        var baseRow = card.Rows[0];
        Assert.False(baseRow.IsPack);
        Assert.Equal("0h", baseRow.PlaytimeText);
        Assert.Equal("never", baseRow.IdleText);
    }

    [Fact]
    public async Task A_patch_after_the_last_session_marks_the_row_unread_and_the_card_says_so()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);
        await fixture.PlayedAsync(steam, 300, MergeQueueFixture.Now.AddYears(-2));
        await fixture.PatchedAsync(steam, MergeQueueFixture.Now.AddDays(-10));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        var patched = card.Rows.Single(row => row.WorkId == steam.WorkId);
        var other = card.Rows.Single(row => row.WorkId == epic.WorkId);

        Assert.True(patched.HasUnread);
        Assert.False(other.HasUnread);
        Assert.EndsWith("Patched since you played", patched.DetailText, StringComparison.Ordinal);

        Assert.True(card.HasUnread);
        Assert.Equal(1, card.UnreadCount);
        Assert.Contains("1 entry patched since you played", card.RollupText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_earliest_ownership_year_appears_in_the_rollup()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(
            Prey, store: "steam", acquiredAt: new DateTime(2021, 3, 4, 0, 0, 0, DateTimeKind.Utc));
        var epic = await fixture.CreateReleaseAsync(
            Prey, store: "epic", acquiredAt: new DateTime(2019, 11, 30, 0, 0, 0, DateTimeKind.Utc));
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        Assert.Equal(2019, card.OwnedSinceYear);
        Assert.Contains("owned since 2019", card.RollupText, StringComparison.Ordinal);
        Assert.DoesNotContain("2021", card.RollupText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_row_wears_the_stores_it_is_owned_on()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(Prey, store: "steam");
        var epic = await fixture.CreateReleaseAsync(Prey, store: "epic");
        await fixture.AlsoOwnedOnAsync(steam, "gog");
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Stores).Cards);
        var twice = card.Rows.Single(row => row.WorkId == steam.WorkId);
        var once = card.Rows.Single(row => row.WorkId == epic.WorkId);
        Assert.Equal(["STEAM", "GOG"], twice.StoreChips);
        Assert.Equal(["EPIC"], once.StoreChips);
        Assert.NotEqual(twice.Label, once.Label);
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Focus_starts_on_the_first_row_of_the_first_pending_card()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        Assert.Null(queue.FocusedRow);
        Assert.Null(queue.FocusedCard);

        await queue.LoadCommand.ExecuteAsync(null);

        var first = Section(queue, MergeSectionKind.Editions).Cards[0];
        Assert.Same(first.Rows[0], queue.FocusedRow);
        Assert.Same(first, queue.FocusedCard);
        Assert.True(first.Rows[0].IsFocused);
        Assert.True(first.IsFocused);
        Assert.Same(first, queue.CardOf(first.Rows[1]));
    }

    [Fact]
    public async Task Focus_walks_rows_across_cards_and_clamps_at_both_ends()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var cards = Section(queue, MergeSectionKind.Editions).Cards;

        Assert.Same(cards[0].Rows[1], queue.MoveFocus(1));
        Assert.Same(cards[1].Rows[0], queue.MoveFocus(1));
        Assert.Same(cards[1], queue.FocusedCard);
        Assert.False(cards[0].IsFocused);
        Assert.True(cards[1].IsFocused);

        Assert.Same(cards[1].Rows[1], queue.MoveFocus(1));
        Assert.Same(cards[1].Rows[1], queue.MoveFocus(1));
        Assert.Same(cards[1].Rows[1], queue.MoveFocus(10));

        Assert.Same(cards[0].Rows[0], queue.MoveFocus(-3));
        Assert.Same(cards[0].Rows[0], queue.MoveFocus(-1));
        Assert.Same(cards[0].Rows[0], queue.FocusedRow);
    }

    [Fact]
    public async Task Moving_focus_on_an_empty_queue_returns_nothing()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.Null(queue.MoveFocus(1));
        Assert.Null(queue.FocusedRow);

        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Null(queue.MoveFocus(1));
        Assert.Null(queue.MoveFocus(-1));
        Assert.Null(queue.FocusedRow);
        Assert.Null(queue.FocusedCard);
    }

    [Fact]
    public async Task Promote_focused_makes_the_cursors_row_the_header()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Witcher, WitcherGoty);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        queue.MoveFocus(1);
        queue.PromoteFocused();

        Assert.Same(card.Rows[1], card.Header);
        Assert.True(card.IsTouched);
        Assert.Same(card.Rows[1], queue.FocusedRow);
    }

    [Fact]
    public async Task Answering_the_focused_card_moves_the_cursor_to_the_card_that_took_its_place()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);
        await fixture.QueuePairAsync(Witcher, WitcherGoty);
        await fixture.QueuePairAsync(Stanley, Stanley);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var cards = Section(queue, MergeSectionKind.Editions).Cards.ToList();
        Assert.Same(cards[0], queue.FocusedCard);

        await queue.SameGameCommand.ExecuteAsync(queue.FocusedCard);
        Assert.Same(cards[1].Rows[0], queue.FocusedRow);
        Assert.Same(cards[1], queue.FocusedCard);

        await queue.DifferentGamesCommand.ExecuteAsync(queue.FocusedCard);
        Assert.Same(cards[2].Rows[0], queue.FocusedRow);
        Assert.Same(cards[2], queue.FocusedCard);

        // Nothing left after the last one: the cursor has nowhere to be.
        await queue.DifferentGamesCommand.ExecuteAsync(queue.FocusedCard);
        Assert.Null(queue.FocusedRow);
        Assert.Null(queue.FocusedCard);
    }

    [Fact]
    public async Task A_keyboard_move_asks_the_view_to_follow()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        var requested = new List<MergeRowViewModel>();
        queue.FocusRequested += requested.Add;

        queue.MoveFocus(1);
        Assert.Equal([card.Rows[1]], requested);

        // Selection follows the view's own focus without an echo back to it.
        queue.FocusRow(card.Rows[0]);
        Assert.Same(card.Rows[0], queue.FocusedRow);
        Assert.Single(requested);
    }

    // ── Expansion proposals ──────────────────────────────────────────────────

    /// <summary>
    /// The one-to-many relation presented once: one base game, both packs, one
    /// card, and the base is the header by the shape of the relation.
    /// </summary>
    [Fact]
    public async Task A_base_game_and_its_packs_are_one_card()
    {
        using var fixture = new MergeQueueFixture();
        var baseGame = await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);
        await fixture.CreateReleaseAsync(CivIvBeyond);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards);
        Assert.Equal(MergeSectionKind.Expansions, card.Section);
        Assert.Equal(3, card.Rows.Count);

        Assert.True(card.Rows[0].IsHeader);
        Assert.False(card.Rows[0].IsPack);
        Assert.Equal("Sid Meier's Civilization IV", card.HeaderTitle);
        Assert.Equal(baseGame.WorkId, card.ParentWorkId);
        Assert.All(card.Rows.Skip(1), row => Assert.True(row.IsPack));
        Assert.All(card.Rows.Skip(1), row => Assert.False(row.IsHeader));

        Assert.Equal(IdentityLinkKinds.ExpansionOf, card.LinkKind);
        Assert.Equal(MergeConfidence.Likely, card.Confidence);
        Assert.NotEmpty(card.Reason);
        Assert.Contains("Sid Meier's Civilization IV", card.Reason, StringComparison.Ordinal);
        Assert.Empty(card.CandidateIds);
        Assert.Equal(2, card.RefusalPairs.Count);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task Same_game_on_an_expansion_card_writes_one_act_of_expansion_links()
    {
        using var fixture = new MergeQueueFixture();
        var baseGame = await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);
        await fixture.CreateReleaseAsync(CivIvBeyond);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards);

        await queue.SameGameCommand.ExecuteAsync(card);

        Assert.Equal(1, fixture.ActCount());
        var links = (await fixture.Links.GetHistoryAsync()).Where(link => link.IsLive).ToList();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => Assert.Equal(IdentityLinkKinds.ExpansionOf, link.Kind));
        Assert.All(links, link => Assert.Equal(baseGame.WorkId, link.ParentWorkId));
        Assert.Equal(card.ChildWorkIds, links.Select(link => link.ChildWorkId).Order());

        var resolution = await fixture.Links.GetResolutionAsync();
        Assert.True(resolution.SameGame.IsEmpty);
        Assert.True(card.IsResolved);
        Assert.Empty(await fixture.ExpansionRefusals.GetAllAsync());

        // And the scan does not ask again.
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Equal(0, queue.PendingCount);
        Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards, strip => strip.IsResolved);
    }

    [Fact]
    public async Task A_refused_expansion_pair_does_not_come_back_on_the_next_load()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.DifferentGamesCommand.ExecuteAsync(
            Assert.Single(Section(queue, MergeSectionKind.Expansions).Cards));

        Assert.Single(await fixture.ExpansionRefusals.GetAllAsync());

        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Empty(Section(queue, MergeSectionKind.Expansions).Cards);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task A_demo_lands_in_TEST_BUILDS_as_a_variant()
    {
        using var fixture = new MergeQueueFixture();
        var baseGame = await fixture.CreateReleaseAsync(CivIv);
        var demo = await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords Demo", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(Section(queue, MergeSectionKind.Tests).Cards);
        Assert.Empty(Section(queue, MergeSectionKind.Expansions).Cards);
        Assert.Equal(IdentityLinkKinds.VariantOf, card.LinkKind);
        Assert.Equal(RelationLabels.Demo, card.RelationLabel);
        Assert.Equal(baseGame.WorkId, card.ParentWorkId);
        Assert.Equal([demo.WorkId], card.ChildWorkIds);
        Assert.True(card.Rows[1].IsPack);

        await queue.SameGameCommand.ExecuteAsync(card);
        var link = Assert.Single(await fixture.Links.GetHistoryAsync(), l => l.IsLive);
        Assert.Equal(IdentityLinkKinds.VariantOf, link.Kind);
        Assert.Equal(RelationLabels.Demo, link.RelationLabel);
    }

    // ── The answer path reads nothing ────────────────────────────────────────

    /// <summary>
    /// A previous build of this screen re-planned every remaining card on every
    /// answer and froze for about two seconds at 200 pending pairs. Cards are
    /// disjoint over resolved works, so an answer inside one cannot change
    /// another and there is nothing to re-read. Asserted by counting the reads
    /// rather than by timing them.
    /// </summary>
    [Fact]
    public async Task Answering_reads_nothing_however_long_the_queue_is()
    {
        using var fixture = new MergeQueueFixture();
        for (var i = 0; i < 60; i++)
        {
            await fixture.QueuePairAsync(
                new SeedSide($"Bastion {i}", 2011, "Supergiant Games"),
                new SeedSide($"Bastion {i}", 2011, "Supergiant Games"));
        }

        var counting = fixture.CountingCandidates();
        var queue = fixture.CreateViewModel(candidates: counting);
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);

        Assert.Equal(60, section.PendingCount);
        Assert.Equal(1, counting.PendingReads);

        var cards = section.Cards.ToList();
        for (var i = 0; i < 20; i++)
        {
            await queue.SameGameCommand.ExecuteAsync(cards[i]);
        }

        // Not one extra read of the queue, and not one status write: a link
        // answers the proposal by existing.
        Assert.Equal(1, counting.PendingReads);
        Assert.Equal(0, counting.StatusWrites);
        Assert.Equal(40, section.PendingCount);
        Assert.Equal(20, fixture.ActCount());

        // The cards that are left are the same objects they were, in the
        // same places: nothing was rebuilt underneath the user.
        Assert.Equal(cards, section.Cards);
    }

    [Fact]
    public async Task A_rejected_proposal_stays_rejected_when_the_resolver_runs_again()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.DifferentGamesCommand.ExecuteAsync(
            Assert.Single(Section(queue, MergeSectionKind.Editions).Cards));

        Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(id));

        await queue.LoadCommand.ExecuteAsync(null);
        Assert.All(queue.Sections, section => Assert.Empty(section.Cards));
        Assert.Equal(0, queue.PendingCount);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    // ── Who joins, row by row ────────────────────────────────────────────────
    //
    // Restored on 2026-09-02 after the screen was tried: a group that arrives
    // with one wrong member needs answering without refusing the rest. The
    // radio picks the header; the checkbox picks who joins; the row itself
    // opens the game's details.

    [Fact]
    public async Task Setting_a_rows_radio_moves_the_header()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        var other = card.Rows[1];

        // The radio writes IsHeader; the card hears it through the row.
        other.IsHeader = true;

        Assert.Equal(1, card.HeaderIndex);
        Assert.Same(other, card.Header);
        Assert.False(card.Rows[0].IsHeader);
        Assert.True(card.IsTouched);
        Assert.Equal(card.Key, other.GroupName);
        Assert.Equal([card.Rows[0].WorkId], card.ChildWorkIds);
    }

    [Fact]
    public async Task A_row_left_out_is_not_linked_and_its_proposals_are_recorded()
    {
        using var fixture = new MergeQueueFixture();
        var (a, b, c) = await fixture.CreateTripleAsync();
        var ab = await fixture.QueueScoredPairAsync(a, b);
        var ac = await fixture.QueueScoredPairAsync(a, c);
        var bc = await fixture.QueueScoredPairAsync(b, c);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Editions);
        var card = Assert.Single(section.Cards);
        var leftOut = card.Rows.Single(row => row.WorkId == c.WorkId);

        leftOut.IsIncluded = false;

        Assert.Equal("LEFT OUT", leftOut.HeaderMark);
        Assert.Single(card.ExcludedRows);
        Assert.DoesNotContain(c.WorkId, card.ChildWorkIds);
        Assert.Equal([ac, bc], card.RejectedCandidateIds.Order());
        Assert.Contains("1 left out", card.RollupText, StringComparison.Ordinal);
        Assert.True(card.CanAnswer);

        await queue.SameGameCommand.ExecuteAsync(card);

        // One link, between the two rows still in; the left-out row's two
        // proposals recorded as answered no; the one it was not part of is
        // the one that was linked.
        var links = await fixture.LiveLinksAsync();
        var link = Assert.Single(links);
        Assert.Equal(card.ParentWorkId, link.Parent);
        Assert.NotEqual(c.WorkId, link.Child);
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(ab));
        Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(ac));
        Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(bc));
        Assert.True(card.IsResolved);
        Assert.Equal("1 nested · 1 left out · nothing was deleted.", queue.DockNote);

        // One Undo reverses the whole answer: the act and the recorded noes.
        await queue.UndoCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(ac));
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(bc));
        Assert.True(card.IsPending);
        Assert.False(leftOut.IsIncluded);
    }

    [Fact]
    public async Task Same_game_is_refused_while_every_other_row_is_left_out()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);

        card.Rows[1].IsIncluded = false;
        Assert.False(card.CanAnswer);

        await queue.SameGameCommand.ExecuteAsync(card);

        Assert.True(card.IsPending);
        Assert.False(card.IsDecided);
        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(id));
        Assert.False(queue.IsDockOpen);

        // Bringing the row back re-arms the answer.
        card.Rows[1].IsIncluded = true;
        Assert.True(card.CanAnswer);
    }

    [Fact]
    public async Task Making_a_left_out_row_the_header_brings_it_back_in()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        var row = card.Rows[1];

        row.IsIncluded = false;
        Assert.False(card.CanAnswer);

        card.Promote(row);

        Assert.True(row.IsHeader);
        Assert.True(row.IsIncluded);
        Assert.False(row.CanExclude);
        Assert.True(card.CanAnswer);
        Assert.Equal([card.Rows[0].WorkId], card.ChildWorkIds);
    }

    [Fact]
    public async Task Leaving_a_pack_out_refuses_only_that_pair()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(CivIv);
        await fixture.CreateReleaseAsync(CivIvWarlords);
        await fixture.CreateReleaseAsync(CivIvBeyond);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var section = Section(queue, MergeSectionKind.Expansions);
        var card = Assert.Single(section.Cards);
        var leftOut = card.Rows[2];

        leftOut.IsIncluded = false;
        var refused = Assert.Single(card.RefusedPairs);
        Assert.Equal(leftOut.WorkId, refused.ChildWorkId);

        await queue.SameGameCommand.ExecuteAsync(card);

        var link = Assert.Single(await fixture.LiveLinksAsync());
        Assert.Equal(card.Rows[1].WorkId, link.Child);
        var refusal = Assert.Single(await fixture.ExpansionRefusals.GetAllAsync());
        Assert.Equal(leftOut.WorkId, refusal.ChildWorkId);
        Assert.Equal(card.ParentWorkId, refusal.BaseWorkId);

        await queue.UndoCommand.ExecuteAsync(null);

        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.Empty(await fixture.ExpansionRefusals.GetAllAsync());
        Assert.True(card.IsPending);
    }

    [Fact]
    public async Task A_click_on_a_row_asks_for_its_details_and_takes_the_cursor()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(Prey, PreyUnknown);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(Section(queue, MergeSectionKind.Editions).Cards);
        var row = card.Rows[1];

        MergeRowViewModel? asked = null;
        queue.DetailsRequested += r => asked = r;

        queue.OpenDetailsCommand.Execute(row);

        Assert.Same(row, asked);
        Assert.Same(row, queue.FocusedRow);
        Assert.Equal("Open details", row.DetailsTip);
        Assert.StartsWith("Details of ", row.DetailsAutomationName, StringComparison.Ordinal);

        // Asking for details is not promoting: the header stays put.
        Assert.Equal(0, card.HeaderIndex);
    }

    private static MergeSectionViewModel Section(MergeQueueViewModel queue, MergeSectionKind kind)
        => queue.Sections.Single(section => section.Kind == kind);

    /// <summary>A release as a store feed would describe it, before it is scored.</summary>
    private sealed record SeedSide(string Title, int? Year, string? Publisher);

    /// <summary>A seeded release: the row id, its work id, and the metadata it was seeded with.</summary>
    private sealed record SeededRelease(long ReleaseId, long WorkId, SeedSide Side);

    /// <summary>
    /// Counts what the screen asks of the candidate repository, so "the answer
    /// path reads nothing" is an assertion rather than a stopwatch.
    /// </summary>
    private sealed class CountingCandidateRepository(IMergeCandidateRepository inner)
        : IMergeCandidateRepository
    {
        public int PendingReads { get; private set; }

        public int StatusWrites { get; private set; }

        public Task<long> InsertAsync(MergeCandidate candidate, CancellationToken ct = default)
            => inner.InsertAsync(candidate, ct);

        public Task<IReadOnlyList<MergeCandidate>> GetPendingAsync(CancellationToken ct = default)
        {
            PendingReads++;
            return inner.GetPendingAsync(ct);
        }

        public Task<IReadOnlyList<MergeCandidate>> GetAllAsync(CancellationToken ct = default)
            => inner.GetAllAsync(ct);

        public Task<MergeCandidate?> GetAsync(long id, CancellationToken ct = default)
            => inner.GetAsync(id, ct);

        public Task<MergeCandidate?> FindByPairAsync(
            long leftReleaseId, long rightReleaseId, CancellationToken ct = default)
            => inner.FindByPairAsync(leftReleaseId, rightReleaseId, ct);

        public Task SetStatusAsync(long id, string status, CancellationToken ct = default)
        {
            StatusWrites++;
            return inner.SetStatusAsync(id, status, ct);
        }

        public Task<bool> UpdatePendingScoreAsync(
            long id, double score, string? signalsJson, CancellationToken ct = default)
            => inner.UpdatePendingScoreAsync(id, score, signalsJson, ct);

        public Task<bool> WithdrawPendingAsync(long id, CancellationToken ct = default)
            => inner.WithdrawPendingAsync(id, ct);
    }

    private sealed class MergeQueueFixture : IDisposable
    {
        /// <summary>The fake clock's now. Every idle span and staleness test is measured from here.</summary>
        public static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private readonly TempDatabase _db = new();
        private readonly Dictionary<long, (long Left, long Right)> _pairs = [];
        private int _appId = 100000;

        public MergeQueueFixture()
        {
            Works = new WorkRepository(_db.Factory);
            Releases = new ReleaseRepository(_db.Factory);
            Candidates = new MergeCandidateRepository(_db.Factory);
            ResolveState = new ResolveStateRepository(_db.Factory);
            Links = new IdentityLinkRepository(_db.Factory);
            Ownership = new OwnershipRepository(_db.Factory);
            ExpansionRefusals = new ExpansionRefusalRepository(_db.Factory);
            Plays = new PlayRecordRepository(_db.Factory);
            Updates = new UpdateEventRepository(_db.Factory);
        }

        /// <summary>For the assertions that have to look at the database itself.</summary>
        public SqliteConnectionFactory Factory => _db.Factory;

        /// <summary>The screen's clock: idle spans, the dock timer, and nothing else.</summary>
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(Now));

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public IResolveStateRepository ResolveState { get; }

        public IIdentityLinkRepository Links { get; }

        public IOwnershipRepository Ownership { get; }

        public IExpansionRefusalRepository ExpansionRefusals { get; }

        public IPlayRecordRepository Plays { get; }

        public IUpdateEventRepository Updates { get; }

        public CountingCandidateRepository CountingCandidates() => new(Candidates);

        /// <summary>
        /// No cover cache, the fake clock, and an inline poster so the dock
        /// timer's callback runs on the test thread.
        /// </summary>
        public MergeQueueViewModel CreateViewModel(
            bool withResolveState = true, IMergeCandidateRepository? candidates = null)
            => new(
                candidates ?? Candidates,
                Releases,
                Works,
                Links,
                Ownership,
                new LibraryExpansionScan(Releases, Links, ExpansionRefusals),
                ExpansionRefusals,
                new LibraryQueryRepository(_db.Factory),
                covers: null,
                resolveState: withResolveState ? ResolveState : null,
                clock: Clock,
                post: action => action());

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

        /// <summary>One game as three entries on one store: three works, three entries.</summary>
        public async Task<(SeededRelease A, SeededRelease B, SeededRelease C)> CreateTripleAsync()
            => (
                await CreateReleaseAsync(Prey),
                await CreateReleaseAsync(Prey),
                await CreateReleaseAsync(Prey));

        /// <summary>One game as three stores list it, and the three proposals a sweep would write.</summary>
        public async Task QueueCrossStoreTripleAsync()
        {
            var a = await CreateReleaseAsync(Prey, store: "steam");
            var b = await CreateReleaseAsync(Prey, store: "epic");
            var c = await CreateReleaseAsync(Prey, store: "gog");
            await QueueScoredPairAsync(a, b);
            await QueueScoredPairAsync(a, c);
            await QueueScoredPairAsync(b, c);
        }

        /// <summary>Creates both releases on one store and queues them, exactly as the resolver would.</summary>
        public async Task<long> QueuePairAsync(SeedSide left, SeedSide right)
        {
            var (leftRelease, rightRelease) = await CreatePairAsync(left, right);
            return await QueueScoredPairAsync(leftRelease, rightRelease);
        }

        /// <summary>
        /// Scores with the real matcher and writes the real payload, so the view
        /// model is decoding what the resolver actually produces rather than a
        /// hand-written approximation of it. <paramref name="band"/> overrides
        /// only the band, which is the one fact a fixture cannot conjure from
        /// realistic metadata.
        /// </summary>
        public async Task<long> QueueScoredPairAsync(
            SeededRelease left, SeededRelease right, SoftMatchBand? band = null)
        {
            var score = new SoftMatcher().Score(Subject(left), Subject(right));
            Assert.True(score.ShouldQueue, $"Fixture pair scored {score.Score:F2} and would not be queued.");

            if (band is { } forced)
            {
                score = score with { Band = forced };
            }

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

        /// <summary>Writes a link act directly, as an earlier session would have. Returns the act id.</summary>
        public Task<long> LinkAsync(
            SeededRelease parent,
            SeededRelease child,
            string kind = IdentityLinkKinds.SameGame,
            string? relationLabel = null)
            => Links.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = parent.WorkId,
                ChildWorkIds = [child.WorkId],
                Kind = kind,
                RelationLabel = relationLabel,
            });

        /// <summary>
        /// Reads a candidate's status back. <c>FindByPairAsync</c> is the only
        /// read that returns a row in any status, which is exactly the lookup
        /// the resolver uses to keep an answered pair out of the queue.
        /// </summary>
        public async Task<string?> StatusOfAsync(long candidateId)
        {
            var (left, right) = _pairs[candidateId];
            return (await Candidates.FindByPairAsync(left, right))?.Status;
        }

        /// <summary>Every live link, as (child, parent), ordered.</summary>
        public async Task<IReadOnlyList<(long Child, long Parent)>> LiveLinksAsync()
        {
            var links = await Links.GetHistoryAsync();
            return
            [
                .. links
                    .Where(l => l.IsLive)
                    .Select(l => (l.ChildWorkId, l.ParentWorkId))
                    .OrderBy(l => l.ChildWorkId)
                    .ThenBy(l => l.ParentWorkId),
            ];
        }

        /// <summary>How many link acts were recorded. Retraction adds its own.</summary>
        public int ActCount()
        {
            using var conn = _db.Factory.Open();
            return (int)conn.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM identity_acts WHERE kind = 'link';");
        }

        /// <summary>A second store for a release the fixture already made.</summary>
        public Task AlsoOwnedOnAsync(SeededRelease release, string store)
            => Ownership.UpsertAsync(new OwnershipUpsert(
                release.ReleaseId, store, null, null, null, null));

        /// <summary>
        /// One play record on the release's ownership, the way a Steam scan
        /// would write it. The bucket query the screen reads folds it into the
        /// row's hours and idle time.
        /// </summary>
        public async Task PlayedAsync(SeededRelease release, long minutes, DateTime? lastPlayed)
        {
            var ownership = (await Ownership.GetByReleaseAsync(release.ReleaseId)).First();
            await Plays.InsertAsync(new PlayRecord
            {
                OwnershipId = ownership.Id,
                PlaytimeMinutes = minutes,
                LastPlayedAt = lastPlayed,
                Source = "steam_localconfig",
                ObservedAt = Now,
            });
        }

        /// <summary>
        /// A build push with an announcement beside it, which is what the
        /// bucket query counts as one update (§5.2).
        /// </summary>
        public async Task PatchedAsync(SeededRelease release, DateTime pushedAt)
        {
            await Updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = release.ReleaseId,
                Kind = UpdateEventKinds.BuildPush,
                OccurredAt = pushedAt,
                BuildId = "1",
            });
            await Updates.InsertAsync(new UpdateEvent
            {
                ReleaseId = release.ReleaseId,
                Kind = UpdateEventKinds.Announcement,
                OccurredAt = pushedAt.AddDays(1),
                Title = "Patch notes",
            });
        }

        public async Task<SeededRelease> CreateReleaseAsync(
            SeedSide side,
            string platform = "windows",
            string? store = "steam",
            DateTime? acquiredAt = null)
        {
            var workId = await Works.InsertAsync(new Work
            {
                Name = side.Title,
                FirstReleaseYear = side.Year,
                Publisher = side.Publisher,
            });

            var releaseId = await Releases.InsertAsync(new Release
            {
                WorkId = workId,
                Name = side.Title,
                Platform = platform,
            });

            await Releases.AddExternalIdAsync(new ExternalId
            {
                ReleaseId = releaseId,
                Provider = ExternalIdProviders.Steam,
                ProviderId = (++_appId).ToString(CultureInfo.InvariantCulture),
            });

            if (store is { Length: > 0 })
            {
                await Ownership.UpsertAsync(new OwnershipUpsert(
                    releaseId, store, null, acquiredAt, null, null));
            }

            return new SeededRelease(releaseId, workId, side);
        }

        public void Dispose() => _db.Dispose();
    }
}
