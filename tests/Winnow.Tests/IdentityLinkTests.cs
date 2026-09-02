using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Identity links (migration 0018, TASK-70.2). Covers the seven acceptance
/// criteria: linking and retraction, prior-parent restoration, idempotent
/// retraction, depth-one enforcement at both the repository and database
/// levels, type-level separation of same-game resolution from expansion
/// grouping, inertness of the bucket query, and the merge_candidates repair.
/// </summary>
public class IdentityLinkTests
{
    // ── Linking ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The simplest case: two works, one act, one live link, and the
    /// resolution reflects the relationship in every accessor.
    /// </summary>
    [Fact]
    public async Task Linking_two_works_produces_one_live_link_and_one_act()
    {
        using var db = new TempDatabase();
        var (a, b) = (Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        var actId = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = a,
            ChildWorkIds = [b],
        });

        var act = Assert.Single(await links.GetActsAsync());
        Assert.Equal(actId, act.Id);
        Assert.Equal(IdentityActKinds.Link, act.Kind);

        var link = Assert.Single(await links.GetHistoryAsync());
        Assert.Equal(actId, link.ActId);
        Assert.Equal(b, link.ChildWorkId);
        Assert.Equal(a, link.ParentWorkId);
        Assert.Equal(IdentityLinkKinds.SameGame, link.Kind);
        Assert.Equal(IdentityLinkSources.User, link.Source);
        Assert.True(link.IsLive);

