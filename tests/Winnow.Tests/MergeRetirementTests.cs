
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Data;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Migration 0019 retires the destructive merge. Before it drops the
/// undo journal, a C# one-shot (<see cref="StandingMergeReplay"/>,
/// between 0018 and 0019) replays every merge still standing into a
/// restored work plus a live identity link, keeping the decision and
/// recovering every row.
///
/// <para>These tests drive the real path:
/// <see cref="DatabaseInitializer.Initialize"/>, the two-pass upgrade,
/// the replay, then 0019. Each one rewinds a migrated database past
/// 0019 and seeds the state a merge would have left, because the
/// executor that used to leave it has been deleted. The journal is the
/// contract, not the executor: a database upgrading from an older build
/// arrives carrying exactly these rows.</para>
/// </summary>
public sealed class MergeRetirementTests
{
    [Fact]
    public void Enrichment_replacing_a_moved_release_facet_restores_membership_with_unknown_rank()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);
        var merge = SeedMissingReleaseFacet(db);
        long currentFacet;
        using (var seed = db.Factory.Open())
        {
            currentFacet = seed.ExecuteScalar<long>("""
                INSERT INTO facets (kind, slug, name)
                VALUES ('store_tag', 'current', 'Current tag') RETURNING id;
                """);
            seed.Execute("INSERT INTO release_facets VALUES (@SurvivingRelease, @currentFacet, 1);",
                new { merge.SurvivingRelease, currentFacet });
        }

        db.Initializer.Initialize();
        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        var restored = Assert.Single(after.Query<(long Facet, int? Rank)>(
            "SELECT facet_id, rank FROM release_facets WHERE release_id = @AbsorbedRelease;", merge));
        Assert.Equal(merge.FacetId, restored.Facet);
        Assert.Null(restored.Rank);
        Assert.Equal([(currentFacet, (int?)1)], after.Query<(long Facet, int? Rank)>(
            "SELECT facet_id, rank FROM release_facets WHERE release_id = @SurvivingRelease;", merge));
        Assert.Equal(merge.AbsorbedWork, after.ExecuteScalar<long>(
            "SELECT work_id FROM releases WHERE id = @AbsorbedRelease;", merge));
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM identity_links;"));
        Assert.Empty(after.Query("PRAGMA foreign_key_check;"));
    }

    [Theory]
    [InlineData("wrong-source")]
    [InlineData("wrong-target")]
    [InlineData("missing-source-release")]
    [InlineData("not-restored-by-journal")]
    public void Missing_release_facet_recovery_requires_a_journal_restored_application_release(string invalid)
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);
        var merge = SeedMissingReleaseFacet(db);
        using (var seed = db.Factory.Open())
        {
            seed.Execute(invalid switch
            {
                "wrong-source" => """
                    UPDATE merge_undo_rows SET key_json = json_object('release_id', -1, 'facet_id', @FacetId)
                    WHERE application_id = @ApplicationId AND table_name = 'release_facets';
                    """,
                "wrong-target" => """
                    UPDATE merge_undo_rows SET before_json = json_object('release_id', -1)
                    WHERE application_id = @ApplicationId AND table_name = 'release_facets';
                    """,
                "missing-source-release" => "DELETE FROM releases WHERE id = @SurvivingRelease;",
                _ => """
                    DELETE FROM merge_undo_rows WHERE application_id = @ApplicationId AND table_name = 'releases';
                    INSERT INTO releases (id, work_id, name) VALUES (@AbsorbedRelease, @SurvivingWork, 'Later release');
                    """,
            }, merge);
        }

        var refused = Assert.ThrowsAny<InvalidOperationException>(db.Initializer.Initialize);

        Assert.Contains("release_facets", refused.Message, StringComparison.Ordinal);
        AssertReplayRolledBack(db, merge);
        using var after = db.Factory.Open();
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM release_facets;"));
    }

    private static FacetMerge SeedMissingReleaseFacet(TempDatabase db)
    {
        var merge = SeedMissingWorkFacet(db);
        using var seed = db.Factory.Open();
        seed.Execute("""
            UPDATE merge_undo_rows SET op = 'delete', key_json = json_object('id', @AbsorbedRelease),
                before_json = json_object('id', @AbsorbedRelease, 'work_id', @AbsorbedWork, 'name', 'Absorbed')
            WHERE application_id = @ApplicationId AND table_name = 'releases';
            DELETE FROM releases WHERE id = @AbsorbedRelease;
            UPDATE merge_undo_rows SET table_name = 'release_facets',
                key_json = json_object('release_id', @SurvivingRelease, 'facet_id', @FacetId),
                before_json = json_object('release_id', @AbsorbedRelease)
            WHERE application_id = @ApplicationId AND table_name = 'work_facets';
            """, merge);
        return merge;
    }

    [Fact]
    public void Journal_integer_identifiers_retain_precision_above_double_exact_range()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);
        const long absorbedWork = 9_007_199_254_740_993;
        long absorbedRelease;
        using (var seed = db.Factory.Open())
        {
            var (survivingWork, survivingRelease) = PreRetirementDatabase.SeedGame(seed, "Survivor");
            seed.Execute("INSERT INTO works (id, name) VALUES (@absorbedWork, 'Absorbed');", new { absorbedWork });
            absorbedRelease = seed.ExecuteScalar<long>("""
                INSERT INTO releases (work_id, name) VALUES (@absorbedWork, 'Absorbed') RETURNING id;
                """, new { absorbedWork });
            PreRetirementDatabase.SeedCandidate(seed, survivingRelease, absorbedRelease, "confirmed");
            PreRetirementDatabase.ApplyMergeByHand(
                seed, survivingWork, absorbedWork, survivingRelease, absorbedRelease, journalVersion: 1);
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        Assert.Equal("Absorbed", after.ExecuteScalar<string>("SELECT name FROM works WHERE id = @absorbedWork;", new { absorbedWork }));
        Assert.Equal(absorbedWork, after.ExecuteScalar<long>(
            "SELECT work_id FROM releases WHERE id = @absorbedRelease;", new { absorbedRelease }));
        Assert.Equal(absorbedWork, after.ExecuteScalar<long>("SELECT child_work_id FROM identity_links;"));
        Assert.Empty(after.Query("PRAGMA foreign_key_check;"));
    }

    [Fact]
    public void Enrichment_replacing_a_moved_work_facet_does_not_block_retirement()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);
        var merge = SeedMissingWorkFacet(db);
        long currentFacet;
        using (var seed = db.Factory.Open())
        {
            currentFacet = seed.ExecuteScalar<long>("""
                INSERT INTO facets (kind, slug, name)
                VALUES ('genre', 'current', 'Current genre') RETURNING id;
                """);
            seed.Execute("INSERT INTO work_facets VALUES (@SurvivingWork, @currentFacet);",
                new { merge.SurvivingWork, currentFacet });
        }

        db.Initializer.Initialize();
        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        Assert.Equal([merge.FacetId], after.Query<long>(
            "SELECT facet_id FROM work_facets WHERE work_id = @AbsorbedWork;", merge));
        Assert.Equal([currentFacet], after.Query<long>(
            "SELECT facet_id FROM work_facets WHERE work_id = @SurvivingWork;", merge));
        Assert.Equal(merge.AbsorbedWork, after.ExecuteScalar<long>(
            "SELECT work_id FROM releases WHERE id = @AbsorbedRelease;", merge));
        var link = Assert.Single(after.Query<(long Child, long Parent)>(
            "SELECT child_work_id, parent_work_id FROM identity_links;"));
        Assert.Equal((merge.AbsorbedWork, merge.SurvivingWork), link);
        Assert.Empty(after.Query<string>("SELECT name FROM sqlite_master WHERE name = 'merge_undo_rows';"));
        Assert.Empty(after.Query("PRAGMA foreign_key_check;"));
    }

    [Theory]
    [InlineData("missing-vocabulary")]
    [InlineData("wrong-parent")]
    [InlineData("wrong-child")]
    [InlineData("wrong-operation")]
    public void Missing_work_facet_recovery_requires_exact_journal_identity(string invalid)
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);
        var merge = SeedMissingWorkFacet(db);
        using (var seed = db.Factory.Open())
        {
            seed.Execute(invalid switch
            {
                "missing-vocabulary" => "DELETE FROM facets WHERE id = @FacetId;",
                "wrong-parent" => """
                    UPDATE merge_undo_rows SET key_json = json_object('work_id', -1, 'facet_id', @FacetId)
                    WHERE application_id = @ApplicationId AND table_name = 'work_facets';
                    """,
                "wrong-child" => """
                    UPDATE merge_undo_rows SET before_json = json_object('work_id', @SurvivingWork)
                    WHERE application_id = @ApplicationId AND table_name = 'work_facets';
                    """,
                _ => """
                    UPDATE merge_undo_rows SET op = 'update'
                    WHERE application_id = @ApplicationId AND table_name = 'work_facets';
                    """,
            }, merge);
        }

        var refused = Assert.ThrowsAny<InvalidOperationException>(db.Initializer.Initialize);

        Assert.Contains("work_facets", refused.Message, StringComparison.Ordinal);
        AssertReplayRolledBack(db, merge);
    }

    [Theory]
    [InlineData("release")]
    [InlineData("list-item")]
    public void Missing_identity_or_user_row_still_refuses_and_rolls_back_facet_recovery(string missing)
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);
        var merge = SeedMissingWorkFacet(db);
        using (var seed = db.Factory.Open())
        {
            // Put the fatal journal row after the recoverable facet so this
            // verifies the recovery participates in the replay transaction.
            if (missing == "release")
            {
                seed.Execute("""
                    UPDATE merge_undo_rows SET seq = 4
                    WHERE application_id = @ApplicationId AND table_name = 'releases';
                    DELETE FROM releases WHERE id = @AbsorbedRelease;
                    """, merge);
            }
            else
            {
                seed.Execute("""
                    INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
                    VALUES (@ApplicationId, 4, 'list_items', 'repoint',
                        json_object('list_id', 1, 'release_id', @SurvivingRelease),
                        json_object('release_id', @AbsorbedRelease));
                    """, merge);
            }
        }

        var refused = Assert.ThrowsAny<InvalidOperationException>(db.Initializer.Initialize);

        Assert.Contains(missing == "release" ? "releases" : "list_items", refused.Message, StringComparison.Ordinal);
        AssertReplayRolledBack(db, merge);
    }

    private static FacetMerge SeedMissingWorkFacet(TempDatabase db)
    {
        using var seed = db.Factory.Open();
        var (survivingWork, survivingRelease) = PreRetirementDatabase.SeedGame(seed, "Survivor");
        var (absorbedWork, absorbedRelease) = PreRetirementDatabase.SeedGame(seed, "Absorbed");
        PreRetirementDatabase.SeedCandidate(seed, survivingRelease, absorbedRelease, "confirmed");
        var applicationId = PreRetirementDatabase.ApplyMergeByHand(
            seed, survivingWork, absorbedWork, survivingRelease, absorbedRelease, journalVersion: 1);
        var facetId = seed.ExecuteScalar<long>("""
            INSERT INTO facets (kind, slug, name)
            VALUES ('genre', 'previous', 'Previous genre') RETURNING id;
            """);
        var merge = new FacetMerge(applicationId, survivingWork, absorbedWork, survivingRelease, absorbedRelease, facetId);
        seed.Execute("""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            VALUES (@ApplicationId, 3, 'work_facets', 'repoint',
                json_object('work_id', @SurvivingWork, 'facet_id', @FacetId),
                json_object('work_id', @AbsorbedWork));
            """, merge);
        return merge;
    }

    private static void AssertReplayRolledBack(TempDatabase db, FacetMerge merge)
    {
        using var after = db.Factory.Open();
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM works WHERE id = @AbsorbedWork;", merge));
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM work_facets WHERE work_id = @AbsorbedWork;", merge));
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM identity_links;"));
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications WHERE undone_at IS NULL;"));
    }

    private sealed record FacetMerge(
        long ApplicationId, long SurvivingWork, long AbsorbedWork, long SurvivingRelease, long AbsorbedRelease, long FacetId);

    [Fact]
    public void A_standing_merge_with_a_journal_becomes_a_restored_work_and_a_live_link()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);

        long survivingWork;
        long absorbedWork;
        long survivingRelease;
        long absorbedRelease;

        using (var seed = db.Factory.Open())
        {
            (survivingWork, survivingRelease) = PreRetirementDatabase.SeedGame(seed, "Prey");
            (absorbedWork, absorbedRelease) = PreRetirementDatabase.SeedGame(seed, "Prey (Epic)");

            PreRetirementDatabase.SeedCandidate(seed, survivingRelease, absorbedRelease, "confirmed");
            PreRetirementDatabase.ApplyMergeByHand(
                seed, survivingWork, absorbedWork, survivingRelease, absorbedRelease,
                journalVersion: 1);

            // The state a work-only merge leaves: the absorbed works row is
            // gone and both store entries hang off the survivor.
            Assert.Equal(0, seed.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM works WHERE id = @absorbedWork;", new { absorbedWork }));
            Assert.Equal(2, seed.ExecuteScalar<long>(
                "SELECT COUNT(*) FROM releases WHERE work_id = @survivingWork;",
                new { survivingWork }));
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        // The absorbed game is back, at its own id, with its own name.
        Assert.Equal("Prey (Epic)", after.ExecuteScalar<string>(
            "SELECT name FROM works WHERE id = @absorbedWork;", new { absorbedWork }));

        // And its store entry is back under it.
        Assert.Equal(absorbedWork, after.ExecuteScalar<long>(
            "SELECT work_id FROM releases WHERE id = @absorbedRelease;", new { absorbedRelease }));

        // The decision survived as a link, not as a deletion.
        var link = after.QuerySingle<(long Child, long Parent, string Kind, string? Retracted)>("""
            SELECT child_work_id, parent_work_id, kind, retracted_at FROM identity_links;
            """);
        Assert.Equal(absorbedWork, link.Child);
        Assert.Equal(survivingWork, link.Parent);
        Assert.Equal("same_game", link.Kind);
        Assert.Null(link.Retracted);

        // One act, and it is a link act, so it retracts like any other.
        Assert.Equal("link", after.ExecuteScalar<string>("SELECT kind FROM identity_acts;"));

        // The journal and the log are gone.
        Assert.Empty(after.Query<string>(
            "SELECT name FROM sqlite_master "
            + "WHERE name IN ('merge_applications', 'merge_undo_rows');"));
    }

    [Fact]
    public void A_standing_merge_without_a_journal_fails_the_migration_by_name()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);

        long applicationId;
        using (var seed = db.Factory.Open())
        {
            var (survivingWork, survivingRelease) = PreRetirementDatabase.SeedGame(seed, "Prey");
            var (absorbedWork, absorbedRelease) = PreRetirementDatabase.SeedGame(seed, "Prey (Epic)");

            PreRetirementDatabase.SeedCandidate(seed, survivingRelease, absorbedRelease, "confirmed");
            applicationId = PreRetirementDatabase.ApplyMergeByHand(
                seed, survivingWork, absorbedWork, survivingRelease, absorbedRelease,
                journalVersion: null);
        }

        var refused = Assert.ThrowsAny<InvalidOperationException>(db.Initializer.Initialize);

        // Named, so the person reading the crash knows which merge to deal
        // with, and told what to do about it.
        Assert.Contains(
            applicationId.ToString(CultureInfo.InvariantCulture),
            refused.Message,
            StringComparison.Ordinal);
        Assert.Contains("0017", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing has been changed", refused.Message, StringComparison.Ordinal);

        // And nothing was changed: 0019 did not run, so the log is still there.
        using var after = db.Factory.Open();
        Assert.Equal(1, after.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_applications;"));
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM identity_links;"));
    }

    [Fact]
    public void An_already_undone_merge_needs_nothing_but_its_candidate_reset()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);

        long left;
        long right;
        using (var seed = db.Factory.Open())
        {
            var (survivingWork, survivingRelease) = PreRetirementDatabase.SeedGame(seed, "Prey");
            var (absorbedWork, absorbedRelease) = PreRetirementDatabase.SeedGame(seed, "Prey (Epic)");
            left = Math.Min(survivingRelease, absorbedRelease);
            right = Math.Max(survivingRelease, absorbedRelease);

            // Undone under the old model: the rows are already back and the
            // pair reads 'undone', which is exactly what made it terminal.
            PreRetirementDatabase.SeedCandidate(seed, survivingRelease, absorbedRelease, "undone");
            seed.Execute("""
                INSERT INTO merge_applications (
                    candidate_id, left_release_id, right_release_id, mode,
                    surviving_work_id, absorbed_work_id, applied_at,
                    undone_at, undo_journal_version)
                VALUES (1, @left, @right, 'work_only', @survivingWork, @absorbedWork,
                        '2026-08-30 10:00:00', '2026-08-30 11:00:00', 1);
                """,
                new { left, right, survivingWork, absorbedWork });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        // Back in the queue, and nothing was linked on the user's behalf.
        Assert.Equal("pending", after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE left_release_id = @left;", new { left }));
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM identity_links;"));
        Assert.Empty(after.Query<string>(
            "SELECT name FROM sqlite_master "
            + "WHERE name IN ('merge_applications', 'merge_undo_rows');"));
    }

    [Fact]
    public void A_pair_confirmed_under_the_two_step_flow_becomes_a_link()
    {
        using var db = new TempDatabase();
        PreRetirementDatabase.Rewind(db);

        long named;
        long provisional;
        long left;
        using (var seed = db.Factory.Open())
        {
            // The ladder's second rung decides: one side carries a real store
            // title, the other a machine-minted placeholder.
            (named, var namedRelease) = PreRetirementDatabase.SeedGame(seed, "Prey");
            (provisional, var provisionalRelease) = PreRetirementDatabase.SeedGame(seed, "App 480490", provisional: true);
            left = Math.Min(namedRelease, provisionalRelease);

            PreRetirementDatabase.SeedCandidate(seed, namedRelease, provisionalRelease, "confirmed");
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        var link = after.QuerySingle<(long Child, long Parent)>(
            "SELECT child_work_id, parent_work_id FROM identity_links WHERE retracted_at IS NULL;");
        Assert.Equal(provisional, link.Child);
        Assert.Equal(named, link.Parent);

        // Both works are still there. The answer moved; no row did.
        Assert.Equal(2, after.ExecuteScalar<long>("SELECT COUNT(*) FROM works;"));

        // The row itself reads pending, because 'confirmed' no longer exists;
        // the affirmative answer is the live link, and the grouped queue drops
        // a proposal whose two sides already resolve to one work.
        Assert.Equal("pending", after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE left_release_id = @left;", new { left }));
    }

    [Fact]
    public void The_narrowed_status_set_refuses_the_two_that_went_with_the_merge()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var (_, left) = PreRetirementDatabase.SeedGame(conn, "Prey");
        var (_, right) = PreRetirementDatabase.SeedGame(conn, "Prey (Epic)");
        PreRetirementDatabase.SeedCandidate(conn, left, right, "pending");

        conn.Execute("UPDATE merge_candidates SET status = 'rejected';");
        Assert.Equal("rejected", conn.ExecuteScalar<string>("SELECT status FROM merge_candidates;"));

        foreach (var gone in new[] { "confirmed", "undone" })
        {
            Assert.Throws<SqliteException>(() => conn.Execute(
                "UPDATE merge_candidates SET status = @gone;", new { gone }));
        }
    }
}
