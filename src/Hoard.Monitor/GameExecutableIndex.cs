namespace Hoard.Monitor;

/// <summary>One executable found under one owned game's install directory.</summary>
/// <param name="Path">Absolute path to the executable.</param>
/// <param name="OwnershipId">The <c>ownerships.id</c> whose install directory contains it.</param>
public sealed record GameExecutable(string Path, long OwnershipId);

/// <summary>
/// The executable→release map of §5.2, in the shape the two tiers actually need
/// it: a <b>set of names</b> for Tier 1 to filter on, and a <b>set of install
/// roots</b> for Tier 2 to attribute a resolved path to.
///
/// <para><b>Why the map is by install directory and not by exact executable
/// path.</b> An exact-path map is only as good as the moment it was built. Games
/// gain executables when they are patched, ship platform-specific launchers that
/// appear on first run, and Unreal titles keep their real binary several levels
/// down beside a shim. A prefix match against the install root answers correctly
/// for every executable a game will ever have, including the ones that did not
/// exist when the scan ran; the scan's only remaining job is to supply the
/// <i>names</i> Tier 1 filters on, and a stale name set costs a missed session
/// rather than a wrong one.</para>
///
/// <para>Immutable, and rebuilt wholesale rather than mutated — the watcher can
/// swap the reference between polls without a lock.</para>
/// </summary>
public sealed class GameExecutableIndex
{
    /// <summary>
    /// Path comparison for install roots. Windows and macOS filesystems are
    /// case-insensitive; Linux ones are not. Same rule, for the same reason, as
    /// <c>SteamLibrarySource</c>'s root deduplication — folding case on Linux
    /// would let one game's directory swallow another's.
    /// </summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly IReadOnlyList<InstallRoot> _roots;
    private readonly Dictionary<string, HashSet<long>> _ownershipsByName;

    /// <param name="executables">Every executable discovered under every installed game.</param>
    /// <param name="installRoots">Install directory per ownership. May include ownerships with no executables found.</param>
    public GameExecutableIndex(
        IEnumerable<GameExecutable> executables,
        IEnumerable<(string InstallPath, long OwnershipId)> installRoots)
    {
        ArgumentNullException.ThrowIfNull(executables);
        ArgumentNullException.ThrowIfNull(installRoots);

        _ownershipsByName = new Dictionary<string, HashSet<long>>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var executable in executables)
        {
            // Process.ProcessName carries no extension and no directory on
            // Windows, and /proc/<pid>/comm is the same shape on Linux, so the
            // index is keyed the way the enumeration will ask for it.
            var name = Path.GetFileNameWithoutExtension(executable.Path);
            if (name.Length == 0)
            {
                continue;
            }

            names.Add(name);
            if (!_ownershipsByName.TryGetValue(name, out var owners))
            {
                owners = [];
                _ownershipsByName[name] = owners;
            }

            owners.Add(executable.OwnershipId);
            count++;
        }

        ProcessNames = names;
        ExecutableCount = count;

        var roots = new List<InstallRoot>();
        foreach (var (installPath, ownershipId) in installRoots)
        {
            if (string.IsNullOrWhiteSpace(installPath))
            {
                continue;
            }

            roots.Add(new InstallRoot(Normalize(installPath), ownershipId));
        }

        // Longest first, so a game installed inside another game's directory
        // (GOG's "Games" root can be configured that way) attributes to the
        // inner one. Cheap to get right once; impossible to debug later.
        roots.Sort(static (a, b) => b.Path.Length.CompareTo(a.Path.Length));
        _roots = roots;
    }

    /// <summary>
    /// The Tier 1 filter set: every executable name belonging to an installed,
    /// owned game. <b>This is the only thing the 5-second poll consults</b>, and
    /// the reason the poll costs a hash lookup per running process instead of a
    /// path resolution per running process.
    /// </summary>
    public IReadOnlySet<string> ProcessNames { get; }

    /// <summary>Executables indexed, for logging. Not used in matching.</summary>
    public int ExecutableCount { get; }

    /// <summary>Install roots indexed, for logging.</summary>
    public int InstallRootCount => _roots.Count;

    /// <summary>An index that matches nothing — the state before the first build.</summary>
    public static GameExecutableIndex Empty { get; } = new([], []);

    /// <summary>
    /// The ownership a running process belongs to, or null for "not one of ours".
    ///
    /// <para><paramref name="executablePath"/> is authoritative when present: a
    /// process is this game's only if it is running from inside this game's
    /// install directory, which is what makes a stray <c>handler.exe</c>
    /// elsewhere on the disk — sharing a name with something under a game folder
    /// — correctly ignored.</para>
    ///
    /// <para>When the path is null (the OS refused it; see
    /// <see cref="ITrackedProcess.ExecutablePath"/>) the name is the only
    /// evidence left, and it is accepted <b>only if it is unambiguous across the
    /// whole library</b>. Guessing between two games that both ship a
    /// <c>Launcher.exe</c> would write a session against the wrong game, and a
    /// wrong session is worse than a missing one — it is indistinguishable from
    /// data once it is in the table.</para>
    /// </summary>
    public long? Match(string? executablePath, string processName)
    {
        if (!string.IsNullOrEmpty(executablePath))
        {
            var normalized = Normalize(executablePath);
            foreach (var root in _roots)
            {
                if (IsUnder(normalized, root.Path))
                {
                    return root.OwnershipId;
                }
            }

            // A path we could read that lies outside every install directory is
            // a definite no, not a fall-through to the weaker name rule.
            return null;
        }

        return _ownershipsByName.TryGetValue(processName, out var owners) && owners.Count == 1
            ? owners.First()
            : null;
    }

    private static bool IsUnder(string path, string root)
        => path.Length > root.Length
            && (path[root.Length] == Path.DirectorySeparatorChar
                || path[root.Length] == Path.AltDirectorySeparatorChar)
            && path.AsSpan(0, root.Length).Equals(root, PathComparison);

    private static string Normalize(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A path the OS will not canonicalise cannot match anything; keeping
            // it verbatim makes that outcome explicit instead of throwing out of
            // a poll.
            return path;
        }
    }

    private readonly record struct InstallRoot(string Path, long OwnershipId);
}
