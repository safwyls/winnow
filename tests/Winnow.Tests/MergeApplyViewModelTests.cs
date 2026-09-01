using Dapper;
using Winnow.App.ViewModels;
using Winnow.Core.Merging;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Same Game screen's other two halves: applying a confirmed pair and
/// the history with its undo.
///
/// <para>These run against a real migrated SQLite file, the real repositories
/// and the real <c>MergeExecutor</c>, because the facts worth pinning are facts
/// about what the engine reports and what the screen then says. The one thing
/// that must never be true is a control claiming more than it does, so every
/// assertion here is on a string the user would read or on an enabled state
/// they would act on.</para>
/// </summary>
public class MergeApplyViewModelTests
{
    // ── The preview ──────────────────────────────────────────────────────────

    [Fact]
    public async Task The_preview_names_the_surviving_identity_and_the_mode()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(queue.Outstanding);

        // The lower id wins every tie in ChooseWork/ChooseRelease, so the left
        // side survives and the sentence must say so by name.
        Assert.Equal("Hades", row.SurvivingTitle);
        Assert.Equal("Hades (Epic)", row.AbsorbedTitle);
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                MergeCopy.SurvivorLineFormat, "Hades", "Hades (Epic)"),
            row.SurvivorLine);

        Assert.Equal(MergeMode.ReleaseCollapse, row.Mode);
        Assert.Equal(MergeCopy.ModeReleaseCollapse, row.ModeText);
        Assert.Equal(MergeCopy.ApplySectionLabel, row.SectionLabel);
        Assert.True(row.CanApply);
        Assert.False(row.HasLimitation);
        Assert.False(row.IsBlocked);
    }

    /// <summary>
    /// A collapse the data refuses is not a refusal of the merge: the two works
    /// still become one and the two store entries stay two rows. The preview has
    /// to say which of those two things is happening.
    /// </summary>
    [Fact]
    public async Task A_collapse_limited_to_the_work_layer_says_why_it_stays_two_entries()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, "Prey", "Prey (2006)");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE releases SET edition_note = 'Gold Edition' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(queue.Outstanding);

        Assert.Equal(MergeMode.WorkOnly, row.Mode);
        Assert.Equal(MergeCopy.ModeWorkOnly, row.ModeText);
        Assert.True(row.HasLimitation);
        Assert.Equal(MergeCopy.LimitedDistinctEditions, row.LimitationText);

        // Still applicable, and the survivor is still named. A limitation is an
        // explanation, never a disabled button.
        Assert.True(row.CanApply);
        Assert.False(row.IsBlocked);
        Assert.Contains("Prey", row.SurvivorLine, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocked_plan_shows_its_reason_and_offers_no_apply()
    {
        var row = new MergeApplyViewModel(
            MergePlan.Nothing(7, MergeBlocker.AchievementsOnBothSides),
            "Hollow Knight",
            "Hollow Knight",
            absorbedReleaseId: 12);

        Assert.True(row.IsBlocked);
        Assert.False(row.CanApply);
        Assert.False(row.HasMode);
        Assert.Equal(MergeCopy.BlockedLabel, row.SectionLabel);
        Assert.Equal(MergeCopy.RefusedAchievementsOnBothSides, row.RefusalText);
    }

    /// <summary>
    /// The live refusal path. A pair answered between the load and the click is
    /// no longer confirmed, and the screen must say the engine wrote nothing
    /// rather than simply going quiet.
    /// </summary>
    [Fact]
    public async Task Applying_a_pair_the_engine_refuses_writes_nothing_and_says_so()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, "Bastion", "Bastion (GOG)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        var row = Assert.Single(queue.Outstanding);

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE merge_candidates SET status = 'rejected' WHERE id = @id;",
                new { id = seed.CandidateId });
        }

        await queue.ApplyCommand.ExecuteAsync(row);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                MergeCopy.AppliedNothingFormat, MergeCopy.RefusedCandidateNotConfirmed),
            queue.ReportMessage);

        Assert.Empty(queue.History);
        Assert.Empty(queue.Outstanding);

        using var after = db.Factory.Open();
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));
    }

    // ── Applying ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Applying_reports_what_changed()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);

        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                MergeCopy.AppliedReportFormat,
                "Hades", "Hades (Epic)", MergeCopy.ModeReleaseCollapse),
            queue.ReportMessage);

        // The list it came from is empty and the history it went to is not.
        Assert.Empty(queue.Outstanding);
        Assert.False(queue.HasOutstanding);
        Assert.Single(queue.History);
    }

    [Fact]
    public async Task The_batch_path_applies_every_outstanding_pair_and_counts_them()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");
        Seed(db, "Celeste", "Celeste (GOG)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, queue.OutstandingCount);
        Assert.Equal("2", queue.OutstandingCountText);

        await queue.ApplyAllCommand.ExecuteAsync(null);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                MergeCopy.AppliedBatchFormat, 2, 2, 0),
            queue.ReportMessage);

        Assert.Empty(queue.Outstanding);
        Assert.Equal(2, queue.History.Count);
    }

    // ── History ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_history_list_names_which_two_games_became_one()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);

        var row = Assert.Single(queue.History);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                MergeCopy.HistoryRowFormat, "Hades (Epic)", "Hades"),
            row.Description);

        Assert.NotEqual("—", row.AppliedAtText);
        Assert.False(row.IsUndone);
        Assert.True(row.CanUndo);
        Assert.True(row.ShowUndoControl);
        Assert.False(row.IsBlocked);

        // Counts belong in the detail view, closed until asked for.
        Assert.False(row.IsCountsOpen);
        Assert.Equal(MergeCopy.CountsShow, row.CountsToggleText);
        Assert.True(row.HasCounts);
        row.ToggleCountsCommand.Execute(null);
        Assert.True(row.IsCountsOpen);
        Assert.Equal(MergeCopy.CountsHide, row.CountsToggleText);

        var releases = row.Counts.Single(c => c.Label == MergeCopy.CountReleases);
        Assert.Equal("1", releases.CountText);

        // Nothing that did not move is printed as a zero.
        Assert.DoesNotContain(row.Counts, c => c.Count == 0);
    }

    // ── The four disabled reasons ────────────────────────────────────────────

    /// <summary>
    /// The only disabled reason with a user action attached. The blocked row
    /// names the later merge that consumed one of its identities and offers
    /// to undo it directly, so the user has a way through without scrolling
    /// the history to find the right row.
    /// </summary>
    [Fact]
    public async Task A_later_merge_names_the_merge_that_must_be_undone_first()
    {
        using var db = new TempDatabase();
        SeedChain(db);

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyAllCommand.ExecuteAsync(null);

        // Newest first, so the second merge leads and the first is behind it.
        var later = queue.History[0];
        var earlier = queue.History[1];

        Assert.True(later.CanUndo);

        Assert.False(earlier.CanUndo);
        Assert.True(earlier.IsBlocked);
        Assert.Equal(MergeUndoBlocker.LaterMergeConsumedIdentity, earlier.Blocker);
        Assert.Equal(later.ApplicationId, earlier.BlockingApplicationId);

        // The only disabled reason with an action attached, and it names the
        // merge the action is about.
        Assert.True(earlier.HasBlockingMerge);
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                MergeCopy.UndoBlockedLaterMergeFormat, later.BlockingLabel),
            earlier.BlockedText);
        Assert.Contains(later.Description, earlier.BlockedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_merge_that_predates_the_journal_says_so_and_offers_nothing()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);

        using (var conn = db.Factory.Open())
        {
            conn.Execute("UPDATE merge_applications SET undo_journal_version = NULL;");
        }

        await queue.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(queue.History);
        Assert.False(row.CanUndo);
        Assert.True(row.IsBlocked);
        Assert.False(row.HasBlockingMerge);
        Assert.Equal(MergeUndoBlocker.PredatesUndoSupport, row.Blocker);
        Assert.Equal(MergeCopy.UndoBlockedPredatesUndoSupport, row.BlockedText);
    }

    [Fact]
    public async Task A_merge_whose_game_is_gone_says_so_and_offers_nothing()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);

        var survivingWorkId = queue.History[0].Application!.SurvivingWorkId;
        using (var conn = db.Factory.Open())
        {
            conn.Execute("DELETE FROM works WHERE id = @id;", new { id = survivingWorkId });
        }

        await queue.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(queue.History);
        Assert.False(row.CanUndo);
        Assert.True(row.IsBlocked);
        Assert.False(row.HasBlockingMerge);
        Assert.Equal(MergeUndoBlocker.GameNoLongerExists, row.Blocker);
        Assert.Equal(MergeCopy.UndoBlockedGameNoLongerExists, row.BlockedText);
    }

    /// <summary>
    /// The fourth reason is the one that removes the affordance rather than
    /// disabling it: an undone merge is history, and a dimmed Undo beside it
    /// would be a control the user could still try to reach.
    /// </summary>
    [Fact]
    public async Task An_undone_merge_is_history_with_no_control_at_all()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);
        await queue.UndoCommand.ExecuteAsync(queue.History[0]);

        var row = Assert.Single(queue.History);
        Assert.True(row.IsUndone);
        Assert.False(row.ShowUndoControl);
        Assert.False(row.CanUndo);
        Assert.False(row.IsBlocked);
        Assert.NotEqual("—", row.UndoneAtText);
        Assert.Equal(MergeUndoBlocker.AlreadyUndone, row.Blocker);
        Assert.Equal(MergeCopy.UndoBlockedAlreadyUndone, row.BlockedText);
    }

    // ── Reversibility is recomputed, never cached ────────────────────────────

    /// <summary>
    /// The same screen instance, the same row, two loads, two answers.
    /// Reversibility is a fact about every merge applied after this one, so a
    /// verdict carried across a load would be a stale claim about the database.
    /// </summary>
    [Fact]
    public async Task The_undo_verdict_changes_between_loads_when_the_log_does()
    {
        using var db = new TempDatabase();
        SeedChain(db);

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);

        // Apply only the first pair.
        var first = queue.Outstanding[0];
        await queue.ApplyCommand.ExecuteAsync(first);

        var applicationId = queue.History[0].ApplicationId;
        Assert.True(queue.History[0].CanUndo);
        Assert.Equal(MergeUndoBlocker.None, queue.History[0].Blocker);

        // Apply the second, which consumes an identity the first produced.
        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);

        var sameMerge = queue.History.Single(row => row.ApplicationId == applicationId);
        Assert.False(sameMerge.CanUndo);
        Assert.Equal(MergeUndoBlocker.LaterMergeConsumedIdentity, sameMerge.Blocker);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Undoing_reports_what_came_back_and_the_row_moves_to_undone()
    {
        using var db = new TempDatabase();
        Seed(db, "Hades", "Hades (Epic)");

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyCommand.ExecuteAsync(queue.Outstanding[0]);
        await queue.UndoCommand.ExecuteAsync(queue.History[0]);

        Assert.NotNull(queue.ReportMessage);
        Assert.StartsWith("Hades (Epic)", queue.ReportMessage, StringComparison.Ordinal);
        Assert.True(queue.History[0].IsUndone);

        using var conn = db.Factory.Open();
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));

        // Terminal, so no sweep re-queues it and no batch pass re-applies it.
        Assert.Equal("undone", conn.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates;"));
        Assert.Empty(queue.Outstanding);
    }

    /// <summary>
    /// The shortcut from the blocked row, not a manual scroll to the blocking
    /// merge. Undoing the blocker must free the earlier row in the same load
    /// so the user can act on it without a second round trip.
    /// </summary>
    [Fact]
    public async Task Undo_that_merge_first_reverses_the_blocking_merge_and_frees_the_row()
    {
        using var db = new TempDatabase();
        SeedChain(db);

        var queue = Screen(db);
        await queue.LoadCommand.ExecuteAsync(null);
        await queue.ApplyAllCommand.ExecuteAsync(null);

        var earlier = queue.History[1];
        Assert.False(earlier.CanUndo);

        await queue.UndoBlockingCommand.ExecuteAsync(earlier);

        // The later merge is now history; the earlier one is reachable again.
        var later = queue.History.Single(row => row.IsUndone);
        Assert.False(later.ShowUndoControl);

        var freed = queue.History.Single(row => row.ApplicationId == earlier.ApplicationId);
        Assert.True(freed.CanUndo);
        Assert.False(freed.IsBlocked);
    }

    // ── The wording the queue itself uses ────────────────────────────────────

    /// <summary>
    /// The button keeps the label the copy table mandates, and everything around
    /// it now says what the label cannot: pressing it writes to the library. The
    /// previous build's wording - answering records a decision and a second
    /// control applies it - would be a lie about this screen.
    /// </summary>
    [Fact]
    public void Answering_a_pair_says_it_merges_now()
    {
        using var db = new TempDatabase();
        var queue = Screen(db);

        Assert.Equal(MergeCopy.QueueIntro, queue.IntroMessage);
        Assert.Equal(MergeCopy.DifferentGamesTooltip, queue.DifferentGamesTooltip);

        Assert.DoesNotContain(
            "separate step", queue.IntroMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("undone", queue.IntroMessage, StringComparison.OrdinalIgnoreCase);

        // The mergeable card's tooltip promises a merge; the blocked card's
        // refuses to, and that difference is this screen's whole honesty budget.
        var mergeable = new MergePreviewViewModel(
            new MergePlan { CandidateId = 1, Mode = MergeMode.ReleaseCollapse },
            "Hollow Knight",
            "Hollow Knight");
        Assert.Equal(MergeCopy.SameGameTooltip, mergeable.SameGameTooltip);
        Assert.Contains("merges", mergeable.SameGameTooltip, StringComparison.OrdinalIgnoreCase);

        var blocked = new MergePreviewViewModel(
            MergePlan.Nothing(2, MergeBlocker.DistinctEditions),
            "Hollow Knight",
            "Hollow Knight");
        Assert.Equal(MergeCopy.SameGameBlockedTooltip, blocked.SameGameTooltip);
        Assert.DoesNotContain("merges", blocked.SameGameTooltip, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_empty_screen_directs_rather_than_apologises()
    {
        using var db = new TempDatabase();
        var queue = Screen(db);

        await queue.LoadCommand.ExecuteAsync(null);

        // The leftover section explains itself by being absent: an install that
        // never saw the two-step flow has nothing to be told about it.
        Assert.False(queue.HasOutstanding);

        Assert.True(queue.ShowHistoryEmpty);
        Assert.False(queue.HasReport);
        Assert.Equal(MergeCopy.HistoryEmpty, queue.HistoryEmptyMessage);
    }

    // ── Fixture ──────────────────────────────────────────────────────────────

    private static MergeQueueViewModel Screen(TempDatabase db)
        => new(
            new MergeCandidateRepository(db.Factory),
            new ReleaseRepository(db.Factory),
            new WorkRepository(db.Factory),
            TestMergeExecutor.For(db));

    private sealed record SeedIds(
        long CandidateId,
        long LeftWorkId,
        long RightWorkId,
        long LeftReleaseId,
        long RightReleaseId);

    /// <summary>
    /// Two works, one release each, and a confirmed pair between them. The two
    /// titles differ so the preview and the history row have something to name;
    /// nothing in the planner reads a name, so the merge behaves exactly as it
    /// does on two identically titled rows.
    /// </summary>
    private static SeedIds Seed(TempDatabase db, string leftTitle, string rightTitle)
    {
        using var conn = db.Factory.Open();

        var leftWorkId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@leftTitle) RETURNING id;", new { leftTitle });
        var rightWorkId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@rightTitle) RETURNING id;", new { rightTitle });

        var leftReleaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@leftWorkId, @leftTitle) RETURNING id;",
            new { leftWorkId, leftTitle });
        var rightReleaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@rightWorkId, @rightTitle) RETURNING id;",
            new { rightWorkId, rightTitle });

        conn.Execute("""
            INSERT INTO external_ids (release_id, provider, provider_id) VALUES
                (@leftReleaseId,  'steam', @leftKey),
                (@rightReleaseId, 'epic',  @rightKey);
            """,
            new
            {
                leftReleaseId,
                rightReleaseId,
                leftKey = "left-" + leftReleaseId,
                rightKey = "right-" + rightReleaseId,
            });

        var candidateId = conn.ExecuteScalar<long>("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (@leftReleaseId, @rightReleaseId, 0.93, 'confirmed')
            RETURNING id;
            """, new { leftReleaseId, rightReleaseId });

        return new SeedIds(candidateId, leftWorkId, rightWorkId, leftReleaseId, rightReleaseId);
    }

    /// <summary>
    /// Three entries and two confirmed pairs, the second of which names the
    /// release the first leaves standing. Undoing the first would have to
    /// reconstruct a state that never existed, which is the whole reason the
    /// later-merge blocker exists.
    /// </summary>
    private static void SeedChain(TempDatabase db)
    {
        using var conn = db.Factory.Open();

        var ids = new List<long>();
        var titles = new[] { "Celeste", "Celeste (Epic)", "Celeste (GOG)" };
        foreach (var title in titles)
        {
            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES (@title) RETURNING id;", new { title });
            var releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, @title) RETURNING id;",
                new { workId, title });
            conn.Execute("""
                INSERT INTO external_ids (release_id, provider, provider_id)
                VALUES (@releaseId, 'steam', @key);
                """, new { releaseId, key = "celeste-" + releaseId });
            ids.Add(releaseId);
        }

        conn.Execute("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (@a, @b, 0.9, 'confirmed'), (@a, @c, 0.9, 'confirmed');
            """, new { a = ids[0], b = ids[1], c = ids[2] });
    }
}
