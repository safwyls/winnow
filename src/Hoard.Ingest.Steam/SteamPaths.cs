using Microsoft.Win32;

namespace Hoard.Ingest.Steam;

/// <summary>
/// Locates the Steam installation root (§4.1). Discovery only — the VDF/ACF
/// readers that consume these paths land separately, built on ValveKeyValue.
/// All access to Steam's files is read-only (§4.1: never write while Steam
/// is running; Steam Cloud can clobber local edits).
/// </summary>
public static class SteamPaths
{
    /// <summary>Linux default install root (relative to $HOME).</summary>
    public const string LinuxDefaultRoot = ".steam/steam";

    /// <summary>Linux XDG data root (relative to $HOME).</summary>
    public const string LinuxShareRoot = ".local/share/Steam";

    /// <summary>Linux Flatpak app root (relative to $HOME); Steam data lives beneath it.</summary>
    public const string LinuxFlatpakRoot = ".var/app/com.valvesoftware.Steam/.local/share/Steam";

    /// <summary>macOS install root (relative to $HOME).</summary>
    public const string MacRoot = "Library/Application Support/Steam";

    /// <summary>
    /// Attempts to find the Steam root for the current OS. On Windows:
    /// HKCU\Software\Valve\Steam SteamPath, falling back to
    /// %ProgramFiles(x86)%\Steam. On Linux/macOS: the §4.1 candidate paths,
    /// first one that exists on disk wins. Returns null when Steam cannot
    /// be located.
    /// </summary>
    public static string? FindSteamRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            return FindWindowsSteamRoot();
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
        {
            return null;
        }

        string[] candidates = OperatingSystem.IsMacOS()
            ? [MacRoot]
            : [LinuxDefaultRoot, LinuxShareRoot, LinuxFlatpakRoot];

        foreach (var relative in candidates)
        {
            var path = Path.Combine(home, relative);
            if (Directory.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    private static string? FindWindowsSteamRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string registryPath
                && !string.IsNullOrWhiteSpace(registryPath))
            {
                // Steam writes forward slashes into the registry value.
                var normalized = Path.GetFullPath(registryPath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(normalized))
                {
                    return normalized;
                }
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            var fallback = Path.Combine(programFilesX86, "Steam");
            if (Directory.Exists(fallback))
            {
                return fallback;
            }
        }

        return null;
    }
}
