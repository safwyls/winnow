using Dapper;
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
        var journalRows = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM SchemaVersions;");
        Assert.Equal(1, journalRows);
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
