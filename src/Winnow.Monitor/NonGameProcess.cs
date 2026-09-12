namespace Winnow.Monitor;

/// <summary>Known auxiliary processes that must never contribute gameplay sessions.</summary>
internal static class NonGameProcess
{
    /// <summary>
    /// Executables that live inside game directories and are never the game (crash
    /// reporters, prerequisite installers, store helpers, anti-cheat installers,
    /// uninstallers). Matched case-insensitively on filename without extension.
    /// </summary>
    private static readonly IReadOnlySet<string> NonGameExecutables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Crash reporters can outlive a game and falsely extend its session.
            "crashpad_handler", "crashreportclient", "crashreportclient-win64-debug",
            "crashreportclient-win64-shipping", "unitycrashhandler32", "unitycrashhandler64",
            "unrealcefsubprocess", "bugsplat", "bssndrpt", "crashsender",
            // Store/runtime helpers that ship inside the game folder.
            "epicwebhelper", "epiconlineservicesinstaller", "eoshelper",
            "steamerrorreporter", "steamerrorreporter64", "gameoverlayui",
            "galaxycommunication", "gogglaxycommunication",
            // Prerequisite installers. These are launched on first run and are
            // long-lived enough to be caught by a poll.
            "ueprereqsetup_x64", "ueprereqsetup_x86", "ue4prereqsetup_x64", "ue4prereqsetup_x86",
            "vc_redist.x64", "vc_redist.x86", "vcredist_x64", "vcredist_x86",
            "dxsetup", "dxwebsetup", "oalinst", "xnafx40_redist",
            "dotnetfx40_full_x86_x64", "dotnetfx35setup", "ndp451-kb2858728-x86-x64-allos-enu",
            // Anti-cheat *installers* and services, which ship in the game
            // folder and can run without the game — a false session's worth of
            // risk each. The anti-cheat launcher shims that run alongside the
            // game are deliberately left in: they resolve to the same ownership
            // and simply join the session the game is already having.
            "easyanticheat_setup", "easyanticheat_eos_setup", "beservice", "beservice_x64", "beservice_x86", "bedaisy",
            // Uninstallers. InnoSetup's unins000 is ubiquitous in GOG installs.
            "setup", "install", "installer", "unins000", "unins001", "uninstall", "uninstaller", "unrealengineuninstall",
        };

    internal static bool IsExcluded(string? executablePath, string processName)
    {
        if (IsExcludedName(processName))
        {
            return true;
        }

        if (string.IsNullOrEmpty(executablePath))
        {
            return false;
        }

        // Wine paths can use either separator regardless of the host OS.
        var parts = executablePath.Replace('\\', '/').Split('/');
        return IsExcludedName(parts[^1])
            || parts.SkipLast(1).Any(part => part.Equals("__installer", StringComparison.OrdinalIgnoreCase)
                || part.Equals("_commonredist", StringComparison.OrdinalIgnoreCase)
                || part.Equals("commonredist", StringComparison.OrdinalIgnoreCase)
                || part.Equals("redist", StringComparison.OrdinalIgnoreCase)
                || part.Equals("redistributables", StringComparison.OrdinalIgnoreCase)
                || part.Equals("prerequisites", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExcludedName(string name)
        => NonGameExecutables.Contains(name)
            || (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && NonGameExecutables.Contains(name[..^4]));
}
