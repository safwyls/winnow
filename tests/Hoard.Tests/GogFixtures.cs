using System.Text.RegularExpressions;
using Hoard.Ingest.Gog;
using Microsoft.Data.Sqlite;

namespace Hoard.Tests;

/// <summary>
/// Locates the sanitized GOG fixtures and builds a Galaxy tree from them
/// (tests/fixtures/gog/README.md). Nothing here writes back into the fixtures.
/// </summary>
internal static class GogFixtures
{
    /// <summary>GWENT: owned, installed, played. Its title in Galaxy is English; every local source has the Polish one.</summary>
    internal const string GwentProductId = "1971477531";

    /// <summary>The Witcher 3: owned, NOT installed, 50 minutes of playtime.</summary>
    internal const string Witcher3ProductId = "1207664643";

    /// <summary>Tyrian 2000: owned, never played — a GameTimes row of 0 and no LastPlayedDates row.</summary>
    internal const string TyrianProductId = "1207658901";

    /// <summary>"New Game +": owned and flagged isDlc = 1.</summary>
    internal const string NewGamePlusProductId = "1430742983";

    /// <summary>
    /// <b>The double-count trap.</b> Cyberpunk 2077 sits in Galaxy's
    /// <c>LibraryReleases</c> as a Steam release with <c>isOwned = 1</c>. Ingesting
    /// it would re-import a game the Steam ingest already owns.
    /// </summary>
    internal const string ContaminatingSteamReleaseKey = "steam_1091500";

    internal static string PathOf(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "gog", fileName);
}

/// <summary>
/// A throwaway Galaxy installation: <c>config.json</c> naming a storage directory
/// that holds a copy of the fixture client database.
/// </summary>
internal sealed class GalaxyFixtureTree : IDisposable
{
    private readonly string _root;

    private GalaxyFixtureTree(string root, string galaxyRoot, string storagePath)
    {
        _root = root;
        GalaxyRoot = galaxyRoot;
        StoragePath = storagePath;
    }

    /// <summary>The directory holding <c>config.json</c>; hand this to <c>GogLibrarySource.Scan</c>.</summary>
    internal string GalaxyRoot { get; }

    /// <summary>The storage directory the config names — GOG-owned in production.</summary>
    internal string StoragePath { get; }

    /// <summary>The client database inside <see cref="StoragePath"/>.</summary>
    internal string DatabasePath => Path.Combine(StoragePath, GogPaths.ClientDatabaseFileName);

    /// <param name="storageDirectoryName">
    /// Name of the storage directory, so a test can prove <c>config.json</c>'s
    /// <c>storagePath</c> is actually honoured rather than the path being guessed.
    /// </param>
    /// <param name="walMode">
    /// Switch the copy into WAL journal mode first. Production's database is WAL
    /// and that is what makes an in-place read-only open write <c>-wal</c>/<c>-shm</c>
    /// into the store's directory; the fixture ships in rollback-journal mode, so a
    /// test of that hazard has to arrange it.
    /// </param>
    internal static GalaxyFixtureTree Create(string storageDirectoryName = "storage", bool walMode = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "hoard-gog-fixture-" + Guid.NewGuid().ToString("N"));
        var galaxyRoot = Path.Combine(root, "GOG.com", "Galaxy");
        var storage = Path.Combine(galaxyRoot, storageDirectoryName);
        Directory.CreateDirectory(storage);

        var database = Path.Combine(storage, GogPaths.ClientDatabaseFileName);
        File.Copy(GogFixtures.PathOf("galaxy-2.0.min.db"), database);

        if (walMode)
        {
            SetWalMode(database);
        }

        File.WriteAllText(
            Path.Combine(galaxyRoot, GogPaths.ConfigFileName),
            $$"""
            {
                 "installationPaths": [],
                 "installationSource": "gog",
                 "libraryPath": "{{JsonPath(Path.Combine(root, "Games"))}}",
                 "storagePath": "{{JsonPath(storage)}}"
            }
            """);

