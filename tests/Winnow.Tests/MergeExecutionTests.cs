using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Merging;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Merge execution (F09, F20). Covers the five cases the stabilization review
/// named: rollback on failure, collision-heavy same-store pairs, distinct
/// editions preserved as two releases under one work, achievements never blended
/// across platforms, and idempotency decided from state rather than from the
/// application log. Also covers the cascade tripwire, the canonicality
/// constraints F20 asks for, repointing of merge candidates involving the
/// absorbed release, and (TASK-70.1) the survivor reason the plan reports and
/// the survivor-choice contract.
/// </summary>
public class MergeExecutionTests
{
    // ── AC #1: a confirmed pair collapses to one release ─────────────────────

    [Fact]
    public async Task Confirmed_pair_collapses_to_one_release_keeping_both_external_ids()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.True(outcome.Applied);
        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);

        using var conn = db.Factory.Open();

        // One work, one release, both storefront anchors on it.
        Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));

        var providers = conn.Query<string>(
                "SELECT provider FROM external_ids WHERE release_id = @id ORDER BY provider;",
                new { id = outcome.Plan.SurvivingReleaseId })
            .ToList();
        Assert.Equal(["epic", "steam"], providers);

        // Different stores are still two ownerships: ux_ownerships_release_store
        // permits one per store, and cross-store ownership is the whole point.
        Assert.Equal(2, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM ownerships WHERE release_id = @id;",
            new { id = outcome.Plan.SurvivingReleaseId }));

        // Play history from both sides survives, attached to its own ownership.
        Assert.Equal(2, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM play_records;"));
    }

    [Fact]
    public async Task Surviving_work_is_the_one_holding_the_igdb_id_not_the_older_row()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            // The older work is the provisional, unenriched one; the newer holds
            // the igdb_id, which works.igdb_id being UNIQUE makes uncopyable.
            conn.Execute(
                "UPDATE works SET name_is_provisional = 1 WHERE id = @id;", new { id = seed.LeftWorkId });
            conn.Execute(
                "UPDATE works SET igdb_id = 4242, summary = 'From IGDB' WHERE id = @id;",
                new { id = seed.RightWorkId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(seed.RightWorkId, outcome.Plan.SurvivingWorkId);
        Assert.Equal(seed.LeftWorkId, outcome.Plan.AbsorbedWorkId);
        Assert.True(seed.LeftWorkId < seed.RightWorkId);

        using var after = db.Factory.Open();
        Assert.Equal(4242, after.ExecuteScalar<long>(
            "SELECT igdb_id FROM works WHERE id = @id;", new { id = seed.RightWorkId }));
    }

    [Fact]
    public async Task Work_columns_are_filled_never_overwritten()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE works SET igdb_id = 1, summary = 'kept', publisher = NULL WHERE id = @id;",
                new { id = seed.LeftWorkId });
            conn.Execute(
                "UPDATE works SET summary = 'discarded', publisher = 'filled in' WHERE id = @id;",
                new { id = seed.RightWorkId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(seed.LeftWorkId, outcome.Plan.SurvivingWorkId);

        using var after = db.Factory.Open();
        Assert.Equal("kept", after.ExecuteScalar<string>(
            "SELECT summary FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
        Assert.Equal("filled in", after.ExecuteScalar<string>(
            "SELECT publisher FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
    }

    // ── The plan says why the survivor won (TASK-70.1) ───────────────────────

    /// <summary>
    /// The plan carries the reason the survivor was chosen, not just the survivor
    /// itself. Two unenriched works report <c>AddedFirst</c>; enriching one with
    /// an igdb_id shifts the reason to <c>IgdbMatch</c>.
    /// </summary>
    [Fact]
    public async Task The_plan_names_the_rung_that_decided_the_survivor()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        // Two unenriched works, one release each: nothing discriminates but the
        // order they were ingested in, and the plan admits exactly that.
        var plain = await repository.PreviewAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(seed.LeftWorkId, plain.SurvivingWorkId);
        Assert.Equal(MergeSurvivorReason.AddedFirst, plain.SurvivorReason);

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE works SET igdb_id = 4242 WHERE id = @id;", new { id = seed.RightWorkId });
        }

        var enriched = await repository.PreviewAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(seed.RightWorkId, enriched.SurvivingWorkId);
        Assert.Equal(MergeSurvivorReason.IgdbMatch, enriched.SurvivorReason);
    }

    /// <summary>
    /// A preferred surviving work overrides the ladder and applies the merge in
    /// the chosen direction. The plan reports <c>ChosenByYou</c>, the database
    /// keeps the chosen side, and the other side is gone.
    /// </summary>
    [Fact]
    public async Task A_chosen_survivor_overrides_the_ladder_and_applies_in_that_direction()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        // Nothing discriminates the two works, so the ladder would keep the
        // left one on ingestion order alone. The user picks the right one.
        var repository = new MergeExecutionRepository(db.Factory);
        Assert.Equal(
            MergeSurvivorReason.AddedFirst,
            (await repository.PreviewAsync(new MergeRequest { CandidateId = seed.CandidateId }))
                .SurvivorReason);

        var request = new MergeRequest
        {
            CandidateId = seed.CandidateId,
            PreferredSurvivingWorkId = seed.RightWorkId,
        };

        var plan = await repository.PreviewAsync(request);
        Assert.Equal(seed.RightWorkId, plan.SurvivingWorkId);
        Assert.Equal(seed.LeftWorkId, plan.AbsorbedWorkId);
        Assert.Equal(MergeSurvivorReason.ChosenByYou, plan.SurvivorReason);

        var outcome = await repository.ApplyAsync(request);
        Assert.True(outcome.Applied);
        Assert.Equal(seed.RightWorkId, outcome.Plan.SurvivingWorkId);

        using var after = db.Factory.Open();
        Assert.Equal(seed.RightWorkId, after.ExecuteScalar<long>("SELECT id FROM works;"));
    }

    /// <summary>
    /// A preferred work that is neither side of the pair returns
    /// <see cref="MergeBlocker.PreferredSurvivorNotInPair"/> and writes nothing.
    /// Falling back to the ladder would merge in a direction the user did not
    /// ask for.
    /// </summary>
    [Fact]
    public async Task A_chosen_survivor_naming_neither_side_refuses_rather_than_falling_back()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);
        var before = Snapshot(db);

        var request = new MergeRequest
        {
            CandidateId = seed.CandidateId,
            PreferredSurvivingWorkId = seed.LeftWorkId + seed.RightWorkId + 1,
        };

        var plan = await repository.PreviewAsync(request);
        Assert.Equal(MergeMode.NothingToDo, plan.Mode);
        Assert.Equal(MergeBlocker.PreferredSurvivorNotInPair, plan.Blocker);

        var outcome = await repository.ApplyAsync(request);
        Assert.False(outcome.Applied);
        Assert.Equal(before, Snapshot(db));
    }

    /// <summary>
    /// Choosing a survivor that does not hold the <c>igdb_id</c> the other
    /// side carries returns <see cref="MergeBlocker.SurvivorCannotHoldIgdbId"/>
    /// and writes nothing. <c>works.igdb_id</c> is UNIQUE, so the COALESCE
    /// fill would collide with the row about to be deleted. Under the ladder
    /// alone this is unreachable (rung one keeps the holder); it becomes
    /// reachable only once a survivor can be chosen. The link model
    /// (TASK-70.2 onward) does not have the problem at all, because both
    /// <c>igdb_id</c> values stay on their own rows.
    /// </summary>
    [Fact]
    public async Task A_choice_that_would_strand_the_igdb_match_is_refused_by_the_destructive_merge()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE works SET igdb_id = 4242 WHERE id = @id;", new { id = seed.LeftWorkId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var before = Snapshot(db);

        // The right work does not hold an igdb_id; choosing it as the survivor
        // would strand the left work's igdb_id. The link model (TASK-70.2) does
        // not have this problem because both rows stay.
        var plan = await repository.PreviewAsync(new MergeRequest
        {
            CandidateId = seed.CandidateId,
            PreferredSurvivingWorkId = seed.RightWorkId,
        });

        Assert.Equal(MergeMode.NothingToDo, plan.Mode);
        Assert.Equal(MergeBlocker.SurvivorCannotHoldIgdbId, plan.Blocker);

        var outcome = await repository.ApplyAsync(new MergeRequest
        {
            CandidateId = seed.CandidateId,
            PreferredSurvivingWorkId = seed.RightWorkId,
        });

        Assert.False(outcome.Applied);
        Assert.Equal(before, Snapshot(db));

        // The ladder's own answer still applies, untouched.
        Assert.Equal(
            seed.LeftWorkId,
            (await repository.PreviewAsync(new MergeRequest { CandidateId = seed.CandidateId }))
                .SurvivingWorkId);
    }

    // ── Distinct editions are preserved ──────────────────────────────────────

    [Fact]
    public async Task Distinct_editions_stay_two_releases_under_one_work()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE releases SET edition_note = 'Gold Edition' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var plan = await repository.PlanAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.WorkOnly, plan.Mode);
        Assert.Equal(MergeBlocker.DistinctEditions, plan.Blocker);

        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.True(outcome.Applied);

        using var after = db.Factory.Open();

        // One work, two releases: Skyrim SE is not Skyrim (§6).
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM releases;"));
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(DISTINCT work_id) FROM releases;"));

        // Both editions keep their own external id and their own play history.
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM external_ids;"));
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM play_records;"));
    }

    [Fact]
    public async Task A_caller_can_ask_for_less_than_the_data_permits_but_never_more()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE releases SET edition_note = 'Gold Edition' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);

        // Withholding a collapse the data would permit: honoured.
        var withheld = await repository.PlanAsync(new MergeRequest
        {
            CandidateId = seed.CandidateId,
            AllowReleaseCollapse = false,
        });
        Assert.Equal(MergeMode.WorkOnly, withheld.Mode);

        // Asking for a collapse the data forbids: refused all the same. The
        // repository re-derives its own verdict from the stored rows.
        var demanded = await repository.PlanAsync(new MergeRequest
        {
            CandidateId = seed.CandidateId,
            AllowReleaseCollapse = true,
        });
        Assert.Equal(MergeMode.WorkOnly, demanded.Mode);
        Assert.Equal(MergeBlocker.DistinctEditions, demanded.Blocker);
    }

    // ── §6.2: achievements are never blended across platforms ────────────────

    [Fact]
    public async Task Achievements_on_both_sides_refuse_the_collapse_and_are_never_blended()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO achievements (release_id, provider_key, name) VALUES
                    (@left,  'STEAM_FIRST_BLOOD', 'First Blood'),
                    (@left,  'STEAM_COMPLETIONS', 'Completionist'),
                    (@right, 'EPIC_FIRST_BLOOD',  'First Blood');
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
            conn.Execute("""
                INSERT INTO achievement_unlocks (release_id, provider_key, unlocked_at)
                VALUES (@left, 'STEAM_FIRST_BLOOD', '2026-01-01 00:00:00');
                """, new { left = seed.LeftReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.True(outcome.Applied);
        Assert.Equal(MergeMode.WorkOnly, outcome.Plan.Mode);
        Assert.Equal(MergeBlocker.AchievementsOnBothSides, outcome.Plan.Blocker);

        using var after = db.Factory.Open();

        // The two sets stay on two release rows. No release_id carries both
        // stores' provider keys, so no query can average them into one figure.
        Assert.Equal(2, after.ExecuteScalar<long>(
            "SELECT COUNT(DISTINCT release_id) FROM achievements;"));
        Assert.Equal(2, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM achievements WHERE release_id = @id;",
            new { id = seed.LeftReleaseId }));
        Assert.Equal(1, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM achievements WHERE release_id = @id;",
            new { id = seed.RightReleaseId }));
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM achievement_unlocks;"));
    }

    [Fact]
    public async Task Achievements_on_one_side_make_that_side_the_survivor_so_nothing_moves()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            // On the higher-id side, so the id tie-break cannot explain the result.
            conn.Execute("""
                INSERT INTO achievements (release_id, provider_key, name)
                VALUES (@right, 'EPIC_FIRST_BLOOD', 'First Blood');
                """, new { right = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);
        Assert.Equal(seed.RightReleaseId, outcome.Plan.SurvivingReleaseId);
        Assert.Equal(0, outcome.Repointed.Achievements);

        using var after = db.Factory.Open();
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM achievements;"));
        Assert.Equal(seed.RightReleaseId, after.ExecuteScalar<long>(
            "SELECT release_id FROM achievements;"));
    }

    // ── Collision-heavy: same-store ownerships on both sides ─────────────────

    [Fact]
    public async Task Same_store_ownerships_fold_without_losing_a_single_session_or_record()
    {
        using var db = new TempDatabase();

        // Two Steam appids for one game: the only shape that can collide on
        // ux_ownerships_release_store when two releases collapse.
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                VALUES (@left, 310, '2026-02-02 00:00:00'),
                       (@right, 145, '2026-03-02 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
            conn.Execute("""
                INSERT INTO sessions (ownership_id, started_at, ended_at, duration_s, detection_method)
                VALUES (@left,  '2026-02-01 20:00:00', '2026-02-01 21:00:00', 3600, 'process_watch'),
                       (@right, '2026-03-01 20:00:00', '2026-03-01 20:30:00', 1800, 'process_watch');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
            conn.Execute("""
                INSERT INTO ownership_accounts
                    (ownership_id, account_ref, playtime_minutes, last_played_at, source, first_seen_at, last_seen_at)
                VALUES (@left,  'acct-1', 310, '2026-02-01 00:00:00', 'steam_web',   '2026-01-01 00:00:00', '2026-02-02 00:00:00'),
                       (@right, 'acct-1', 145, '2026-03-01 00:00:00', 'steam_local', '2026-02-15 00:00:00', '2026-03-02 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);
        Assert.Equal(1, outcome.Repointed.OwnershipsFolded);

        using var after = db.Factory.Open();

        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM ownerships;"));
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM releases WHERE id <> @id;",
            new { id = outcome.Plan.SurvivingReleaseId }));

        // Nothing was dropped: both observations, both snapshots, both sessions.
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM play_records;"));
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM playtime_snapshots;"));
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM sessions;"));

        // The only row that went away is the second statement about one account,
        // which is merged below rather than kept twice.
        Assert.Equal(1, outcome.Repointed.DuplicateRowsDropped);

        // Both appids survive as external ids on the one release.
        Assert.Equal(2, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM external_ids WHERE provider = 'steam';"));

        // The account row is one coherent observed tuple - the newer one - with
        // the first-seen edge widened, not a recombination of both rows.
        var account = after.QueryFirst(
            "SELECT playtime_minutes, last_played_at, source, first_seen_at, last_seen_at FROM ownership_accounts;");
        Assert.Equal(145L, (long)account.playtime_minutes);
        Assert.Equal("2026-03-01 00:00:00", (string)account.last_played_at);
        Assert.Equal("steam_local", (string)account.source);
        Assert.Equal("2026-01-01 00:00:00", (string)account.first_seen_at);
        Assert.Equal("2026-03-02 00:00:00", (string)account.last_seen_at);
    }

    [Fact]
    public async Task Byte_identical_observations_are_deduplicated_not_duplicated()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");

        using (var conn = db.Factory.Open())
        {
            // Make the two sides report the same observation - every column of
            // the unique key, which for this table is every column but the id.
            conn.Execute("""
                UPDATE play_records
                SET playtime_minutes = 310,
                    last_played_at   = '2026-02-01 00:00:00',
                    source           = 'steam_web',
                    observed_at      = '2026-02-02 00:00:00'
                WHERE ownership_id = @right;
                """, new { right = seed.RightOwnershipId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(1, outcome.Repointed.DuplicateRowsDropped);

        using var after = db.Factory.Open();
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM play_records;"));
        Assert.Equal(310, after.ExecuteScalar<long>("SELECT playtime_minutes FROM play_records;"));
    }

    [Fact]
    public async Task Overlapping_list_and_facet_membership_collapses_to_one_row()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            var listId = conn.ExecuteScalar<long>(
                "INSERT INTO lists (name) VALUES ('Backlog') RETURNING id;");
            conn.Execute("""
                INSERT INTO list_items (list_id, release_id, position)
                VALUES (@listId, @left, 1), (@listId, @right, 2);
                """, new { listId, left = seed.LeftReleaseId, right = seed.RightReleaseId });
            conn.Execute("""
                INSERT INTO release_facets (release_id, facet_id, rank)
                VALUES (@left, 1, 1), (@right, 1, 3), (@right, 2, 1);
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
            conn.Execute("""
                INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id)
                VALUES (@left, '2026-08-01', 'bounced'), (@right, '2026-08-01', 'bounced');
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);

        using var after = db.Factory.Open();
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM list_items;"));
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM release_facets;"));
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM feed_surfacings;"));
    }

    // ── Update events: a distinct fact refuses the collapse ──────────────────

    [Fact]
    public async Task Conflicting_update_events_downgrade_the_merge_rather_than_drop_one()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            // Same (kind, occurred_at) - the unique key - different titles.
            conn.Execute("""
                INSERT INTO update_events (release_id, kind, occurred_at, title)
                VALUES (@left,  'announcement', '2026-07-01 12:00:00', 'Steam patch notes'),
                       (@right, 'announcement', '2026-07-01 12:00:00', 'Epic patch notes');
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.WorkOnly, outcome.Plan.Mode);
        Assert.Equal(MergeBlocker.ConflictingUpdateEvents, outcome.Plan.Blocker);

        using var after = db.Factory.Open();
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM update_events;"));
    }

    [Fact]
    public async Task Equivalent_update_events_collapse_to_one_row()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO update_events (release_id, kind, occurred_at, title)
                VALUES (@left,  'announcement', '2026-07-01 12:00:00', 'Patch notes'),
                       (@right, 'announcement', '2026-07-01 12:00:00', 'Patch notes'),
                       (@right, 'build_push',   '2026-07-02 12:00:00', NULL);
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);

        using var after = db.Factory.Open();
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM update_events;"));
    }

    // ── AC #4: idempotency ───────────────────────────────────────────────────

    [Fact]
    public async Task Re_running_a_collapsed_merge_is_a_no_op()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        var first = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.True(first.Applied);

        var before = Snapshot(db);
        var second = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.False(second.Applied);
        Assert.Equal(MergeMode.NothingToDo, second.Plan.Mode);
        Assert.Equal(MergeBlocker.CandidateNotFound, second.Plan.Blocker);
        Assert.Equal(before, Snapshot(db));

        using var after = db.Factory.Open();
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));
    }

    [Fact]
    public async Task Re_running_a_work_only_merge_is_a_no_op()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE releases SET edition_note = 'Gold Edition' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        Assert.True((await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId })).Applied);

        var before = Snapshot(db);
        var second = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.False(second.Applied);
        Assert.Equal(MergeBlocker.DistinctEditions, second.Plan.Blocker);
        Assert.Equal(before, Snapshot(db));
    }

    [Fact]
    public async Task Only_confirmed_pairs_are_reachable()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        foreach (var status in new[] { "pending", "rejected" })
        {
            using (var conn = db.Factory.Open())
            {
                conn.Execute("UPDATE merge_candidates SET status = @status WHERE id = @id;",
                    new { status, id = seed.CandidateId });
            }

            var before = Snapshot(db);
            var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

            Assert.False(outcome.Applied);
            Assert.Equal(MergeBlocker.CandidateNotConfirmed, outcome.Plan.Blocker);
            Assert.Equal(before, Snapshot(db));
        }

        var missing = await repository.ApplyAsync(new MergeRequest { CandidateId = 9_999 });
        Assert.Equal(MergeBlocker.CandidateNotFound, missing.Plan.Blocker);
    }

    /// <summary>
    /// The prospective read path exists so the review card can state what an
    /// answer will do before it is given, which means it looks at an unanswered
    /// pair — the one case <see cref="Only_confirmed_pairs_are_reachable"/>
    /// forbids for the write path. It has to be provably read-only.
    /// </summary>
    [Fact]
    public async Task Previewing_a_pending_pair_leaves_the_database_untouched()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        using (var conn = db.Factory.Open())
        {
            conn.Execute("UPDATE merge_candidates SET status = 'pending' WHERE id = @id;",
                new { id = seed.CandidateId });
        }

        var before = Snapshot(db);
        var plan = await repository.PreviewAsync(new MergeRequest { CandidateId = seed.CandidateId });

        // A real plan, not a refusal — the whole point of the path.
        Assert.NotEqual(MergeMode.NothingToDo, plan.Mode);
        Assert.NotEqual(MergeBlocker.CandidateNotConfirmed, plan.Blocker);
        Assert.Equal(before, Snapshot(db));
    }

    /// <summary>
    /// Admitting <c>pending</c> admits exactly that. A rejected or undone row is
    /// terminal, and a screen that previewed one would be offering an outcome
    /// for a question already closed.
    /// </summary>
    [Fact]
    public async Task Previewing_a_terminal_pair_refuses_like_the_write_path()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        foreach (var status in new[] { "rejected", "undone" })
        {
            using (var conn = db.Factory.Open())
            {
                conn.Execute("UPDATE merge_candidates SET status = @status WHERE id = @id;",
                    new { status, id = seed.CandidateId });
            }

            var plan = await repository.PreviewAsync(new MergeRequest { CandidateId = seed.CandidateId });
            Assert.Equal(MergeMode.NothingToDo, plan.Mode);
            Assert.Equal(MergeBlocker.CandidateNotConfirmed, plan.Blocker);
        }
    }

    // ── AC #3: the cascade tripwire, and rollback ────────────────────────────

    [Fact]
    public async Task A_stranded_dependent_aborts_the_merge_instead_of_being_cascade_deleted()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        var repository = new MergeExecutionRepository(db.Factory);

        var plan = await repository.PlanAsync(new MergeRequest { CandidateId = seed.CandidateId });
        var absorbed = plan.AbsorbedReleaseId!.Value;

        using (var conn = db.Factory.Open())
        {
            // Stands in for a table that gains a foreign key to releases and is
            // never added to the repointing pass: a row appears against the
            // absorbed release after that table's own repoint has run.
            conn.Execute($"""
                CREATE TRIGGER strand_a_dependent AFTER DELETE ON merge_candidates
                BEGIN
                    INSERT INTO feed_verdicts (release_id, kind, created_at)
                    VALUES ({absorbed}, 'not_interested', '2026-08-30 00:00:00');
                END;
                """);
        }

        var before = Snapshot(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId }));
        Assert.Contains("Refusing to delete releases row", error.Message, StringComparison.Ordinal);
        Assert.Contains("FeedVerdicts=1", error.Message, StringComparison.Ordinal);

        // The whole merge is gone, not just the delete: both releases, both
        // works, both external ids and both play records are exactly as they were.
        Assert.Equal(before, Snapshot(db));
    }

    [Fact]
    public async Task A_failure_mid_merge_leaves_the_database_exactly_as_it_was()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            // external_ids is repointed before ownerships, so this aborts after
            // the merge has already written.
            conn.Execute("""
                CREATE TRIGGER fail_partway BEFORE UPDATE ON ownerships
                BEGIN
                    SELECT RAISE(ABORT, 'injected mid-merge failure');
                END;
                """);
        }

        var before = Snapshot(db);
        var repository = new MergeExecutionRepository(db.Factory);

        await Assert.ThrowsAsync<SqliteException>(
            () => repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId }));

        Assert.Equal(before, Snapshot(db));

        using var after = db.Factory.Open();
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));
    }

    // ── AC #2 / F20: canonicality is the repository's, not the caller's ──────

    [Fact]
    public void The_schema_rejects_self_pairs_and_mirror_duplicates()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using var conn = db.Factory.Open();

        Assert.Throws<SqliteException>(() => conn.Execute("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score)
            VALUES (@id, @id, 0.9);
            """, new { id = seed.LeftReleaseId }));

        // The mirror image of the seeded pair.
        Assert.Throws<SqliteException>(() => conn.Execute("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score)
            VALUES (@right, @left, 0.9);
            """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId }));
    }

    [Fact]
    public async Task The_repository_canonicalises_a_mirrored_insert_and_refuses_a_self_pair()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("DELETE FROM merge_candidates;");
        }

        var candidates = new MergeCandidateRepository(db.Factory);

        var id = await candidates.InsertAsync(new Core.Domain.MergeCandidate
        {
            LeftReleaseId = seed.RightReleaseId,
            RightReleaseId = seed.LeftReleaseId,
            Score = 0.91,
        });

        var stored = await candidates.GetAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(seed.LeftReleaseId, stored.LeftReleaseId);
        Assert.Equal(seed.RightReleaseId, stored.RightReleaseId);

        await Assert.ThrowsAsync<ArgumentException>(() => candidates.InsertAsync(
            new Core.Domain.MergeCandidate
            {
                LeftReleaseId = seed.LeftReleaseId,
                RightReleaseId = seed.LeftReleaseId,
                Score = 0.5,
            }));
    }

    [Fact]
    public async Task Other_pairs_involving_the_absorbed_release_move_to_the_survivor()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        long thirdReleaseId;
        using (var conn = db.Factory.Open())
        {
            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Something else') RETURNING id;");
            thirdReleaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Something else') RETURNING id;",
                new { workId });

            // A decision the user already made about the absorbed release.
            conn.Execute("""
                INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
                VALUES (MIN(@right, @third), MAX(@right, @third), 0.7, 'rejected');
                """, new { right = seed.RightReleaseId, third = thirdReleaseId });
        }

        var repository = new MergeExecutionRepository(db.Factory);
        var outcome = await repository.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);
        var survivor = outcome.Plan.SurvivingReleaseId!.Value;

        using var after = db.Factory.Open();

        // The rejection survives, now stated about the surviving release, so no
        // sweep can re-ask a question the user has already answered.
        var row = after.QueryFirst("""
            SELECT left_release_id, right_release_id, status FROM merge_candidates;
            """);
        Assert.Equal("rejected", (string)row.status);
        Assert.Equal(Math.Min(survivor, thirdReleaseId), (long)row.left_release_id);
        Assert.Equal(Math.Max(survivor, thirdReleaseId), (long)row.right_release_id);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private sealed record SeedIds(
        long CandidateId,
        long LeftWorkId,
        long RightWorkId,
        long LeftReleaseId,
        long RightReleaseId,
        long LeftOwnershipId,
        long RightOwnershipId);

    /// <summary>
    /// Two works, one release each, one ownership each, one play record each,
    /// and a confirmed pair joining them — the shape the resolver leaves behind
    /// when the same game arrives from two storefronts.
    /// </summary>
    private static SeedIds Seed(TempDatabase db, string leftStore, string rightStore)
    {
        using var conn = db.Factory.Open();

        var leftWorkId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Hades') RETURNING id;");
        var rightWorkId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Hades') RETURNING id;");

        var leftReleaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@leftWorkId, 'Hades') RETURNING id;",
            new { leftWorkId });
        var rightReleaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@rightWorkId, 'Hades') RETURNING id;",
            new { rightWorkId });

        conn.Execute("""
            INSERT INTO external_ids (release_id, provider, provider_id) VALUES
                (@leftReleaseId,  @leftStore,  'left-id'),
                (@rightReleaseId, @rightStore, 'right-id');
            """, new { leftReleaseId, rightReleaseId, leftStore, rightStore });

        var leftOwnershipId = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@leftReleaseId, @leftStore) RETURNING id;",
            new { leftReleaseId, leftStore });
        var rightOwnershipId = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@rightReleaseId, @rightStore) RETURNING id;",
            new { rightReleaseId, rightStore });

        // Play history on both sides, from different sources at different times:
        // the merge has to carry both, and neither is a duplicate of the other.
        conn.Execute("""
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
            VALUES (@leftOwnershipId,  310, '2026-02-01 00:00:00', 'steam_web',   '2026-02-02 00:00:00'),
                   (@rightOwnershipId, 145, '2026-03-01 00:00:00', 'steam_local', '2026-03-02 00:00:00');
            """, new { leftOwnershipId, rightOwnershipId });

        var candidateId = conn.ExecuteScalar<long>("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (@leftReleaseId, @rightReleaseId, 0.93, 'confirmed')
            RETURNING id;
            """, new { leftReleaseId, rightReleaseId });

        return new SeedIds(
            candidateId, leftWorkId, rightWorkId,
            leftReleaseId, rightReleaseId, leftOwnershipId, rightOwnershipId);
    }

    /// <summary>
    /// Every row of every table a merge can touch, ordered, as one string. Two
    /// equal snapshots mean the database is byte-for-byte the same, which is
    /// what "a failed merge changes nothing" has to mean.
    /// </summary>
    private static string Snapshot(TempDatabase db)
    {
        string[] tables =
        [
            "works", "releases", "external_ids", "ownerships",
            "play_records", "playtime_snapshots", "sessions", "session_notes",
            "ownership_accounts", "achievements", "achievement_unlocks",
            "update_events", "update_acknowledgements", "lists", "list_items",
            "work_facets", "release_facets", "feed_verdicts", "feed_surfacings",
            "merge_candidates", "merge_applications",
        ];

        using var conn = db.Factory.Open();
        var lines = new List<string>();

        foreach (var table in tables)
        {
            var columns = conn.Query<string>(
                    $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid;")
                .ToList();
            var projection = string.Join(" || '|' || ", columns.Select(c => $"COALESCE(CAST({c} AS TEXT), '<null>')"));

            foreach (var row in conn.Query<string>($"SELECT {projection} FROM {table} ORDER BY 1;"))
            {
                lines.Add($"{table}: {row}");
            }
        }

        return string.Join('\n', lines);
    }
}
