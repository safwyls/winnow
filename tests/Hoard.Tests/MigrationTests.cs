using Dapper;
using Hoard.Data;
using Xunit;

namespace Hoard.Tests;

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
    ];

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
}