        var resolution = await links.GetResolutionAsync();
        Assert.Equal(a, resolution.SameGame.Resolve(b));
        Assert.Equal(a, resolution.SameGame.Resolve(a));
        Assert.Equal([b], resolution.SameGame.ChildrenOf(a));
        Assert.Equal([a, b], resolution.SameGame.GroupOf(b));
    }

    /// <summary>
    /// One base and three expansions as one act, and one retraction undoes
    /// the whole group. This is the Civilization IV case: act_id groups
    /// one-to-many links under a single undo.
    /// </summary>
    [Fact]
    public async Task One_act_links_a_whole_group_and_one_retraction_undoes_it()
    {
        using var db = new TempDatabase();
        var parent = Work(db, "Civilization IV");
        var children = new[]
        {
            Work(db, "Beyond the Sword"),
            Work(db, "Warlords"),
            Work(db, "Colonization"),
        };

        var links = new IdentityLinkRepository(db.Factory);
        var actId = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = parent,
            ChildWorkIds = children,
            Kind = IdentityLinkKinds.ExpansionOf,
        });

        Assert.Single(await links.GetActsAsync());

        var grouping = (await links.GetResolutionAsync()).Expansions;
        Assert.Equal([.. children.Order()], grouping.ExpansionsOf(parent));

        Assert.True(await links.RetractActAsync(actId));
        Assert.True((await links.GetResolutionAsync()).Expansions.IsEmpty);
    }

    /// <summary>
    /// Re-linking a child to a different parent retracts the prior link and
    /// records the displacing act on the retracted row. The retracted row
    /// stays in history.
    /// </summary>
    [Fact]
    public async Task Linking_a_child_that_already_has_a_parent_replaces_the_live_link_and_keeps_history()
    {
        using var db = new TempDatabase();
        var (a, b, c) = (Work(db, "Prey"), Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        var first = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = a,
            ChildWorkIds = [c],
        });
        var second = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = b,
            ChildWorkIds = [c],
        });

        var resolution = await links.GetResolutionAsync();
        Assert.Equal(b, resolution.SameGame.Resolve(c));
        Assert.Empty(resolution.SameGame.ChildrenOf(a));

        var history = await links.GetHistoryAsync(c);
        Assert.Equal(2, history.Count);

        var retracted = history.Single(link => link.ActId == first);
        Assert.False(retracted.IsLive);
        Assert.Equal(a, retracted.ParentWorkId);
        Assert.Equal(second, retracted.RetractedByActId);

        Assert.True(history.Single(link => link.ActId == second).IsLive);
    }

    // ── Retracting ───────────────────────────────────────────────────────────

    /// <summary>
    /// Acceptance criterion #3: retracting an act restores every child to the
    /// parent it had immediately before that act, using retracted_by_act_id
    /// as the foreign-key lookup.
    /// </summary>
    [Fact]
    public async Task Retracting_an_act_restores_every_child_to_its_prior_parent()
    {
        using var db = new TempDatabase();
        var original = Work(db, "Prey");
        var newParent = Work(db, "Prey");
        var (x, y) = (Work(db, "Prey"), Work(db, "Prey"));

        var links = new IdentityLinkRepository(db.Factory);
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = original,
            ChildWorkIds = [x, y],
        });

        var moved = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = newParent,
            ChildWorkIds = [x, y],
        });

        var after = await links.GetResolutionAsync();
        Assert.Equal(newParent, after.SameGame.Resolve(x));
        Assert.Equal(newParent, after.SameGame.Resolve(y));

        Assert.True(await links.RetractActAsync(moved));

        var restored = (await links.GetResolutionAsync()).SameGame;
        Assert.Equal(original, restored.Resolve(x));
        Assert.Equal(original, restored.Resolve(y));
        Assert.Equal([x, y], restored.ChildrenOf(original).Order());
    }

    /// <summary>
    /// Retracting an already-retracted act returns false and writes nothing.
    /// Idempotent retraction is the fix for the user's complaint that undo
    /// made a pair permanently unmergeable.
    /// </summary>
    [Fact]
    public async Task Retracting_an_act_that_is_already_retracted_changes_nothing()
    {
        using var db = new TempDatabase();
        var (a, b) = (Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        var actId = await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = a,
            ChildWorkIds = [b],
        });

        Assert.True(await links.RetractActAsync(actId));

        var before = Snapshot(db);
        Assert.False(await links.RetractActAsync(actId));
        Assert.Equal(before, Snapshot(db));
    }

    /// <summary>
    /// Four rounds of link-then-retract produce the same live state as one
    /// round. The live state is all that any read sees; the retracted rows
    /// are history.
    /// </summary>
    [Fact]
    public async Task Link_and_retract_repeated_ends_where_one_link_and_one_retract_ends()
    {
        using var once = new TempDatabase();
        using var repeatedly = new TempDatabase();

        Assert.Equal(
            await LiveLinkShapeAsync(once, rounds: 1),
            await LiveLinkShapeAsync(repeatedly, rounds: 4));
    }

    /// <summary>
    /// Acceptance criterion #2: link, retract, link again produces the same
    /// live state as linking once. Re-linking after an unlink is a fresh row,
    /// not a resurrection.
    /// </summary>
    [Fact]
    public async Task Link_retract_link_ends_identical_to_linking_once()
    {
        using var once = new TempDatabase();
        using var thrice = new TempDatabase();

        var (a, b) = (Work(once, "Prey"), Work(once, "Prey"));
        var onceLinks = new IdentityLinkRepository(once.Factory);
        await onceLinks.LinkAsync(new IdentityLinkRequest { ParentWorkId = a, ChildWorkIds = [b] });

        var (c, d) = (Work(thrice, "Prey"), Work(thrice, "Prey"));
        var thriceLinks = new IdentityLinkRepository(thrice.Factory);
        for (var round = 0; round < 3; round++)
        {
            var actId = await thriceLinks.LinkAsync(
                new IdentityLinkRequest { ParentWorkId = c, ChildWorkIds = [d] });
            if (round < 2)
            {
                await thriceLinks.RetractActAsync(actId);
            }
        }

        // The live state is what every read sees, and it is identical. The
        // retracted rows behind it are history, which is the point of an
        // append-only table.
        Assert.Equal(LiveShape(once), LiveShape(thrice));
    }

    // ── Depth one ────────────────────────────────────────────────────────────

    /// <summary>
    /// Acceptance criterion #4 at the repository level: A is a child of B,
    /// so B is already a child and cannot become a parent.
    /// </summary>
    [Fact]
    public async Task A_two_cycle_is_refused()
    {
        using var db = new TempDatabase();
        var (a, b) = (Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = b, ChildWorkIds = [a] });

        var refused = await Assert.ThrowsAsync<IdentityLinkRefusedException>(
            () => links.LinkAsync(new IdentityLinkRequest { ParentWorkId = a, ChildWorkIds = [b] }));

        Assert.Equal(IdentityLinkRefusal.ParentIsAlreadyAChild, refused.Refusal);
        AssertNoLinkDeeperThanOne(db);
    }

    /// <summary>
    /// Acceptance criterion #4 at the repository level: B is a child of A,
    /// so B cannot be used as a parent for a new child C.
    /// </summary>
    [Fact]
    public async Task A_parent_that_is_already_a_child_is_refused()
    {
        using var db = new TempDatabase();
        var (a, b, c) = (Work(db, "Prey"), Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = a, ChildWorkIds = [b] });

        var refused = await Assert.ThrowsAsync<IdentityLinkRefusedException>(
            () => links.LinkAsync(new IdentityLinkRequest { ParentWorkId = b, ChildWorkIds = [c] }));

        Assert.Equal(IdentityLinkRefusal.ParentIsAlreadyAChild, refused.Refusal);

        var resolution = await links.GetResolutionAsync();
        Assert.Equal(c, resolution.SameGame.Resolve(c));
        AssertNoLinkDeeperThanOne(db);
    }

    /// <summary>
    /// The accommodation half of depth one: B has child A, then B becomes a
    /// child of C. A is re-parented onto C inside the same act, so one
    /// retraction puts A back under B.
    /// </summary>
    [Fact]
    public async Task Linking_a_work_that_has_children_reparents_them_inside_the_same_act()
    {
        using var db = new TempDatabase();
        var (a, b, c) = (Work(db, "Prey"), Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        // b holds a. Then b itself becomes a child of c: a would be two levels
        // down, so it moves onto c inside the same act.
        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = b, ChildWorkIds = [a] });
        var moved = await links.LinkAsync(
            new IdentityLinkRequest { ParentWorkId = c, ChildWorkIds = [b] });

        var resolution = (await links.GetResolutionAsync()).SameGame;
        Assert.Equal(c, resolution.Resolve(a));
        Assert.Equal(c, resolution.Resolve(b));
        Assert.Equal([a, b], resolution.ChildrenOf(c).Order());
        AssertNoLinkDeeperThanOne(db);

        // One act, so one undo: retracting puts a back under b.
        Assert.True(await links.RetractActAsync(moved));

        var restored = (await links.GetResolutionAsync()).SameGame;
        Assert.Equal(b, restored.Resolve(a));
        Assert.Equal(b, restored.Resolve(b));
        AssertNoLinkDeeperThanOne(db);
    }

    /// <summary>
    /// Acceptance criterion #4 at the DATABASE level: ux_identity_links_live
    /// rejects a second live parent for one child. The repository cannot give
    /// this guarantee alone; this test bypasses it and writes raw SQL to prove
    /// the index enforces the invariant.
    /// </summary>
    [Fact]
    public void The_schema_rejects_two_live_parents_for_one_child()
    {
        using var db = new TempDatabase();
        var (a, b, c) = (Work(db, "Prey"), Work(db, "Prey"), Work(db, "Prey"));

        using var conn = db.Factory.Open();
        var actId = conn.ExecuteScalar<long>("""
            INSERT INTO identity_acts (kind, performed_at)
            VALUES ('link', '2026-09-01 00:00:00') RETURNING id;
            """);

        conn.Execute("""
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source, applied_at)
            VALUES (@actId, @c, @a, 'same_game', 'user', '2026-09-01 00:00:00');
            """, new { actId, a, c });

        // ux_identity_links_live is what makes "at most one live parent" a fact
        // about the database rather than a promise made by the repository.
        var thrown = Assert.Throws<SqliteException>(() => conn.Execute("""
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source, applied_at)
            VALUES (@actId, @c, @b, 'same_game', 'user', '2026-09-01 00:00:00');
            """, new { actId, b, c }));

        Assert.Contains("UNIQUE", thrown.Message, StringComparison.Ordinal);

        // Retracting the first frees the child, so the same insert then stands.
        conn.Execute("""
            UPDATE identity_links
            SET retracted_at = '2026-09-02 00:00:00', retracted_by_act_id = @actId
            WHERE child_work_id = @c;
            """, new { actId, c });

        conn.Execute("""
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source, applied_at)
            VALUES (@actId, @c, @b, 'same_game', 'user', '2026-09-02 00:00:00');
            """, new { actId, b, c });
    }

    /// <summary>
    /// Acceptance criterion #4 at the DATABASE level: CHECK (child_work_id
    /// &lt;&gt; parent_work_id) rejects a self-link.
    /// </summary>
    [Fact]
    public void The_schema_rejects_a_work_linked_to_itself()
    {
        using var db = new TempDatabase();
        var a = Work(db, "Prey");

        using var conn = db.Factory.Open();
        var actId = conn.ExecuteScalar<long>("""
            INSERT INTO identity_acts (kind, performed_at)
            VALUES ('link', '2026-09-01 00:00:00') RETURNING id;
            """);

        Assert.Throws<SqliteException>(() => conn.Execute("""
            INSERT INTO identity_links (
                act_id, child_work_id, parent_work_id, kind, source, applied_at)
            VALUES (@actId, @a, @a, 'same_game', 'user', '2026-09-01 00:00:00');
            """, new { actId, a }));
    }

    // ── The two kinds are not interchangeable ────────────────────────────────

    /// <summary>
    /// Acceptance criterion #5: the same-game resolver and the expansion
    /// grouper are separate types and do not see each other. An expansion
    /// resolves to itself (no playtime roll-up), and a same-game child is
    /// not an expansion.
    /// </summary>
    [Fact]
    public async Task The_same_game_resolver_and_the_expansion_grouper_do_not_see_each_other()
    {
        using var db = new TempDatabase();
        var civ = Work(db, "Civilization IV");
        var expansion = Work(db, "Beyond the Sword");
        var steamPrey = Work(db, "Prey");
        var epicPrey = Work(db, "Prey");

        var links = new IdentityLinkRepository(db.Factory);
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = civ,
            ChildWorkIds = [expansion],
            Kind = IdentityLinkKinds.ExpansionOf,
        });
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steamPrey,
            ChildWorkIds = [epicPrey],
        });

        var resolution = await links.GetResolutionAsync();

        // An expansion is a separate product. It resolves to itself, so no
        // count, playtime, bucket or recommendation folds it into its base.
        Assert.Equal(expansion, resolution.SameGame.Resolve(expansion));
        Assert.False(resolution.SameGame.IsChild(expansion));
        Assert.Empty(resolution.SameGame.ChildrenOf(civ));

        // And identity is not a grouping: the Epic entry is the same game as
        // the Steam one, not an expansion of it.
        Assert.Equal(steamPrey, resolution.SameGame.Resolve(epicPrey));
        Assert.Null(resolution.Expansions.BaseOf(epicPrey));
        Assert.Empty(resolution.Expansions.ExpansionsOf(steamPrey));

        Assert.Equal(civ, resolution.Expansions.BaseOf(expansion));
        Assert.Equal([expansion], resolution.Expansions.ExpansionsOf(civ));
    }

    // ── Inert ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acceptance criterion #6: a live link does not move a single bucket
    /// row. The bucket query is the single chokepoint every grid, rail count
    /// and filter option reads through, so this is the inertness proof.
    /// </summary>
    [Fact]
    public async Task A_live_link_changes_no_bucket_row()
    {
        using var db = new TempDatabase();
        var (steamWorkId, _) = Owned(db, "Prey", "steam", playtimeMinutes: 300);
        var (epicWorkId, _) = Owned(db, "Prey", "epic", playtimeMinutes: 40);

        var buckets = new LibraryQueryRepository(db.Factory);
        var before = await buckets.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(2, before.Count);

        var links = new IdentityLinkRepository(db.Factory);
        await links.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = steamWorkId,
            ChildWorkIds = [epicWorkId],
        });

        var after = await buckets.GetOwnershipBucketsAsync(BucketThresholds.Default);
        Assert.Equal(before, after);
    }

    // ── The 0018 repair ──────────────────────────────────────────────────────

    /// <summary>
    /// Acceptance criterion #7: a reversed merge (undone candidate with
    /// undone_at set on its application) returns to 'pending', while a
    /// standing merge (confirmed candidate with application still live) is
    /// left alone.
    /// </summary>
    [Fact]
    public void Migration_0018_returns_a_reversed_merge_to_the_queue_and_leaves_a_standing_one_alone()
    {
        using var db = new TempDatabase();

        long reversed;
        long standing;
        using (var conn = db.Factory.Open())
        {
            RewindPast0018(conn);

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Prey') RETURNING id;");
            var releaseIds = new long[4];
            for (var i = 0; i < releaseIds.Length; i++)
            {
                releaseIds[i] = conn.ExecuteScalar<long>(
                    "INSERT INTO releases (work_id, name) VALUES (@workId, 'Prey') RETURNING id;",
                    new { workId });
            }

            reversed = Candidate(conn, releaseIds[0], releaseIds[1], "undone");
            standing = Candidate(conn, releaseIds[2], releaseIds[3], "confirmed");

            // The reversed merge's rows are already back; the pair is genuinely
            // an open question again.
            Application(conn, reversed, releaseIds[0], releaseIds[1], workId, undone: true);
            Application(conn, standing, releaseIds[2], releaseIds[3], workId, undone: false);
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        Assert.Equal("pending", after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE id = @reversed;", new { reversed }));
        Assert.Equal("confirmed", after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE id = @standing;", new { standing }));
    }

    /// <summary>
    /// Acceptance criterion #7, the defensive guard: a candidate at 'undone'
    /// that has both a reversed application AND a still-standing one is left
    /// alone. The pair is not open.
    /// </summary>
    [Fact]
    public void Migration_0018_leaves_an_undone_candidate_whose_merge_still_stands_alone()
    {
        using var db = new TempDatabase();

        long candidateId;
        using (var conn = db.Factory.Open())
        {
            RewindPast0018(conn);

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Prey') RETURNING id;");
            var left = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Prey') RETURNING id;",
                new { workId });
            var right = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Prey') RETURNING id;",
                new { workId });

            candidateId = Candidate(conn, left, right, "undone");

            // Two applications for one pair: one reversed, one still standing.
            // The pair is not open, so the repair must not touch it.
            Application(conn, candidateId, left, right, workId, undone: true);
            Application(conn, candidateId, left, right, workId, undone: false);
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        Assert.Equal("undone", after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE id = @candidateId;", new { candidateId }));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static long Work(TempDatabase db, string name)
    {
        using var conn = db.Factory.Open();
        return conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@name) RETURNING id;", new { name });
    }

    private static (long WorkId, long OwnershipId) Owned(
        TempDatabase db, string name, string store, long playtimeMinutes)
    {
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES (@name) RETURNING id;", new { name });
        var releaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, @name) RETURNING id;",
            new { workId, name });
        var ownershipId = conn.ExecuteScalar<long>(
            "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, @store) RETURNING id;",
            new { releaseId, store });

        conn.Execute("""
            INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
            VALUES (@ownershipId, @playtimeMinutes, '2026-01-01 00:00:00', 'steam_local', '2026-01-02 00:00:00');
            """, new { ownershipId, playtimeMinutes });

        return (workId, ownershipId);
    }

    private static long Candidate(SqliteConnection conn, long left, long right, string status)
        => conn.ExecuteScalar<long>("""
            INSERT INTO merge_candidates (left_release_id, right_release_id, score, status)
            VALUES (MIN(@left, @right), MAX(@left, @right), 0.9, @status)
            RETURNING id;
            """, new { left, right, status });

    private static void Application(
        SqliteConnection conn, long candidateId, long left, long right, long workId, bool undone)
        => conn.Execute("""
            INSERT INTO merge_applications (
                candidate_id, left_release_id, right_release_id, mode,
                surviving_work_id, applied_at, undone_at, undo_journal_version)
            VALUES (@candidateId, @left, @right, 'work_only',
                    @workId, '2026-08-01 00:00:00', @undoneAt, 1);
            """,
            new
            {
                candidateId,
                left,
                right,
                workId,
                undoneAt = undone ? "2026-08-02 00:00:00" : null,
            });

    /// <summary>Puts the database back in its pre-0018 shape so 0018 alone is pending.</summary>
    private static void RewindPast0018(SqliteConnection conn)
    {
        conn.Execute("DROP TABLE IF EXISTS identity_links;");
        conn.Execute("DROP TABLE IF EXISTS identity_acts;");
        conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0018%';");
    }

    /// <summary>Runs N rounds of link-then-retract and returns the live state string.</summary>
    private static async Task<string> LiveLinkShapeAsync(TempDatabase db, int rounds)
    {
        var (a, b) = (Work(db, "Prey"), Work(db, "Prey"));
        var links = new IdentityLinkRepository(db.Factory);

        for (var round = 0; round < rounds; round++)
        {
            var actId = await links.LinkAsync(
                new IdentityLinkRequest { ParentWorkId = a, ChildWorkIds = [b] });
            await links.RetractActAsync(actId);
        }

        return LiveShape(db);
    }

    /// <summary>Every live link as one string: what every read of the library sees.</summary>
    private static string LiveShape(TempDatabase db)
    {
        using var conn = db.Factory.Open();
        return string.Join('\n', conn.Query<string>("""
            SELECT child_work_id || '->' || parent_work_id || ':' || kind || ':' || source
            FROM identity_links
            WHERE retracted_at IS NULL
            ORDER BY child_work_id;
            """));
    }

    /// <summary>Every identity row as one string, for "nothing changed" assertions.</summary>
    private static string Snapshot(TempDatabase db)
    {
        using var conn = db.Factory.Open();

        var acts = conn.Query<string>("""
            SELECT id || '|' || kind || '|' || performed_at || '|' || COALESCE(note, '<null>')
            FROM identity_acts ORDER BY id;
            """);
        var links = conn.Query<string>("""
            SELECT id || '|' || act_id || '|' || child_work_id || '|' || parent_work_id
                   || '|' || kind || '|' || source
                   || '|' || COALESCE(retracted_at, '<null>')
                   || '|' || COALESCE(CAST(retracted_by_act_id AS TEXT), '<null>')
            FROM identity_links ORDER BY id;
            """);

        return string.Join('\n', acts) + "\n--\n" + string.Join('\n', links);
    }

    /// <summary>
    /// Depth one, asserted against the database rather than the repository:
    /// no live child may also be a live parent. SQLite cannot express this as a
    /// CHECK, so it is a query every test that writes links runs afterwards.
    /// </summary>
    private static void AssertNoLinkDeeperThanOne(TempDatabase db)
    {
        using var conn = db.Factory.Open();
        Assert.Equal(0, conn.ExecuteScalar<long>("""
            SELECT COUNT(*)
            FROM identity_links child
            JOIN identity_links parent
              ON parent.child_work_id = child.parent_work_id
             AND parent.retracted_at IS NULL
            WHERE child.retracted_at IS NULL;
            """));
    }
}
