using Winnow.App;
using Winnow.App.Services;
using Winnow.Covers;
using Winnow.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The <c>--data-dir</c> override: argument parsing in both spellings and the
/// refusal when the value is missing; the resolved root and database path; that
/// a real database and its WAL sidecar land inside the override and nowhere
/// else; that the real composition root puts the database, covers and themes
/// inside it; that a legacy Hoard directory beside the override is left
/// byte-for-byte alone and nothing is copied into it; that an override pointed
/// at an old folder opens <c>hoard.db</c> in place; that ordinary resolution
/// still migrates as it always did; and one test per shape of unusable path.
/// </summary>
public sealed class DataDirectoryOverrideTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), "winnow-datadir-" + Guid.NewGuid().ToString("N"));

    /// <summary>A throwaway directory inside the sandbox, used as the
    /// <c>--data-dir</c> target.</summary>
    private string Scratch => Path.Combine(_sandbox, "scratch");

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

    // ── Parsing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Both_spellings_of_the_argument_carry_the_path()
    {
        Assert.Equal(Scratch, WinnowDataLocation.OverrideFrom(["--data-dir", Scratch]));
        Assert.Equal(Scratch, WinnowDataLocation.OverrideFrom([$"--data-dir={Scratch}"]));
    }

    [Fact]
    public void An_absent_argument_is_no_override()
    {
        Assert.Null(WinnowDataLocation.OverrideFrom(["--no-sync", "--seed-sample"]));
        Assert.Null(WinnowDataLocation.OverrideFrom([]));
    }

    [Fact]
    public void The_argument_without_a_path_is_refused_rather_than_ignored()
    {
        foreach (var args in new string[][]
                 {
                     new[] { "--data-dir" },
                     new[] { "--data-dir", "--no-sync" },
                     new[] { "--data-dir=" },
                     new[] { "--data-dir=   " },
                 })
        {
            var refused = Assert.Throws<DataDirectoryOverrideException>(
                () => WinnowDataLocation.OverrideFrom(args));
            Assert.Contains(
                WinnowDataLocation.OverrideArgument, refused.Message, StringComparison.Ordinal);
        }
    }

    // ── AC#1: the override is the data directory ────────────────────────────

    [Fact]
    public void The_override_becomes_the_root_and_the_database_sits_inside_it()
    {
        var location = WinnowDataLocation.ResolveFrom(["--data-dir", Scratch]);

        Assert.Equal(DataMigrationOutcome.Overridden, location.Outcome);
        Assert.Equal(Scratch, location.Root);
        Assert.Equal(Path.Combine(Scratch, "winnow.db"), location.DatabasePath);
        Assert.True(Directory.Exists(Scratch));
    }

    [Fact]
    public void A_relative_override_is_resolved_to_a_full_path()
    {
        var location = WinnowDataLocation.ResolveOverride(
            Path.GetRelativePath(Environment.CurrentDirectory, Scratch));

        Assert.Equal(Scratch, location.Root);
    }

    [Fact]
    public void The_database_and_its_sidecar_are_created_in_the_override_and_nowhere_else()
    {
        var location = WinnowDataLocation.ResolveFrom([$"--data-dir={Scratch}"]);

        var factory = new SqliteConnectionFactory(location.DatabasePath, pooling: false);
        new DatabaseInitializer(factory)
        {
            Backups = DatabaseBackupPolicy.Default with
            {
                Directory = Path.Combine(Scratch, "backups"),
            },
        }.Initialize();

        Assert.True(File.Exists(Path.Combine(Scratch, "winnow.db")));

        // The sidecars are SQLite's own, and they land beside the database:
        // WAL mode creates them on the first write and removes them when the
        // last connection closes.
        using (var connection = factory.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE probe (id INTEGER PRIMARY KEY)";
            command.ExecuteNonQuery();

            Assert.True(File.Exists(Path.Combine(Scratch, "winnow.db-wal")));
        }

        // Nothing this run wrote landed outside the override directory.
        foreach (var written in Directory.EnumerateFiles(
                     _sandbox, "*", SearchOption.AllDirectories))
        {
            Assert.StartsWith(Scratch, written, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── AC#2: the whole data directory, not just the database ───────────────

    [Fact]
    public void The_composition_root_puts_the_database_covers_and_themes_in_the_override()
    {
        var location = WinnowDataLocation.ResolveFrom(["--data-dir", Scratch]);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        Program.ConfigureServices(services, location);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            location.DatabasePath,
            provider.GetRequiredService<ISqliteConnectionFactory>().DatabasePath);
        Assert.Equal(
            Path.Combine(Scratch, "covers"),
            provider.GetRequiredService<CoverCacheOptions>().CacheDirectory);
        Assert.Equal(
            Path.Combine(Scratch, "themes"),
            provider.GetRequiredService<UserThemeStore>().Directory);
    }

    // ── AC#3: the Hoard migration does not run under an override ────────────

    [Fact]
    public void An_override_leaves_a_legacy_directory_completely_alone()
    {
        // Laid out the way %LOCALAPPDATA%\Hoard is on an install that predates
        // the rename, and beside the override so that a migration which ran at
        // all would be visible here.
        var legacy = Path.Combine(_sandbox, "Hoard");
        Directory.CreateDirectory(Path.Combine(legacy, "covers"));
        File.WriteAllText(Path.Combine(legacy, "hoard.db"), "legacy");
        File.WriteAllText(Path.Combine(legacy, "hoard.db-wal"), "legacy-wal");
        File.WriteAllText(Path.Combine(legacy, "covers", "440.jpg"), "cover");

        var location = WinnowDataLocation.ResolveFrom(["--data-dir", Scratch]);

        Assert.Equal(Scratch, location.Root);
        Assert.Equal(DataMigrationOutcome.Overridden, location.Outcome);

        Assert.True(Directory.Exists(legacy));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(legacy, "hoard.db")));
        Assert.Equal("legacy-wal", File.ReadAllText(Path.Combine(legacy, "hoard.db-wal")));
        Assert.Equal("cover", File.ReadAllText(Path.Combine(legacy, "covers", "440.jpg")));

        // And nothing was copied into the override either.
        Assert.Empty(Directory.EnumerateFileSystemEntries(Scratch));
    }

    [Fact]
    public void An_override_pointed_at_an_old_folder_opens_the_database_it_finds_without_renaming_it()
    {
        Directory.CreateDirectory(Scratch);
        File.WriteAllText(Path.Combine(Scratch, "hoard.db"), "legacy");

        var location = WinnowDataLocation.ResolveFrom(["--data-dir", Scratch]);

        Assert.Equal(Path.Combine(Scratch, "hoard.db"), location.DatabasePath);
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(Scratch, "hoard.db")));
        Assert.False(File.Exists(Path.Combine(Scratch, "winnow.db")));
    }

    [Fact]
    public void Without_the_argument_nothing_about_the_normal_resolution_changes()
    {
        var root = Path.Combine(_sandbox, "Winnow");
        var legacy = Path.Combine(_sandbox, "Hoard");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "hoard.db"), "legacy");

        var location = WinnowDataLocation.Resolve(root, legacy);

        Assert.Equal(DataMigrationOutcome.Moved, location.Outcome);
        Assert.Equal(Path.Combine(root, "winnow.db"), location.DatabasePath);
    }

    // ── AC#4: an unusable override is a refusal, never a fallback ───────────

    [Fact]
    public void A_path_that_is_an_existing_file_is_refused()
    {
        Directory.CreateDirectory(_sandbox);
        var file = Path.Combine(_sandbox, "not-a-directory");
        File.WriteAllText(file, "x");

        var refused = Assert.Throws<DataDirectoryOverrideException>(
            () => WinnowDataLocation.ResolveFrom(["--data-dir", file]));

        Assert.Contains(file, refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_directory_that_cannot_be_created_is_refused()
    {
        Directory.CreateDirectory(_sandbox);
        var blocker = Path.Combine(_sandbox, "blocker");
        File.WriteAllText(blocker, "x");

        var refused = Assert.Throws<DataDirectoryOverrideException>(
            () => WinnowDataLocation.ResolveFrom(["--data-dir", Path.Combine(blocker, "winnow")]));

        Assert.Contains("blocker", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_path_that_is_not_a_legal_path_is_refused()
    {
        var illegal = Path.Combine(_sandbox, "scr\0atch");

        var refused = Assert.Throws<DataDirectoryOverrideException>(
            () => WinnowDataLocation.ResolveFrom(["--data-dir", illegal]));

        Assert.NotEmpty(refused.Message);
    }

    [Fact]
    public void A_directory_that_exists_but_cannot_be_written_to_is_refused()
    {
        // The one shape creating the directory cannot detect: it is already
        // there, and a write into it fails. Blocking the write probe is how that
        // is staged without an ACL — the branch under test is the probe, not the
        // particular reason the write did not work.
        Directory.CreateDirectory(Path.Combine(Scratch, ".winnow-write-probe"));

        var refused = Assert.Throws<DataDirectoryOverrideException>(
            () => WinnowDataLocation.ResolveFrom(["--data-dir", Scratch]));

        Assert.Contains(Scratch, refused.Message, StringComparison.OrdinalIgnoreCase);
    }
}
