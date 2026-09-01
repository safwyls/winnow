using System.Globalization;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Merging;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

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

    /// <summary>
    /// The correction this screen exists for. Answering used to write a status
    /// and leave the merge to a second control further down the page; it now
    /// carries the merge out where it stands, and the report says what the
    /// engine actually did rather than what was asked for.
    /// </summary>
    [Fact]
    public async Task Same_game_applies_the_merge_then_and_there_and_reports_it()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.SameGameCommand.ExecuteAsync(queue.Candidates[0]);

        Assert.Empty(queue.Candidates);
        Assert.True(queue.HasReport);
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.AppliedReportFormat, "Prey", "Prey", MergeCopy.ModeReleaseCollapse),
            queue.ReportMessage);

        // The library really moved: two works and two releases became one each.
        using var conn = fixture.Factory.Open();
        Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));
        Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));

        // Undo is reachable from the report without the history surface having
        // been read, which is what makes answering-applies safe.
        Assert.True(queue.CanUndoReport);

        // And the merge is on the history surface with its undo intact. Read
        // by going there: the answer path deliberately does not rebuild a list
        // that is not on screen, and arriving recomputes it.
        await queue.ShowHistoryCommand.ExecuteAsync(null);
        Assert.Single(queue.History);
        Assert.True(queue.History[0].CanUndo);
    }

    /// <summary>
    /// Undo is what makes "confirming applies" safe, so it has to be reachable
    /// from the report rather than only from the other surface.
    /// </summary>
    [Fact]
    public async Task The_report_offers_the_undo_that_makes_answering_safe()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.SameGameCommand.ExecuteAsync(queue.Candidates[0]);

        Assert.True(queue.CanUndoReport);

        await queue.UndoReportCommand.ExecuteAsync(null);

        Assert.False(queue.CanUndoReport);
        Assert.StartsWith("Prey", queue.ReportMessage, StringComparison.Ordinal);
        Assert.True(queue.History[0].IsUndone);

        using var conn = fixture.Factory.Open();
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));
    }

    [Fact]
    public async Task Different_games_writes_rejected_and_applies_nothing()
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

        // The other answer merges on the press; this one must not, and must not
        // report a merge either.
        Assert.False(queue.HasReport);
        Assert.Empty(queue.History);

        using var conn = fixture.Factory.Open();
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));
        Assert.Equal(0, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));
    }

    // ── The outcome stated before the answer ─────────────────────────────────

    /// <summary>
    /// There is no second step to catch a wrong outcome, so the card has to name
    /// the surviving identity and what becomes of the two store entries before
    /// the button is pressed.
    /// </summary>
    [Fact]
    public async Task The_card_names_the_survivor_and_the_mode_before_the_answer()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = queue.Candidates[0];
        Assert.True(card.HasPreview);
        var preview = card.Preview!;

        Assert.False(preview.IsBlocked);
        Assert.False(card.IsPreviewBlocked);
        Assert.Equal(MergeCopy.OutcomeLabel, preview.Label);
        Assert.Equal(MergeMode.ReleaseCollapse, preview.Mode);

        // The lowest id wins every tie in ChooseWork/ChooseRelease, so the left
        // side survives and the sentence names it.
        Assert.Equal("Prey", preview.SurvivingTitle);
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentCulture, MergeCopy.PreviewSurvivorFormat, "Prey", "Prey"),
            preview.SurvivorLine);

        // The second line is the one that says whether two rows become one.
        Assert.Equal(MergeCopy.PreviewCollapse, preview.EffectLine);
        Assert.Equal(MergeCopy.SameGameTooltip, preview.SameGameTooltip);
    }

    [Fact]
    public async Task A_work_only_outcome_says_both_entries_stay_and_why()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("The Witcher 3: Wild Hunt", 2015, "CD PROJEKT RED"),
            new SeedSide("The Witcher 3: Wild Hunt - Game of the Year Edition", 2016, "CD PROJEKT RED"));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var preview = queue.Candidates[0].Preview!;

        Assert.False(preview.IsBlocked);
        Assert.Equal(MergeMode.WorkOnly, preview.Mode);
        Assert.Equal(MergeBlocker.DistinctEditions, preview.Blocker);
        Assert.Equal(MergeCopy.PreviewWorkOnlyDistinctEditions, preview.EffectLine);
    }

    /// <summary>
    /// The sharpest case on the screen. A pair whose plan can do nothing states
    /// that above the answers, and the answer does exactly what the card says:
    /// it closes the question and writes nothing to the library.
    ///
    /// <para>Both answers stay live on purpose. Disabling them would strand the
    /// pair in the queue with no honest way out — "Different games" would record
    /// a rejection that is false, because the library already holds the two
    /// entries under one game.</para>
    /// </summary>
    [Fact]
    public async Task A_blocked_pair_states_the_block_before_the_answer_and_answering_writes_nothing()
    {
        using var fixture = new MergeQueueFixture();
        var id = fixture.QueueAlreadyOneGamePair();

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        var card = queue.Candidates[0];
        var preview = card.Preview!;

        Assert.True(preview.IsBlocked);
        Assert.True(card.IsPreviewBlocked);
        Assert.Equal(MergeCopy.BlockedLabel, preview.Label);
        Assert.Equal(MergeCopy.PreviewBlockedAlreadyOneGame, preview.SurvivorLine);

        // The promise the button is held to.
        Assert.Equal(MergeCopy.PreviewBlockedAnswerEffect, preview.EffectLine);
        Assert.Equal(MergeCopy.SameGameBlockedTooltip, preview.SameGameTooltip);
        // The announcement names both sides and says nothing is merged, so two
        // blocked cards are not one indistinguishable target (§8).
        Assert.Contains(preview.EffectLine, card.SameGameAutomationName, StringComparison.Ordinal);
        Assert.Contains(card.Left.ReleaseText, card.SameGameAutomationName, StringComparison.Ordinal);
        Assert.Contains(card.Right.ReleaseText, card.SameGameAutomationName, StringComparison.Ordinal);

        await queue.SameGameCommand.ExecuteAsync(card);

        // The question is closed…
        Assert.Empty(queue.Candidates);
        Assert.Equal(MergeCandidateStatuses.Confirmed, await fixture.StatusOfAsync(id));

        // …and nothing was written, which the report says in the same words the
        // card used.
        Assert.Equal(
            string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.AppliedNothingFormat, MergeCopy.RefusedDistinctEditions),
            queue.ReportMessage);
        Assert.False(queue.CanUndoReport);
        Assert.Empty(queue.History);

        using var conn = fixture.Factory.Open();
        Assert.Equal(0, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));
    }

    /// <summary>
    /// A merge repoints the other pairs that named the absorbed release, so a
    /// card left on screen can be a card about a different pair than the one it
    /// was built for. Its outcome block is restated from the database rather
    /// than left holding the promise it was built with — the one failure this
    /// screen cannot have is a card that says one thing and does another.
    /// </summary>
    [Fact]
    public async Task Answering_one_pair_restates_the_cards_still_on_screen()
    {
        using var fixture = new MergeQueueFixture();

        var (a, b) = await fixture.CreatePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));
        var c = await fixture.CreateReleaseAsync(new SeedSide("Prey", null, null));

        await fixture.QueueScoredPairAsync(a, b);
        var chained = await fixture.QueueScoredPairAsync(b, c);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, queue.PendingCount);

        var survivor = queue.Candidates.Single(card => card.Id == chained);
        var answered = queue.Candidates.Single(card => card.Id != chained);

        await queue.SameGameCommand.ExecuteAsync(answered);

        // The pair that was (b, c) is now (a, c): b's release was absorbed.
        var card = Assert.Single(queue.Candidates);
        Assert.Same(survivor, card);

        var preview = card.Preview!;
        Assert.NotNull(preview);

        // The survivor is named, not numbered. Looking the title up by the id
        // the card was built with would have found a release that no longer
        // exists and printed "release 2" here.
        Assert.Equal("Prey", preview.SurvivingTitle);
        Assert.DoesNotContain("release ", preview.SurvivorLine, StringComparison.Ordinal);
    }

    // ── The two surfaces ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_screen_opens_on_the_queue()
    {
        using var fixture = new MergeQueueFixture();
        await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.True(queue.IsReviewVisible);
        Assert.False(queue.IsHistoryVisible);
    }

    /// <summary>
    /// Reversibility depends on every merge applied after a given one, so a
    /// verdict computed at the last load is a claim about a database that may
    /// have moved. Arriving at the surface asks again.
    /// </summary>
    [Fact]
    public async Task Opening_the_history_surface_recomputes_it()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Empty(queue.History);

        // A merge applied by something other than this screen. What the screen
        // is holding is now a stale answer.
        await fixture.Candidates.SetStatusAsync(id, MergeCandidateStatuses.Confirmed);
        await fixture.Merges.ApplyAsync(id);
        Assert.Empty(queue.History);

        await queue.ShowHistoryCommand.ExecuteAsync(null);

        Assert.True(queue.IsHistoryVisible);
        Assert.False(queue.IsReviewVisible);
        Assert.Single(queue.History);
        Assert.True(queue.History[0].CanUndo);

        queue.ShowReviewCommand.Execute(null);
        Assert.True(queue.IsReviewVisible);
    }

    /// <summary>
    /// The upgrade path. A pair answered by the build where confirming and
    /// applying were two steps is already answered, so it does not belong in a
    /// queue that asks questions; it waits on the history surface, which is
    /// where every already-answered pair now lives.
    /// </summary>
    [Fact]
    public async Task Pairs_confirmed_under_the_previous_flow_wait_on_the_history_surface()
    {
        using var fixture = new MergeQueueFixture();
        var id = await fixture.QueuePairAsync(
            new SeedSide("Prey", 2017, "Bethesda Softworks"),
            new SeedSide("Prey", null, null));

        // The old flow's answer: confirmed, and nothing applied it.
        await fixture.Candidates.SetStatusAsync(id, MergeCandidateStatuses.Confirmed);

        var queue = fixture.CreateViewModel();
        await queue.LoadCommand.ExecuteAsync(null);

        Assert.Empty(queue.Candidates);
        Assert.True(queue.ShowEmpty);

        Assert.True(queue.HasOutstanding);
        Assert.Equal("1", queue.OutstandingCountText);

        var row = Assert.Single(queue.Outstanding);
        Assert.True(row.CanApply);
        Assert.Contains("Prey", row.SurvivorLine, StringComparison.Ordinal);

        await queue.ApplyCommand.ExecuteAsync(row);

        // Drained, and it does not come back: nothing this build does adds to
        // that list.
        Assert.False(queue.HasOutstanding);
        Assert.Single(queue.History);
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
            ["TITLE", "YEAR", "PUBLISHER", "COVER", "EDITION"],
            card.Signals.Select(s => s.Label));

        var year = card.Signals.Single(s => s.Label == "YEAR");
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

        var year = card.Signals.Single(s => s.Label == "YEAR");
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
            "Nothing to review yet. Still comparing your library for duplicates.",
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
            "Nothing to review. No ambiguous pairs found.",
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
            Merges = TestMergeExecutor.For(_db);
        }

        public Winnow.Resolve.MergeExecutor Merges { get; }

        /// <summary>For the assertions that have to look at the database itself.</summary>
        public SqliteConnectionFactory Factory => _db.Factory;

        public IWorkRepository Works { get; }

        public IReleaseRepository Releases { get; }

        public IMergeCandidateRepository Candidates { get; }

        public IResolveStateRepository ResolveState { get; }

        /// <summary>No cover cache: the queue must compose on procedural art alone.</summary>
        public MergeQueueViewModel CreateViewModel(bool withResolveState = true)
            => new(
                Candidates, Releases, Works, Merges,
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
        /// A pending pair whose two releases already sit under one work, in two
        /// different editions. That is the one shape a pending pair can take
        /// that the planner refuses outright: the sides are already one game, so
        /// nothing is left to unify, and the editions differ, so the two rows
        /// cannot collapse either.
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
        /// read that returns a row in any status — which is exactly the lookup
        /// the resolver uses to keep an answered pair out of the queue.
        /// </summary>
        public async Task<string?> StatusOfAsync(long candidateId)
        {
            var (left, right) = _pairs[candidateId];
            return (await Candidates.FindByPairAsync(left, right))?.Status;
        }

        private readonly Dictionary<long, (long Left, long Right)> _pairs = [];

        public async Task<SeededRelease> CreateReleaseAsync(SeedSide side)
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
