using Dapper;
using Winnow.Data;
using Xunit;

namespace Winnow.Tests;

public class MigrationTests
{
    private static readonly string[] ExpectedTables =
    [
        "works", "releases", "external_ids",
        "ownerships", "play_records", "playtime_snapshots",
        "sessions", "session_notes",
        "achievements", "achievement_unlocks",
        "update_events",
        "lists", "list_items",
        "merge_candidates",
        "metadata_cache", "settings",
        "feed_verdicts", "feed_surfacings",
        "update_acknowledgements",
        "account_transactions", "account_licenses",
        "merge_applications",
    ];

    /// <summary>The <c>merge_candidates</c> shape as 0001 shipped it, mirrors and self-pairs allowed.</summary>
    private const string PreCanonicalMergeCandidates = """
        CREATE TABLE merge_candidates (
            id                INTEGER PRIMARY KEY,
            left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
            right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
            score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
            signals_json      TEXT,
            status            TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'confirmed', 'rejected')),
            UNIQUE (left_release_id, right_release_id)
        );
        CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);
        """;

    /// <summary>merge_candidates and merge_applications exactly as 0016 left them.</summary>
    private const string PostCanonicalMergeTables = """
        CREATE TABLE merge_candidates (
            id                INTEGER PRIMARY KEY,
            left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
            right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
            score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
            signals_json      TEXT,
            status            TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'confirmed', 'rejected')),
            CHECK (left_release_id < right_release_id),
            UNIQUE (left_release_id, right_release_id)
        );
        CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);

        CREATE TABLE merge_applications (
            id                    INTEGER PRIMARY KEY,
            candidate_id          INTEGER NOT NULL,
            left_release_id       INTEGER NOT NULL,
            right_release_id      INTEGER NOT NULL,
            mode                  TEXT NOT NULL CHECK (mode IN ('work_only', 'release_collapse')),
            surviving_work_id     INTEGER NOT NULL,
            absorbed_work_id      INTEGER,
            surviving_release_id  INTEGER,
            absorbed_release_id   INTEGER,
            applied_at            TEXT NOT NULL,
            summary_json          TEXT,
            CHECK (mode <> 'release_collapse'
                OR (surviving_release_id IS NOT NULL
                    AND absorbed_release_id IS NOT NULL
                    AND surviving_release_id <> absorbed_release_id)),
            CHECK (absorbed_work_id IS NULL OR absorbed_work_id <> surviving_work_id)
        );
        CREATE INDEX ix_merge_applications_candidate ON merge_applications(candidate_id);
        CREATE INDEX ix_merge_applications_absorbed ON merge_applications(absorbed_release_id);
        """;

