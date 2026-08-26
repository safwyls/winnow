using System.Text.Json;
using Microsoft.Win32;

namespace Hoard.Ingest.Gog;

/// <summary>
/// Locates GOG Galaxy's local data (docs/spikes/epic-gog-local-files.md
/// section 10). Discovery only; every file under these paths is read-only, and
/// the client database is never opened in place at all — see
/// <see cref="GalaxyDatabaseSnapshot"/>.
/// </summary>
public static class GogPaths
{
    /// <summary>Galaxy's root beneath <c>%PROGRAMDATA%</c>.</summary>
    public const string ProgramDataRelativeGalaxyRoot = @"GOG.com\Galaxy";

    /// <summary>Small plain-JSON file naming <c>storagePath</c> and <c>libraryPath</c>.</summary>
    public const string ConfigFileName = "config.json";

    /// <summary>The client database file name inside the storage directory.</summary>
    public const string ClientDatabaseFileName = "galaxy-2.0.db";

    /// <summary>Registry key proving Galaxy is installed (32-bit view path, verbatim).</summary>
    public const string GalaxyClientRegistryKey = @"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient";

    /// <summary>Registry key under which GOG records one subkey per installed game.</summary>
    public const string InstalledGamesRegistryKey = @"SOFTWARE\WOW6432Node\GOG.com\Games";

    /// <summary>
    /// Finds Galaxy's root directory (the one holding <c>config.json</c>), or null.
    /// </summary>
    public static string? FindGalaxyRoot()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrEmpty(programData))
        {
            return null;
        }

        var root = Path.Combine(programData, ProgramDataRelativeGalaxyRoot);
        return Directory.Exists(root) ? root : null;
    }

    /// <summary>
    /// Resolves the client database path by reading <c>config.json</c>'s
    /// <c>storagePath</c>. Falls back to <c>&lt;galaxyRoot&gt;\storage</c> when the
    /// config is missing or unreadable. Returns null when no database exists there.
    ///
    /// <para>Do not hardcode the storage path: Galaxy publishes it, and users can
    /// move it.</para>
    /// </summary>
    public static string? FindClientDatabase(string galaxyRoot)
    {
        ArgumentNullException.ThrowIfNull(galaxyRoot);

        var storagePath = ReadStoragePath(Path.Combine(galaxyRoot, ConfigFileName))
            ?? Path.Combine(galaxyRoot, "storage");

        var database = Path.Combine(storagePath, ClientDatabaseFileName);
        return File.Exists(database) ? database : null;
    }

    /// <summary>
    /// Reads <c>storagePath</c> out of Galaxy's <c>config.json</c>, or null when
    /// the file is absent, unreadable, or does not name one.
    /// </summary>
    public static string? ReadStoragePath(string configPath)
    {
        ArgumentNullException.ThrowIfNull(configPath);

        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("storagePath", out var value)
                || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var path = value.GetString();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the Galaxy client is registered on this machine. Not required for
    /// ingest — the database's presence is the real test — but useful for logging
    /// and for telling "Galaxy-less user" apart from "Galaxy installed but never
    /// signed in".
    /// </summary>
    public static bool IsGalaxyClientInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GalaxyClientRegistryKey);
            return key is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
