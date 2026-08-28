using Winnow.App.Services;
using Winnow.App.Themes;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The one-time move of <c>%LOCALAPPDATA%\Hoard</c> to
/// <c>%LOCALAPPDATA%\Winnow</c>.
///
/// <para>Every test here runs against a temp directory laid out like the real
/// one — a database, both SQLite sidecars, and the three subdirectories
/// (<c>covers</c>, <c>themes</c>, <c>WebView2</c>) — because the failure this
/// code exists to prevent is not "the database did not move", it is "the
/// database moved and the covers did not".</para>
/// </summary>
public sealed class WinnowDataLocationTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), "winnow-datamove-" + Guid.NewGuid().ToString("N"));

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
    }

    // ── Case 1: old only — the migration that matters ───────────────────────

    [Fact]
    public void Old_only_moves_the_whole_tree_and_renames_the_database()
    {
        SeedLegacy();

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.Moved, location.Outcome);
        Assert.Equal(Root, location.Root);
        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);

        // The database, under its new name, with both sidecars beside it.
        Assert.True(File.Exists(Path.Combine(Root, "winnow.db")));
        Assert.True(File.Exists(Path.Combine(Root, "winnow.db-wal")));
        Assert.True(File.Exists(Path.Combine(Root, "winnow.db-shm")));
        Assert.False(File.Exists(Path.Combine(Root, "hoard.db")));
        Assert.False(File.Exists(Path.Combine(Root, "hoard.db-wal")));

        // And everything that is not the database.
        Assert.True(File.Exists(Path.Combine(Root, "covers", "440.jpg")));
        Assert.True(File.Exists(Path.Combine(Root, "themes", "bottle-green.json")));
        Assert.True(File.Exists(Path.Combine(Root, "WebView2", "EBWebView", "profile.dat")));

        // A move, not a copy: nothing is left behind to go stale.
        Assert.False(Directory.Exists(Legacy));
    }

    [Fact]
    public void The_moved_database_is_the_same_bytes()
    {
        SeedLegacy();
        var before = File.ReadAllBytes(Path.Combine(Legacy, "hoard.db"));

        WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Root, "winnow.db")));
    }

    // ── Case 2: new only — the steady state ─────────────────────────────────

    [Fact]
    public void New_only_does_nothing()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "winnow.db"), "current");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.None, location.Outcome);
        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);
        Assert.Equal("current", File.ReadAllText(Path.Combine(Root, "winnow.db")));
    }

    [Fact]
    public void Running_twice_changes_nothing_the_second_time()
    {
        SeedLegacy();

        var first = WinnowDataLocation.Resolve(Root, Legacy);
        var second = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.Moved, first.Outcome);
        Assert.Equal(DataMigrationOutcome.None, second.Outcome);
        Assert.Equal(first.DatabasePath, second.DatabasePath);
        Assert.True(File.Exists(Path.Combine(Root, "winnow.db")));
    }

    [Fact]
    public void Neither_directory_is_not_an_error()
    {
        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.None, location.Outcome);
        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);

        // Resolving must not CREATE anything either — DatabaseInitializer owns that.
        Assert.False(Directory.Exists(Root));
    }

    // ── Case 3: both — prefer the new one, merge nothing ────────────────────

    [Fact]
    public void Both_prefers_the_new_one_and_leaves_the_legacy_tree_alone()
    {
        SeedLegacy();
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "winnow.db"), "current");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.BothPresent, location.Outcome);
        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);
        Assert.Equal("current", File.ReadAllText(Path.Combine(Root, "winnow.db")));

        // Not merged, and not deleted.
        Assert.True(File.Exists(Path.Combine(Legacy, "hoard.db")));
        Assert.True(File.Exists(Path.Combine(Legacy, "covers", "440.jpg")));
        Assert.False(Directory.Exists(Path.Combine(Root, "covers")));
    }

    // ── Case 4: locked — fail onto the real data, never onto nothing ────────

    [Fact]
    public void A_locked_database_leaves_everything_where_it_is()
    {
        SeedLegacy();

        using var held = new FileStream(
            Path.Combine(Legacy, "hoard.db"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.SourceBusy, location.Outcome);

        // The whole point: this run reads the REAL library where it lies,
        // rather than opening an empty database at the new path.
        Assert.Equal(Legacy, location.Root);
        Assert.Equal(Path.Combine(Legacy, "hoard.db"), location.DatabasePath);

        // Nothing half-moved.
        Assert.False(Directory.Exists(Root));
        Assert.True(File.Exists(Path.Combine(Legacy, "hoard.db")));
        Assert.True(File.Exists(Path.Combine(Legacy, "hoard.db-wal")));
        Assert.True(File.Exists(Path.Combine(Legacy, "covers", "440.jpg")));
    }

    [Fact]
    public void A_locked_sidecar_also_stops_the_move()
    {
        SeedLegacy();

        using var held = new FileStream(
            Path.Combine(Legacy, "hoard.db-wal"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.SourceBusy, location.Outcome);
        Assert.False(Directory.Exists(Root));
    }

    [Fact]
    public void The_move_succeeds_once_the_lock_is_released()
    {
        SeedLegacy();

        using (new FileStream(
            Path.Combine(Legacy, "hoard.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(
                DataMigrationOutcome.SourceBusy,
                WinnowDataLocation.Resolve(Root, Legacy).Outcome);
        }

        Assert.Equal(
            DataMigrationOutcome.Moved,
            WinnowDataLocation.Resolve(Root, Legacy).Outcome);
    }

    // ── Shapes that are not the happy one ───────────────────────────────────

    [Fact]
    public void A_legacy_folder_with_no_database_still_moves_its_contents()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "covers"));
        File.WriteAllText(Path.Combine(Legacy, "covers", "440.jpg"), "art");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.Moved, location.Outcome);
        Assert.True(File.Exists(Path.Combine(Root, "covers", "440.jpg")));
    }

    [Fact]
    public void A_database_with_no_sidecars_moves_cleanly()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "hoard.db"), "library");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        Assert.Equal(DataMigrationOutcome.Moved, location.Outcome);
        Assert.Equal("library", File.ReadAllText(Path.Combine(Root, "winnow.db")));
        Assert.False(File.Exists(Path.Combine(Root, "winnow.db-wal")));
    }

    // ── A tree that moved but kept the old file name ───────────────────────

    [Fact]
    public void A_new_directory_still_holding_hoard_db_is_finished_rather_than_ignored()
    {
        // What a previous run leaves behind if the tree moved but the file
        // rename did not: a whole library under the new folder, stale name.
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "covers"));
        File.WriteAllText(Path.Combine(Root, "hoard.db"), "a thousand games");
        File.WriteAllText(Path.Combine(Root, "hoard.db-wal"), "uncheckpointed sessions");

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        // The retry finishes the job.
        Assert.Equal(DataMigrationOutcome.None, location.Outcome);
        Assert.Equal(Path.Combine(Root, "winnow.db"), location.DatabasePath);
        Assert.Equal("a thousand games", File.ReadAllText(Path.Combine(Root, "winnow.db")));
        Assert.True(File.Exists(Path.Combine(Root, "winnow.db-wal")));
        Assert.False(File.Exists(Path.Combine(Root, "hoard.db")));
    }

    [Fact]
    public void A_stale_name_that_cannot_be_renamed_is_opened_as_it_is()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "hoard.db"), "a thousand games");

        // Hold it open so the rename cannot happen on this run.
        using var held = new FileStream(
            Path.Combine(Root, "hoard.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var location = WinnowDataLocation.Resolve(Root, Legacy);

        // The library is whole and under the right folder, so it is used — the
        // one answer that must never come back here is an empty winnow.db.
        Assert.Equal(Root, location.Root);
        Assert.Equal(Path.Combine(Root, "hoard.db"), location.DatabasePath);
    }

    private void SeedLegacy()
    {
        Directory.CreateDirectory(Legacy);
        Directory.CreateDirectory(Path.Combine(Legacy, "covers"));
        Directory.CreateDirectory(Path.Combine(Legacy, "themes"));
        Directory.CreateDirectory(Path.Combine(Legacy, "WebView2", "EBWebView"));

        File.WriteAllText(Path.Combine(Legacy, "hoard.db"), "a thousand games");
        File.WriteAllText(Path.Combine(Legacy, "hoard.db-wal"), "uncheckpointed sessions");
        File.WriteAllText(Path.Combine(Legacy, "hoard.db-shm"), "shared memory");
        File.WriteAllText(Path.Combine(Legacy, "covers", "440.jpg"), "art");
        File.WriteAllText(Path.Combine(Legacy, "themes", "bottle-green.json"), "{}");
        File.WriteAllText(Path.Combine(Legacy, "WebView2", "EBWebView", "profile.dat"), "epic session");
    }
}

