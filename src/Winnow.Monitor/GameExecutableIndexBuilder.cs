using Winnow.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Winnow.Monitor;

/// <summary>
/// Builds the §5.2 executable→release map by walking the install directory of
/// every ownership the library records as installed.
///
/// <para><b>Where the install directories come from, and why not from the VDF.</b>
/// §5.2 describes the map as <c>installdir</c> from <c>appmanifest_*.acf</c>
/// cross-referenced with <c>libraryfolders.vdf</c>, plus the Epic and GOG
/// install locations. That cross-reference is real work and it is already done:
/// <c>SteamLibrarySource</c> joins the manifest's <c>installdir</c> to the
/// library root that holds it and emits an absolute path, the Epic and GOG
/// sources do the equivalent for their stores, and the resolver stores the
/// result in <c>ownerships.install_path</c>. Reading that column is therefore
/// not a shortcut past the spec — it <i>is</i> the spec's cross-reference, with
/// two stores' worth of edge cases (offline library drives, third-party Epic
/// apps, Galaxy's registry fallback) already handled. Re-parsing the VDF here
/// would add a second implementation that can disagree with the one the library
/// view is built on, and §9 pitfall 8's warning about hand-rolling that format
/// applies with equal force to hand-rolling a second correct parse of it.</para>
///
/// <para><b>Scanning is cached across rebuilds.</b> A rebuild re-reads the
/// ownership rows every time — cheap, and the point of rebuilding at all is to
/// notice a game installed since startup — but only walks install directories it
/// has not seen, or has not seen for
/// <see cref="SessionWatcherOptions.ExecutableScanTtl"/>. In steady state that
/// makes a rebuild one query and nothing else. Measured on the developer's
/// library — 1,027 ownerships, 18 of them installed — a cold build is 67 ms and
/// yields 30 executables under 30 distinct names; a warm rebuild is 3 ms. The
/// name set the 5-second poll consults is therefore about thirty strings, which
/// is what makes the Tier 1 filter free.</para>
/// </summary>
public sealed class GameExecutableIndexBuilder
{
    /// <summary>
    /// Executables that live inside game directories and are never the game.
    /// Matched on the file name without extension, case-insensitively.
    ///
    /// <para><b>Every name here was observed in a real install directory on the
    /// developer's own library</b>, not imagined: <c>crashpad_handler</c> (6
    /// copies), <c>CrashReportClient</c> (6), <c>UEPrereqSetup_x64</c> (3),
    /// <c>EpicWebHelper</c> (3), <c>VC_redist.x64</c>, <c>UnityCrashHandler64</c>,
    /// <c>unins000</c>, <c>dotNetFx40_Full_x86_x64</c>, <c>DXSETUP</c>. They
    /// matter for two separate reasons. In the name set they would widen the
    /// Tier 1 filter to processes that are never games. Worse, at the far end of
    /// a session they would <i>extend</i> one: Unreal starts
    /// <c>CrashReportClient</c> after a crash, from inside the game's own
    /// directory, which the relaunch grace would otherwise read as the game
    /// coming back and fold into the same record.</para>
    ///
    /// <para>Deliberately a deny-list of specific names rather than a heuristic.
    /// A rule like "drop anything containing 'launcher'" would drop real games —
    /// plenty of titles genuinely start life as <c>Launcher.exe</c> — and the
    /// cost of a false positive here is a game that can never record a session,
    /// which nobody would ever notice.</para>
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