    [Fact]
    public void Migration_applies_cleanly_to_fresh_temp_file_database()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var tables = conn.Query<string>(
                "SELECT name FROM sqlite_master WHERE type = 'table';")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var table in ExpectedTables)
        {
            Assert.Contains(table, tables);
        }
    }

    [Fact]
    public void Migration_journal_lives_in_same_database_and_reruns_are_noops()
    {
        using var db = new TempDatabase();

        // Second run must be a no-op, not a failure.
        db.Initializer.Initialize();

        using var conn = db.Factory.Open();
        var scripts = conn.Query<string>("SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;")
            .ToList();

        // Derived from the shipped migration set rather than a hard-coded list:
        // the invariant under test is "every migration recorded exactly once,
        // in order, after two runs", which is true of any number of them. A
        // literal list would instead fail on the next migration anyone adds,
        // which teaches people to edit the assertion rather than read it.
        var expected = typeof(DatabaseInitializer).Assembly
            .GetManifestResourceNames()
            .Where(name => name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, scripts);
        Assert.Equal(scripts.Count, scripts.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Migration_0003_makes_release_plus_store_unique_for_ownerships()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Prey') RETURNING id;");
        var releaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'Prey (2017)') RETURNING id;",
            new { workId });

        conn.Execute(
            "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam');",
            new { releaseId });

        // Second ownership for the same release on the same store: the split
        // play history the resolver's read-then-insert could produce.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute(
                "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam');",
                new { releaseId }));

        // A different store for the same release is still a separate ownership —
        // the constraint must not collapse cross-store ownership.
        conn.Execute(
            "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'gog');",
            new { releaseId });
        Assert.Equal(2, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM ownerships WHERE release_id = @releaseId;", new { releaseId }));
    }

    [Fact]
    public void Migration_0003_collapses_pre_existing_duplicate_ownerships_keeping_play_history()
    {
        // A database created before 0003 could already hold duplicates, and the
        // unique index would fail to build over them. Simulate that shape by
        // migrating only through 0002 (index dropped), inserting the duplicate,
        // then letting 0003 run.
        using var db = new TempDatabase();

        long releaseId;
        long keptId;
        long dupId;
        using (var conn = db.Factory.Open())
        {
            conn.Execute("DROP INDEX ux_ownerships_release_store;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0003%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Prey') RETURNING id;");
            releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Prey (2017)') RETURNING id;",
                new { workId });

            keptId = conn.ExecuteScalar<long>(
                "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam') RETURNING id;",
                new { releaseId });
            dupId = conn.ExecuteScalar<long>(
                "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam') RETURNING id;",
                new { releaseId });

            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, source, observed_at)
                VALUES (@dupId, 500, 'steam_local', '2026-08-01 00:00:00');
                """, new { dupId });
            conn.Execute("""
                INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                VALUES (@dupId, 500, '2026-08-01 00:00:00');
                """, new { dupId });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        // One ownership survives — the lowest id — and the duplicate's play
        // history moved onto it rather than being cascade-deleted.
        var surviving = after.ExecuteScalar<long>(
            "SELECT id FROM ownerships WHERE release_id = @releaseId;", new { releaseId });
        Assert.Equal(keptId, surviving);
        Assert.NotEqual(keptId, dupId);

        Assert.Equal(500, after.ExecuteScalar<long>(
            "SELECT playtime_minutes FROM play_records WHERE ownership_id = @keptId;", new { keptId }));
        Assert.Equal(500, after.ExecuteScalar<long>(
            "SELECT playtime_minutes FROM playtime_snapshots WHERE ownership_id = @keptId;", new { keptId }));
    }

    [Fact]
    public void Migration_0002_adds_name_is_provisional_defaulting_to_false()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        // Applies on top of 0001 without disturbing it: an insert that names no
        // flag (as pre-0002 code would) yields a real, non-provisional name.
        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Elden Ring') RETURNING id;");
        Assert.Equal(0, conn.ExecuteScalar<long>(
            "SELECT name_is_provisional FROM works WHERE id = @workId;", new { workId }));

        var provisionalId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name, name_is_provisional) VALUES ('App 1203620', 1) RETURNING id;");
        Assert.Equal(1, conn.ExecuteScalar<long>(
            "SELECT name_is_provisional FROM works WHERE id = @provisionalId;", new { provisionalId }));

        // The flag is a boolean, enforced.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("INSERT INTO works (name, name_is_provisional) VALUES ('X', 2);"));
    }

    /// <summary>
    /// 0005 adds the publisher column §5.3 has always scored and §6 never had
    /// anywhere to put. Applied on top of the 0004 shape — the state every
    /// existing database is actually in — with rows already on disk.
    /// </summary>
    [Fact]
    public void Migration_0005_adds_publisher_on_top_of_the_0004_schema()
    {
        using var db = new TempDatabase();

        using (var conn = db.Factory.Open())
        {
            // Rewind to 0004: drop the column and forget the script ran.
            conn.Execute("ALTER TABLE works DROP COLUMN publisher;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0005%';");

            // A row written by 0004-era code, which knew nothing about publishers.
            conn.Execute("INSERT INTO works (name) VALUES ('Riven');");
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        Assert.Contains(
            "publisher",
            after.Query<string>("SELECT name FROM pragma_table_info('works');"));

        // NULL means "unknown", which the matcher reads as absent evidence
        // rather than as a mismatch. The pre-existing row is not disturbed.
        Assert.Null(after.ExecuteScalar<string?>(
            "SELECT publisher FROM works WHERE name = 'Riven';"));

        // And 0004's work is still standing.
        Assert.Equal(1, after.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ux_update_events_identity';
            """));

        var workId = after.ExecuteScalar<long>(
            "INSERT INTO works (name, publisher) VALUES ('Portal 2', 'Valve') RETURNING id;");
        Assert.Equal("Valve", after.ExecuteScalar<string>(
            "SELECT publisher FROM works WHERE id = @workId;", new { workId }));
    }

    /// <summary>
    /// 0006 adds Valve's own <c>common.type</c> for the Steam appid, which
    /// <c>DemoConsolidation</c> reads as its first gate. Applied on top of the
    /// 0005 shape with rows already on disk.
    /// </summary>
    [Fact]
    public void Migration_0006_adds_steam_app_type_on_top_of_the_0005_schema()
    {
        using var db = new TempDatabase();

        using (var conn = db.Factory.Open())
        {
            conn.Execute("ALTER TABLE works DROP COLUMN steam_app_type;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0006%';");

            conn.Execute("INSERT INTO works (name, publisher) VALUES ('Riven', 'Brøderbund');");
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        Assert.Contains(
            "steam_app_type",
            after.Query<string>("SELECT name FROM pragma_table_info('works');"));

        // NULL is "not known", never "not a demo" — several appids are
        // unreadable without a Steam Web API key. The pre-existing row keeps
        // both its publisher and its unknown type.
        Assert.Null(after.ExecuteScalar<string?>(
            "SELECT steam_app_type FROM works WHERE name = 'Riven';"));
        Assert.Equal("Brøderbund", after.ExecuteScalar<string>(
            "SELECT publisher FROM works WHERE name = 'Riven';"));

        // No CHECK constraint: the vocabulary is Valve's, undocumented, and can
        // gain a value at any time. A constraint would turn a new Steam app type
        // into a failed enrichment write.
        foreach (var type in new[] { "Game", "game", "Demo", "Tool", "Config", "SomethingNew" })
        {
            after.Execute(
                "INSERT INTO works (name, steam_app_type) VALUES (@type, @type);", new { type });
        }

        Assert.Equal(6, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM works WHERE steam_app_type IS NOT NULL;"));
    }

    /// <summary>
    /// 0009 adds Epic's own <c>categories[].path</c> list for the catalog item,
    /// which the library view's non-game filter reads through the same
    /// <c>EpicGameFilter</c> the local Epic scan uses. Applied on top of a schema
    /// that already has rows.
    /// </summary>
    [Fact]
    public void Migration_0009_adds_epic_categories_and_leaves_existing_rows_unknown()
    {
        using var db = new TempDatabase();

        using (var conn = db.Factory.Open())
        {
            conn.Execute("ALTER TABLE works DROP COLUMN epic_categories;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0009%';");

            conn.Execute(
                "INSERT INTO works (name, steam_app_type) VALUES ('Riven', 'Game');");
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        Assert.Contains(
            "epic_categories",
            after.Query<string>("SELECT name FROM pragma_table_info('works');"));

        // NULL is "nobody has read it", never "not a game". Every Epic work
        // named from catcache.bin before this column existed is in exactly this
        // state, and hiding those would empty the store from the library view.
        Assert.Null(after.ExecuteScalar<string?>(
            "SELECT epic_categories FROM works WHERE name = 'Riven';"));

        // 0006 is undisturbed — the two columns are siblings, not replacements.
        Assert.Equal("Game", after.ExecuteScalar<string>(
            "SELECT steam_app_type FROM works WHERE name = 'Riven';"));

        // No CHECK constraint: Epic's vocabulary is undocumented and still
        // growing (`freegames` and `games/experience` appear on the author's
        // account and in neither earlier survey). A constraint would turn a new
        // Epic category into a failed enrichment write.
        foreach (var categories in new[]
                 {
                     "public,games,applications",
                     "engines,engines/ue4",
                     "assets,assets/showcasedemos",
                     "hidden",
                     "somethingEpicHasNotInventedYet",
                 })
        {
            after.Execute(
                "INSERT INTO works (name, epic_categories) VALUES (@categories, @categories);",
                new { categories });
        }

        Assert.Equal(5, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM works WHERE epic_categories IS NOT NULL;"));
    }

    /// <summary>
    /// 0008 corrects the rows the two readers' disagreement already wrote: a
    /// last-played of 1970-01-02, produced by the Web API reader mapping Steam's
    /// 86400 placeholder to a literal date while the local reader called the
    /// same value unknown. 45 such rows were on the author's live database.
    ///
    /// <para>The date is wrong; the minutes beside it are not. The migration
    /// nulls the column and leaves the row — deleting it would throw away a true
    /// measurement to correct a false one.</para>
    /// </summary>
    [Fact]
    public void Migration_0008_nulls_placeholder_last_played_dates_and_keeps_the_playtime()
    {
        using var db = new TempDatabase();

        long ownershipId;
        using (var conn = db.Factory.Open())
        {
            // Rewind: forget 0008 ran, then write the rows pre-0008 code produced.
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0008%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Ricochet') RETURNING id;");
            var releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Ricochet') RETURNING id;",
                new { workId });
            ownershipId = conn.ExecuteScalar<long>(
                "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam') RETURNING id;",
                new { releaseId });

            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES
                    -- The bogus pair: 86400 seconds rendered as a date.
                    (@ownershipId, 3, '1970-01-02 00:00:00', 'steam_web_api', '2026-08-25 02:25:07'),
                    -- Same row, written with a 'T' separator: the fix is on the
                    -- instant, not on the spelling.
                    (@ownershipId, 3, '1970-01-02T00:00:00', 'steam_web_api', '2026-08-25 02:25:08'),
                    -- The local reader's answer for the same observation.
                    (@ownershipId, 3, NULL, 'steam_local', '2026-08-25 02:25:09'),
                    -- A real date, which must not be touched.
                    (@ownershipId, 280, '2018-05-25 03:07:27', 'steam_local', '2026-08-25 02:25:10');
                """, new { ownershipId });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        // Nothing was deleted: four rows in, four rows out.
        Assert.Equal(4, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM play_records WHERE ownership_id = @ownershipId;", new { ownershipId }));

        // No placeholder date survives, in either spelling.
        Assert.Equal(0, after.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM play_records
            WHERE last_played_at IS NOT NULL
            AND   CAST(strftime('%s', last_played_at) AS INTEGER) < 315532800;
            """));

        // And the minutes those rows carried are all still there.
        Assert.Equal(3, after.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM play_records
            WHERE ownership_id = @ownershipId AND playtime_minutes = 3;
            """, new { ownershipId }));

        // The real date is untouched.
        Assert.Equal("2018-05-25 03:07:27", after.ExecuteScalar<string>("""
            SELECT last_played_at FROM play_records
            WHERE ownership_id = @ownershipId AND playtime_minutes = 280;
            """, new { ownershipId }));
    }

    /// <summary>
    /// A second run of 0008 over a corrected database changes nothing — the
    /// journal makes it a no-op, and the statement itself is idempotent anyway.
    /// </summary>
    [Fact]
    public void Migration_0008_is_a_no_op_on_a_database_with_no_placeholder_dates()
    {
        using var db = new TempDatabase();

        using (var conn = db.Factory.Open())
        {
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0008%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Portal') RETURNING id;");
            var releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Portal') RETURNING id;",
                new { workId });
            var ownershipId = conn.ExecuteScalar<long>(
                "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam') RETURNING id;",
                new { releaseId });

            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@ownershipId, 280, '2018-05-25 03:07:27', 'steam_local', '2026-08-25 02:25:10');
                """, new { ownershipId });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        Assert.Equal("2018-05-25 03:07:27", after.ExecuteScalar<string>(
            "SELECT last_played_at FROM play_records;"));
    }

    /// <summary>
    /// 0010 adds <c>sessions.attributed_by</c>: whether Winnow started the game
    /// itself or worked out afterwards which game a process belonged to (M3b).
    ///
    /// <para>Applied on top of a database that already holds sessions, because
    /// that is the only interesting case — the user has real play history and it
    /// must survive. NULL on those rows is the correct answer and not a gap to be
    /// back-filled: nothing in a finished session says whether a human clicked
    /// Play in Winnow or in Steam, so writing 'inferred' over them would be
    /// inventing history rather than describing it.</para>
    /// </summary>
    [Fact]
    public void Migration_0010_adds_session_attribution_and_leaves_old_rows_null()
    {
        using var db = new TempDatabase();

        long sessionId;
        using (var conn = db.Factory.Open())
        {
            conn.Execute("ALTER TABLE sessions DROP COLUMN attributed_by;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0010%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Riven') RETURNING id;");
            var releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Riven') RETURNING id;",
                new { workId });
            var ownershipId = conn.ExecuteScalar<long>(
                "INSERT INTO ownerships (release_id, store) VALUES (@releaseId, 'steam') RETURNING id;",
                new { releaseId });

            sessionId = conn.ExecuteScalar<long>(
                """
                INSERT INTO sessions (ownership_id, started_at, ended_at, duration_s, detection_method)
                VALUES (@ownershipId, '2026-08-01 20:00:00', '2026-08-01 21:00:00', 3600, 'process_watch')
                RETURNING id;
                """,
                new { ownershipId });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        Assert.Contains(
            "attributed_by",
            after.Query<string>("SELECT name FROM pragma_table_info('sessions');"));

        // The pre-existing sitting is intact, and unattributed rather than guessed at.
        Assert.Equal(3600, after.ExecuteScalar<long>(
            "SELECT duration_s FROM sessions WHERE id = @sessionId;", new { sessionId }));
        Assert.Null(after.ExecuteScalar<string?>(
            "SELECT attributed_by FROM sessions WHERE id = @sessionId;", new { sessionId }));

        // The vocabulary is ours and closed, so unlike 0009's epic_categories it
        // IS constrained: a third value should have to be a schema change.
        after.Execute(
            "UPDATE sessions SET attributed_by = 'launch' WHERE id = @sessionId;", new { sessionId });
        after.Execute(
            "UPDATE sessions SET attributed_by = 'inferred' WHERE id = @sessionId;", new { sessionId });

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            after.Execute(
                "UPDATE sessions SET attributed_by = 'guessed' WHERE id = @sessionId;",
                new { sessionId }));

        // detection_method is untouched. The two axes are orthogonal: a launched
        // session is still timed by the process watcher, and saying otherwise
        // would be a claim about its timestamps that is not true.
        Assert.Equal("process_watch", after.ExecuteScalar<string>(
            "SELECT detection_method FROM sessions WHERE id = @sessionId;", new { sessionId }));
    }

    /// <summary>
    /// 0011 adds the feedback loop's two tables: what the user told the feed
    /// (verdicts) and what the feed showed the user (surfacings). Both are
    /// facts, not derived values — §6.1's rule is that scores stay queries,
    /// and neither of these is a score. The vocabulary and the
    /// kind-implies-expiry pairing are CHECK-constrained the way 0010's
    /// attributed_by is, because both vocabularies are ours and closed: a
    /// verdict kind that silently starts meaning something else later is the
    /// failure the constraint exists to prevent.
    /// </summary>
    [Fact]
    public void Migration_0011_adds_feedback_tables_with_closed_vocabularies()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Riven') RETURNING id;");
        var releaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'Riven') RETURNING id;",
            new { workId });

        // The two shapes the writer is allowed to produce.
        conn.Execute("""
            INSERT INTO feed_verdicts (release_id, kind, created_at)
            VALUES (@releaseId, 'not_interested', '2026-08-27 12:00:00');
            """, new { releaseId });
        conn.Execute("""
            INSERT INTO feed_verdicts (release_id, kind, created_at, expires_at)
            VALUES (@releaseId, 'snoozed', '2026-08-27 12:00:00', '2026-09-26 12:00:00');
            """, new { releaseId });

        // The vocabulary is closed: a third kind is a schema change.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO feed_verdicts (release_id, kind, created_at)
                VALUES (@releaseId, 'more_like_this', '2026-08-27 12:00:00');
                """, new { releaseId }));

        // A snooze with no expiry is a dismissal wearing a different name —
        // the writer must say which one it means.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO feed_verdicts (release_id, kind, created_at)
                VALUES (@releaseId, 'snoozed', '2026-08-27 12:00:00');
                """, new { releaseId }));

        // And a not-interested cannot smuggle an expiry in.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO feed_verdicts (release_id, kind, created_at, expires_at)
                VALUES (@releaseId, 'not_interested', '2026-08-27 12:00:00', '2026-09-26 12:00:00');
                """, new { releaseId }));

        // One row per release per day: the engine's one-work-one-shelf rule
        // expressed as a primary key.
        conn.Execute("""
            INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id)
            VALUES (@releaseId, '2026-08-27', 'on_your_taste');
            """, new { releaseId });
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id)
                VALUES (@releaseId, '2026-08-27', 'ready_to_play');
                """, new { releaseId }));

        // Both tables hang off releases with the usual FK enforcement.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO feed_verdicts (release_id, kind, created_at)
                VALUES (999999, 'not_interested', '2026-08-27 12:00:00');
                """));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO feed_surfacings (release_id, surfaced_on, shelf_id)
                VALUES (999999, '2026-08-27', 'on_your_taste');
                """));
    }

    /// <summary>
    /// 0012 adds the "I've seen this patch" watermark: the instant the user
    /// dismissed §5.2's unread dot on one release. A fact, stored, like 0011's
    /// verdicts — and, like them, appended and revoked rather than updated in
    /// place, with no "active" column and (unlike a snooze) no expiry.
    ///
    /// <para>What this asserts about the SHAPE is as much about what is absent
    /// as what is present: no kind, no expires_at, no uniqueness on release_id.
    /// Each of those would be a different feature — a vocabulary, a lapse, or an
    /// upsert — and all three are ruled out in the migration's header.</para>
    /// </summary>
    [Fact]
    public void Migration_0012_adds_the_acknowledgement_watermark_as_an_append_only_log()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Riven') RETURNING id;");
        var releaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'Riven') RETURNING id;",
            new { workId });

        // Repeated dismissals ACCUMULATE. There is deliberately no unique index
        // on release_id: a second, later patch dismissed is a second row, and
        // the bucket query takes MAX(acknowledged_through) rather than making
        // the writer overwrite history to say which one wins.
        conn.Execute("""
            INSERT INTO update_acknowledgements (release_id, acknowledged_through, created_at)
            VALUES (@releaseId, '2026-03-01 00:00:00', '2026-03-02 09:00:00'),
                   (@releaseId, '2026-06-01 00:00:00', '2026-06-02 09:00:00');
            """, new { releaseId });

        Assert.Equal(2, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements WHERE release_id = @releaseId;",
            new { releaseId }));

        // "Standing" is revoked_at IS NULL, evaluated in the query — never a
        // stored column. Both rows stand until something stamps them.
        Assert.Equal("2026-06-01 00:00:00", conn.ExecuteScalar<string>("""
            SELECT MAX(acknowledged_through) FROM update_acknowledgements
            WHERE release_id = @releaseId AND revoked_at IS NULL;
            """, new { releaseId }));

        // Undo is a stamp, not a DELETE: the row survives its own revocation.
        conn.Execute("""
            UPDATE update_acknowledgements SET revoked_at = '2026-06-10 09:00:00'
            WHERE release_id = @releaseId AND revoked_at IS NULL;
            """, new { releaseId });
        Assert.Equal(2, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements WHERE release_id = @releaseId;",
            new { releaseId }));
        Assert.Null(conn.ExecuteScalar<string?>("""
            SELECT MAX(acknowledged_through) FROM update_acknowledgements
            WHERE release_id = @releaseId AND revoked_at IS NULL;
            """, new { releaseId }));

        // No expiry column, and there must not be one: an acknowledgement is
        // answered by the next real patch, never by a clock. A dismissal that
        // timed out would re-raise the dot for an update already read, which is
        // the one thing §5.2's mark must never do.
        var columns = conn.Query<string>(
            "SELECT name FROM pragma_table_info('update_acknowledgements');").ToList();
        Assert.Equal(
            ["id", "release_id", "acknowledged_through", "created_at", "revoked_at"],
            columns);

        // Indexed by release, like 0011's verdicts — the bucket query groups on it.
        Assert.Equal(1, conn.ExecuteScalar<long>("""
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ix_update_acknowledgements_release';
            """));

        // Hangs off releases with the usual FK enforcement...
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute("""
                INSERT INTO update_acknowledgements (release_id, acknowledged_through, created_at)
                VALUES (999999, '2026-03-01 00:00:00', '2026-03-02 09:00:00');
                """));

        // ...and cascades on delete, matching 0011.
        conn.Execute("DELETE FROM releases WHERE id = @releaseId;", new { releaseId });
        Assert.Equal(0, conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements;"));
    }

    /// <summary>
    /// 0012 applies to a database that already holds update events and buckets
    /// them — the state every existing install is in. Nothing is back-filled:
    /// an empty acknowledgement table means "the user has dismissed nothing",
    /// which is the correct reading and leaves every existing badge standing.
    /// </summary>
    [Fact]
    public void Migration_0012_applies_over_existing_update_events_and_acknowledges_nothing()
    {
        using var db = new TempDatabase();

        long releaseId;
        using (var conn = db.Factory.Open())
        {
            conn.Execute("DROP TABLE update_acknowledgements;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0012%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Riven') RETURNING id;");
            releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Riven') RETURNING id;",
                new { workId });
            conn.Execute("""
                INSERT INTO update_events (release_id, kind, occurred_at, title)
                VALUES (@releaseId, 'build_push', '2026-05-01 00:00:00', NULL),
                       (@releaseId, 'announcement', '2026-05-02 00:00:00', 'Patch notes');
                """, new { releaseId });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        // The raw signals are untouched. §4.5 requires both be kept so the
        // heuristic can be retuned; the acknowledgement is a fact layered over
        // them, and must never be implemented by pruning one.
        Assert.Equal(2, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_events WHERE release_id = @releaseId;", new { releaseId }));
        Assert.Equal(0, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM update_acknowledgements;"));
    }

    [Fact]
    public void Every_connection_has_wal_and_foreign_keys_enabled()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        Assert.Equal("wal", conn.ExecuteScalar<string>("PRAGMA journal_mode;"));
        Assert.Equal(1, conn.ExecuteScalar<long>("PRAGMA foreign_keys;"));
    }

    [Fact]
    public void Check_constraints_reject_invalid_enumish_values()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('X') RETURNING id;");
        var releaseId = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'X') RETURNING id;",
            new { workId });

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute(
                "INSERT INTO update_events (release_id, kind, occurred_at) VALUES (@releaseId, 'not_a_kind', '2026-01-01 00:00:00');",
                new { releaseId }));

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute(
                "INSERT INTO merge_candidates (left_release_id, right_release_id, score, status) VALUES (@releaseId, @releaseId, 0.5, 'maybe');",
                new { releaseId }));

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute(
                "INSERT INTO external_ids (release_id, provider, provider_id) VALUES (@releaseId, 'psn', '1');",
                new { releaseId }));
    }

    [Fact]
    public void Foreign_keys_are_enforced()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            conn.Execute(
                "INSERT INTO releases (work_id, name) VALUES (999999, 'orphan');"));
    }

    /// <summary>
    /// 0013 gives an observation an identity. Applied on top of the 0012 shape
    /// with duplicate rows ALREADY on disk — which is the only state that
    /// matters, because a database that has been running the pre-fix resolver is
    /// exactly where the duplicates are, and a unique index that cannot be
    /// created on a populated database is a migration that bricks the app on
    /// launch.
    /// </summary>
    [Fact]
    public void Migration_0013_dedupes_before_it_constrains_and_keeps_the_canonical_row()
    {
        using var db = new TempDatabase();

        long ownershipId;
        using (var conn = db.Factory.Open())
        {
            // Rewind to 0012: drop the indexes and forget the script ran.
            conn.Execute("DROP INDEX ux_play_records_observation;");
            conn.Execute("DROP INDEX ux_playtime_snapshots_observation;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0013%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Portal 2') RETURNING id;");
            var releaseId = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Portal 2') RETURNING id;",
                new { workId });
            ownershipId = conn.ExecuteScalar<long>("""
                INSERT INTO ownerships (release_id, store, installed)
                VALUES (@releaseId, 'steam', 1) RETURNING id;
                """, new { releaseId });

            // What the pre-fix resolver actually wrote: the same stale reading
            // re-appended on every pass, with a null date — the case a plain
            // UNIQUE over a nullable column would not catch.
            for (var i = 0; i < 4; i++)
            {
                conn.Execute("""
                    INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                    VALUES (@ownershipId, 40, NULL, 'steam_web_api', '2019-03-04 21:00:00');
                    """, new { ownershipId });
            }

            // The same again, this time WITH a date, to prove the dedupe keys on
            // the whole fact rather than on the address.
            for (var i = 0; i < 3; i++)
            {
                conn.Execute("""
                    INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                    VALUES (@ownershipId, 40, '2019-03-01 10:00:00', 'steam_web_api', '2019-03-04 21:00:00');
                    """, new { ownershipId });
            }

            // A genuinely different observation at the same instant: another
            // reader, disagreeing. It is not a duplicate and must survive.
            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@ownershipId, 900, NULL, 'steam_local', '2019-03-04 21:00:00');
                """, new { ownershipId });

            for (var i = 0; i < 5; i++)
            {
                conn.Execute("""
                    INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                    VALUES (@ownershipId, 40, '2019-03-04 21:00:00');
                    """, new { ownershipId });
            }
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        // Three distinct facts out of eight rows in, and the survivors are the
        // lowest ids — the canonical row, not the last one written.
        var records = after.Query<(long Id, long Minutes, string? LastPlayed, string Source)>("""
            SELECT id, playtime_minutes, last_played_at, source
            FROM play_records WHERE ownership_id = @ownershipId ORDER BY id;
            """, new { ownershipId }).AsList();

        Assert.Equal(3, records.Count);
        Assert.Equal([1L, 5L, 8L], records.Select(r => r.Id));
        Assert.Equal([40L, 40L, 900L], records.Select(r => r.Minutes));

        Assert.Equal(1, after.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM playtime_snapshots WHERE ownership_id = @ownershipId;",
            new { ownershipId }));

        // Both indexes exist and are unique.
        foreach (var index in new[] { "ux_play_records_observation", "ux_playtime_snapshots_observation" })
        {
            Assert.Equal(1, after.ExecuteScalar<long>("""
                SELECT COUNT(*) FROM sqlite_master
                WHERE type = 'index' AND name = @index;
                """, new { index }));
        }

        // And they bite: the null-date replay that filled the table is now one
        // observation, rejected rather than appended.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            after.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@ownershipId, 40, NULL, 'steam_web_api', '2019-03-04 21:00:00');
                """, new { ownershipId }));

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            after.Execute("""
                INSERT INTO playtime_snapshots (ownership_id, playtime_minutes, observed_at)
                VALUES (@ownershipId, 40, '2019-03-04 21:00:00');
                """, new { ownershipId }));
    }

    /// <summary>
    /// 0015 seeds the per-account membership table from what the old
    /// single-winner <c>ownerships.account_ref</c> column held, so an existing
    /// install has rows before its first sync rather than after it.
    ///
    /// <para>The seed's own honesty is the thing under test. It stamps
    /// <c>source = 'ownerships.account_ref'</c> because a seeded row carries the
    /// exact ambiguity the table replaces — it names whoever played the game
    /// most, which on a shared game is routinely not the only owner — and the
    /// bucket query refuses to hide anything on that evidence alone.</para>
    /// </summary>
    [Fact]
    public void Migration_0015_seeds_memberships_from_the_winning_account_ref()
    {
        using var db = new TempDatabase();

        using (var conn = db.Factory.Open())
        {
            // Rewind to 0014: drop the table and forget the script ran.
            conn.Execute("DROP TABLE ownership_accounts;");
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0015%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Portal 2') RETURNING id;");

            long Release(string name) => conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, @name) RETURNING id;",
                new { workId, name });

            long Ownership(long releaseId, string store, string? accountRef)
                => conn.ExecuteScalar<long>("""
                    INSERT INTO ownerships (release_id, store, account_ref, installed)
                    VALUES (@releaseId, @store, @accountRef, 1) RETURNING id;
                    """, new { releaseId, store, accountRef });

            // An attributed ownership with a play history: the newest reading is
            // the one the seed must carry, by (observed_at, id) like every other
            // reader of this table.
            var attributed = Ownership(Release("Attributed"), "steam", "11111");
            conn.Execute("""
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at)
                VALUES (@attributed, 40, '2024-01-01 00:00:00', 'steam_local', '2024-01-02 00:00:00'),
                       (@attributed, 900, '2026-08-01 00:00:00', 'steam_local', '2026-08-02 00:00:00');
                """, new { attributed });

            // Attributed, never played: no play record to borrow figures from.
            var unplayed = Ownership(Release("Unplayed"), "steam", "22222");

            // Unattributed, and a blank that is the same absence as a null.
            var anonymous = Ownership(Release("Anonymous"), "epic", null);
            var blank = Ownership(Release("Blank"), "gog", "   ");

            Assert.NotEqual(0, unplayed);
            Assert.NotEqual(0, anonymous);
            Assert.NotEqual(0, blank);
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        var rows = after.Query<(long OwnershipId, string AccountRef, long? Minutes, string? LastPlayed, string Source, string FirstSeen, string LastSeen)>("""
            SELECT oa.ownership_id, oa.account_ref, oa.playtime_minutes, oa.last_played_at,
                   oa.source, oa.first_seen_at, oa.last_seen_at
            FROM ownership_accounts oa
            JOIN ownerships o ON o.id = oa.ownership_id
            ORDER BY oa.account_ref;
            """).ToList();

        // Two rows: the two ownerships that named an account. A null and a blank
        // name nobody, and a row about nobody would count as evidence AGAINST
        // the user in the filter.
        Assert.Equal(2, rows.Count);
        Assert.Equal(["11111", "22222"], rows.Select(r => r.AccountRef));
        Assert.All(rows, r => Assert.Equal("ownerships.account_ref", r.Source));

        // The newest reading, and the observation time that produced it — the
        // seed states nothing it did not read.
        var attributedRow = rows[0];
        Assert.Equal(900, attributedRow.Minutes);
        Assert.Equal("2026-08-01 00:00:00", attributedRow.LastPlayed);
        Assert.Equal("2026-08-02 00:00:00", attributedRow.FirstSeen);
        Assert.Equal(attributedRow.FirstSeen, attributedRow.LastSeen);

        // Never played: the account holds it and nothing measured a session.
        var unplayedRow = rows[1];
        Assert.Null(unplayedRow.Minutes);
        Assert.Null(unplayedRow.LastPlayed);
        Assert.NotEmpty(unplayedRow.FirstSeen);

        // And the key bites: one row per (ownership, account).
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            after.Execute("""
                INSERT INTO ownership_accounts (
                    ownership_id, account_ref, source, first_seen_at, last_seen_at)
                VALUES (@ownershipId, '11111', 'steam_local', '2026-08-26', '2026-08-26');
                """, new { ownershipId = attributedRow.OwnershipId }));
    }

    [Fact]
    public void Migration_0016_canonicalises_pairs_and_keeps_terminal_decisions()
    {
        // A database created before 0016 could hold a self-pair and both
        // orientations of one pair; the new CHECK and unique key would fail to
        // build over them. Simulate that shape, then let 0016 run.
        using var db = new TempDatabase();

        long releaseA;
        long releaseB;
        long releaseC;
        using (var conn = db.Factory.Open())
        {
            conn.Execute("DROP TABLE merge_applications;");
            conn.Execute("DROP TABLE merge_candidates;");
            conn.Execute(PreCanonicalMergeCandidates);
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0016%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Hades') RETURNING id;");
            releaseA = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'A') RETURNING id;", new { workId });
            releaseB = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'B') RETURNING id;", new { workId });
            releaseC = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'C') RETURNING id;", new { workId });

            conn.Execute("""
                INSERT INTO merge_candidates (left_release_id, right_release_id, score, status) VALUES
                    (@a, @a, 0.99, 'pending'),
                    (@a, @b, 0.80, 'pending'),
                    (@b, @a, 0.80, 'rejected'),
                    (@c, @b, 0.70, 'confirmed');
                """, new { a = releaseA, b = releaseB, c = releaseC });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        var rows = after.Query<(long Left, long Right, string Status)>(
            "SELECT left_release_id, right_release_id, status FROM merge_candidates ORDER BY left_release_id;")
            .ToList();

        // The self-pair is gone; the mirrored pair is one row in canonical
        // orientation, and the user's rejection beat the untouched proposal.
        Assert.Equal(2, rows.Count);
        Assert.Equal((Math.Min(releaseA, releaseB), Math.Max(releaseA, releaseB), "rejected"), rows[0]);
        Assert.Equal((Math.Min(releaseB, releaseC), Math.Max(releaseB, releaseC), "confirmed"), rows[1]);

        // And the constraints now bite, whoever writes.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => after.Execute(
            "INSERT INTO merge_candidates (left_release_id, right_release_id, score) VALUES (@a, @a, 0.5);",
            new { a = releaseA }));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => after.Execute(
            "INSERT INTO merge_candidates (left_release_id, right_release_id, score) VALUES (@c, @a, 0.5);",
            new { a = releaseA, c = releaseC }));
    }

    [Fact]
    public void Migration_0017_preserves_every_candidate_and_status()
    {
        using var db = new TempDatabase();

        long releaseA;
        long releaseB;
        long releaseC;
        long releaseD;
        using (var conn = db.Factory.Open())
        {
            // Back to the shape 0016 leaves behind: three statuses, no journal.
            conn.Execute("DROP TABLE merge_undo_rows;");
            conn.Execute("DROP TABLE merge_applications;");
            conn.Execute("DROP TABLE merge_candidates;");
            conn.Execute(PostCanonicalMergeTables);
            conn.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0017%';");

            var workId = conn.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Hades') RETURNING id;");
            releaseA = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'A') RETURNING id;", new { workId });
            releaseB = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'B') RETURNING id;", new { workId });
            releaseC = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'C') RETURNING id;", new { workId });
            releaseD = conn.ExecuteScalar<long>(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'D') RETURNING id;", new { workId });

            conn.Execute("""
                INSERT INTO merge_candidates (
                    id, left_release_id, right_release_id, score, signals_json, status) VALUES
                    (11, @a, @b, 0.80, '{"title":1}', 'pending'),
                    (12, @a, @c, 0.70, NULL,          'rejected'),
                    (13, @b, @d, 0.95, '{"year":1}',  'confirmed');
                """, new { a = releaseA, b = releaseB, c = releaseC, d = releaseD });
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();

        var rows = after.Query<(long Id, long Left, long Right, double Score, string? Signals, string Status)>(
            "SELECT id, left_release_id, right_release_id, score, signals_json, status "
            + "FROM merge_candidates ORDER BY id;")
            .ToList();

        // Ids, scores, payloads and statuses all survive the rebuild.
        Assert.Equal(3, rows.Count);
        Assert.Equal((11L, releaseA, releaseB, 0.80, "{\"title\":1}", "pending"), rows[0]);
        Assert.Equal((12L, releaseA, releaseC, 0.70, null, "rejected"), rows[1]);
        Assert.Equal((13L, releaseB, releaseD, 0.95, "{\"year\":1}", "confirmed"), rows[2]);

        // The fourth status is now admissible, and nothing else is.
        after.Execute("UPDATE merge_candidates SET status = 'undone' WHERE id = 11;");
        Assert.Equal("undone", after.ExecuteScalar<string>(
            "SELECT status FROM merge_candidates WHERE id = 11;"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => after.Execute(
            "UPDATE merge_candidates SET status = 'reversed' WHERE id = 11;"));

        // And the journal arrived, with its hard reference to the audit row and
        // its CHECK on the operation name.
        Assert.Equal(0, after.ExecuteScalar<long>("SELECT COUNT(*) FROM merge_undo_rows;"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => after.Execute("""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            VALUES (999, 1, 'works', 'delete', '{}', '{}');
            """));

        var applicationId = after.ExecuteScalar<long>("""
            INSERT INTO merge_applications (
                candidate_id, left_release_id, right_release_id, mode,
                surviving_work_id, applied_at, undo_journal_version)
            VALUES (1, 1, 2, 'work_only', 1, '2026-08-31 00:00:00', 1)
            RETURNING id;
            """);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => after.Execute("""
            INSERT INTO merge_undo_rows (application_id, seq, table_name, op, key_json, before_json)
            VALUES (@applicationId, 1, 'works', 'sideways', '{}', '{}');
            """, new { applicationId }));

        // undone_at is NULL while a merge stands, and undo_journal_version is
        // NULL for every row that predates this migration.
        Assert.Null(after.ExecuteScalar<string>(
            "SELECT undone_at FROM merge_applications WHERE id = @applicationId;", new { applicationId }));
    }

    [Fact]
    public void The_twice_rebuilt_merge_candidates_still_rejects_self_pairs_and_mirrors()
    {
        using var db = new TempDatabase();
        using var conn = db.Factory.Open();

        var workId = conn.ExecuteScalar<long>(
            "INSERT INTO works (name) VALUES ('Hades') RETURNING id;");
        var releaseA = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'A') RETURNING id;", new { workId });
        var releaseB = conn.ExecuteScalar<long>(
            "INSERT INTO releases (work_id, name) VALUES (@workId, 'B') RETURNING id;", new { workId });

        conn.Execute(
            "INSERT INTO merge_candidates (left_release_id, right_release_id, score) VALUES (@a, @b, 0.9);",
            new { a = releaseA, b = releaseB });

        // 0016's two invariants survive 0017's rebuild of the same table.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => conn.Execute(
            "INSERT INTO merge_candidates (left_release_id, right_release_id, score) VALUES (@a, @a, 0.5);",
            new { a = releaseA }));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => conn.Execute(
            "INSERT INTO merge_candidates (left_release_id, right_release_id, score) VALUES (@b, @a, 0.5);",
            new { a = releaseA, b = releaseB }));
    }
}
