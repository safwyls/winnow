using Microsoft.Win32;

namespace Hoard.Ingest.Gog;

/// <summary>
/// One <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\&lt;gameID&gt;</c> subkey — GOG's
/// own record of an installed game, written by every GOG installer whether or not
/// Galaxy is present (docs/spikes/epic-gog-local-files.md section 16). All values
/// are <c>REG_SZ</c>.
/// </summary>
/// <param name="GameId">
/// <c>gameID</c> (identical to <c>productID</c>). The GOG product id, and
/// byte-identical to the Galaxy release key's suffix and to
/// <c>goggame-&lt;id&gt;.info</c>'s <c>gameId</c>.
/// </param>
/// <param name="GameName">
/// <c>gameName</c>. <b>The installer-locale title, and additionally
/// diacritic-stripped</b> — GWENT's reads <c>GWINT: Wiedzminska Gra Karciana</c>
/// where the <c>.info</c> file has <c>Wiedźmińska</c>, so the two "same" titles
/// are not even string-equal. Treat this key as an id and path source; take the
/// title from Galaxy where Galaxy exists.
/// </param>
/// <param name="InstallPath"><c>path</c> (same as <c>workingDir</c>).</param>
/// <param name="Executable"><c>exe</c> — the absolute path to the launchable binary.</param>
/// <param name="BuildId"><c>BUILDID</c>.</param>
/// <param name="Version"><c>ver</c>.</param>
/// <param name="InstallDateLocal">
/// <c>INSTALLDATE</c>. <b>Local time, not UTC</b> — Galaxy's
/// <c>installationDate</c> for the same install differs by the machine's offset.
/// Kept as the raw string precisely so nobody parses it into a UTC field by
/// accident; nothing in the candidate feed uses it.
/// </param>
public sealed record GogRegistryGame(
    string GameId,
    string? GameName,
    string? InstallPath,
    string? Executable,
    string? BuildId,
    string? Version,
    string? InstallDateLocal);

/// <summary>
/// Enumerates GOG's per-game install registry. Exists as an interface for one
/// reason: the registry cannot be faked on a test machine, and the Galaxy-less
/// fallback is exactly the path that most needs testing.
/// </summary>
public interface IGogInstalledGameRegistry
{
    /// <summary>
    /// Every installed GOG game this machine records. Empty — never an exception —
    /// off Windows, when GOG has never installed anything, or when the key cannot
    /// be read.
    /// </summary>
    IReadOnlyList<GogRegistryGame> Enumerate();
}

/// <summary>
/// Reads <c>HKLM\SOFTWARE\WOW6432Node\GOG.com\Games</c>, read-only. The path
/// already names <c>WOW6432Node</c> explicitly, so it is opened verbatim rather
/// than through a 32-bit registry view.
///
/// <para>GOG also writes an Inno Setup uninstall entry at
/// <c>…\CurrentVersion\Uninstall\&lt;gameId&gt;_is1</c>; it is redundant with this
/// key and is not read.</para>
/// </summary>
public sealed class WindowsGogInstalledGameRegistry : IGogInstalledGameRegistry
{
    /// <inheritdoc/>
    public IReadOnlyList<GogRegistryGame> Enumerate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using var games = Registry.LocalMachine.OpenSubKey(GogPaths.InstalledGamesRegistryKey);
            if (games is null)
            {
                return [];
            }

            var results = new List<GogRegistryGame>();
            foreach (var subkeyName in games.GetSubKeyNames())
            {
                using var game = games.OpenSubKey(subkeyName);
                if (game is null)
                {
                    continue;
                }

                var gameId = Value(game, "gameID") ?? Value(game, "productID") ?? subkeyName;
                if (string.IsNullOrWhiteSpace(gameId))
                {
                    continue;
                }

                results.Add(new GogRegistryGame(
                    GameId: gameId,
                    GameName: Value(game, "gameName"),
                    InstallPath: Value(game, "path") ?? Value(game, "workingDir"),
                    Executable: Value(game, "exe"),
                    BuildId: Value(game, "BUILDID"),
                    Version: Value(game, "ver"),
                    InstallDateLocal: Value(game, "INSTALLDATE")));
            }

            return results;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static string? Value(RegistryKey key, string name)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return key.GetValue(name) is string value && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
