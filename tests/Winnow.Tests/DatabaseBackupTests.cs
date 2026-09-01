using Dapper;
using Winnow.Data;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The copy taken before a schema changes.
///
/// <para>Transaction-per-script stops a script from applying halfway. It does
/// nothing about a script that applies perfectly and is wrong, about a disk that
/// fails mid-upgrade, or about a bug in the code that reads the new shape — and
/// the database is the only durable thing Winnow owns. So a migration that would
/// move the schema version has to leave a restorable copy behind first, and has
/// to refuse to run if it cannot.</para>
/// </summary>
public sealed class DatabaseBackupTests
{
    [Fact]
    public void A_pending_migration_writes_a_backup_named_for_the_schema_it_replaces()
    {
        using var db = new TempDatabase();
        Rewind(db);

        db.Initializer.Initialize();

        var backups = Backups(db);
        var backup = Assert.Single(backups);

        // Named after the version the copy holds, so a restore is a decision
        // rather than a guess.
        Assert.StartsWith(
            Path.GetFileName(db.DatabasePath) + ".pre-0011.", backup.Name, StringComparison.Ordinal);
        Assert.EndsWith(".bak", backup.Name, StringComparison.Ordinal);
        Assert.Equal(backup.FullName, db.Initializer.LastBackupPath);

        // Taken BEFORE the change: the copy is the old shape, the live database
        // is the new one. This is the assertion the whole feature rests on.
        var copied = TablesIn(backup.FullName);
        Assert.DoesNotContain("update_acknowledgements", copied);
        Assert.Contains("works", copied);
        Assert.Contains("update_acknowledgements", TablesIn(db.DatabasePath));
    }

    [Fact]
    public void The_backup_is_a_database_in_its_own_right()
    {
        using var db = new TempDatabase();

        using (var seed = db.Factory.Open())
        {
            var workId = seed.ExecuteScalar<long>(
                "INSERT INTO works (name) VALUES ('Riven') RETURNING id;");
            seed.Execute(
                "INSERT INTO releases (work_id, name) VALUES (@workId, 'Riven');", new { workId });
        }

        Rewind(db);
        db.Initializer.Initialize();

        var backup = Assert.Single(Backups(db));

        // Openable, checks clean, and still holds the rows that were there.
        var check = SqliteDatabaseCheck.Inspect(backup.FullName);
        Assert.Equal(DatabaseHealth.Healthy, check.Health);

        using var restored = new SqliteConnectionFactory(backup.FullName, pooling: false).Open();
        Assert.Equal("Riven", restored.ExecuteScalar<string>("SELECT name FROM works;"));
        Assert.Equal("Riven", restored.ExecuteScalar<string>("SELECT name FROM releases;"));
    }

    [Fact]
    public void Nothing_pending_takes_no_backup()
    {
        using var db = new TempDatabase();

        // The steady state: the schema version is not moving, so there is no
        // moment to protect and no copy to pay for.
        db.Initializer.Initialize();
        db.Initializer.Initialize();

        Assert.Empty(Backups(db));
        Assert.Null(db.Initializer.LastBackupPath);
    }

    [Fact]
    public void Creating_a_database_takes_no_backup()
    {
        // Twelve migrations are pending on a first run, and there is nothing
        // behind them: a refusal here would be a first launch that fails on a
        // full disk for the sake of copying an empty file.
        using var db = new TempDatabase(migrate: false);

        db.Initializer.Initialize();

        Assert.Empty(Backups(db));
        Assert.Null(db.Initializer.LastBackupPath);
        Assert.Equal(
            DatabaseHealth.Healthy,
            SqliteDatabaseCheck.Inspect(db.DatabasePath).Health);
    }