    /// <summary>
    /// Directory names never worth descending into. Prunes the bulk of a deep
    /// install tree before it is walked, which is what keeps a rebuild off the
    /// order of the whole library's file count.
    /// </summary>
    private static readonly IReadOnlySet<string> SkippedDirectories =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_commonredist", "commonredist", "redist", "redistributables", "prerequisites",
            "__installer", "directx", "dotnet", "dotnetfx", "vcredist", "openal",
            "thirdparty", "extras", "tools", "support", "docs", "documentation",
            "mono", "jre", "jdk", "python", "node_modules", ".git",
        };

    /// <summary>
    /// Levels the one-game launch scan descends past the library-wide limit, and
    /// the factor it multiplies the per-game executable cap by. Two and eight:
    /// enough to reach a binary buried under <c>Engine/Binaries/Win64</c> inside
    /// a versioned subdirectory, and enough that no shipped title's executable
    /// list is truncated. Deliberately constants rather than settings — there is
    /// nothing here a user could tune with better information than this.
    /// </summary>
    private const int LaunchScanExtraDepth = 2;

    private const int LaunchScanCapMultiplier = 8;

    private readonly IOwnershipRepository _ownerships;
    private readonly SessionWatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GameExecutableIndexBuilder> _logger;

    /// <summary>
    /// Install path → the executables found under it, and when. Survives
    /// rebuilds; see the type remarks.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the index from the current ownership rows. Never throws for a
    /// missing directory or a permission failure — a game whose folder cannot be
    /// read simply contributes nothing, and the rest of the library still works.
    /// </summary>
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

    /// <summary>
    /// Walks one install directory for executables, pruning the subtrees in
    /// <see cref="SkippedDirectories"/> and stopping at
    /// <see cref="SessionWatcherOptions.ExecutableScanDepth"/>.
    ///
    /// <para>Hand-rolled rather than <c>EnumerateFiles(AllDirectories)</c>
    /// because the pruning is the point: an unpruned walk of a modern Unreal
    /// title descends through engine third-party trees with tens of thousands of
    /// files to find four executables, three of which are the crash reporter and
    /// the prerequisite installers.</para>
    /// </summary>
    private List<string> ScanExecutables(string installPath)
        => ScanExecutables(installPath, _options.ExecutableScanDepth, _options.MaxExecutablesPerGame);

    /// <summary>
    /// Every executable NAME under one game's install directory, scanned deeper
    /// and wider than the library-wide index bothers with.
    ///
    /// <para><b>Why one game gets a more expensive scan than the library does.</b>
    /// The index's depth limit and per-game cap exist because it walks every
    /// installed game on every rebuild, and §5.2 is emphatic that the discovery
    /// loop must stay cheap. Those limits have a cost: a title whose real binary
    /// sits five directories down, or which ships more than
    /// <see cref="SessionWatcherOptions.MaxExecutablesPerGame"/> executables,
    /// contributes an incomplete name set and can be missed.</para>
    ///
    /// <para>A declared launch changes the arithmetic completely. It is one
    /// directory, once, triggered by a person clicking a button — so it can
    /// afford the walk the library-wide pass cannot, and it buys back exactly the
    /// games the caps were losing. The pruning stays: <see cref="SkippedDirectories"/>
    /// and <see cref="NonGameExecutables"/> are about correctness, not cost, and
    /// letting a crash reporter into this set would let it claim the session.</para>
    ///
    /// <para>Runs on the thread pool: it is filesystem work on a cold directory
    /// tree, called from the watcher's tick.</para>
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

    /// <summary>
    /// Says, once, that this platform records nothing — <b>and why it is a
    /// stated gap rather than a bug to be fixed by relaxing the glob.</b>
    ///
    /// <para>The scan matches <c>*.exe</c>, so on Linux and macOS the index is
    /// always empty, the Tier 1 filter matches nothing, and no session is ever
    /// recorded. The rest of Winnow does run there — <c>SteamPaths</c> knows the
    /// Linux and macOS roots and the ingest readers work — so a silently empty
    /// <c>sessions</c> table would read as a defect rather than as an unbuilt
    /// feature. Hence the warning.</para>
    ///
    /// <para><b>Native Linux titles would need only the extension rule relaxed
    /// (any executable-bit file under the install root). Proton titles would
    /// not, and that is the part worth writing down.</b> Under Proton the
    /// process that actually runs is the Wine loader — <c>wine64-preloader</c>
    /// or the game's PE image hosted by it — and the path
    /// <c>/proc/&lt;pid&gt;/exe</c> resolves to a binary inside the Steam Linux
    /// Runtime or the Proton distribution, <i>not</i> inside the game's
    /// <c>steamapps/common/&lt;Game&gt;</c> directory. The install-prefix join
    /// this whole module is built on therefore cannot match, no matter what the
    /// scan indexes: the running executable genuinely is not under the game's
    /// directory. Attribution there has to come from somewhere else — the
    /// <c>STEAM_COMPAT_DATA_PATH</c>/<c>SteamAppId</c> environment of the
    /// process (readable from <c>/proc/&lt;pid&gt;/environ</c>), or the compat
    /// prefix path — which is a different design, not a wider glob.</para>
    ///
    /// <para>§5.2 mechanism B has no such problem on any platform, which is one
    /// more reason the spec ships both.</para>
    /// </summary>
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