        return new GalaxyFixtureTree(root, galaxyRoot, storage);
    }

    /// <summary>Files currently in the storage directory, for a "nothing was written" assertion.</summary>
    internal IReadOnlyList<string> StorageDirectoryEntries()
        => Directory.EnumerateFileSystemEntries(StoragePath)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }

    /// <summary>
    /// Puts the copy into WAL mode and checkpoints back to a single file, so the
    /// database header says WAL while no <c>-wal</c>/<c>-shm</c> sidecars exist.
    /// This is done to a Hoard-owned copy only; the real store file is never
    /// opened for writing anywhere in this codebase.
    /// </summary>
    private static void SetWalMode(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString();

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            command.ExecuteScalar();
        }

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(databasePath + suffix))
            {
                File.Delete(databasePath + suffix);
            }
        }
    }

    private static string JsonPath(string path)
        => path.Replace("\\", "\\\\", StringComparison.Ordinal);
}

/// <summary>
/// A registry enumeration with a scripted answer, so tests never depend on
/// whether the machine running them happens to have GOG games installed — and so
/// the Galaxy-less fallback, which cannot otherwise be exercised, can be.
/// </summary>
internal sealed class FakeGogInstalledGameRegistry : IGogInstalledGameRegistry
{
    private readonly IReadOnlyList<GogRegistryGame> _games;

    internal FakeGogInstalledGameRegistry(params GogRegistryGame[] games) => _games = games;

    /// <summary>An empty registry — a machine that has never installed a GOG game.</summary>
    internal static FakeGogInstalledGameRegistry Empty => new();

    public IReadOnlyList<GogRegistryGame> Enumerate() => _games;
}

/// <summary>
/// Parses <c>tests/fixtures/gog/gog-games.reg</c>. Exists so the Galaxy-less test
/// asserts against the real captured value names and casing
/// (<c>gameID</c>, <c>gameName</c>, <c>path</c>, <c>BUILDID</c>, <c>INSTALLDATE</c>)
/// rather than against values invented in a test file.
/// </summary>
internal static class RegFile
{
    private static readonly Regex SectionPattern = new(@"^\[(?<path>[^\]]+)\]\s*$", RegexOptions.Compiled);
    private static readonly Regex ValuePattern = new("^\"(?<name>[^\"]*)\"=\"(?<value>.*)\"\\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Reads a <c>.reg</c> export into <c>{key path: {value name: value}}</c>.
    /// Only string (<c>REG_SZ</c>) values are handled, which is all GOG writes here.
    /// </summary>
    internal static Dictionary<string, Dictionary<string, string>> Parse(string path)
    {
        var keys = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? current = null;

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
            {
                continue;
            }

            var section = SectionPattern.Match(line);
            if (section.Success)
            {
                current = [];
                keys[section.Groups["path"].Value] = current;
                continue;
            }

            var value = ValuePattern.Match(line);
            if (value.Success && current is not null)
            {
                current[value.Groups["name"].Value] = value.Groups["value"].Value;
            }
        }

        return keys;
    }

    /// <summary>
    /// The fixture's <c>GOG.com\Games\&lt;gameId&gt;</c> subkeys, shaped exactly as
    /// <see cref="WindowsGogInstalledGameRegistry"/> shapes the real ones.
    /// </summary>
    internal static IReadOnlyList<GogRegistryGame> InstalledGames(string regFilePath)
    {
        const string prefix = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\";

        return Parse(regFilePath)
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new GogRegistryGame(
                GameId: Value(entry.Value, "gameID") ?? Value(entry.Value, "productID") ?? entry.Key[prefix.Length..],
                GameName: Value(entry.Value, "gameName"),
                InstallPath: Value(entry.Value, "path") ?? Value(entry.Value, "workingDir"),
                Executable: Value(entry.Value, "exe"),
                BuildId: Value(entry.Value, "BUILDID"),
                Version: Value(entry.Value, "ver"),
                InstallDateLocal: Value(entry.Value, "INSTALLDATE")))
            .ToList();
    }

    private static string? Value(Dictionary<string, string> values, string name)
        => values.TryGetValue(name, out var value) && value.Length > 0 ? value : null;
}
