namespace Winnow.Monitor;

/// <summary>
/// One running process as the Tier 1 enumeration sees it: a pid and a name,
/// deliberately without an executable path (path resolution is Tier 2).
/// </summary>
/// <param name="Pid">OS process id. Only unique among <i>live</i> processes — see <see cref="ITrackedProcess"/>.</param>
/// <param name="ProcessName">
/// Executable name without extension or directory, as <c>Process.ProcessName</c>
/// reports it on Windows and <c>/proc/&lt;pid&gt;/comm</c> on Linux. Compared
/// case-insensitively against <see cref="GameExecutableIndex.ProcessNames"/>.
/// </param>
public readonly record struct ProcessListing(int Pid, string ProcessName);

/// <summary>
/// The watcher's OS seam. <see cref="List"/> is Tier 1 (cheap, polled, names
/// only); <see cref="Track"/> is Tier 2 (per-candidate, pins process, arms exit
/// callback). Implementations must not resolve paths in <see cref="List"/>.
/// </summary>
public interface IProcessSource
{
    /// <summary>
    /// Every process currently visible to this user, as pid + name.
    ///
    /// <para>Called once per poll (default 5s), so it must stay cheap: on
    /// Windows this is one <c>NtQuerySystemInformation</c> snapshot, on Linux a
    /// directory listing of <c>/proc</c> plus one <c>comm</c> read each. It must
    /// not open per-process handles and must not throw for processes it cannot
    /// see — an unreadable process is simply absent from the result.</para>
    /// </summary>
    IReadOnlyList<ProcessListing> List();

    /// <summary>
    /// Promotes one listing to a tracked process: resolves its executable path
    /// and true start time, and arms an OS exit callback.
    ///
    /// <para>Returns null when the process cannot be tracked — it exited between
    /// the enumeration and this call (routine; the pid may already belong to
    /// something else), the OS refused a handle, or
    /// <paramref name="expectedName"/> no longer matches, which is the pid-reuse
    /// guard. A null is never an error worth surfacing.</para>
    /// </summary>
    /// <param name="pid">Pid from a <see cref="ProcessListing"/> returned by <see cref="List"/>.</param>
    /// <param name="expectedName">
    /// The name that listing carried. An implementation must re-read the live
    /// process's name and refuse the track if it differs: between the snapshot
    /// and this call the original can exit and the pid be handed to an unrelated
    /// process, and attributing that stranger's runtime to a game is exactly the
    /// silently-wrong record this module must never write.
    /// </param>
    ITrackedProcess? Track(int pid, string expectedName);
}

/// <summary>
/// A process the watcher has taken hold of (Tier 2). Disposing releases the OS
/// handle and unsubscribes the exit callback. The handle is held for the session's
/// lifetime to deliver <see cref="Exited"/> and to pin the pid against reuse.
/// </summary>
public interface ITrackedProcess : IDisposable
{
    /// <summary>OS process id, pinned against reuse for this object's lifetime.</summary>
    int Pid { get; }

    /// <summary>Executable name without extension, re-read from the live process.</summary>
    string ProcessName { get; }

    /// <summary>
    /// Full path of the main executable, or null when the OS refused it.
    ///
    /// <para>Null is common enough on Windows to be a supported state rather
    /// than an error: anti-cheat drivers strip rights from handles to the
    /// processes they protect, and an unelevated Winnow cannot query an elevated
    /// game at all. The watcher falls back to matching on
    /// <see cref="ProcessName"/> alone, and only when that name is unambiguous
    /// across the whole library.</para>
    /// </summary>
    string? ExecutablePath { get; }

    /// <summary>
    /// True wall-clock start, UTC. <b>Not</b> the time the watcher noticed.
    ///
    /// <para>This is the §5.2 guarantee that the poll interval governs when the
    /// app notices and never what it records: a game found on the poll five
    /// seconds after it launched still gets its real start time and therefore
    /// its real duration. Implementations read <c>Process.StartTime</c>
    /// (Windows/Linux both) and convert — that property returns <i>local</i>
    /// time, and a record five hours out is worse than no record.</para>
    /// </summary>
    DateTime StartedAtUtc { get; }

    /// <summary>
    /// Whether the process has exited. Exists only for the race between
    /// <see cref="IProcessSource.Track"/> and the exit callback; not for polling.
    /// </summary>
    bool HasExited { get; }

    /// <summary>
    /// True wall-clock exit, UTC; null while running. Read from the OS's own
    /// record of the exit, so a callback serviced late still yields the right
    /// timestamp — the same property <see cref="StartedAtUtc"/> has, at the
    /// other end.
    /// </summary>
    DateTime? ExitedAtUtc { get; }

    /// <summary>
    /// Raised once, from an OS callback, when the process exits. Fires on a
    /// thread pool thread; handlers must be short and must not touch the
    /// database (the watcher's handler records a timestamp under a lock and
    /// leaves the writing to the next reconciliation pass).
    ///
    /// <para>A handler attached after the process has already exited is
    /// guaranteed to be invoked anyway, so there is no window between arming the
    /// event and subscribing to it.</para>
    /// </summary>
    event EventHandler? Exited;
}
