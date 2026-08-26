namespace Hoard.Monitor;

/// <summary>
/// One running process as the Tier 1 enumeration sees it (§5.2 A): a pid and a
/// name, and deliberately <b>nothing else</b>.
///
/// <para><b>The absence of a path here is the design.</b> §5.2 says resolving
/// the executable path is "substantially more expensive than the enumeration
/// itself". Measured on the developer's Windows 11 machine, with 528 processes
/// running: <b>the enumeration takes 12 ms and resolving every path takes
/// 1,007 ms</b> — eighty-four times the cost, and, at the five-second poll, a
/// permanent twenty per cent of one core spent on a machine that is usually
/// running no games at all. (182 of those 528 refuse the query outright, so a
/// third of the second is spent failing.) The same pass filtered by name first
/// resolves two or three paths and costs nothing measurable.</para>
///
/// <para>A snapshot type carrying an <c>ExecutablePath</c> would make "resolve
/// every path, then filter" the natural way to write the watcher. With the path
/// unavailable until <see cref="IProcessSource.Track"/>, the cheap version is
/// the only version that compiles.</para>
/// </summary>
/// <param name="Pid">OS process id. Only unique among <i>live</i> processes — see <see cref="ITrackedProcess"/>.</param>
/// <param name="ProcessName">
/// Executable name without extension or directory, as <c>Process.ProcessName</c>
/// reports it on Windows and <c>/proc/&lt;pid&gt;/comm</c> on Linux. Compared
/// case-insensitively against <see cref="GameExecutableIndex.ProcessNames"/>.
/// </param>
public readonly record struct ProcessListing(int Pid, string ProcessName);

/// <summary>
/// The whole of the watcher's contact with the operating system, and therefore
/// the seam every test drives. Two methods, split along the §5.2 tier boundary:
/// <see cref="List"/> is Tier 1 (cheap, polled, names only) and
/// <see cref="Track"/> is Tier 2 (expensive, per-candidate, pins the process and
/// arms its exit callback).
///
/// <para><b>Implementations must not resolve executable paths in
/// <see cref="List"/>.</b> That is not a performance note appended to an
/// otherwise free choice; it is the contract. See <see cref="ProcessListing"/>.</para>
///
/// <para>A fake implementing this interface can script every failure mode §5.2
/// names — a launcher that precedes the game, a first executable that hands off
/// to a second, a whole process tree, a pid recycled by the OS, a blip under the
/// debounce floor — with no game, no timer and no real process anywhere. That
/// was the point of putting the seam here rather than around the watcher.</para>
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
/// A process the watcher has taken hold of (§5.2 Tier 2). Disposing releases the
/// OS handle and unsubscribes the exit callback.
///
/// <para><b>Why the handle is held for the whole life of the session, and not
/// released once the path and start time have been read.</b> Two reasons, and
/// only one of them is obvious:</para>
/// <list type="number">
/// <item>It is what delivers <see cref="Exited"/>. The kernel signals the
/// process handle; that is the event-driven exit §5.2 requires in place of
/// polling.</item>
/// <item><b>It pins the pid.</b> Windows will not recycle a process id while any
/// handle to that process object remains open, and the same is true of the
/// zombie entry a Linux parent holds. So for as long as the watcher is tracking
/// a game, no other process on the machine can appear at that pid — which closes,
/// by construction, the race a polling implementation would have to defend
/// against explicitly: seeing "pid 8104 is still there" on the next tick and
/// having no way to know it is a different pid 8104. Nothing in the code below
/// re-checks pid identity per tick because nothing has to.</item>
/// </list>
/// <para>The second reason is the one that gets refactored away by someone
/// tidying up a handle that "isn't used after startup". It is used. Holding it
/// <i>is</i> the use.</para>
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
    /// processes they protect, and an unelevated Hoard cannot query an elevated
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
    /// Whether the process has exited, as last observed <b>by the exit
    /// callback</b>.
    ///
    /// <para><b>Reading this is not exit detection and must never be used as
    /// such</b> (§5.2: "Polling is for discovery only — never for exit
    /// detection"). It exists for the one race the callback cannot cover: a
    /// process that exits between <see cref="IProcessSource.Track"/> arming the
    /// event and the caller reading it back. Everything after that comes from
    /// <see cref="Exited"/>.</para>
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