/// <summary>
/// The other half of the rename, and the one that would have taken the app down
/// on launch: DbUp identifies an applied migration by its embedded-resource
/// name, and that name carries the assembly's root namespace.
/// </summary>
public sealed class LegacyMigrationJournalTests
{
    [Fact]
    public void A_journal_written_by_Hoard_is_re_pointed_rather_than_replayed()
    {
        using var db = new TempDatabase();

        // Put the journal back the way a pre-rename build left it.
        Execute(db, """
            UPDATE SchemaVersions
               SET ScriptName = 'Hoard.Data.Migrations.' || substr(ScriptName, 24);
            """);
        Assert.All(ScriptNames(db), n => Assert.StartsWith("Hoard.Data.Migrations.", n));
        var applied = ScriptNames(db).Count;

        // Re-running the initializer is what happens on the next launch. Without
        // the re-point this throws: every script looks new, and 0001 hits a
        // works table that already exists.
        db.Initializer.Initialize();

        var names = ScriptNames(db);
        Assert.Equal(applied, names.Count);
        Assert.All(names, n => Assert.StartsWith("Winnow.Data.Migrations.", n));
    }

    [Fact]
    public void Re_pointing_the_journal_is_idempotent()
    {
        using var db = new TempDatabase();
        var before = ScriptNames(db);

        db.Initializer.Initialize();
        db.Initializer.Initialize();

        Assert.Equal(before, ScriptNames(db));
    }

