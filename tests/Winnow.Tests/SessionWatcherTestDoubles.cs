using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Monitor;

namespace Winnow.Tests;

/// <summary>
/// A scripted <see cref="IProcessSource"/>: the seam that lets the whole §5.2
/// watcher be tested with no game, no timer and no real process anywhere.
///
/// <para>It does more than return canned data. It <b>enforces</b> the two
/// guarantees the real OS provides and the watcher is built on, so a regression
/// in either shows up as a failing test rather than as a wrong row on someone's
/// machine six months later:</para>
/// <list type="bullet">
/// <item><b>A pid cannot be reused while a handle to it is open.</b>
/// <see cref="Start"/> throws if the pid still belongs to a tracked process the
/// watcher has not disposed. That is exactly the Windows/Linux behaviour
/// <see cref="ITrackedProcess"/> documents, and it makes "the watcher leaked a
/// handle" and "the watcher released a handle too early" both detectable.</item>
/// <item><b>Exit is only observable through the event.</b>
/// <see cref="FakeProcess.HasExited"/> counts its reads, so a watcher that
/// started polling for exits — the one thing §5.2 forbids — fails the assertion
/// instead of quietly working.</item>
/// <item><b>A disposed handle cannot be asked for its pid.</b>
/// <see cref="FakeProcess.Pid"/> throws after <see cref="FakeProcess.Dispose"/>,
/// because <c>System.Diagnostics.Process.Id</c> does — <c>Close()</c> clears the
/// cached id. That is not a detail: the exit callback runs on a thread pool
/// thread, so anything it throws is unhandled and kills the app.</item>
/// </list>
/// </summary>
public sealed class ScriptedProcessSource : IProcessSource
{
    private readonly Dictionary<int, FakeProcess> _running = [];
    private readonly Dictionary<int, FakeProcess> _handles = [];

    /// <summary>Every (pid, name) the watcher promoted to Tier 2, in order.</summary>
    public List<(int Pid, string Name)> TrackCalls { get; } = [];

    /// <summary>How many times Tier 1 enumerated.</summary>
    public int ListCalls { get; private set; }

    /// <summary>Starts a process. Visible to the next <see cref="List"/>.</summary>
    public FakeProcess Start(int pid, string name, string? executablePath, DateTime startedAtUtc)
    {
        if (_handles.TryGetValue(pid, out var held) && !held.Disposed)
        {
            throw new InvalidOperationException(
                $"Pid {pid} is still held by a tracked process the watcher has not disposed. "
                + "The OS would not recycle it; neither will this fake.");
        }

        var process = new FakeProcess(pid, name, executablePath, startedAtUtc);
        _running[pid] = process;
        _handles.Remove(pid);
        return process;
    }

    /// <summary>
    /// Ends a process: it leaves the enumeration and its exit event fires, which
    /// is the only way the watcher can learn about it.
    /// </summary>
    public void Exit(int pid, DateTime exitedAtUtc)
    {
        if (!_running.Remove(pid, out var process))
        {
            throw new InvalidOperationException($"Pid {pid} is not running.");
        }

        process.RaiseExit(exitedAtUtc);
    }

    public IReadOnlyList<ProcessListing> List()
    {
        ListCalls++;

        // Number, not Pid: the OS enumeration keeps reporting a process whose
        // handle this process happens to have closed. Only the handle wrapper
        // loses the id.
        return _running.Values
            .Select(static p => new ProcessListing(p.Number, p.ProcessName))
            .ToList();
    }

    public ITrackedProcess? Track(int pid, string expectedName)
    {
        TrackCalls.Add((pid, expectedName));

        if (!_running.TryGetValue(pid, out var process)
            || !string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        _handles[pid] = process;
        return process;
    }
}

/// <summary>One scripted process. See <see cref="ScriptedProcessSource"/>.</summary>
public sealed class FakeProcess : ITrackedProcess
{
    private bool _exited;

    internal FakeProcess(int pid, string processName, string? executablePath, DateTime startedAtUtc)
    {
        Number = pid;
        ProcessName = processName;
        ExecutablePath = executablePath;
        StartedAtUtc = startedAtUtc;
    }

    /// <summary>The pid as the OS enumeration reports it. Never throws.</summary>
    internal int Number { get; }

    /// <summary>
    /// Runs after the exit event's handler list has been captured but before any
    /// handler is invoked — the instant at which a real OS callback is
    /// irrevocably in flight and the watcher may still be torn down underneath
    /// it. A test sets this to dispose the watcher there.
    /// </summary>
    public Action? WhileExitIsInFlight { get; set; }

    /// <summary>
    /// The pid, as <c>Process.Id</c> behaves: unavailable once the handle has
    /// been closed. See the remarks on <see cref="ScriptedProcessSource"/>.
    /// </summary>
    public int Pid => Disposed
        ? throw new InvalidOperationException(
            "No process is associated with this object. (Modelling Process.Id after Close().)")
        : Number;

    public string ProcessName { get; }

    public string? ExecutablePath { get; }

    public DateTime StartedAtUtc { get; }

    /// <summary>
    /// Reads of <see cref="HasExited"/>. One per attach is the legitimate
    /// race check; anything that grows with the poll count is exit polling.
    /// </summary>
    public int HasExitedReads { get; private set; }

    public bool Disposed { get; private set; }

    public bool HasExited
    {
        get
        {
            HasExitedReads++;
            return _exited;
        }
    }

    public DateTime? ExitedAtUtc { get; private set; }

    public event EventHandler? Exited;

    public void Dispose() => Disposed = true;

    internal void RaiseExit(DateTime exitedAtUtc)
    {
        if (_exited)
        {
            return;
        }

        _exited = true;
        ExitedAtUtc = exitedAtUtc;

        // Captured first, exactly as `Exited?.Invoke(...)` would: from here the
        // invocation is committed and unsubscribing can no longer stop it.
        var handlers = Exited;
        WhileExitIsInFlight?.Invoke();
        handlers?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// A <see cref="ISessionRepository"/> that can be told to reject the next few
/// inserts, so the watcher's write-failure path is reachable from a test. Every
/// other call passes straight through to a real repository on a real database.
/// </summary>
public sealed class FlakySessionRepository(ISessionRepository inner) : ISessionRepository
{
    /// <summary>Inserts to reject before letting writes through again.</summary>
    public int FailNextInserts { get; set; }

    /// <summary>Insert calls received, successful or not.</summary>
    public int InsertAttempts { get; private set; }

    public Task<long> InsertAsync(Session session, CancellationToken ct = default)
    {
        InsertAttempts++;
        if (FailNextInserts > 0)
        {
            FailNextInserts--;
            return Task.FromException<long>(
                new InvalidOperationException("database is locked (simulated)"));
        }

        return inner.InsertAsync(session, ct);
    }

    public Task<Session?> GetAsync(long id, CancellationToken ct = default)
        => inner.GetAsync(id, ct);

    public Task<IReadOnlyList<Session>> GetByOwnershipAsync(long ownershipId, CancellationToken ct = default)
        => inner.GetByOwnershipAsync(ownershipId, ct);

    public Task SetNoteAsync(SessionNote note, CancellationToken ct = default)
        => inner.SetNoteAsync(note, ct);

    public Task<SessionNote?> GetNoteAsync(long sessionId, CancellationToken ct = default)
        => inner.GetNoteAsync(sessionId, ct);
}