    [Fact]
    public void Only_the_newest_few_backups_survive()
    {
        using var db = new TempDatabase();

        var written = new List<string>();
        for (var run = 0; run < 5; run++)
        {
            Rewind(db);
            db.Initializer.Initialize();
            written.Add(db.Initializer.LastBackupPath!);
        }

        Assert.Equal(5, written.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // Three kept, and the three that are kept are the last three taken.
        var kept = Backups(db).Select(file => file.FullName).ToList();
        Assert.Equal(3, kept.Count);
        Assert.Equal(
            written.TakeLast(3).OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            kept.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    /// <remarks>
    /// Not on <see cref="TempDatabase"/>, and not because of the fixture: a
    /// database this test has deliberately made unreadable cannot be assumed
    /// deletable afterwards, so its cleanup has to forgive a file SQLite has not
    /// let go of. The shared fixture should keep failing loudly when a healthy
    /// database is left locked.
    /// </remarks>
    [Fact]
    public void A_database_that_fails_quick_check_is_not_migrated()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "winnow-damaged-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "winnow.db");
        var policy = DatabaseBackupPolicy.Default with
        {
            Directory = Path.Combine(directory, "backups"),
        };

        try
        {
            var factory = new SqliteConnectionFactory(path, pooling: false);
            var initializer = new DatabaseInitializer(factory) { Backups = policy };
            initializer.Initialize();

            using (var seed = factory.Open())
            {
                // Enough rows that the tail of the file is table data rather
                // than the schema and the journal, so the damage below lands
                // where a real disk fault would.
                seed.Execute("""
                    WITH RECURSIVE counter(n) AS (
                        SELECT 1 UNION ALL SELECT n + 1 FROM counter WHERE n < 3000
                    )
                    INSERT INTO works (name) SELECT 'Game ' || n FROM counter;
                    """);

                // Rewound here, while the database still reads, so that exactly
                // one migration is pending when the damage is done.
                seed.Execute("DROP TABLE IF EXISTS update_acknowledgements;");
                seed.Execute("DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0012%';");
            }

            Damage(path);
            Assert.False(SqliteDatabaseCheck.Inspect(path).IsUsable);

            var refused = Assert.Throws<InvalidOperationException>(initializer.Initialize);
            Assert.Contains("Refusing to migrate", refused.Message, StringComparison.Ordinal);

            // Refused before anything was written: no backup of a broken
            // database, and no schema change applied on top of one.
            Assert.Empty(DatabaseBackups.All(path, policy));
            Assert.Null(initializer.LastBackupPath);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception held) when (held is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void A_backup_that_cannot_be_written_stops_the_migration()
    {
        using var db = new TempDatabase();
        Rewind(db);

        // A backup directory that cannot exist: its parent is a file.
        var initializer = new DatabaseInitializer(db.Factory)
        {
            Backups = DatabaseBackupPolicy.Default with
            {
                Directory = Path.Combine(db.DatabasePath, "backups"),
            },
        };

        var refused = Assert.Throws<InvalidOperationException>(initializer.Initialize);
        Assert.Contains("pre-upgrade backup could not be written", refused.Message, StringComparison.Ordinal);

        // The schema did not move. That is the point: the alternative is an
        // irreversible change to the only copy, made straight after failing to
        // copy it.
        Assert.DoesNotContain("update_acknowledgements", TablesIn(db.DatabasePath));
        Assert.Null(initializer.LastBackupPath);
    }

    [Fact]
    public void A_policy_that_does_not_require_a_backup_proceeds_and_says_so()
    {
        using var db = new TempDatabase();
        Rewind(db);

        var initializer = new DatabaseInitializer(db.Factory)
        {
            Backups = DatabaseBackupPolicy.Default with
            {
                Directory = Path.Combine(db.DatabasePath, "backups"),
                Required = false,
            },
        };

        initializer.Initialize();

        // The escape hatch works, and leaves the fact that it was used where
        // somebody can find it: no backup path to report.
        Assert.Contains("update_acknowledgements", TablesIn(db.DatabasePath));
        Assert.Null(initializer.LastBackupPath);
    }

    [Fact]
    public void The_legacy_journal_re_point_still_runs_and_still_costs_no_backup()
    {
        using var db = new TempDatabase();

        using (var journal = db.Factory.Open())
        {
            journal.Execute("""
                UPDATE SchemaVersions
                   SET ScriptName = 'Hoard.Data.Migrations.' || substr(ScriptName, 24);
                """);
        }

        db.Initializer.Initialize();

        using var after = db.Factory.Open();
        var names = after.Query<string>("SELECT ScriptName FROM SchemaVersions;").ToList();
        Assert.All(names, name => Assert.StartsWith("Winnow.Data.Migrations.", name, StringComparison.Ordinal));

        // The re-point has to happen BEFORE the pending count is taken. If it
        // did not, every shipped migration would look pending here — which
        // would show up as a backup nobody asked for, and then as 0001 replaying
        // into a populated database.
        Assert.Empty(Backups(db));
        Assert.Null(db.Initializer.LastBackupPath);
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the database back in its pre-0012 shape so that exactly one
    /// migration is pending: the table 0012 adds is dropped, and the journal
    /// forgets the script ran.
    /// </summary>
    private static void Rewind(TempDatabase db)
    {
        // Rewinds to 0011, so everything after it has to be undone here too —
        // both its journal row and whatever it created, or the re-run fails on
        // an object that is already there. A new migration adds a line.
        using var connection = db.Factory.Open();
        connection.Execute("DROP TABLE IF EXISTS update_acknowledgements;");
        connection.Execute("DROP INDEX IF EXISTS ux_play_records_observation;");
        connection.Execute("DROP INDEX IF EXISTS ux_playtime_snapshots_observation;");
        connection.Execute("DROP TABLE IF EXISTS account_transactions;");
        connection.Execute("DROP TABLE IF EXISTS account_licenses;");
        connection.Execute("DROP TABLE IF EXISTS ownership_accounts;");

        // 0016 is the first migration that REBUILDS a table rather than adding
        // one, so undoing it means putting the old shape back, not just dropping
        // what it created: it replaced merge_candidates with a canonical version
        // carrying CHECK (left_release_id < right_release_id).
        connection.Execute("DROP TABLE IF EXISTS merge_applications;");
        connection.Execute("DROP TABLE IF EXISTS merge_candidates;");
        connection.Execute("""
            CREATE TABLE merge_candidates (
                id                INTEGER PRIMARY KEY,
                left_release_id   INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
                right_release_id  INTEGER NOT NULL REFERENCES releases(id) ON DELETE CASCADE,
                score             REAL NOT NULL CHECK (score >= 0.0 AND score <= 1.0),
                signals_json      TEXT,
                status            TEXT NOT NULL DEFAULT 'pending'
                                  CHECK (status IN ('pending', 'confirmed', 'rejected')),
                UNIQUE (left_release_id, right_release_id)
            );
            """);
        connection.Execute("CREATE INDEX ix_merge_candidates_status ON merge_candidates(status);");

        connection.Execute("""
            DELETE FROM SchemaVersions
            WHERE ScriptName LIKE '%0012%'
               OR ScriptName LIKE '%0013%'
               OR ScriptName LIKE '%0014%'
               OR ScriptName LIKE '%0015%'
               OR ScriptName LIKE '%0016%';
            """);
    }

    /// <summary>Overwrites the back half of the file, the way a bad sector would.</summary>
    private static void Damage(string databasePath)
    {
        using var file = new FileStream(databasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var from = file.Length / 2;
        var rubbish = new byte[file.Length - from];
        Array.Fill(rubbish, (byte)0xA5);

        file.Seek(from, SeekOrigin.Begin);
        file.Write(rubbish);
        file.Flush(flushToDisk: true);
    }

    private static IReadOnlyList<FileInfo> Backups(TempDatabase db)
        => DatabaseBackups.All(
            db.DatabasePath,
            DatabaseBackupPolicy.Default with { Directory = db.BackupDirectory });

    private static HashSet<string> TablesIn(string databasePath)
    {
        using var connection = new SqliteConnectionFactory(databasePath, pooling: false).Open();
        return SqliteDatabaseCheck.Tables(connection);
    }
}
