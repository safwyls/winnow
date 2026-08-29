using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>
/// Builds the executable-to-ownership map by walking installed games' directories
/// (from <c>ownerships.install_path</c>). Directory scans are cached across rebuilds
/// for <see cref="SessionWatcherOptions.ExecutableScanTtl"/>.
/// </summary>
public sealed class GameExecutableIndexBuilder
{
    /// <summary>
    /// Executables that live inside game directories and are never the game (crash
    /// reporters, prerequisite installers, store helpers, anti-cheat installers,
    /// uninstallers). Matched case-insensitively on filename without extension.
    /// </summary>
    private static readonly IReadOnlySet<string> NonGameExecutables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Crash reporters. The extending-a-session hazard above.
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
            "easyanticheat_setup", "beservice", "beservice_x64", "beservice_x86", "bedaisy",
            // Uninstallers. InnoSetup's unins000 is ubiquitous in GOG installs.
            "unins000", "unins001", "uninstall", "uninstaller", "unrealengineuninstall",
        };

    /// <summary>Directory names to skip during executable scans (redist, tools, engine internals).</summary>
    private static readonly IReadOnlySet<string> SkippedDirectories =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_commonredist", "commonredist", "redist", "redistributables", "prerequisites",
            "__installer", "directx", "dotnet", "dotnetfx", "vcredist", "openal",
            "thirdparty", "extras", "tools", "support", "docs", "documentation",
            "mono", "jre", "jdk", "python", "node_modules", ".git",
        };

    /// <summary>Extra depth the one-game launch scan descends past the library-wide limit.</summary>
    private const int LaunchScanExtraDepth = 2;

    private const int LaunchScanCapMultiplier = 8;

    private readonly IOwnershipRepository _ownerships;
    private readonly SessionWatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GameExecutableIndexBuilder> _logger;

    /// <summary>Latched so the platform-gap warning is said once, not every rebuild.</summary>
    private bool _platformGapLogged;

    private readonly Dictionary<string, ScanResult> _scans =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    public GameExecutableIndexBuilder(
        IOwnershipRepository ownerships,
        IOptions<SessionWatcherOptions> options,
        TimeProvider? timeProvider = null,
        ILogger<GameExecutableIndexBuilder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _ownerships = ownerships;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<GameExecutableIndexBuilder>.Instance;
    }

    /// <summary>Rebuilds the index from current ownership rows. Never throws for missing or unreadable directories.</summary>
    public async Task<GameExecutableIndex> BuildAsync(CancellationToken ct = default)
    {
        WarnIfUnsupportedPlatform();

        var ownerships = await _ownerships.GetAllAsync(ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var executables = new List<GameExecutable>();
        var roots = new List<(string InstallPath, long OwnershipId)>();
        var live = new HashSet<string>(_scans.Comparer);
        var scanned = 0;

        foreach (var ownership in ownerships)
        {
            ct.ThrowIfCancellationRequested();

            // Not installed means nothing to watch. The flag is an observation,
            // not a guess (see IOwnershipRepository.UpsertAsync's three-valued
            // rule), so trusting it here is safe — and a stale install_path left
            // behind by an uninstall would otherwise index a directory whose
            // executables belong to whatever was installed there next.
            if (!ownership.Installed || string.IsNullOrWhiteSpace(ownership.InstallPath))
            {
                continue;
            }

            var installPath = ownership.InstallPath;
            if (!SafeDirectoryExists(installPath))
            {
                continue;
            }

            live.Add(installPath);
            roots.Add((installPath, ownership.Id));

            if (!_scans.TryGetValue(installPath, out var scan)
                || now - scan.ScannedAtUtc >= _options.ExecutableScanTtl)
            {
                scan = new ScanResult(ScanExecutables(installPath), now);
                _scans[installPath] = scan;
                scanned++;
            }

            foreach (var path in scan.Executables)
            {
                executables.Add(new GameExecutable(path, ownership.Id));
            }
        }

        // Drop cache entries for games that were uninstalled or moved, so the
        // dictionary tracks the library rather than growing forever.
        foreach (var stale in _scans.Keys.Where(key => !live.Contains(key)).ToList())
        {
            _scans.Remove(stale);
        }

        var index = new GameExecutableIndex(executables, roots);
        _logger.LogDebug(
            "Executable index: {Names} distinct name(s) over {Executables} executable(s) "
            + "in {Roots} installed game(s); {Scanned} directory scan(s) this pass.",
            index.ProcessNames.Count, index.ExecutableCount, index.InstallRootCount, scanned);
        return index;
    }

    /// <summary>Walks one install directory for executables, pruning skipped directories and respecting depth/cap limits.</summary>
    private List<string> ScanExecutables(string installPath)
        => ScanExecutables(installPath, _options.ExecutableScanDepth, _options.MaxExecutablesPerGame);

    /// <summary>
    /// Deep scan of one game's install directory for executable names (used for
    /// launch attribution). Scans deeper/wider than the library-wide index.
    /// </summary>
    public Task<IReadOnlySet<string>> ScanLaunchNamesAsync(
        string? installPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !SafeDirectoryExists(installPath))
        {
            return Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        return Task.Run<IReadOnlySet<string>>(
            () =>
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in ScanExecutables(
                    installPath,
                    _options.ExecutableScanDepth + LaunchScanExtraDepth,
                    _options.MaxExecutablesPerGame * LaunchScanCapMultiplier))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (name.Length > 0)
                    {
                        names.Add(name);
                    }
                }

                return names;
            },
            ct);
    }

    private List<string> ScanExecutables(string installPath, int maxDepth, int cap)
    {
        var found = new List<string>();
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((installPath, 0));

        while (queue.Count > 0 && found.Count < cap)
        {
            var (directory, depth) = queue.Dequeue();

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.exe"))
                {
                    // Windows matches longer extensions against a three-letter
                    // pattern (the 8.3 short-name rule), so "*.exe" also returns
                    // "foo.exefoo". The same defence SteamLibrarySource applies
                    // to "*.acf".
                    if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (NonGameExecutables.Contains(Path.GetFileNameWithoutExtension(file)))
                    {
                        continue;
                    }

                    found.Add(file);
                    if (found.Count >= cap)
                    {
                        _logger.LogDebug(
                            "Executable scan of {Path} hit the {Cap}-executable cap; stopping.",
                            installPath, cap);
                        break;
                    }
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(child);
                    if (name.Length > 0 && !SkippedDirectories.Contains(name))
                    {
                        queue.Enqueue((child, depth + 1));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked or protected subdirectory costs its own contents and
                // nothing else; the walk carries on with its siblings.
                _logger.LogTrace(ex, "Skipping unreadable directory {Directory}.", directory);
            }
        }

        return found;
    }

    /// <summary>Warns once that session detection is Windows-only (*.exe scan).</summary>
    private void WarnIfUnsupportedPlatform()
    {
        if (_platformGapLogged || OperatingSystem.IsWindows())
        {
            return;
        }

        _platformGapLogged = true;
        _logger.LogWarning(
            "Session detection is Windows-only in this build: the executable scan matches *.exe, "
            + "so no game will be watched and no session will be recorded on this platform. "
            + "This is a known gap, not a failure.");
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record ScanResult(List<string> Executables, DateTime ScannedAtUtc);
}