    [Fact]
    public void A_journal_holding_both_spellings_does_not_gain_a_duplicate()
    {
        using var db = new TempDatabase();
        var applied = ScriptNames(db).Count;

        // The pathological case the NOT EXISTS guard is for.
        Execute(db, """
            INSERT INTO SchemaVersions (ScriptName, Applied)
            SELECT 'Hoard.Data.Migrations.' || substr(ScriptName, 24), Applied
              FROM SchemaVersions;
            """);
        Assert.Equal(applied * 2, ScriptNames(db).Count);

        db.Initializer.Initialize();

        var names = ScriptNames(db);
        Assert.Equal(applied, names.Distinct().Count());
        Assert.DoesNotContain(names, n => n.StartsWith("Hoard.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fresh_database_has_no_journal_to_re_point()
    {
        // migrate: false — Initialize() must survive SchemaVersions not existing.
        using var db = new TempDatabase(migrate: false);

        db.Initializer.Initialize();

        Assert.All(ScriptNames(db), n => Assert.StartsWith("Winnow.Data.Migrations.", n));
    }

    private static void Execute(TempDatabase db, string sql)
    {
        using var connection = db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<string> ScriptNames(TempDatabase db)
    {
        using var connection = db.Factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ScriptName FROM SchemaVersions ORDER BY ScriptName;";
        using var reader = command.ExecuteReader();

        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}

/// <summary>
/// The third thing the rename could have quietly broken: a settings row saying
/// <c>appearance.theme = hoard</c>, written by every build before it.
/// </summary>
public sealed class LegacyThemeIdTests
{
    [Fact]
    public void The_pre_rename_theme_id_still_resolves_to_the_house_theme()
    {
        Assert.Equal(WinnowThemes.Winnow, WinnowThemes.ById(WinnowThemes.LegacyDefaultId));
        Assert.Equal(WinnowThemes.Winnow, WinnowThemes.ById("hoard"));
    }

    [Fact]
    public void The_alias_resolves_through_the_service_catalogue_too()
    {
        var service = new ThemeService();

        Assert.Equal("winnow", service.ById("hoard").Id);
    }

    [Fact]
    public void A_user_theme_that_claims_the_old_id_wins_it()
    {
        // The alias is a bridge for old settings, not a reserved word: the
        // catalogue is consulted first, so an authored theme called "hoard"
        // still comes back when the settings row names it.
        var directory = Path.Combine(
            Path.GetTempPath(), "winnow-legacy-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "hoard.json"),
                """
                {
                  "schemaVersion": 1,
                  "id": "hoard",
                  "name": "My Hoard",
                  "reason": "A theme written by a test.",
                  "seeds": {
                    "ground":  "#0F1C1E",
                    "surface": "#16282A",
                    "text":    "#F0EDE7",
                    "flare":   "#FF4D93",
                    "volt":    "#4DE8C2",
                    "amber":   "#FFB63D",
                    "azure":   "#57A8F0",
                    "danger":  "#E04B45"
                  }
                }
                """);

            var service = new ThemeService(userThemes: new UserThemeStore(directory));
            service.ReloadUserThemes();

            var resolved = service.ById("hoard");
            Assert.Equal("hoard", resolved.Id);
            Assert.Equal("My Hoard", resolved.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void An_id_that_is_neither_still_falls_through_to_the_default()
    {
        Assert.Equal(WinnowThemes.Default, WinnowThemes.ById("phosphor"));
        Assert.Equal(WinnowThemes.Default, WinnowThemes.ById(null));
    }
}
