using Microsoft.Data.Sqlite;
using Winnow.App.Services;
using Winnow.Data;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The ways the Hoard→Winnow move could hand the app the WRONG library, or a
/// database standing beside somebody else's write-ahead log.
///
/// <para>These are the cases <see cref="WinnowDataLocationTests"/> could not
/// reach, because they are about what is INSIDE the files rather than where the
/// files are: a directory is adopted for holding a database that opens, and a
/// database moves only together with its own sidecars. Several of them therefore
/// seed real, migrated SQLite databases rather than the text-file stand-ins the
/// pure-movement tests use.</para>
/// </summary>
public sealed class DataMigrationSafetyTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), "winnow-datasafety-" + Guid.NewGuid().ToString("N"));

    private string Root => Path.Combine(_sandbox, "Winnow");

    private string Legacy => Path.Combine(_sandbox, "Hoard");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_sandbox))
            {
                Directory.Delete(_sandbox, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ── An empty new directory must never win ───────────────────────────────

    [Fact]
    public void An_empty_new_directory_loses_to_a_populated_legacy_one()
    {
        SeedLegacyLibrary("Deus Ex");
        Directory.CreateDirectory(Root);

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        // The whole compatibility rule in one assertion: never point at an
        // empty new directory while the real library is still on disk.
        Assert.Equal(DataMigrationOutcome.LegacyPreferred, location.Outcome);
        Assert.Equal(Legacy, location.Root);
        Assert.Equal(Path.Combine(Legacy, "hoard.db"), location.DatabasePath);
        Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));

        // And nothing was merged, moved or deleted to get there.
        Assert.True(File.Exists(Path.Combine(Legacy, "hoard.db")));
        Assert.True(File.Exists(Path.Combine(Legacy, "covers", "440.jpg")));
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db")));
    }

    [Fact]
    public void A_new_directory_holding_an_empty_database_loses_to_a_populated_legacy_one()
    {
        // What a run that created the file and then died before migrating it
        // leaves behind: a real, valid, entirely empty SQLite database.
        SeedLegacyLibrary("Deus Ex");
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(Path.Combine(Root, "winnow.db"), []);

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.LegacyPreferred, location.Outcome);
        Assert.Equal(Path.Combine(Legacy, "hoard.db"), location.DatabasePath);
        Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));
    }

    [Fact]
    public void A_new_directory_holding_a_truncated_database_loses_to_a_populated_legacy_one()
    {
        // The shape the old code adopted silently: half a database from a copy
        // that died, beside a legacy directory that is still whole.
        SeedLegacyLibrary("Deus Ex");
        Directory.CreateDirectory(Root);
        var partial = File.ReadAllBytes(Path.Combine(Legacy, "hoard.db"));
        File.WriteAllBytes(Path.Combine(Root, "winnow.db"), partial[..(partial.Length / 3)]);

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.LegacyPreferred, location.Outcome);
        Assert.Equal(Legacy, location.Root);
        Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));
    }

    [Fact]
    public void A_new_directory_that_really_does_hold_a_library_still_wins()
    {
        // The other half of the rule: validation decides, and when the new
        // directory validates it wins exactly as before. Nothing is merged.
        SeedLegacyLibrary("Deus Ex");
        Directory.CreateDirectory(Root);
        SeedDatabase(Path.Combine(Root, "winnow.db"), "Prey");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.BothPresent, location.Outcome);
        Assert.Equal(Root, location.Root);
        Assert.Equal(["Prey"], WorkNames(location.DatabasePath));
        Assert.True(File.Exists(Path.Combine(Legacy, "hoard.db")));
    }

    // ── One database, one set of sidecars ───────────────────────────────────

    [Fact]
    public void A_directory_holding_both_database_names_renames_nothing()
    {
        // The corruption this rule exists to prevent: hoard.db-wal becoming
        // winnow.db-wal beside a winnow.db it does not describe.
        Directory.CreateDirectory(Root);
        SeedDatabase(Path.Combine(Root, "winnow.db"), "Prey");
        File.WriteAllText(Path.Combine(Root, "hoard.db"), "a different library");
        File.WriteAllText(Path.Combine(Root, "hoard.db-wal"), "its uncheckpointed sessions");
        File.WriteAllText(Path.Combine(Root, "hoard.db-shm"), "its shared memory");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        // Not one file of the legacy set moved.
        Assert.True(File.Exists(Path.Combine(Root, "hoard.db")));
        Assert.True(File.Exists(Path.Combine(Root, "hoard.db-wal")));
        Assert.True(File.Exists(Path.Combine(Root, "hoard.db-shm")));

        // And no sidecar of the OTHER set was invented from them.
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db-wal")));
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db-shm")));

        // The database that opens is the one that gets used.
        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);
        Assert.Equal(["Prey"], WorkNames(location.DatabasePath));
    }

    [Fact]
    public void Legacy_sidecars_are_not_moved_when_the_destination_database_exists()
    {
        // No hoard.db at all — just its orphaned sidecars beside a winnow.db.
        // The old code moved each missing sidecar independently, which is how a
        // foreign write-ahead log ends up named after this database.
        Directory.CreateDirectory(Root);
        SeedDatabase(Path.Combine(Root, "winnow.db"), "Prey");
        File.WriteAllText(Path.Combine(Root, "hoard.db-wal"), "somebody else's sessions");
        File.WriteAllText(Path.Combine(Root, "hoard.db-shm"), "somebody else's shared memory");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.False(File.Exists(Path.Combine(Root, "winnow.db-wal")));
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db-shm")));
        Assert.True(File.Exists(Path.Combine(Root, "hoard.db-wal")));
        Assert.True(File.Exists(Path.Combine(Root, "hoard.db-shm")));

        // The library still opens, and still says what it said before.
        Assert.Equal(["Prey"], WorkNames(location.DatabasePath));
    }

    [Fact]
    public void A_stray_destination_sidecar_blocks_the_whole_rename_rather_than_half_of_it()
    {
        // winnow.db-wal with no winnow.db: a log describing a database that is
        // not there. Renaming hoard.db onto winnow.db would put the two
        // together, so the set stays under its old name and is opened there.
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "hoard.db"), "a thousand games");
        File.WriteAllText(Path.Combine(Root, "hoard.db-wal"), "uncheckpointed sessions");
        File.WriteAllText(Path.Combine(Root, "winnow.db-wal"), "an orphan log");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.True(File.Exists(Path.Combine(Root, "hoard.db")));
        Assert.True(File.Exists(Path.Combine(Root, "hoard.db-wal")));
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db")));
        Assert.Equal(Path.Combine(Root, "hoard.db"), location.DatabasePath);
    }

    [Fact]
    public void A_whole_legacy_set_still_moves_together_when_nothing_is_in_the_way()
    {
        // The rule refuses mixing; it does not refuse the migration. A real
        // database moves under its new name and arrives openable.
        Directory.CreateDirectory(Root);
        SeedDatabase(Path.Combine(Root, "hoard.db"), "Riven");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);
        Assert.False(File.Exists(Path.Combine(Root, "hoard.db")));
        Assert.Equal(["Riven"], WorkNames(location.DatabasePath));
    }

    // ── Staging, promotion, and the interrupted copy ────────────────────────

    [Fact]
    public void An_abandoned_staging_directory_is_swept_and_the_migration_restarts()
    {
        // The interrupted promotion: a copy that died after writing part of the
        // staging tree and before renaming it into place, with cleanup that
        // never ran. The next launch must not adopt any of it.
        SeedLegacyLibrary("Deus Ex");

        var abandoned = Root + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(abandoned);
        SeedDatabase(Path.Combine(abandoned, "hoard.db"), "HALF A LIBRARY");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        // Restarted cleanly: the real tree moved, whole.
        Assert.Equal(DataMigrationOutcome.Moved, location.Outcome);
        Assert.Equal(Root, location.Root);
        Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));
        Assert.True(File.Exists(Path.Combine(Root, "covers", "440.jpg")));

        // And the abandoned work is gone rather than promoted.
        Assert.False(Directory.Exists(abandoned));
        Assert.Empty(Directory.EnumerateDirectories(_sandbox, "Winnow.staging-*"));
    }

    [Fact]
    public void An_abandoned_staging_directory_never_becomes_the_library_it_was_a_copy_of()
    {
        // Same leftover, but the new directory now also exists and is empty:
        // the partial copy must lose to the legacy library, not be adopted as
        // the contents of the new one.
        SeedLegacyLibrary("Deus Ex");
        Directory.CreateDirectory(Root);

        var abandoned = Root + ".staging-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(abandoned);
        SeedDatabase(Path.Combine(abandoned, "hoard.db"), "HALF A LIBRARY");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.LegacyPreferred, location.Outcome);
        Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));
        Assert.False(Directory.Exists(abandoned));
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db")));
    }

    [Fact]
    public void A_copy_that_cannot_be_promoted_leaves_nothing_at_the_destination()
    {
        // The failure the staging design exists for. A file sitting on the new
        // path defeats both the rename and the promotion, so the copy runs and
        // then cannot land — and the old code's equivalent would have left a
        // partial directory for the next launch to take.
        SeedLegacyLibrary("Deus Ex");
        File.WriteAllText(Root, "not a directory");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.Failed, location.Outcome);
        Assert.Equal(Legacy, location.Root);
        Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));

        // Nothing at the destination, and nothing left staged beside it.
        Assert.False(Directory.Exists(Root));
        Assert.Equal("not a directory", File.ReadAllText(Root));
        Assert.Empty(Directory.EnumerateDirectories(_sandbox, "Winnow.staging-*"));

        // The source is untouched and complete.
        Assert.True(File.Exists(Path.Combine(Legacy, "covers", "440.jpg")));
        Assert.True(File.Exists(Path.Combine(Legacy, "themes", "bottle-green.json")));
    }

    [Fact]
    public void A_copy_fallback_stages_checks_and_promotes_the_whole_tree()
    {
        // An open handle inside the tree makes Windows refuse to rename the
        // directory, which is the real-world trigger for the copy path.
        SeedLegacyLibrary("Deus Ex");

        using (new FileStream(
            Path.Combine(Legacy, "covers", "440.jpg"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            var location = WinnowDataLocation.Resolve(Root, Legacy);

            Assert.Equal(DataMigrationOutcome.Copied, location.Outcome);
            Assert.Equal(Root, location.Root);
            Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);

            // The copy is a library, not a file-shaped hole where one was.
            Assert.Equal(["Deus Ex"], WorkNames(location.DatabasePath));
            Assert.True(File.Exists(Path.Combine(Root, "covers", "440.jpg")));
            Assert.True(File.Exists(Path.Combine(Root, "themes", "bottle-green.json")));
            Assert.True(File.Exists(Path.Combine(Root, "WebView2", "EBWebView", "profile.dat")));

            // A copy, so the original survives as a backup — and nothing is
            // left staged.
            Assert.True(File.Exists(Path.Combine(Legacy, "hoard.db")));
            Assert.Empty(Directory.EnumerateDirectories(_sandbox, "Winnow.staging-*"));
        }
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>A legacy tree shaped like the real one, with a real database.</summary>
    private void SeedLegacyLibrary(string workName)
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Path.Combine(Legacy, "covers"));
        Directory.CreateDirectory(Path.Combine(Legacy, "themes"));
        Directory.CreateDirectory(Path.Combine(Legacy, "WebView2", "EBWebView"));

        SeedDatabase(Path.Combine(Legacy, "hoard.db"), workName);

        File.WriteAllText(Path.Combine(Legacy, "covers", "440.jpg"), "art");
        File.WriteAllText(Path.Combine(Legacy, "themes", "bottle-green.json"), "{}");
        File.WriteAllText(Path.Combine(Legacy, "WebView2", "EBWebView", "profile.dat"), "epic session");
    }

    /// <summary>
    /// A migrated Winnow database holding one work, closed cleanly so that the
    /// sidecars are gone and the file on disk is the whole library.
    /// </summary>
    private static void SeedDatabase(string path, string workName)
    {
        var factory = new SqliteConnectionFactory(path, pooling: false);
        new DatabaseInitializer(factory)
        {
            // Never reached — a database being created has nothing to back up —
            // but pointed away from the real directory regardless.
            Backups = DatabaseBackupPolicy.Default with { Directory = path + "-backups" },
        }.Initialize();

        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO works (name) VALUES ($name);";
        command.Parameters.AddWithValue("$name", workName);
        command.ExecuteNonQuery();
    }

    /// <summary>What the database at a path actually says, opened where it lies.</summary>
    private static List<string> WorkNames(string databasePath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM works ORDER BY name;";
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
