using System.Globalization;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Merging;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Same Game screen's view model, whose unit is a GROUP and whose answer is
/// a LINK.
///
/// <para>These run against a real migrated SQLite file and the real
/// repositories, because the facts most worth pinning are all facts about what
/// reaches the database: that answering writes links and rejections and never a
/// merge, that a rejected pair never comes back, and that linking, retracting
/// and linking again leaves the same rows behind every time. No Avalonia
/// application, dispatcher or rendering is involved; the view model is
/// constructed directly and every assertion is on its properties.</para>
/// </summary>
public sealed class MergeQueueViewModelTests
{
    // ── Ordering ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_is_ordered_by_score_descending()
    {
        using var fixture = new MergeQueueFixture();

        // Inserted worst-first, so an unordered read would come back backwards.
        await fixture.QueuePairAsync(
            new SeedSide("Deus Ex: Human Revolution", 2011, "Square Enix"),
            new SeedSide("Deus Ex: Human Revolution - Director's Cut", 2013, "Square Enix"));
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, queue.Groups.Count);
        var scores = queue.Groups.Select(g => g.Score).ToList();
        Assert.Equal(scores.OrderByDescending(s => s), scores);
    }

    [Fact]
    public async Task Strongest_group_is_selected_first()
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

        Assert.Same(queue.Groups[0], queue.SelectedGroup);
        Assert.True(queue.SelectedGroup!.IsSelected);
    }

    // ── One card per group ───────────────────────────────────────────────────

    /// <summary>
    /// The structural answer to the complaint that several proposals name the
    /// same game. Three store entries produce three pairwise proposals and
    /// exactly ONE card, so there is no second card left to go stale.
    /// </summary>
    [Fact]
    public async Task Three_stores_of_one_game_are_one_card()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);
        Assert.Equal(3, card.Members.Count);
        Assert.Equal(3, card.Edges.Count);
        Assert.False(card.IsPair);
        Assert.Equal("3", card.MemberCountText);

        // Three members with one title still answer to three different names,
        // without a database id among them.
        Assert.Equal(3, card.Members.Select(m => m.Label).Distinct().Count());
        Assert.All(card.Members, m => Assert.Equal("Prey", m.Side.Title));
    }

    /// <summary>
    /// One act, one transaction, three-way identity — not three sequential
    /// pairwise operations each invalidating the next.
    /// </summary>
    [Fact]
    public async Task Approving_a_three_store_group_is_one_act()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Groups[0];
        var primary = card.Primary.WorkId;
        var children = card.Others.Select(m => m.WorkId).Order().ToList();

        await queue.SameGameCommand.ExecuteAsync(card);

        Assert.Empty(queue.Groups);
        Assert.True(queue.ShowEmpty);

        Assert.Equal(1, fixture.ActCount());
        Assert.Equal(
            children.Select(child => (child, primary)),
            await fixture.LiveLinksAsync());

        // And the queue stays empty across a reload: the proposals now resolve
        // to one work, so the grouper drops them.
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.Groups);
    }

    /// <summary>
    /// The complaint this stage answers. Under the pair model, answering one
    /// proposal left its neighbours blocked; a group has no neighbours to
    /// stale, because they were never separate cards.
    /// </summary>
    [Fact]
    public async Task Answering_a_member_cannot_stale_a_sibling_card()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        // A second, unrelated group, so "no card went stale" is a claim about a
        // queue that still has cards in it.
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, queue.Groups.Count);

        var survivor = queue.Groups.Single(g => g.IsPair);
        await queue.SameGameCommand.ExecuteAsync(queue.Groups.Single(g => !g.IsPair));

        // The other card is the same object, untouched and still answerable.
        Assert.Same(survivor, Assert.Single(queue.Groups));
        Assert.False(survivor.IsDecided);

        await queue.SameGameCommand.ExecuteAsync(survivor);
        Assert.Empty(queue.Groups);
        Assert.Equal(2, fixture.ActCount());
    }

    // ── Including none, some or all ──────────────────────────────────────────

    /// <summary>
    /// A rejection made inside a group must survive the answer. Without this it
    /// evaporates and the next sweep proposes the same pair again.
    /// </summary>
    [Fact]
    public async Task An_unchecked_member_records_a_rejection_for_its_edge()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Groups[0];

        var dropped = card.Others[0];
        var kept = card.Others[1];
        dropped.IsIncluded = false;

        await queue.SameGameCommand.ExecuteAsync(card);

        // Only the member the user kept was linked.
        Assert.Equal(
            [(kept.WorkId, card.Primary.WorkId)],
            await fixture.LiveLinksAsync());

        // Both edges touching the dropped member are answered "different
        // games"; the edge between the two included members is not, because
        // the link is what answers it.
        var statuses = await fixture.StatusesByEdgeAsync();
        Assert.Equal(
            MergeCandidateStatuses.Rejected,
            statuses[Edge(dropped.WorkId, card.Primary.WorkId)]);
        Assert.Equal(
            MergeCandidateStatuses.Rejected,
            statuses[Edge(dropped.WorkId, kept.WorkId)]);
        Assert.Equal(
            MergeCandidateStatuses.Pending,
            statuses[Edge(kept.WorkId, card.Primary.WorkId)]);
    }

    [Fact]
    public async Task Same_game_with_nothing_checked_links_nothing_and_records_every_edge()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Groups[0];
        foreach (var member in card.Others)
        {
            member.IsIncluded = false;
        }

        await queue.SameGameCommand.ExecuteAsync(card);

        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.Equal(0, fixture.ActCount());
        Assert.Equal(MergeCopy.NothingLinked, queue.ReportMessage);
        Assert.All(
            (await fixture.StatusesByEdgeAsync()).Values,
            status => Assert.Equal(MergeCandidateStatuses.Rejected, status));
    }

    [Fact]
    public async Task Different_games_rejects_every_proposal_in_the_group()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.DifferentGamesCommand.ExecuteAsync(queue.Groups[0]);

        Assert.Empty(queue.Groups);
        Assert.Empty(await fixture.LiveLinksAsync());
        Assert.All(
            (await fixture.StatusesByEdgeAsync()).Values,
            status => Assert.Equal(MergeCandidateStatuses.Rejected, status));
    }

    /// <summary>
    /// The Prey (2006) and Prey (2017) guard, end to end. Two members that each
    /// match a third without matching each other must not arrive as one game.
    /// </summary>
    [Fact]
    public async Task A_below_band_edge_arrives_unchecked()
    {
        using var fixture = new MergeQueueFixture();
        var (a, b, c) = await fixture.CreateTripleAsync();
        await fixture.QueueScoredPairAsync(a, b);
        await fixture.QueueScoredPairAsync(a, c);
        await fixture.QueueScoredPairAsync(b, c, SoftMatchBand.Review);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Groups);

        Assert.Equal(3, card.Members.Count);
        Assert.True(card.Primary.IsIncluded);

        // Exactly one of the two others clears the band against everything
        // already in, so exactly one arrives checked.
        Assert.Single(card.Others, m => m.IsIncluded);
        Assert.Single(card.Others, m => !m.IsIncluded);
    }

    [Fact]
    public async Task A_member_reachable_only_through_a_sibling_says_so()
    {
        using var fixture = new MergeQueueFixture();
        var (a, b, c) = await fixture.CreateTripleAsync();
        await fixture.QueueScoredPairAsync(a, b);
        await fixture.QueueScoredPairAsync(b, c);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Groups);

        var indirect = card.Others.Single(m => !m.IsIncluded);
        Assert.True(indirect.IsIndirect);
        Assert.False(indirect.HasDirectEvidence);
        Assert.NotEmpty(indirect.IndirectText);

        var direct = card.Others.Single(m => m.IsIncluded);
        Assert.False(direct.IsIndirect);
        Assert.True(direct.HasDirectEvidence);
    }

    // ── Choosing the title the library keeps ─────────────────────────────────

    [Fact]
    public async Task Every_card_names_the_reason_its_title_was_kept()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Groups);

        Assert.True(card.HasPrimaryReason);
        Assert.NotEmpty(card.PrimaryReasonText);
        Assert.Equal(MergeCopy.SurvivorReasonAddedFirst, card.PrimaryReasonText);
        Assert.True(card.Primary.IsPrimary);
        Assert.Equal(card.Primary.Side.Title, card.PrimaryTitle);
    }

    [Fact]
    public async Task Moving_the_radio_reports_the_user_as_the_reason_and_links_that_way()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Groups);

        var chosen = card.Others[0];
        var displaced = card.Primary;
        card.SetPrimary(chosen.WorkId);

        Assert.Equal(MergeSurvivorReason.ChosenByYou, card.PrimaryReason);
        Assert.Equal(MergeCopy.SurvivorReasonChosenByYou, card.PrimaryReasonText);
        Assert.Same(chosen, card.Primary);

        // The member that was primary stays in the group rather than dropping
        // out of it: it was included, and it still is.
        Assert.True(displaced.IsIncluded);
        Assert.Same(displaced, Assert.Single(card.Others));

        await queue.SameGameCommand.ExecuteAsync(card);
        Assert.Equal([(displaced.WorkId, chosen.WorkId)], await fixture.LiveLinksAsync());
    }

    [Fact]
    public async Task Choosing_a_title_outside_the_group_is_refused()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = Assert.Single(queue.Groups);
        var before = card.Primary.WorkId;

        Assert.Throws<ArgumentOutOfRangeException>(() => card.SetPrimary(9_999));
        Assert.Equal(before, card.Primary.WorkId);
    }

    // ── Nothing is destroyed, and nothing is terminal ────────────────────────

    /// <summary>
    /// The review path writes a link and destroys nothing. After migration
    /// 0019 there is no destructive path left to reach, so the assertion
    /// moved from "no merge was applied" to "the two statuses that made an
    /// answer terminal are not expressible at all": the merge_candidates
    /// CHECK constraint refuses them.
    /// </summary>
    [Fact]
    public async Task The_review_path_destroys_nothing_and_writes_no_terminal_answer()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);

        using var conn = fixture.Factory.Open();

        // Every work and every store entry is still there. A link is additive.
        Assert.Equal(3, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(3, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));

        // The destructive executor's tables are gone, not merely unused.
        Assert.Empty(conn.Query<string>(
            "SELECT name FROM sqlite_master "
            + "WHERE name IN ('merge_applications', 'merge_undo_rows');"));

        // And 'confirmed' and 'undone' cannot be written at all.
        var id = conn.ExecuteScalar<long>("SELECT MIN(id) FROM merge_candidates;");
        foreach (var status in new[] { "confirmed", "undone" })
        {
            Assert.Throws<SqliteException>(() => conn.Execute(
                "UPDATE merge_candidates SET status = @status WHERE id = @id;",
                new { status, id }));
        }
    }

    /// <summary>
    /// The user's fifth complaint: undo reported success and the pair then read
    /// as unmergeable. Four cycles must leave exactly what one leaves.
    /// </summary>
    [Fact]
    public async Task Link_retract_and_link_again_ends_where_linking_once_ends()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();

        IReadOnlyList<(long Child, long Parent)> afterFirst = [];
        for (var cycle = 0; cycle < 4; cycle++)
        {
            await queue.LoadCommand.ExecuteAsync(null);

            // The card comes back every time. Nothing is terminal.
            var card = Assert.Single(queue.Groups);
            Assert.False(card.IsDecided);

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

            Assert.True(queue.CanUndoReport);
            await queue.UndoReportCommand.ExecuteAsync(null);
            Assert.Empty(await fixture.LiveLinksAsync());
        }

        // One more link, and the state is the state a single link produced.
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);
        Assert.Equal(afterFirst, await fixture.LiveLinksAsync());
    }

    [Fact]
    public async Task Retracting_returns_the_group_to_the_queue_as_an_ordinary_pending_row()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);

        // Linked: the proposal is answered by the link's existence, and the
        // card is gone.
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.Groups);
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(id));

        await queue.ShowHistoryCommand.ExecuteAsync(null);
        var row = Assert.Single(queue.LinkHistory);
        Assert.True(row.CanUndo);

        await queue.UndoCommand.ExecuteAsync(row);

        Assert.Single(queue.Groups);
        Assert.Equal(MergeCandidateStatuses.Pending, await fixture.StatusOfAsync(id));
        Assert.Equal(MergeCopy.Undone, queue.ReportMessage);

        // The undone act LEAVES the list. It used to stay on it, stamped
        // RETRACTED, which the user chose against: what is on the log is what
        // is in force.
        Assert.Empty(queue.LinkHistory);
    }

    /// <summary>
    /// Retracting an act restores every child it displaced to the parent it had
    /// immediately before that act, driven from the screen rather than from the
    /// repository.
    /// </summary>
    [Fact]
    public async Task Retracting_a_regrouping_restores_each_member_to_its_previous_group()
    {
        using var fixture = new MergeQueueFixture();
        var (a, b, c) = await fixture.CreateTripleAsync();
        await fixture.QueueScoredPairAsync(a, b);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        // First act: b under a.
        var first = queue.Groups[0];
        first.SetPrimary(await fixture.WorkOfAsync(a.ReleaseId));
        await queue.SameGameCommand.ExecuteAsync(first);

        var workA = await fixture.WorkOfAsync(a.ReleaseId);
        var workB = await fixture.WorkOfAsync(b.ReleaseId);
        var workC = await fixture.WorkOfAsync(c.ReleaseId);
        Assert.Equal([(workB, workA)], await fixture.LiveLinksAsync());

        // Second act: a chosen as a child of c, which re-parents b inside the
        // same act so depth stays at one.
        await fixture.QueueScoredPairAsync(a, c);
        await queue.LoadCommand.ExecuteAsync(null);
        var second = queue.Groups[0];
        second.SetPrimary(workC);
        await queue.SameGameCommand.ExecuteAsync(second);

        Assert.Equal(
            [(workA, workC), (workB, workC)],
            await fixture.LiveLinksAsync());

        await queue.UndoReportCommand.ExecuteAsync(null);

        // One retraction puts every member the act moved back where it was.
        Assert.Equal([(workB, workA)], await fixture.LiveLinksAsync());
    }

    [Fact]
    public async Task A_proposal_that_is_already_one_game_never_reaches_the_screen()
    {
        using var fixture = new MergeQueueFixture();
        fixture.QueueAlreadyOneGamePair();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Empty(queue.Groups);
        Assert.True(queue.ShowEmpty);
    }

    [Fact]
    public async Task A_rejected_proposal_stays_rejected_when_the_resolver_runs_again()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.DifferentGamesCommand.ExecuteAsync(queue.Groups[0]);

        Assert.Equal(MergeCandidateStatuses.Rejected, await fixture.StatusOfAsync(id));

        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.Groups);
    }

    [Fact]
    public async Task Answering_twice_writes_only_the_first_answer()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        var card = queue.Groups[0];

        await queue.SameGameCommand.ExecuteAsync(card);
        await queue.DifferentGamesCommand.ExecuteAsync(card);

        Assert.Equal(1, fixture.ActCount());
        Assert.Single(await fixture.LiveLinksAsync());
    }

    // ── The answer path reads nothing ────────────────────────────────────────

    /// <summary>
    /// A previous build of this screen re-planned every remaining card on every
    /// answer and froze for about two seconds at 200 pending pairs, because
    /// Microsoft.Data.Sqlite completes synchronously and this is the surface
    /// worked down with repeated keypresses.
    ///
    /// <para>Groups are disjoint over resolved works, so an answer inside one
    /// cannot change another and there is nothing to re-plan. That is asserted
    /// by counting the reads rather than by timing them.</para>
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

        Assert.Equal(60, queue.Groups.Count);
        Assert.Equal(1, counting.PendingReads);

        var remaining = queue.Groups.Skip(20).ToList();
        for (var i = 0; i < 20; i++)
        {
            await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);
        }

        // Not one extra read of the queue, and not one status write: every
        // member was included, so there was no edge left outside the link.
        Assert.Equal(1, counting.PendingReads);
        Assert.Equal(0, counting.StatusWrites);

        // The cards that are left are the same objects they were: nothing was
        // rebuilt underneath the user.
        Assert.Equal(remaining, queue.Groups);
        Assert.Equal(20, fixture.ActCount());
    }

    // ── Selection ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Answering_moves_the_cursor_to_the_group_that_took_its_place()
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

        var second = queue.Groups[1];
        await queue.DifferentGamesCommand.ExecuteAsync(queue.Groups[0]);

        Assert.Same(second, queue.SelectedGroup);
        Assert.True(second.IsSelected);
    }

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
    }

    [Fact]
    public void Moving_selection_on_an_empty_queue_is_a_no_op()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.Equal(-1, queue.MoveSelection(1));
        Assert.Null(queue.SelectedGroup);
    }

    // ── The store each member comes from ─────────────────────────────────────

    /// <summary>
    /// The store is the fact that decides whether a pair is the Steam entry and
    /// the Epic entry of one game or two different games, so every member must
    /// carry it.
    /// </summary>
    [Fact]
    public async Task Each_member_carries_the_store_it_is_owned_on()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: "steam");
        var epic = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(queue.Groups);
        Assert.True(group.IsPair);

        var chips = group.Members.Select(m => Assert.Single(m.StoreChips)).OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(["EPIC", "STEAM"], chips);
        Assert.All(group.Members, m => Assert.True(m.HasStores));
    }

    /// <summary>
    /// A member owned on two stores wears a chip for each; the chip row is a
    /// list, not a single badge.
    /// </summary>
    [Fact]
    public async Task A_member_owned_twice_carries_both_stores()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: "steam");
        var epic = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: "epic");
        await fixture.AlsoOwnedOnAsync(steam, "gog");
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(queue.Groups);
        var twice = group.Members.Single(m => m.StoreChips.Count == 2);
        Assert.Equal(["STEAM", "GOG"], twice.StoreChips);
        Assert.Equal("Steam, GOG", twice.StoreNames);
    }

    /// <summary>
    /// Two members with one title are told apart by store in every automation
    /// name, which is what a screen reader reads. Without the store a column
    /// of radios all called "Keep Prey #1024" would be one target.
    /// </summary>
    [Fact]
    public async Task Automation_names_tell_two_same_titled_members_apart_by_store()
    {
        using var fixture = new MergeQueueFixture();
        var steam = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: "steam");
        var epic = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: "epic");
        await fixture.QueueScoredPairAsync(steam, epic);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(queue.Groups);
        var left = group.Primary;
        var right = Assert.Single(group.Others);

        Assert.Equal(left.Side.Title, right.Side.Title);
        Assert.NotEqual(left.PrimaryAutomationName, right.PrimaryAutomationName);
        Assert.NotEqual(left.IncludeAutomationName, right.IncludeAutomationName);

        var names = new[] { left.PrimaryAutomationName, right.PrimaryAutomationName };
        Assert.Contains(names, n => n.Contains("Steam", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("Epic", StringComparison.Ordinal));
    }

    /// <summary>
    /// No ownership row means no chip row and no store in the automation name.
    /// The label falls back to the store-less format so it contains no comma.
    /// </summary>
    [Fact]
    public async Task A_member_with_no_ownership_row_states_no_store()
    {
        using var fixture = new MergeQueueFixture();
        var left = await fixture.CreateReleaseAsync(new SeedSide("Prey", 2017, "Bethesda Softworks"), store: null);
        var right = await fixture.CreateReleaseAsync(new SeedSide("Prey", null, null), store: null);
        await fixture.QueueScoredPairAsync(left, right);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(queue.Groups);
        Assert.All(group.Members, m => Assert.False(m.HasStores));
        Assert.All(group.Members, m => Assert.Empty(m.StoreChips));
        Assert.All(group.Members, m => Assert.DoesNotContain(",", m.Label, StringComparison.Ordinal));
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
        var card = queue.Groups[0];
        var edge = Assert.Single(card.Edges);

        Assert.True(card.IsPair);
        Assert.Same(edge, Assert.Single(card.Others).Evidence);

        // The three diffs §6 names by hand.
        Assert.Equal("0.00", edge.TitleDistanceText);   // 1 - similarity
        Assert.Equal("Δ1", edge.YearDeltaText);
        Assert.Equal("SAME", edge.PublisherMatchText);
        Assert.Equal("0.87", edge.ScoreText);
        Assert.True(card.IsPriority);

        Assert.Equal(
            ["TITLE", "YEAR", "PUBLISHER", "COVER", "EDITION"],
            edge.Signals.Select(s => s.Label));

        var year = edge.Signals.Single(s => s.Label == "YEAR");
        Assert.True(year.Fired);
        Assert.Equal("Δ1", year.ValueText);
        Assert.Contains("2015 vs 2016", year.Detail, StringComparison.Ordinal);

        // One side is a content bundle. The verdict is the evidence; the signed
        // points the row used to print were the scorer's arithmetic and nobody
        // on this screen tunes weights.
        var edition = edge.Signals.Single(s => s.Label == "EDITION");
        Assert.Equal("DIFFERENT", edition.ValueText);
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
        var card = queue.Groups[0];
        var edge = Assert.Single(card.Edges);

        Assert.Equal("0.65", edge.ScoreText);
        Assert.False(card.IsPriority);
        Assert.Equal("—", edge.YearDeltaText);
        Assert.Equal("—", edge.PublisherMatchText);

        var year = edge.Signals.Single(s => s.Label == "YEAR");
        Assert.False(year.Fired);
        Assert.Equal("—", year.ValueText);

        var publisher = edge.Signals.Single(s => s.Label == "PUBLISHER");
        Assert.False(publisher.Fired);
        Assert.Equal("—", publisher.ValueText);

        // Both members still name themselves. The entry numbers that used to
        // tell two identically titled records apart are database ids and are
        // gone from the screen (§10.5); the automation label still separates
        // them, now on the facts the row itself draws.
        Assert.All(card.Members, m => Assert.Equal("Prey", m.Side.Title));
        Assert.NotEqual(card.Primary.Label, Assert.Single(card.Others).Label);
        Assert.All(card.Members, m => Assert.DoesNotContain("#", m.Label, StringComparison.Ordinal));
    }

    /// <summary>
    /// A member is a work, so its face comes from the work row rather than from
    /// the matcher's frozen payload. That is what makes an unrecorded payload a
    /// card without a breakdown instead of a card without names.
    /// </summary>
    [Fact]
    public async Task A_proposal_with_no_recorded_payload_still_names_its_members()
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
        var card = queue.Groups[0];
        var edge = Assert.Single(card.Edges);

        Assert.False(edge.HasSignals);
        Assert.Equal("—", edge.TitleDistanceText);
        Assert.Equal("—", edge.YearDeltaText);
        Assert.All(card.Members, m => Assert.Equal("Bastion", m.Side.Title));
        Assert.Equal("2011", card.Primary.Side.YearText);
    }

    // ── The two surfaces ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_screen_opens_on_the_queue()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.IsReviewVisible);
        Assert.False(queue.IsHistoryVisible);
    }

    [Fact]
    public async Task The_history_surface_lists_the_link_act_and_names_what_it_grouped()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);

        await queue.ShowHistoryCommand.ExecuteAsync(null);

        var row = Assert.Single(queue.LinkHistory);
        Assert.Equal(2, row.ChildTitles.Count);
        Assert.Equal("Prey", row.ParentTitle);
        Assert.NotEqual("—", row.LinkedAtText);

        // The automation name identifies the group, not the verb.
        Assert.Contains(row.Description, row.UndoAutomationName, StringComparison.Ordinal);
    }

    // ── Empty state (§7) ─────────────────────────────────────────────────────

    [Fact]
    public void An_unloaded_queue_shows_neither_cards_nor_an_empty_state()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.False(queue.ShowEmpty);
        Assert.False(queue.HasPending);
        Assert.Empty(queue.Groups);
    }

    [Fact]
    public async Task An_empty_queue_before_any_sweep_says_the_comparison_has_not_run()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.False(queue.HasCompletedSweep);
        Assert.Equal(MergeCopy.EmptyNotSwept, queue.EmptyMessage);
    }

    [Fact]
    public async Task An_empty_queue_after_a_sweep_says_the_comparison_found_nothing()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.ResolveState.SetLastSoftMatchSweepAsync(DateTimeOffset.UtcNow);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.HasCompletedSweep);
        Assert.Equal(MergeCopy.EmptySwept, queue.EmptyMessage);
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
        Assert.Equal(MergeCopy.EmptyNotSwept, queue.EmptyMessage);
    }

    [Fact]
    public async Task Clearing_the_last_group_returns_the_queue_to_its_empty_state()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.True(queue.HasPending);
        Assert.Equal(1.0, queue.RowOpacity);

        await queue.DifferentGamesCommand.ExecuteAsync(queue.Groups[0]);

        Assert.True(queue.ShowEmpty);
        Assert.False(queue.HasPending);
        Assert.Null(queue.SelectedGroup);
    }

    [Fact]
    public async Task The_count_is_the_number_of_pending_groups()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        // Four pending rows, two cards.
        Assert.Equal(2, queue.PendingCount);
        Assert.Equal("2", queue.PendingCountText);
        Assert.True(queue.HasPending);
        Assert.False(queue.ShowEmpty);
    }

    // EXPANSIONS, the screen's third surface.
    //
    // The card is a different card from the same-game one, deliberately. A
    // same-game group is N peers of one game and its KEEP radio asks a real
    // question; an expansion group is one base plus N packs, and the parent is
    // fixed by the relation. There is no primary radio on this card and no
    // property on its member that could produce one.
    //
    // These tests hold the answer vocabulary apart too. REVIEW answers Same
    // game / Different games and this answers Group / Not expansions, which is
    // why they are two segments rather than two kinds of card in one scroll,
    // and why the history row says "grouped under" and never "linked under".

    /// <summary>
    /// The one-to-many relation presented once: one base game, both packs, one
    /// card. The user asked for exactly this instead of repeated pairwise
    /// operations.
    /// </summary>
    [Fact]
    public async Task A_base_game_and_its_packs_are_one_card()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Beyond the Sword", 2007, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.ExpansionGroups);
        Assert.Equal("Sid Meier's Civilization IV", card.BaseTitle);
        Assert.Equal(2, card.Members.Count);

        // Checked by default: an expansion proposal is one direct claim about
        // one pair, already corroborated, so there is no transitive closure to
        // guard against the way a same-game component has.
        Assert.All(card.Members, m => Assert.True(m.IsIncluded));

        // The evidence is what the title ADDS, not how far two titles are
        // apart, which is the fact the soft matcher cannot supply.
        Assert.Contains(card.Members, m => m.SuffixText == "beyond sword");
        Assert.Contains(card.Members, m => m.SuffixText == "warlords");

        // The queue opens on review; the expansion surface is a segment.
        Assert.True(queue.IsReviewVisible);
        Assert.False(queue.IsExpansionsVisible);
        queue.ShowExpansionsCommand.Execute(null);
        Assert.True(queue.IsExpansionsVisible);
        Assert.False(queue.IsReviewVisible);
    }

    /// <summary>
    /// None, some or all, in one gesture. The checked pack is grouped, the
    /// unchecked one is recorded as a separate game, and neither answer comes
    /// back on the next scan.
    /// </summary>
    [Fact]
    public async Task Taking_some_groups_the_checked_and_records_the_rest()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Beyond the Sword", 2007, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.ExpansionGroups);
        var dropped = card.Members.Single(m => m.SuffixText == "warlords");
        var kept = card.Members.Single(m => m.SuffixText == "beyond sword");
        dropped.IsIncluded = false;

        await queue.GroupExpansionsCommand.ExecuteAsync(card);

        // One link, at the expansion kind, and nothing at the same-game kind.
        var resolution = await fixture.Links.GetResolutionAsync();
        Assert.Equal(card.BaseWorkId, resolution.Expansions.BaseOf(kept.WorkId));
        Assert.Null(resolution.Expansions.BaseOf(dropped.WorkId));
        Assert.True(resolution.SameGame.IsEmpty);

        // The unchecked one is an answer, not a card that returns.
        var refusal = Assert.Single(await fixture.ExpansionRefusals.GetAllAsync());
        Assert.Equal(dropped.WorkId, refusal.ChildWorkId);

        Assert.Empty(queue.ExpansionGroups);
        Assert.NotNull(queue.ReportUndoActId);

        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.ExpansionGroups);
    }

    /// <summary>
    /// Taking none is the same answer as "not expansions", and it is recorded
    /// the same way: nothing linked, every pack written down.
    /// </summary>
    [Fact]
    public async Task Taking_none_links_nothing_and_records_every_pack()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.ExpansionGroups);
        card.Members[0].IsIncluded = false;

        await queue.GroupExpansionsCommand.ExecuteAsync(card);

        Assert.True((await fixture.Links.GetResolutionAsync()).Expansions.IsEmpty);
        Assert.Single(await fixture.ExpansionRefusals.GetAllAsync());
        Assert.Null(queue.ReportUndoActId);
        Assert.Empty(queue.ExpansionGroups);
    }

    /// <summary>The negative answer, from its own button.</summary>
    [Fact]
    public async Task Not_expansions_records_every_pack_and_links_nothing()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Beyond the Sword", 2007, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.NotExpansionsCommand.ExecuteAsync(Assert.Single(queue.ExpansionGroups));

        Assert.True((await fixture.Links.GetResolutionAsync()).Expansions.IsEmpty);
        Assert.Equal(2, (await fixture.ExpansionRefusals.GetAllAsync()).Count);
        Assert.Empty(queue.ExpansionGroups);
    }

    /// <summary>
    /// A grouping and a same-game link are different facts, so the history row
    /// says which one it recorded. A row that read the same for both would
    /// invite the user to retract the wrong one.
    /// </summary>
    [Fact]
    public async Task The_history_row_says_grouped_rather_than_linked()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.GroupExpansionsCommand.ExecuteAsync(Assert.Single(queue.ExpansionGroups));

        await queue.ShowHistoryCommand.ExecuteAsync(null);

        var row = Assert.Single(queue.LinkHistory);
        Assert.True(row.IsExpansionAct);
        Assert.Contains("grouped under", row.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("linked under", row.Description, StringComparison.Ordinal);
        Assert.True(row.CanUndo);

        // Retracting is ordinary: the proposal comes back.
        await queue.UndoCommand.ExecuteAsync(row);
        Assert.Single(queue.ExpansionGroups);
    }

    // ── The expansion surface answers the card the user is looking at ────────

    // The expansion surface had no selection input: SelectedExpansionGroup was
    // whatever the last load set (the first card), so G wrote a link for a card
    // the user was not looking at. MoveExpansionSelection is what the window's
    // Up/Down calls, and the shortcut answers SelectedExpansionGroup, never a
    // list position. Same defect class as the S/D fix on the review queue (TASK-66).

    [Fact]
    public async Task A_shortcut_answers_the_selected_expansion_card_not_the_first()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"));
        await fixture.CreateReleaseAsync(
            new SeedSide("The Witcher 3: Wild Hunt - Blood and Wine", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        queue.ShowExpansionsCommand.Execute(null);

        Assert.Equal(2, queue.ExpansionGroups.Count);
        var first = queue.ExpansionGroups[0];
        var second = queue.ExpansionGroups[1];

        // What the view does when focus or a pointer lands on the second card.
        queue.SelectExpansion(second);
        Assert.Same(second, queue.SelectedExpansionGroup);
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);

        // What OnMergeQueueKeyDown does on G.
        await queue.GroupExpansionsCommand.ExecuteAsync(queue.SelectedExpansionGroup);

        var resolution = await fixture.Links.GetResolutionAsync();
        foreach (var member in second.Members)
        {
            Assert.Equal(second.BaseWorkId, resolution.Expansions.BaseOf(member.WorkId));
        }

        // The card the user was NOT looking at is untouched and still on screen.
        foreach (var member in first.Members)
        {
            Assert.Null(resolution.Expansions.BaseOf(member.WorkId));
        }

        Assert.Same(first, Assert.Single(queue.ExpansionGroups));
    }

    [Fact]
    public async Task Expansion_selection_moves_by_card_and_clamps_at_the_ends()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"));
        await fixture.CreateReleaseAsync(
            new SeedSide("The Witcher 3: Wild Hunt - Blood and Wine", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(1, queue.MoveExpansionSelection(1));
        Assert.Equal(1, queue.MoveExpansionSelection(1));
        Assert.Equal(0, queue.MoveExpansionSelection(-1));
        Assert.Equal(0, queue.MoveExpansionSelection(-1));
        Assert.Same(queue.ExpansionGroups[0], queue.SelectedExpansionGroup);
    }

    [Fact]
    public void Moving_expansion_selection_on_an_empty_surface_is_a_no_op()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();

        Assert.Equal(-1, queue.MoveExpansionSelection(1));
        Assert.Null(queue.SelectedExpansionGroup);
    }

    // ── The outcome report belongs to the surface that raised it ─────────────

    // The report belongs to the surface that raised it, is dropped on a segment
    // switch and on a reload, and a retraction's own outcome survives the reload
    // it triggers.

    [Fact]
    public async Task A_review_report_does_not_render_on_another_surface()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);

        Assert.True(queue.HasReviewReport);
        Assert.False(queue.HasExpansionsReport);
        Assert.False(queue.HasHistoryReport);
        Assert.True(queue.CanUndoReport);

        // Leaving the surface takes its report with it: the Undo button on that
        // note belongs to an act the next surface did not perform.
        queue.ShowExpansionsCommand.Execute(null);
        Assert.False(queue.HasReport);
        Assert.False(queue.HasReviewReport);
        Assert.False(queue.HasExpansionsReport);
        Assert.False(queue.CanUndoReport);
    }

    [Fact]
    public async Task An_expansion_report_does_not_render_on_another_surface()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        queue.ShowExpansionsCommand.Execute(null);

        await queue.GroupExpansionsCommand.ExecuteAsync(Assert.Single(queue.ExpansionGroups));

        Assert.True(queue.HasExpansionsReport);
        Assert.False(queue.HasReviewReport);
        Assert.False(queue.HasHistoryReport);

        queue.ShowReviewCommand.Execute(null);
        Assert.False(queue.HasReport);
    }

    [Fact]
    public async Task A_reload_clears_the_report()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.GroupExpansionsCommand.ExecuteAsync(Assert.Single(queue.ExpansionGroups));
        Assert.True(queue.HasReport);

        await queue.LoadCommand.ExecuteAsync(null);

        Assert.False(queue.HasReport);
        Assert.Null(queue.ReportMessage);
        Assert.False(queue.CanUndoReport);
    }

    [Fact]
    public async Task Retracting_from_history_reports_on_the_history_surface()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.GroupExpansionsCommand.ExecuteAsync(Assert.Single(queue.ExpansionGroups));
        await queue.ShowHistoryCommand.ExecuteAsync(null);

        // Arriving at HISTORY cleared the expansion surface's report.
        Assert.False(queue.HasReport);

        await queue.UndoCommand.ExecuteAsync(Assert.Single(queue.LinkHistory));

        // The reload inside the retraction must not eat the retraction's own
        // outcome line, and the line belongs to HISTORY.
        Assert.True(queue.HasHistoryReport);
        Assert.False(queue.HasReviewReport);
        Assert.False(queue.HasExpansionsReport);
    }

    // ── One card layout, at every member count ───────────────────────────────

    // The card used to hold two Grids switched on IsPair and two member
    // templates, one of which served as both a pair side and a roster column:
    // two designs in one card. There is now one arrangement — the primary's
    // capsule on the left, every other member a row on the right — and what
    // varies with the count is inside the row.

    /// <summary>
    /// Two members: the one child draws its cover at 200x300 with the whole diff
    /// open and NO include checkbox, because the two answer buttons already carry
    /// include and exclude (TASK-70.3).
    /// </summary>
    [Fact]
    public async Task A_two_member_card_draws_its_child_at_full_size_with_no_checkbox()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);
        Assert.Equal(2, card.Members.Count);

        var child = Assert.Single(card.Others);
        Assert.True(child.IsSoleChild);

        // §6's two covers side by side at 200x300, kept literally.
        Assert.Equal(MergeQueueViewModel.CoverWidth, card.Primary.CoverWidth);
        Assert.Equal(MergeQueueViewModel.CoverWidth, child.CoverWidth);
        Assert.Equal(300, child.CoverHeight);

        // The full diff, open, with no disclosure and no condensed line.
        Assert.True(child.ShowFullEvidence);
        Assert.False(child.ShowEvidenceDisclosure);
        Assert.False(child.ShowCondensedEvidence);

        // The include control means something now, and its meaning is "not at
        // two members".
        Assert.False(child.ShowIncludeControl);
        Assert.False(card.Primary.ShowIncludeControl);
        Assert.True(child.IsIncluded);
        Assert.Single(card.IncludedChildWorkIds);
    }

    /// <summary>
    /// Moving the primary on a two-member card moves which member is the full
    /// size row. Nothing about the card's outer geometry changes.
    /// </summary>
    [Fact]
    public async Task Moving_the_primary_at_two_members_moves_the_full_size_row()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);
        var wasPrimary = card.Primary;
        var wasChild = Assert.Single(card.Others);

        card.SetPrimary(wasChild.WorkId);

        Assert.Same(wasChild, card.Primary);
        Assert.Same(wasPrimary, Assert.Single(card.Others));
        Assert.True(wasPrimary.IsSoleChild);
        Assert.False(wasChild.IsSoleChild);
        Assert.False(wasPrimary.ShowIncludeControl);
        Assert.True(wasPrimary.IsIncluded);
        Assert.Equal(MergeQueueViewModel.CoverWidth, wasPrimary.CoverWidth);
    }

    /// <summary>
    /// Three members: every child is a chip with a condensed line, a disclosure
    /// and a checkbox. The primary keeps its capsule, so the card's outer
    /// geometry is the same one the two-member card draws.
    /// </summary>
    [Fact]
    public async Task A_three_member_card_makes_every_child_a_chip_with_an_include_control()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);
        Assert.Equal(3, card.Members.Count);
        Assert.Equal(2, card.Others.Count);

        Assert.Equal(MergeQueueViewModel.CoverWidth, card.Primary.CoverWidth);
        Assert.False(card.Primary.ShowIncludeControl);

        Assert.All(card.Others, m =>
        {
            Assert.False(m.IsSoleChild);
            Assert.True(m.ShowIncludeControl);
            Assert.Equal(MergeGroupMemberViewModel.ChipWidth, m.CoverWidth);
            Assert.Equal(96, m.CoverHeight);
            Assert.True(m.ShowCondensedEvidence);
            Assert.False(m.ShowFullEvidence);
        });
    }

    /// <summary>
    /// Six members: the same arrangement, five rows, and six names a screen
    /// reader can tell apart without a database id between them.
    /// </summary>
    [Fact]
    public async Task A_six_member_card_draws_five_rows_and_six_distinct_names()
    {
        using var fixture = new MergeQueueFixture();

        var seeded = new List<SeededRelease>();
        for (var i = 0; i < 6; i++)
        {
            seeded.Add(await fixture.CreateReleaseAsync(
                new SeedSide("Prey", 2017, "Bethesda Softworks")));
        }

        for (var i = 1; i < seeded.Count; i++)
        {
            await fixture.QueueScoredPairAsync(seeded[i - 1], seeded[i]);
        }

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);
        Assert.Equal(6, card.Members.Count);
        Assert.Equal(5, card.Others.Count);
        Assert.Equal("6", card.MemberCountText);

        Assert.Equal(MergeQueueViewModel.CoverWidth, card.Primary.CoverWidth);
        Assert.All(card.Others, m => Assert.Equal(MergeGroupMemberViewModel.ChipWidth, m.CoverWidth));

        Assert.Equal(6, card.Members.Select(m => m.Label).Distinct().Count());
        Assert.All(card.Members, m => Assert.DoesNotContain("#", m.Label, StringComparison.Ordinal));
    }

    /// <summary>
    /// The cover request follows the geometry: the primary and a two-member
    /// card's one child ask for the capsule's width; a roster chip asks for a
    /// third of it. Nothing decodes a 600x900 source for a 64px chip.
    /// </summary>
    [Fact]
    public async Task Every_member_asks_for_the_cover_at_the_size_it_draws()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueueTripleAsync();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);
        Assert.Equal(
            MergeQueueViewModel.CoverWidth,
            card.Primary.CoverWidth);
        Assert.All(
            card.Others,
            m => Assert.Equal(MergeGroupMemberViewModel.ChipWidth, m.CoverWidth));
    }

    // ── Evidence: the figure stays, the arithmetic goes ──────────────────────

    /// <summary>
    /// Confidence survives the pass — it is what sorts the queue — and so does
    /// the matcher's band, under a name that says what it is. The signed
    /// contribution points and the per-row score restatement do not.
    /// </summary>
    [Fact]
    public async Task The_card_states_its_confidence_and_the_matchers_band()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = Assert.Single(queue.Groups);

        Assert.Equal("0.87", card.ScoreText);
        Assert.Equal(MergeCopy.ConfidenceLabel, card.ConfidenceLabel);

        // The band, not a queue position: several cards can carry it at once.
        Assert.True(card.IsPriority);
        Assert.Equal(MergeCopy.PriorityBandLabel, card.PriorityBandLabel);
        Assert.DoesNotContain(
            "QUEUE", card.PriorityBandLabel, StringComparison.OrdinalIgnoreCase);
    }

    // ── History is one chronological log of what is in force ─────────────────

    /// <summary>
    /// Newest first, both relations in one list, and an act that has been undone
    /// is off the list rather than on it struck through. That reverses the
    /// TASK-70.8 line about a retracted row staying on screen with the date it
    /// was reversed: the user chose the log that shows what stands.
    /// </summary>
    [Fact]
    public async Task The_history_log_is_newest_first_and_holds_only_what_stands()
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

        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);
        await queue.SameGameCommand.ExecuteAsync(queue.Groups[0]);

        await queue.ShowHistoryCommand.ExecuteAsync(null);

        Assert.Equal(2, queue.LinkHistory.Count);
        Assert.True(
            queue.LinkHistory[0].ActId > queue.LinkHistory[1].ActId,
            "The log is not newest first.");

        var newest = queue.LinkHistory[0];
        await queue.UndoCommand.ExecuteAsync(newest);

        var remaining = Assert.Single(queue.LinkHistory);
        Assert.NotEqual(newest.ActId, remaining.ActId);
        Assert.True(remaining.CanUndo);
    }

    // ── The expansion row says what the relation is ──────────────────────────

    /// <summary>
    /// The row reads the storefront's own word for the relation rather than the
    /// link kind, which is what stops a playtest reading as an expansion. Three
    /// kinds exist; the vocabulary is open.
    /// </summary>
    [Fact]
    public void An_expansion_row_states_the_relation_in_the_stores_own_word()
    {
        var side = new MergeSideViewModel(1, "Prey Playtest", 2017, "Bethesda Softworks");
        var evidence = new ExpansionEvidence("prey", "playtest", true, 0, false);

        var named = new ExpansionMemberViewModel(1, side, evidence, RelationLabels.Playtest);
        Assert.True(named.HasRelation);
        Assert.Equal("PLAYTEST", named.RelationText);

        var standaloneExpansion = new ExpansionMemberViewModel(
            1, side, evidence, RelationLabels.StandaloneExpansion);
        Assert.Equal("STANDALONE EXPANSION", standaloneExpansion.RelationText);

        // Nothing named it: the row draws no relation word rather than guessing.
        var unnamed = new ExpansionMemberViewModel(1, side, evidence);
        Assert.False(unnamed.HasRelation);
        Assert.Equal(string.Empty, unnamed.RelationText);
    }

    // ── The rail counts both surfaces ────────────────────────────────────────

    // The rail counts review plus expansions and recedes only when both are empty.

    [Fact]
    public async Task The_rail_counts_expansion_work_with_an_empty_review_queue()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.CreateReleaseAsync(new SeedSide("Sid Meier's Civilization IV", 2005, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("Sid Meier's Civilization IV: Warlords", 2006, "2K"));
        await fixture.CreateReleaseAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"));
        await fixture.CreateReleaseAsync(
            new SeedSide("The Witcher 3: Wild Hunt - Blood and Wine", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(2, queue.ExpansionCount);
        Assert.Equal(2, queue.OutstandingCount);
        Assert.Equal("2", queue.OutstandingCountText);
        Assert.True(queue.HasOutstanding);
        Assert.Equal(1.0, queue.RowOpacity);
    }

    [Fact]
    public async Task The_rail_recedes_only_when_both_surfaces_are_empty()
    {
        using var fixture = new MergeQueueFixture();
        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Equal(0, queue.OutstandingCount);
        Assert.False(queue.HasOutstanding);
        Assert.Equal(0.4, queue.RowOpacity);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private static (long, long) Edge(long a, long b) => a < b ? (a, b) : (b, a);

    /// <summary>A release as a store feed would describe it, before it is scored.</summary>
    private sealed record SeedSide(string Title, int? Year, string? Publisher);

    /// <summary>A seeded release: the row id plus the metadata it was seeded with.</summary>
    private sealed record SeededRelease(long ReleaseId, SeedSide Side);

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
        }

        /// <summary>For the assertions that have to look at the database itself.</summary>
        public SqliteConnectionFactory Factory => _db.Factory;

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public IResolveStateRepository ResolveState { get; }

        public IIdentityLinkRepository Links { get; }

        public IOwnershipRepository Ownership { get; }

        public IExpansionRefusalRepository ExpansionRefusals { get; }

        public CountingCandidateRepository CountingCandidates() => new(Candidates);

        /// <summary>No cover cache: the queue must compose on procedural art alone.</summary>
        public MergeQueueViewModel CreateViewModel(
            bool withResolveState = true, IMergeCandidateRepository? candidates = null)
            => new(
                candidates ?? Candidates, Releases, Works, Links, Ownership,
                new LibraryExpansionScan(Releases, Links, ExpansionRefusals),
                ExpansionRefusals,
                null,
                withResolveState ? ResolveState : null);

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

        /// <summary>One game as three stores list it: three works, three entries.</summary>
        public async Task<(SeededRelease A, SeededRelease B, SeededRelease C)> CreateTripleAsync()
        {
            var side = new SeedSide("Prey", 2017, "Bethesda Softworks");
            return (
                await CreateReleaseAsync(side),
                await CreateReleaseAsync(side),
                await CreateReleaseAsync(side));
        }

        /// <summary>The three pairwise proposals a sweep would write for a triple.</summary>
        public async Task QueueTripleAsync()
        {
            var (a, b, c) = await CreateTripleAsync();
            await QueueScoredPairAsync(a, b);
            await QueueScoredPairAsync(a, c);
            await QueueScoredPairAsync(b, c);
        }

        /// <summary>Creates both releases and queues them, exactly as the resolver would.</summary>
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
        /// realistic metadata and which decides what arrives checked.
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

        /// <summary>
        /// A pending pair whose two releases already sit under one work, in two
        /// different editions. The question it asks has been answered, so the
        /// grouper must drop it rather than render a card nothing can act on.
        /// </summary>
        public long QueueAlreadyOneGamePair()
        {
            using var conn = _db.Factory.Open();

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Prey') RETURNING id;");

            var left = conn.ExecuteScalar<long>("""
                INSERT INTO releases (work_id, name, platform)
                VALUES (@workId, 'Prey', 'windows') RETURNING id;
                """, new { workId });

            var right = conn.ExecuteScalar<long>("""
                INSERT INTO releases (work_id, name, platform, edition_note)
                VALUES (@workId, 'Prey', 'windows', 'Gold Edition') RETURNING id;
                """, new { workId });

            var id = conn.ExecuteScalar<long>("""
                INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
                VALUES (@left, @right, 0.9, 'pending') RETURNING id;
                """, new { left, right });

            _pairs[id] = (left, right);
            return id;
        }

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

        /// <summary>Every proposal's status, keyed by the two works it joins.</summary>
        public async Task<Dictionary<(long, long), string>> StatusesByEdgeAsync()
        {
            var statuses = new Dictionary<(long, long), string>();
            foreach (var (id, pair) in _pairs)
            {
                var row = await Candidates.GetAsync(id);
                if (row is null)
                {
                    continue;
                }

                statuses[Edge(await WorkOfAsync(pair.Left), await WorkOfAsync(pair.Right))] =
                    row.Status;
            }

            return statuses;
        }

        public async Task<long> WorkOfAsync(long releaseId)
            => (await Releases.GetAsync(releaseId))!.WorkId;

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

        public async Task<SeededRelease> CreateReleaseAsync(
            SeedSide side, string platform = "windows", string? store = "steam")
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
                    releaseId, store, null, null, null, null));
            }

            return new SeededRelease(releaseId, side);
        }

        public void Dispose() => _db.Dispose();
    }
}
