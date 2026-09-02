
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
