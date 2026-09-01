using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Domain;
using Winnow.Core.Merging;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Resolve;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Merge undo (TASK-62), against the row-level journal migration 0017 adds.
///
/// <para>The question these tests answer is whether the journal captures enough
/// to put the database back exactly as it was — including the two operations the
/// 0016 audit recorded nothing about at all (the surviving work's COALESCE fill
/// and the surviving ownership_accounts row's in-place merge) and the four tables
/// that drop rows carrying payload the survivor does not have — and whether an
/// undo that cannot be faithful refuses instead of half-reversing.</para>
/// </summary>
public class MergeUndoTests
{
    // ── Faithfulness round-trip ──────────────────────────────────────────────

    [Fact]
    public async Task A_release_collapse_round_trips_the_whole_database()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        Enrich(db, seed);

        var before = Snapshot(db, DataTables);
        var candidatesBefore = Candidates(db);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);
        Assert.NotEqual(before, Snapshot(db, DataTables));

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, DataTables));

        // The pair itself comes back whole; only its status differs, and that
        // difference is the point.
        Assert.Equal(
            candidatesBefore.Select(c => c with { Status = MergeCandidateStatuses.Undone }),
            Candidates(db));
    }

    [Fact]
    public async Task A_work_only_merge_round_trips_and_every_release_moves_back()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        Enrich(db, seed);

        long strayReleaseId;
        using (var conn = db.Factory.Open())
        {
            // A second release hanging off the absorbed work: work membership is
            // an equivalence class, so the merge moves this one too.
            strayReleaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@w, 'Hades soundtrack') RETURNING id;",
                new { w = seed.RightWorkId });

            // Distinct editions: the collapse is refused and the merge is
            // work-only, so the absorbed release survives as a row.
            conn.Execute(
                "UPDATE releases SET edition_note = 'Gold Edition' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var before = Snapshot(db, DataTables);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(MergeMode.WorkOnly, outcome.Plan.Mode);

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, DataTables));

        using var after = db.Factory.Open();
        Assert.Equal(seed.RightWorkId, after.ExecuteScalar<long>(
            "SELECT work_id FROM releases WHERE id = @id;", new { id = strayReleaseId }));
    }

    [Fact]
    public async Task The_survivors_own_rows_never_move()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        Enrich(db, seed);

        var before = Rows(db, """
            SELECT 'ownership:' || id || '->' || release_id FROM ownerships WHERE release_id = @r
            UNION ALL
            SELECT 'play:' || id || '->' || ownership_id FROM play_records
             WHERE ownership_id IN (SELECT id FROM ownerships WHERE release_id = @r)
            UNION ALL
            SELECT 'facet:' || facet_id FROM release_facets WHERE release_id = @r
            UNION ALL
            SELECT 'list:' || list_id || ':' || position FROM list_items WHERE release_id = @r
            """, new { r = seed.LeftReleaseId });

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Rows(db, """
            SELECT 'ownership:' || id || '->' || release_id FROM ownerships WHERE release_id = @r
            UNION ALL
            SELECT 'play:' || id || '->' || ownership_id FROM play_records
             WHERE ownership_id IN (SELECT id FROM ownerships WHERE release_id = @r)
            UNION ALL
            SELECT 'facet:' || facet_id FROM release_facets WHERE release_id = @r
            UNION ALL
            SELECT 'list:' || list_id || ':' || position FROM list_items WHERE release_id = @r
            """, new { r = seed.LeftReleaseId }));
    }

    [Fact]
    public async Task Coalesce_filled_work_columns_go_back_to_null_and_kept_ones_are_left_alone()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                UPDATE works
                SET igdb_id = 1, summary = 'kept', publisher = NULL, cover_url = NULL
                WHERE id = @id;
                """, new { id = seed.LeftWorkId });
            conn.Execute("""
                UPDATE works
                SET summary = 'discarded', publisher = 'filled in', cover_url = 'http://cover'
                WHERE id = @id;
                """, new { id = seed.RightWorkId });
        }

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(seed.LeftWorkId, outcome.Plan.SurvivingWorkId);

        using (var filled = db.Factory.Open())
        {
            Assert.Equal("filled in", filled.ExecuteScalar<string>(
                "SELECT publisher FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        using var after = db.Factory.Open();
        var row = after.QueryFirst(
            "SELECT summary, publisher, cover_url FROM works WHERE id = @id;",
            new { id = seed.LeftWorkId });

        Assert.Equal("kept", (string)row.summary);
        Assert.Null(row.publisher);
        Assert.Null(row.cover_url);

        // And the absorbed row is back with the values that were only ever its.
        Assert.Equal("discarded", after.ExecuteScalar<string>(
            "SELECT summary FROM works WHERE id = @id;", new { id = seed.RightWorkId }));
    }

    [Fact]
    public async Task The_provisional_name_promotion_is_reverted()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            // The survivor holds the igdb_id (uncopyable) but only a provisional
            // name; the absorbed row holds the real one.
            conn.Execute("""
                UPDATE works SET igdb_id = 77, name = 'hades', name_is_provisional = 1
                WHERE id = @id;
                """, new { id = seed.LeftWorkId });
            conn.Execute("""
                UPDATE works SET name = 'Hades', name_is_provisional = 0 WHERE id = @id;
                """, new { id = seed.RightWorkId });
        }

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var promoted = db.Factory.Open())
        {
            Assert.Equal("Hades", promoted.ExecuteScalar<string>(
                "SELECT name FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
            Assert.Equal(0, promoted.ExecuteScalar<long>(
                "SELECT name_is_provisional FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        using var after = db.Factory.Open();
        Assert.Equal("hades", after.ExecuteScalar<string>(
            "SELECT name FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
        Assert.Equal(1, after.ExecuteScalar<long>(
            "SELECT name_is_provisional FROM works WHERE id = @id;", new { id = seed.LeftWorkId }));
    }

    [Fact]
    public async Task The_ownership_account_in_place_merge_is_reverted_field_by_field()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");

        using (var conn = db.Factory.Open())
        {
            // One account, two ownerships. The absorbed row was seen later, so
            // the merge takes its playtime tuple whole and widens the window.
            conn.Execute("""
                INSERT INTO ownership_accounts (
                    ownership_id, account_ref, playtime_minutes, last_played_at,
                    source, first_seen_at, last_seen_at)
                VALUES
                    (@left,  '76561', 310, '2026-02-01 00:00:00', 'steam_web',
                     '2026-01-01 00:00:00', '2026-02-02 00:00:00'),
                    (@right, '76561', 145, '2026-03-01 00:00:00', 'steam_local',
                     '2026-02-15 00:00:00', '2026-03-02 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
        }

        var before = Snapshot(db, ["ownership_accounts"]);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var merged = db.Factory.Open())
        {
            var row = merged.QueryFirst("""
                SELECT playtime_minutes, last_played_at, source, first_seen_at, last_seen_at
                FROM ownership_accounts WHERE ownership_id = @id;
                """, new { id = seed.LeftOwnershipId });

            Assert.Equal(145, (long)row.playtime_minutes);
            Assert.Equal("steam_local", (string)row.source);
            Assert.Equal("2026-01-01 00:00:00", (string)row.first_seen_at);
            Assert.Equal("2026-03-02 00:00:00", (string)row.last_seen_at);
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["ownership_accounts"]));
    }

    // ── Deduplicated rows: the cases a count cannot restore ──────────────────

    [Fact]
    public async Task A_deduplicated_play_record_comes_back_on_the_restored_ownership()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");

        using (var conn = db.Factory.Open())
        {
            // A byte-identical observation on both sides, which
            // ux_play_records_observation collapses to one on the fold.
            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@left,  99, '2026-04-01 00:00:00', 'steam_local', '2026-04-02 00:00:00'),
                       (@right, 99, '2026-04-01 00:00:00', 'steam_local', '2026-04-02 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
        }

        var before = Snapshot(db, ["play_records"]);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var merged = db.Factory.Open())
        {
            Assert.Equal(1, merged.ExecuteScalar<long>("""
                SELECT COUNT(*) FROM play_records
                WHERE playtime_minutes = 99 AND source = 'steam_local';
                """));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["play_records"]));
    }

    [Fact]
    public async Task A_deduplicated_playtime_snapshot_comes_back()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                VALUES (@left,  240, '2026-05-01 00:00:00'),
                       (@right, 240, '2026-05-01 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
        }

        var before = Snapshot(db, ["playtime_snapshots"]);
        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["playtime_snapshots"]));
    }

    [Fact]
    public async Task A_folded_ownership_account_comes_back_with_its_own_payload_not_the_survivors()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO ownership_accounts (
                    ownership_id, account_ref, playtime_minutes, last_played_at,
                    source, first_seen_at, last_seen_at)
                VALUES
                    (@left,  '76561', 310, '2026-02-01 00:00:00', 'steam_web',
                     '2026-01-01 00:00:00', '2026-02-02 00:00:00'),
                    (@right, '76561', 145, '2026-03-01 00:00:00', 'steam_local',
                     '2026-02-15 00:00:00', '2026-03-02 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
        }

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(1, outcome.Repointed.OwnershipsFolded);

        var undone = await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        using var after = db.Factory.Open();
        var row = after.QueryFirst("""
            SELECT playtime_minutes, last_played_at, source, first_seen_at, last_seen_at
            FROM ownership_accounts WHERE ownership_id = @id;
            """, new { id = seed.RightOwnershipId });

        // Its own tuple, not a recombination and not the survivor's.
        Assert.Equal(145, (long)row.playtime_minutes);
        Assert.Equal("steam_local", (string)row.source);
        Assert.Equal("2026-02-15 00:00:00", (string)row.first_seen_at);
        Assert.Equal("2026-03-02 00:00:00", (string)row.last_seen_at);
        Assert.False(undone.IdentityIdsReused);
    }

    [Fact]
    public async Task A_dropped_list_item_comes_back_with_its_own_position()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            var listId = conn.ExecuteScalar<long>(
                "INSERT INTO lists (name) VALUES ('Backlog') RETURNING id;");
            conn.Execute("""
                INSERT INTO list_items (list_id, release_id, position) VALUES
                    (@listId, @left,  1),
                    (@listId, @right, 9);
                """, new { listId, left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var before = Snapshot(db, ["list_items"]);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var merged = db.Factory.Open())
        {
            // The survivor's position stands; the absorbed row's 9 is gone, and
            // no count could put it back.
            Assert.Equal(1, merged.ExecuteScalar<long>("SELECT position FROM list_items;"));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["list_items"]));
    }

    [Fact]
    public async Task A_dropped_release_facet_comes_back_with_its_own_rank()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO release_facets (release_id, facet_id, rank) VALUES
                    (@left,  1, 1),
                    (@right, 1, 7);
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var before = Snapshot(db, ["release_facets"]);
        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["release_facets"]));
    }

    [Fact]
    public async Task A_dropped_feed_surfacing_comes_back_with_its_own_shelf_id()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id) VALUES
                    (@left,  '2026-08-01', 'bounced'),
                    (@right, '2026-08-01', 'stale_but_patched');
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var before = Snapshot(db, ["feed_surfacings"]);
        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["feed_surfacings"]));
    }

    [Fact]
    public async Task A_dropped_work_facet_comes_back()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                INSERT INTO work_facets (work_id, facet_id) VALUES (@left, 1), (@right, 1), (@right, 2);
                """, new { left = seed.LeftWorkId, right = seed.RightWorkId });
        }

        var before = Snapshot(db, ["work_facets"]);
        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var merged = db.Factory.Open())
        {
            // One moved, one collided and was dropped.
            Assert.Equal(2, merged.ExecuteScalar<long>("SELECT COUNT(*) FROM work_facets;"));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["work_facets"]));
    }

    [Fact]
    public async Task A_deduplicated_update_event_comes_back_on_the_restored_release()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            // Identical on every column the conflict check looks at, so the
            // collapse is permitted and the absorbed row is dropped.
            conn.Execute("""
                INSERT INTO update_events (release_id, kind, build_id, occurred_at, title, raw_json, url)
                VALUES (@left,  'build_push', '900', '2026-07-01 00:00:00', 'Patch', '{}', 'http://p'),
                       (@right, 'build_push', '900', '2026-07-01 00:00:00', 'Patch', '{}', 'http://p'),
                       (@right, 'announcement', NULL, '2026-07-02 00:00:00', 'News', '{}', NULL);
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId });
        }

        var before = Snapshot(db, ["update_events"]);
        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(MergeMode.ReleaseCollapse, outcome.Plan.Mode);

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, ["update_events"]));
    }

    // ── The collision-heavy composite ────────────────────────────────────────

    [Fact]
    public async Task Every_deduplication_path_firing_at_once_round_trips()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam");
        Enrich(db, seed);
        Collide(db, seed);

        var before = Snapshot(db, DataTables);
        var candidatesBefore = Candidates(db);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        Assert.True(outcome.Applied);
        Assert.Equal(1, outcome.Repointed.OwnershipsFolded);
        Assert.True(outcome.Repointed.DuplicateRowsDropped >= 7);

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, DataTables));
        Assert.Equal(
            candidatesBefore.Select(c => c with { Status = MergeCandidateStatuses.Undone }),
            Candidates(db));
    }

    // ── merge_candidates ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_answered_pair_comes_back_with_its_score_and_signals_at_status_undone()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE merge_candidates SET signals_json = '{\"title\":1.0}' WHERE id = @id;",
                new { id = seed.CandidateId });
        }

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var merged = db.Factory.Open())
        {
            Assert.Equal(0, merged.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_candidates;"));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        using var after = db.Factory.Open();
        var row = after.QueryFirst(
            "SELECT id, left_release_id, right_release_id, score, signals_json, status FROM merge_candidates;");

        Assert.Equal(seed.CandidateId, (long)row.id);
        Assert.Equal(seed.LeftReleaseId, (long)row.left_release_id);
        Assert.Equal(seed.RightReleaseId, (long)row.right_release_id);
        Assert.Equal(0.93, (double)row.score);
        Assert.Equal("{\"title\":1.0}", (string)row.signals_json);
        Assert.Equal(MergeCandidateStatuses.Undone, (string)row.status);
    }

    [Fact]
    public async Task A_work_only_undo_flips_the_standing_row_from_confirmed_to_undone()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute(
                "UPDATE releases SET platform = 'switch' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(MergeMode.WorkOnly, outcome.Plan.Mode);

        using (var merged = db.Factory.Open())
        {
            // Nothing collapsed, so the row was never touched.
            Assert.Equal(MergeCandidateStatuses.Confirmed, merged.ExecuteScalar<string>(
                "SELECT status FROM merge_candidates WHERE id = @id;", new { id = seed.CandidateId }));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        using var after = db.Factory.Open();
        Assert.Equal(MergeCandidateStatuses.Undone, after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE id = @id;", new { id = seed.CandidateId }));
    }

    [Fact]
    public async Task A_third_party_pair_moves_back_and_is_recanonicalised()
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

            // A decision about the ABSORBED release and a third one. The
            // absorbed id is lower than the third, so pre-merge the pair reads
            // (absorbed, third); after the merge it reads (surviving, third),
            // which is the other orientation relative to its partner.
            conn.Execute("""
                INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
                VALUES (MIN(@right, @third), MAX(@right, @third), 0.7, 'rejected');
                """, new { right = seed.RightReleaseId, third = thirdReleaseId });
        }

        var before = Candidates(db);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        var survivor = outcome.Plan.SurvivingReleaseId!.Value;

        using (var merged = db.Factory.Open())
        {
            var moved = merged.QueryFirst(
                "SELECT left_release_id, right_release_id FROM merge_candidates WHERE status = 'rejected';");
            Assert.Equal(Math.Min(survivor, thirdReleaseId), (long)moved.left_release_id);
            Assert.Equal(Math.Max(survivor, thirdReleaseId), (long)moved.right_release_id);
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        // Back on the restored release, canonical, and the CHECK is satisfied
        // rather than merely un-substituted.
        Assert.Equal(
            before.Select(c => c.Status == MergeCandidateStatuses.Confirmed
                ? c with { Status = MergeCandidateStatuses.Undone }
                : c),
            Candidates(db));
    }

    [Fact]
    public async Task A_pending_proposal_displaced_by_a_decision_is_restored()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Third') RETURNING id;");
            var thirdReleaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Third') RETURNING id;",
                new { workId });

            // A decision about (absorbed, third) that will land on (surviving,
            // third), and a proposal already sitting there. The proposal gives
            // way; the journal is the only record it existed.
            conn.Execute("""
                INSERT INTO merge_candidates (left_release_id, right_release_id, score, status) VALUES
                    (MIN(@right, @third), MAX(@right, @third), 0.71, 'rejected'),
                    (MIN(@left,  @third), MAX(@left,  @third), 0.42, 'pending');
                """, new { left = seed.LeftReleaseId, right = seed.RightReleaseId, third = thirdReleaseId });
        }

        var before = Candidates(db);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var merged = db.Factory.Open())
        {
            Assert.Equal(0, merged.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM merge_candidates WHERE status = 'pending';"));
        }

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.Equal(
            before.Select(c => c.Status == MergeCandidateStatuses.Confirmed
                ? c with { Status = MergeCandidateStatuses.Undone }
                : c),
            Candidates(db));
    }

    // ── Loop prevention ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_undone_candidate_is_not_offered_for_re_application()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("UPDATE releases SET platform = 'switch' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var merges = Merge(db);
        var outcome = await merges.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        // The batch selector's predicate.
        Assert.Empty(await merges.GetConfirmedUnappliedCandidateIdsAsync());

        // The plan builder's predicate.
        var replan = await merges.PlanAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(MergeMode.NothingToDo, replan.Mode);
        Assert.Equal(MergeBlocker.CandidateNotConfirmed, replan.Blocker);

        var reapply = await merges.ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.False(reapply.Applied);
        Assert.Equal(MergeBlocker.CandidateNotConfirmed, reapply.Plan.Blocker);

        // And the ordinary confirm queue does not offer it either: re-merging
        // needs a deliberate re-confirmation, not the pending list.
        Assert.Empty(await new MergeCandidateRepository(db.Factory).GetPendingAsync());
    }

    [Fact]
    public async Task A_fresh_sweep_neither_requeues_an_undone_pair_nor_trips_the_unique_key()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        using (var conn = db.Factory.Open())
        {
            conn.Execute("UPDATE releases SET platform = 'switch' WHERE id = @id;",
                new { id = seed.RightReleaseId });
        }

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        var candidates = new MergeCandidateRepository(db.Factory);
        var resolver = new SoftMatchResolver(new SoftMatcher(), candidates, db.Factory);

        var left = new MatchSubject { ReleaseId = seed.LeftReleaseId, Title = "Hades", ReleaseYear = 2020 };
        var right = new MatchSubject { ReleaseId = seed.RightReleaseId, Title = "Hades", ReleaseYear = 2020 };

        var pass = await resolver.ResolveAsync([new SoftMatchRequest(left, [right])]);

        Assert.Equal(0, pass.Queued);
        Assert.Equal(1, pass.PreviouslyUndone);
        Assert.Equal(0, pass.AlreadyPending);

        var row = Assert.Single(await candidates.GetAllAsync());
        Assert.Equal(MergeCandidateStatuses.Undone, row.Status);
    }

    // ── Refusal ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_later_merge_that_consumed_the_survivor_blocks_the_earlier_undo()
    {
        using var db = new TempDatabase();
        var chain = SeedChain(db);

        var merges = Merge(db);
        var first = await merges.ApplyAsync(new MergeRequest { CandidateId = chain.FirstCandidateId });
        var second = await merges.ApplyAsync(new MergeRequest { CandidateId = chain.SecondCandidateId });

        var undo = Undo(db);
        var plan = await undo.PlanUndoAsync(first.ApplicationId!.Value);

        Assert.False(plan.Reversible);
        Assert.Equal(MergeUndoBlocker.LaterMergeConsumedIdentity, plan.PrimaryBlocker);
        Assert.Equal(second.ApplicationId, plan.BlockingApplicationId);

        var before = Snapshot(db, DataTables);
        var refusal = await Assert.ThrowsAsync<MergeUndoRefusedException>(
            () => undo.UndoAsync(first.ApplicationId!.Value));

        Assert.Equal(MergeUndoBlocker.LaterMergeConsumedIdentity, refusal.Blocker);
        Assert.Equal(before, Snapshot(db, DataTables));
    }

    [Fact]
    public async Task Undoing_the_later_merge_first_lets_both_reverse()
    {
        using var db = new TempDatabase();
        var chain = SeedChain(db);
        var before = Snapshot(db, DataTables);

        var merges = Merge(db);
        var first = await merges.ApplyAsync(new MergeRequest { CandidateId = chain.FirstCandidateId });
        var second = await merges.ApplyAsync(new MergeRequest { CandidateId = chain.SecondCandidateId });

        var undo = Undo(db);
        await undo.UndoAsync(second.ApplicationId!.Value);

        var plan = await undo.PlanUndoAsync(first.ApplicationId!.Value);
        Assert.True(plan.Reversible);

        await undo.UndoAsync(first.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, DataTables));
    }

    [Fact]
    public async Task Two_merges_on_disjoint_identities_undo_in_either_order()
    {
        using var db = new TempDatabase();
        var one = Seed(db, leftStore: "steam", rightStore: "epic");
        var two = Seed(db, leftStore: "gog", rightStore: "epic", title: "Celeste");

        var before = Snapshot(db, DataTables);

        var merges = Merge(db);
        var firstOutcome = await merges.ApplyAsync(new MergeRequest { CandidateId = one.CandidateId });
        var secondOutcome = await merges.ApplyAsync(new MergeRequest { CandidateId = two.CandidateId });

        var undo = Undo(db);
        Assert.True((await undo.PlanUndoAsync(firstOutcome.ApplicationId!.Value)).Reversible);

        // The earlier one first: LIFO is scoped to identities, not to position.
        await undo.UndoAsync(firstOutcome.ApplicationId!.Value);
        await undo.UndoAsync(secondOutcome.ApplicationId!.Value);

        Assert.Equal(before, Snapshot(db, DataTables));
    }

    [Fact]
    public async Task A_merge_recorded_without_a_journal_reports_that_it_predates_undo_support()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var conn = db.Factory.Open())
        {
            // The shape an install that upgrades across 0017 carries.
            conn.Execute("UPDATE merge_applications SET undo_journal_version = NULL;");
            conn.Execute("DELETE FROM merge_undo_rows;");
        }

        var undo = Undo(db);
        var plan = await undo.PlanUndoAsync(outcome.ApplicationId!.Value);

        Assert.False(plan.Reversible);
        Assert.Equal(MergeUndoBlocker.PredatesUndoSupport, plan.PrimaryBlocker);

        var refusal = await Assert.ThrowsAsync<MergeUndoRefusedException>(
            () => undo.UndoAsync(outcome.ApplicationId!.Value));
        Assert.Equal(MergeUndoBlocker.PredatesUndoSupport, refusal.Blocker);
    }

    [Fact]
    public async Task A_journalled_row_something_else_deleted_aborts_the_undo()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var conn = db.Factory.Open())
        {
            // A repointed row that is no longer where the merge left it.
            conn.Execute("DELETE FROM external_ids WHERE provider_id LIKE 'right-id%';");
        }

        var before = Snapshot(db, DataTables);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Undo(db).UndoAsync(outcome.ApplicationId!.Value));

        Assert.Contains("external_ids", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(db, DataTables));

        using var after = db.Factory.Open();
        Assert.Null(after.ExecuteScalar<string>("SELECT undone_at FROM merge_applications;"));
    }

    [Fact]
    public async Task A_reused_absorbed_id_restores_the_identity_at_a_fresh_one()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(seed.RightReleaseId, outcome.Plan.AbsorbedReleaseId);

        using (var conn = db.Factory.Open())
        {
            // SQLite hands out max+1, and the absorbed rows held the maxima, so
            // these take the freed ids.
            var squatterWorkId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Squatter') RETURNING id;");
            var squatterReleaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@w, 'Squatter') RETURNING id;",
                new { w = squatterWorkId });

            Assert.Equal(seed.RightWorkId, squatterWorkId);
            Assert.Equal(seed.RightReleaseId, squatterReleaseId);
        }

        var result = await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        Assert.True(result.IdentityIdsReused);
        Assert.NotEqual(seed.RightReleaseId, result.RestoredReleaseId);
        Assert.NotEqual(seed.RightWorkId, result.RestoredWorkId);

        using var after = db.Factory.Open();

        // Every journalled child landed on the fresh identity.
        Assert.Equal(seed.RightOwnershipId, after.ExecuteScalar<long>(
            "SELECT id FROM ownerships WHERE release_id = @id;",
            new { id = result.RestoredReleaseId }));
        Assert.Equal("right-id" + seed.RightReleaseId, after.ExecuteScalar<string>(
            "SELECT provider_id FROM external_ids WHERE release_id = @id;",
            new { id = result.RestoredReleaseId }));
        Assert.Equal(result.RestoredWorkId, after.ExecuteScalar<long>(
            "SELECT work_id FROM releases WHERE id = @id;",
            new { id = result.RestoredReleaseId }));

        // And the restored pair is canonical whichever side the fresh id sorts on.
        var pair = after.QueryFirst(
            "SELECT left_release_id, right_release_id FROM merge_candidates;");
        Assert.True((long)pair.left_release_id < (long)pair.right_release_id);
    }

    [Fact]
    public async Task Undoing_twice_does_not_double_restore()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        Enrich(db, seed);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        var undo = Undo(db);
        await undo.UndoAsync(outcome.ApplicationId!.Value);

        var afterFirst = Snapshot(db, DataTables);

        var refusal = await Assert.ThrowsAsync<MergeUndoRefusedException>(
            () => undo.UndoAsync(outcome.ApplicationId!.Value));

        Assert.Equal(MergeUndoBlocker.AlreadyUndone, refusal.Blocker);
        Assert.Equal(afterFirst, Snapshot(db, DataTables));
    }

    [Fact]
    public async Task A_failure_mid_undo_leaves_the_database_exactly_as_it_was()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");
        Enrich(db, seed);

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        var merged = Snapshot(db, DataTables);

        using (var conn = db.Factory.Open())
        {
            // Fires during the repoint reversals, after the restore has already
            // re-inserted the absorbed identities.
            conn.Execute("""
                CREATE TRIGGER fail_partway BEFORE UPDATE ON external_ids
                BEGIN
                    SELECT RAISE(ABORT, 'injected mid-undo failure');
                END;
                """);
        }

        await Assert.ThrowsAsync<SqliteException>(
            () => Undo(db).UndoAsync(outcome.ApplicationId!.Value));

        Assert.Equal(merged, Snapshot(db, DataTables));

        using var after = db.Factory.Open();
        Assert.Null(after.ExecuteScalar<string>("SELECT undone_at FROM merge_applications;"));
    }

    // ── Structural ───────────────────────────────────────────────────────────

    [Fact]
    public void The_journal_inventory_equals_the_executors_dependent_table_inventory()
    {
        // achievements and achievement_unlocks are dependents that never move,
        // so the tripwire counts them and the journal has nothing to record.
        // works is the other way round: a merge edits and deletes it, but it is
        // nobody's dependent.
        var journalled = MergeUndoJournal.All
            .Select(table => table.Name)
            .Concat(["achievements", "achievement_unlocks"])
            .Except(["works"])
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(MergeExecutionRepository.DependentTables, journalled);
    }

    [Fact]
    public void Every_journalled_table_lists_every_column_the_schema_has()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        foreach (var table in MergeUndoJournal.All)
        {
            var live = conn.Query<string>(
                    "SELECT name FROM pragma_table_info(@table) ORDER BY name;", new { table = table.Name })
                .ToList();

            // A migration that adds a column and does not add it here would make
            // the journal quietly lossy for that column.
            Assert.Equal(live, table.Columns.Order(StringComparer.Ordinal).ToList());
        }
    }

    [Fact]
    public async Task A_summary_count_that_disagrees_with_the_journal_aborts_the_undo()
    {
        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "epic");

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });

        using (var conn = db.Factory.Open())
        {
            conn.Execute("""
                UPDATE merge_applications
                SET summary_json = json_set(summary_json, '$.external_ids', 99);
                """);
        }

        var before = Snapshot(db, DataTables);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Undo(db).UndoAsync(outcome.ApplicationId!.Value));

        Assert.Contains("summary says 99", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(db, DataTables));
    }

    // ── Buckets ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_merge_that_moved_a_game_between_buckets_moves_it_back()
    {
        var thresholds = new BucketThresholds(
            BouncedFloorMinutes: 120,
            RetiredFloorMinutes: 3000,
            StaleWindowMonths: 3,
            UpdateCorrelationWindowDays: 7);

        using var db = new TempDatabase();
        var seed = Seed(db, leftStore: "steam", rightStore: "steam", withPlayRecords: false);

        using (var conn = db.Factory.Open())
        {
            // Zero minutes and no date on the survivor: never touched. The
            // absorbed side's ownership carries the minutes, and folding the two
            // same-store ownerships moves them onto the survivor.
            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@left,  0,   NULL,                  'steam_local', '2026-01-01 00:00:00'),
                       (@right, 240, '2026-03-01 00:00:00', 'steam_local', '2026-03-02 00:00:00');
                """, new { left = seed.LeftOwnershipId, right = seed.RightOwnershipId });
        }

        var query = new LibraryQueryRepository(db.Factory);

        Assert.Equal(
            LibraryBuckets.NeverPlayed,
            Bucket(await query.GetOwnershipBucketsAsync(thresholds), seed.LeftOwnershipId));

        var outcome = await Merge(db).ApplyAsync(new MergeRequest { CandidateId = seed.CandidateId });
        Assert.Equal(1, outcome.Repointed.OwnershipsFolded);

        Assert.Equal(
            LibraryBuckets.Bounced,
            Bucket(await query.GetOwnershipBucketsAsync(thresholds), seed.LeftOwnershipId));

        await Undo(db).UndoAsync(outcome.ApplicationId!.Value);

        var restored = await query.GetOwnershipBucketsAsync(thresholds);
        Assert.Equal(LibraryBuckets.NeverPlayed, Bucket(restored, seed.LeftOwnershipId));
        Assert.Equal(LibraryBuckets.Bounced, Bucket(restored, seed.RightOwnershipId));
    }

    private static string Bucket(IReadOnlyList<OwnershipBucket> buckets, long ownershipId)
        => Assert.Single(buckets, b => b.OwnershipId == ownershipId).Bucket;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static MergeExecutionRepository Merge(TempDatabase db) => new(db.Factory);

    private static MergeUndoRepository Undo(TempDatabase db) => new(db.Factory);

    private sealed record SeedIds(
        long CandidateId,
        long LeftWorkId,
        long RightWorkId,
        long LeftReleaseId,
        long RightReleaseId,
        long LeftOwnershipId,
        long RightOwnershipId);

    private sealed record ChainIds(long FirstCandidateId, long SecondCandidateId);

    private static SeedIds Seed(
        TempDatabase db,
        string leftStore,
        string rightStore,
        string title = "Hades",
        bool withPlayRecords = true)
    {
        using var conn = db.Factory.Open();

        var leftWorkId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@title) RETURNING id;", new { title });
        var rightWorkId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@title) RETURNING id;", new { title });

        var leftReleaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@leftWorkId, @title) RETURNING id;",
            new { leftWorkId, title });
        var rightReleaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@rightWorkId, @title) RETURNING id;",
            new { rightWorkId, title });

        conn.Execute("""
            INSERT INTO external_ids (release_id, provider, provider_id) VALUES
                (@leftReleaseId,  @leftStore,  @leftKey),
                (@rightReleaseId, @rightStore, @rightKey);
            """,
            new
            {
                leftReleaseId,
                rightReleaseId,
                leftStore,
                rightStore,
                leftKey = "left-id" + leftReleaseId,
                rightKey = "right-id" + rightReleaseId,
            });

        var leftOwnershipId = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@leftReleaseId, @leftStore) RETURNING id;",
            new { leftReleaseId, leftStore });
        var rightOwnershipId = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@rightReleaseId, @rightStore) RETURNING id;",
            new { rightReleaseId, rightStore });

        if (withPlayRecords)
        {
            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@leftOwnershipId,  310, '2026-02-01 00:00:00', 'steam_web',   '2026-02-02 00:00:00'),
                       (@rightOwnershipId, 145, '2026-03-01 00:00:00', 'steam_local', '2026-03-02 00:00:00');
                """, new { leftOwnershipId, rightOwnershipId });
        }

        var candidateId = conn.ExecuteScalar<long>("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (@leftReleaseId, @rightReleaseId, 0.93, 'confirmed')
            RETURNING id;
            """, new { leftReleaseId, rightReleaseId });

        return new SeedIds(
            candidateId, leftWorkId, rightWorkId,
            leftReleaseId, rightReleaseId, leftOwnershipId, rightOwnershipId);
    }

    /// <summary>Rows on both sides that a merge has to carry but never dedupes.</summary>
    private static void Enrich(TempDatabase db, SeedIds seed)
    {
        using var conn = db.Factory.Open();

        var listId = conn.ExecuteScalar<long>(
            "INSERT INTO lists (name) VALUES ('Backlog') RETURNING id;");

        conn.Execute("""
            INSERT INTO list_items (list_id, release_id, position) VALUES (@listId, @left, 3);
            INSERT INTO release_facets (release_id, facet_id, rank) VALUES (@left, 1, 2);
            INSERT INTO work_facets (work_id, facet_id) VALUES (@leftWork, 3);
            INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id)
                VALUES (@right, '2026-06-01', 'bounced');
            INSERT INTO feed_verdicts (release_id, kind, created_at)
                VALUES (@right, 'not_interested', '2026-06-02 00:00:00');
            INSERT INTO update_events (release_id, kind, build_id, occurred_at, title)
                VALUES (@right, 'build_push', '1', '2026-06-03 00:00:00', 'Patch');
            INSERT INTO update_acknowledgements (release_id, acknowledged_through, created_at)
                VALUES (@right, '2026-06-03 00:00:00', '2026-06-04 00:00:00');
            INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                VALUES (@rightOwnership, 145, '2026-03-02 00:00:00');
            INSERT INTO sessions (ownership_id, started_at, ended_at, duration_s, detection_method)
                VALUES (@rightOwnership, '2026-03-01 10:00:00', '2026-03-01 11:00:00', 3600, 'process_watch');
            """,
            new
            {
                listId,
                left = seed.LeftReleaseId,
                right = seed.RightReleaseId,
                leftWork = seed.LeftWorkId,
                rightOwnership = seed.RightOwnershipId,
            });

        var sessionId = conn.ExecuteScalar<long>("SELECT MAX(id) FROM sessions;");
        conn.Execute(
            "INSERT INTO session_notes (session_id, note, rating) VALUES (@sessionId, 'Good run', 4);",
            new { sessionId });
    }

    /// <summary>Every deduplication path a merge has, all firing at once.</summary>
    private static void Collide(TempDatabase db, SeedIds seed)
    {
        using var conn = db.Factory.Open();

        var listId = conn.ExecuteScalar<long>("SELECT MIN(id) FROM lists;");

        conn.Execute("""
            INSERT INTO list_items (list_id, release_id, position) VALUES (@listId, @right, 11);
            INSERT INTO release_facets (release_id, facet_id, rank) VALUES (@right, 1, 6);
            INSERT INTO work_facets (work_id, facet_id) VALUES (@rightWork, 3), (@rightWork, 4);
            INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id)
                VALUES (@left, '2026-06-01', 'never_touched');
            INSERT INTO update_events (release_id, kind, build_id, occurred_at, title)
                VALUES (@left, 'build_push', '1', '2026-06-03 00:00:00', 'Patch');
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@leftOwnership,  501, '2026-05-01 00:00:00', 'steam_local', '2026-05-02 00:00:00'),
                       (@rightOwnership, 501, '2026-05-01 00:00:00', 'steam_local', '2026-05-02 00:00:00');
            INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                VALUES (@leftOwnership, 145, '2026-03-02 00:00:00');
            INSERT INTO ownership_accounts (
                ownership_id, account_ref, playtime_minutes, last_played_at,
                source, first_seen_at, last_seen_at)
            VALUES
                (@leftOwnership,  '76561', 310, '2026-02-01 00:00:00', 'steam_web',
                 '2026-01-01 00:00:00', '2026-02-02 00:00:00'),
                (@rightOwnership, '76561', 145, '2026-03-01 00:00:00', 'steam_local',
                 '2026-02-15 00:00:00', '2026-03-02 00:00:00'),
                (@rightOwnership, '99999', 12, NULL, 'steam_local',
                 '2026-02-15 00:00:00', '2026-03-02 00:00:00');
            """,
            new
            {
                listId,
                left = seed.LeftReleaseId,
                right = seed.RightReleaseId,
                rightWork = seed.RightWorkId,
                leftOwnership = seed.LeftOwnershipId,
                rightOwnership = seed.RightOwnershipId,
            });
    }

    /// <summary>
    /// Three releases and two confirmed pairs, so the second merge absorbs the
    /// identity the first one produced.
    /// </summary>
    private static ChainIds SeedChain(TempDatabase db)
    {
        using var conn = db.Factory.Open();

        var ids = new List<long>();
        for (var index = 0; index < 3; index++)
        {
            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Celeste') RETURNING id;");
            var releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Celeste') RETURNING id;",
                new { workId });
            conn.Execute("""
                INSERT INTO external_ids (release_id, provider, provider_id)
                VALUES (@releaseId, 'steam', @key);
                """, new { releaseId, key = "celeste-" + index });
            ids.Add(releaseId);
        }

        // (0,1) collapses onto 0; then (0,2) collapses onto 0, absorbing the
        // release the first merge left standing.
        var first = conn.ExecuteScalar<long>("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (@a, @b, 0.9, 'confirmed') RETURNING id;
            """, new { a = ids[0], b = ids[1] });
        var second = conn.ExecuteScalar<long>("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (@a, @c, 0.9, 'confirmed') RETURNING id;
            """, new { a = ids[0], c = ids[2] });

        return new ChainIds(first, second);
    }

    // ── Snapshots ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every data table a merge can touch. merge_applications and
    /// merge_undo_rows are deliberately absent: an undo marks the first and
    /// keeps the second, so they are the two tables that must NOT come back.
    /// merge_candidates is absent too, because its status legitimately changes;
    /// <see cref="Candidates"/> checks it row by row instead.
    /// </summary>
    private static readonly string[] DataTables =
    [
        "works", "releases", "external_ids", "ownerships",
        "play_records", "playtime_snapshots", "sessions", "session_notes",
        "ownership_accounts", "achievements", "achievement_unlocks",
        "update_events", "update_acknowledgements", "lists", "list_items",
        "facets", "work_facets", "release_facets", "feed_verdicts", "feed_surfacings",
    ];

    private static string Snapshot(TempDatabase db, string[] tables)
    {
        using var conn = db.Factory.Open();
        var lines = new List<string>();

        foreach (var table in tables)
        {
            var columns = conn.Query<string>(
                    "SELECT name FROM pragma_table_info(@table) ORDER BY cid;", new { table })
                .ToList();
            var projection = string.Join(
                " || '|' || ", columns.Select(c => $"COALESCE(CAST(\"{c}\" AS TEXT), '<null>')"));

            foreach (var row in conn.Query<string>($"SELECT {projection} FROM {table} ORDER BY 1;"))
            {
                lines.Add($"{table}: {row}");
            }
        }

        return string.Join('\n', lines);
    }

    private static List<MergeCandidate> Candidates(TempDatabase db)
    {
        using var conn = db.Factory.Open();
        return conn.Query<MergeCandidate>("""
            SELECT id               AS Id,
                   left_release_id  AS LeftReleaseId,
                   right_release_id AS RightReleaseId,
                   score            AS Score,
                   signals_json     AS SignalsJson,
                   status           AS Status
            FROM merge_candidates
            ORDER BY left_release_id, right_release_id;
            """).ToList();
    }

    private static List<string> Rows(TempDatabase db, string sql, object parameters)
    {
        using var conn = db.Factory.Open();
        return conn.Query<string>(sql, parameters).Order(StringComparer.Ordinal).ToList();
    }
}
