
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests.Enforcement;

/// <summary>
/// Two schema rules that are stated as absolutes and that cost a user's data
/// when broken: a shipped migration is never edited, and a derived bucket is
/// never a column.
/// </summary>
public sealed class SchemaDisciplineTests
{
    // ── A shipped migration is immutable ────────────────────────────────────

    /// <summary>
    /// The checked-in hash of every migration that has shipped. Adding a
    /// migration appends a line; editing one fails this test.
    ///
    /// <para>DbUp keys applied scripts by embedded-resource name and records
    /// the name in <c>SchemaVersions</c>, so an edited migration is a script
    /// that will never run again on any database that has already seen it. The
    /// user's schema and the repository's then differ permanently, with nothing
    /// to say so.</para>
    /// </summary>
    private const string ChecksumFile = "src/Winnow.Data/Migrations/checksums.txt";

    [Fact]
    public void No_shipped_migration_has_been_edited()
    {
        var recorded = ReadChecksums();
        var actual = CurrentChecksums();
        var failures = new List<string>();

        foreach (var (name, hash) in actual)
        {
            if (!recorded.TryGetValue(name, out var expected))
            {
                failures.Add(
                    $"{name} has no line in {ChecksumFile}. A new migration appends one: "
                    + $"\"{name}  {hash}\".");
                continue;
            }

            if (!string.Equals(expected, hash, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{name} has changed. A migration that has shipped never changes: DbUp will "
                    + "not re-run it on a database that already applied it, so the edit reaches "
                    + "new installs only. Write a new migration instead.");
            }
        }

        foreach (var name in recorded.Keys.Except(actual.Keys, StringComparer.Ordinal))
        {
            failures.Add($"{name} is in {ChecksumFile} and no longer exists as a migration.");
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void Every_migration_is_an_embedded_resource()
    {
        // DbUp finds them by embedded-resource name. A migration added to the
        // directory but not to the build is a migration that silently does not
        // run.
        var embedded = typeof(Winnow.Data.DatabaseInitializer).Assembly
            .GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n => n[(n.LastIndexOf('.', n.Length - 5) + 1)..])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var onDisk = MigrationFiles().Select(Path.GetFileName).OfType<string>();

        foreach (var file in onDisk)
        {
            Assert.True(
                embedded.Contains(file),
                $"{file} is in the Migrations directory and is not an embedded resource, so "
                + "DbUp will never run it.");
        }
    }

    // ── A derived bucket is a query, never a column ─────────────────────────

    [Fact]
    public void No_table_stores_a_derived_bucket_or_a_score()
    {
        // Buckets and scores change when a threshold is tuned. Stored, they rot
        // silently: the row keeps the answer the old threshold gave, and
        // nothing in the app knows the difference.
        using var db = new TempDatabase();
        using var connection = db.Factory.Open();

        var forbidden = new Regex(
            @"^(bucket|staleness|score|is_(never|bounced|retired|stale))",
            RegexOptions.IgnoreCase);

        // The one stored score, and it is not a derived value: it is the soft
        // matcher's confidence in one specific pair, recorded with the pair at
        // the moment it was queued, so a human reviewing the queue can see what
        // the machine thought. §6's schema declares it. Re-deriving it later
        // would answer a different question, because the matcher will have
        // changed.
        string[] recordedObservations = ["merge_candidates.score"];

        var failures = new List<string>();

        foreach (var table in TableNames(connection))
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = pragma.ExecuteReader();

            while (reader.Read())
            {
                var column = reader.GetString(1);
                var qualified = $"{table}.{column}";

                if (forbidden.IsMatch(column)
                    && !recordedObservations.Contains(qualified, StringComparer.Ordinal))
                {
                    failures.Add(qualified);
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "Derived buckets and scores are queries, never stored columns "
            + "(game-library-design.md §6.1). Found: " + string.Join(", ", failures));
    }

    [Fact]
    public void The_bucket_vocabulary_is_the_one_the_copy_table_names()
    {
        // The data-layer charter used to teach "Never touched / Bounced /
        // Stale-but-patched / Retired / Dead", a vocabulary the copy table
        // forbids. The constants are the vocabulary; this is what stops a
        // second one appearing beside them.
        Assert.Equal("never_played", LibraryBuckets.NeverPlayed);
        Assert.Equal("bounced", LibraryBuckets.Bounced);
        Assert.Equal("stale_but_patched", LibraryBuckets.StaleButPatched);
        Assert.Equal("retired", LibraryBuckets.Retired);
        Assert.Equal("active", LibraryBuckets.Active);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IEnumerable<string> TableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";

        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static IEnumerable<string> MigrationFiles()
        => RepositoryTree.Files("src/Winnow.Data/Migrations", "*.sql")
            .Select(RepositoryTree.Path);

    private static SortedDictionary<string, string> CurrentChecksums()
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in MigrationFiles())
        {
            // Hashed on the content with line endings normalised, so a clone
            // with autocrlf on and one with it off agree.
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            hashes[Path.GetFileName(path)] = Convert.ToHexStringLower(digest);
        }

        return hashes;
    }

    private static Dictionary<string, string> ReadChecksums()
    {
        var path = RepositoryTree.Path(ChecksumFile);

        Assert.True(
            File.Exists(path),
            $"{ChecksumFile} is missing. It carries one SHA-256 per shipped migration and is "
            + "what makes an edit to one detectable.");

        return File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
    }
}
