using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Monitor;

/// <summary>
/// The real <see cref="IProcessSource"/>: <c>System.Diagnostics.Process</c> for
/// enumeration and lifetime, with a Windows-specific path resolver behind it.
/// Every test drives a fake instead, so nothing in this file is exercised by the
/// suite — which is the reason it is kept as thin and as obvious as it is.
/// </summary>
public sealed class SystemProcessSource : IProcessSource
{
    private readonly ILogger<SystemProcessSource> _logger;

    public SystemProcessSource(ILogger<SystemProcessSource>? logger = null)
        => _logger = logger ?? NullLogger<SystemProcessSource>.Instance;

    /// <summary>
    /// §5.2 Tier 1. <c>Process.GetProcesses()</c> is one
    /// <c>NtQuerySystemInformation</c> snapshot on Windows and one <c>/proc</c>
    /// walk on Linux; the <c>Process</c> objects it hands back are lazily
    /// populated and hold no handle until something asks for a property that
    /// needs one. Reading <c>Id</c> and <c>ProcessName</c> does not, so this
    /// method opens nothing.
    ///
    /// <para>The objects are disposed immediately. They are cheap shells here
    /// precisely because no handle was ever opened, and the ones that matter are
    /// re-opened deliberately by <see cref="Track"/>.</para>
    /// </summary>
    public IReadOnlyList<ProcessListing> List()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A failed enumeration costs one poll, never the watcher.
            _logger.LogDebug(ex, "Process enumeration failed; skipping this discovery pass.");
            return [];
        }

        var listings = new List<ProcessListing>(processes.Length);
        foreach (var process in processes)
        {
            try
            {
                // Both of these come out of the snapshot. Touching anything else
                // here — MainModule above all — is what §5.2 forbids.
                listings.Add(new ProcessListing(process.Id, process.ProcessName));
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Exited between the snapshot and now, or a protected process
                // the name of which we are not allowed to read. Neither is
                // interesting: a game we can't name is a game we can't match.
            }
            finally
            {
                process.Dispose();
            }
        }

        return listings;
    }

    /// <summary>
    /// §5.2 Tier 2. Opens the process, reads its true start time and path, and
    /// arms the OS exit callback. See <see cref="ITrackedProcess"/> for why the
    /// handle then stays open.
    /// </summary>
    public ITrackedProcess? Track(int pid, string expectedName)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);

            // Pid-reuse guard, and the reason Track takes the expected name at
            // all. Between List() filling its snapshot and this call, the
            // original process can exit and the OS can hand the number to
            // something else. From here on the retained handle makes reuse
            // impossible, but this instant is before that.
            if (!string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Pid {Pid} is now {Actual}, not {Expected}; refusing the track.",
                    pid, process.ProcessName, expectedName);
                process.Dispose();
                return null;
            }

            var startedAt = ResolveStartTimeUtc(process);

            var tracked = new SystemTrackedProcess(
                process, expectedName, ResolveExecutablePath(process), startedAt);
            process = null; // ownership handed over; the finally must not dispose it
            return tracked;
        }
        catch (ArgumentException)
        {
            // "Process with an Id of N is not running" — it exited during the
            // poll. Routine on any machine, not worth a log line above Trace.
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Could not track pid {Pid} ({Name}).", pid, expectedName);
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// True process creation time, UTC, without ever passing through local time.
    ///
    /// <para><b>Why not <c>Process.StartTime.ToUniversalTime()</c>.</b> The
    /// kernel records creation as a UTC FILETIME, and the BCL's
    /// <c>Process.StartTime</c> converts it to <i>local</i> time before handing
    /// it over; converting back is not the identity function. During the
    /// repeated hour of a DST fall-back, one local timestamp names two distinct
    /// UTC instants and <see cref="DateTime.ToUniversalTime"/> resolves the
    /// ambiguity by assuming standard time — so a game launched in the hour
    /// before the clocks go back is recorded an hour late, and its session an
    /// hour short (or, at the other end, an hour long). Reading the FILETIME
    /// directly has no ambiguous case to resolve.</para>
    ///
    /// <para>Once a year, for one hour, on a subset of sessions — which is
    /// exactly the kind of defect that is never reproduced and never believed.
    /// The non-Windows fallback keeps the round trip because there is no
    /// portable alternative in the BCL; <c>/proc</c> field 22 plus boot time
    /// would be the equivalent fix there.</para>
    /// </summary>
    private static DateTime ResolveStartTimeUtc(Process process)
    {
        if (OperatingSystem.IsWindows()
            && Win32.TryGetProcessTimesUtc(process.Id, out var createdUtc, out _))
        {
            return createdUtc;
        }

        return process.StartTime.ToUniversalTime();
    }

    /// <summary>
    /// Full executable path, or null when the OS will not say.
    ///
    /// <para><b>Why not just <c>MainModule.FileName</c>.</b> The BCL implements
    /// that with <c>EnumProcessModules</c>, which needs
    /// <c>PROCESS_QUERY_INFORMATION | PROCESS_VM_READ</c> — and PROCESS_VM_READ
    /// is precisely the right that kernel-mode anti-cheat (EAC, BattlEye and
    /// friends) strips from every handle opened against the games it protects.
    /// Those drivers leave <c>PROCESS_QUERY_LIMITED_INFORMATION</c> alone,
    /// because Task Manager needs it. So on the exact population of games most
    /// worth recording sessions for, <c>MainModule</c> throws and
    /// <c>QueryFullProcessImageName</c> answers.</para>
    ///
    /// <para><b>And the choice matters more than it looks, because of the
    /// ordering above.</b> <see cref="Track"/> reads <c>Process.StartTime</c>
    /// before it gets here, and the BCL implements <i>that</i> with
    /// <c>GetProcessTimes</c> on a <c>PROCESS_QUERY_LIMITED_INFORMATION</c>
    /// handle — the same right <c>QueryFullProcessImageName</c> needs. So the
    /// two succeed and fail together: any process this method is asked about is
    /// one whose path it can read. With <c>MainModule</c> as the only resolver
    /// there would be a whole class of processes that track successfully and
    /// then have no path, which forces
    /// <see cref="GameExecutableIndex.Match"/> down onto the weaker
    /// name-only rule for exactly the games most likely to have an ambiguous
    /// name. Measured on this machine, 182 of 528 running processes refuse the
    /// limited-information handle; those never reach here at all, because
    /// <c>StartTime</c> already threw.</para>
    ///
    /// <para>Null therefore remains possible but is close to unreachable on
    /// Windows. The real gap is one level up: an elevated game cannot be opened
    /// by an unelevated Hoard at any access level, so it is never tracked and
    /// never records a session. §5.2 mechanism B is the answer for those.</para>
    /// </summary>
    private string? ResolveExecutablePath(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Win32.TryGetProcessImagePath(process.Id);
            if (path is not null)
            {
                return path;
            }
        }

        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            _logger.LogDebug(
                "No executable path for pid {Pid}; falling back to name matching.", process.Id);
            return null;
        }
    }

    /// <summary>
    /// Wraps a live <c>Process</c>. The handle inside it is held until
    /// <see cref="Dispose"/> — see <see cref="ITrackedProcess"/> for the two
    /// reasons, one of which is not obvious.
    /// </summary>
    private sealed class SystemTrackedProcess : ITrackedProcess
    {
        private readonly Process _process;

        /// <summary>
        /// Captured at construction and never read back off the
        /// <see cref="Process"/>.
        ///
        /// <para><b>This is a crash fix, not a micro-optimisation.</b>
        /// <c>Process.Id</c> throws <see cref="InvalidOperationException"/> once
        /// the object has been disposed — <c>Process.Close()</c> clears the
        /// cached id — and the watcher can dispose a tracked process (shutdown,
        /// <c>Dispose</c>) at the same moment an exit callback is already in
        /// flight on a thread pool thread with the delegate captured. That
        /// callback reads the pid first thing. An exception thrown from a thread
        /// pool callback is unhandled and takes the whole app down with it, so
        /// the one property every exit path touches must not be able to
        /// throw.</para>
        /// </summary>
        private readonly int _pid;

        private readonly object _gate = new();
        private bool _exited;
        private DateTime? _exitedAt;
        private bool _disposed;

        internal SystemTrackedProcess(
            Process process, string processName, string? executablePath, DateTime startedAtUtc)
        {
            _process = process;
            _pid = process.Id;
            ProcessName = processName;
            ExecutablePath = executablePath;
            StartedAtUtc = startedAtUtc;

            // Arm first, subscribe second. .NET registers a wait on the process
            // handle when EnableRaisingEvents is set, and a handle that is
            // already signalled fires the callback as soon as a handler exists —
            // so a process that dies in this very method still reports, and
            // there is no window to defend.
            _process.EnableRaisingEvents = true;
            _process.Exited += OnExited;

            // The one legitimate synchronous check (see ITrackedProcess.HasExited):
            // it costs a single GetExitCodeProcess at track time and nothing
            // afterwards. Everything later comes from the callback.
            if (_process.HasExited)
            {
                OnExited(this, EventArgs.Empty);
            }
        }

        public int Pid => _pid;

        public string ProcessName { get; }

        public string? ExecutablePath { get; }

        public DateTime StartedAtUtc { get; }

        public bool HasExited
        {
            get
            {
                lock (_gate)
                {
                    return _exited;
                }
            }
        }

        public DateTime? ExitedAtUtc
        {
            get
            {
                lock (_gate)
                {
                    return _exitedAt;
                }
            }
        }

        public event EventHandler? Exited;

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _process.Exited -= OnExited;
            _process.Dispose();
        }

        private void OnExited(object? sender, EventArgs e)
        {
            lock (_gate)
            {
                // .NET can deliver this once from the registered wait and once
                // from the constructor's HasExited check. Idempotent on purpose.
                if (_exited)
                {
                    return;
                }

                _exited = true;
                _exitedAt = ReadExitTimeUtc();
            }

            Exited?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The OS's own record of when the process ended, so a callback serviced
        /// late — thread pool starvation, a laptop resuming from sleep — still
        /// yields the right end timestamp rather than "whenever we woke up".
        /// Null when the handle will not give it up; the watcher then falls back
        /// to its clock.
        ///
        /// <para>Read as a UTC FILETIME for the same reason the start time is —
        /// see <see cref="ResolveStartTimeUtc"/>. Opening a fresh handle by pid
        /// is safe here even though the process has exited: this object still
        /// holds one, which keeps the process object alive and the pid
        /// unrecycled until <see cref="Dispose"/>.</para>
        /// </summary>
        private DateTime? ReadExitTimeUtc()
        {
            if (OperatingSystem.IsWindows()
                && Win32.TryGetProcessTimesUtc(_pid, out _, out var exitedUtc)
                && exitedUtc is not null)
            {
                return exitedUtc;
            }

            try
            {
                return _process.ExitTime.ToUniversalTime();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return null;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static class Win32
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int ErrorInsufficientBuffer = 122;

        /// <summary>
        /// Creation and exit times as the kernel records them: UTC FILETIMEs,
        /// never routed through local time. <paramref name="exitedUtc"/> is null
        /// while the process is still running (the API writes a zero FILETIME).
        /// </summary>
        internal static bool TryGetProcessTimesUtc(int pid, out DateTime createdUtc, out DateTime? exitedUtc)
        {
            createdUtc = default;
            exitedUtc = null;

            var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, pid);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                if (!GetProcessTimes(handle, out var creation, out var exit, out _, out _)
                    || creation <= 0)
                {
                    return false;
                }

                createdUtc = DateTime.FromFileTimeUtc(creation);
                if (exit > 0)
                {
                    exitedUtc = DateTime.FromFileTimeUtc(exit);
                }

                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                // A FILETIME outside DateTime's range. Not reachable from a real
                // process, but the conversion is documented to throw and this
                // runs on the exit callback path, where an exception is fatal.
                return false;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        internal static string? TryGetProcessImagePath(int pid)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, bInheritHandle: false, pid);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // Long-path aware: Windows paths can exceed MAX_PATH and the
                // call reports ERROR_INSUFFICIENT_BUFFER rather than truncating,
                // so grow once and retry instead of silently returning a cut
                // path that would then match no install directory.
                for (var capacity = 512; capacity <= 32768; capacity *= 4)
                {
                    var buffer = new char[capacity];
                    var size = (uint)capacity;
                    if (QueryFullProcessImageName(handle, 0, buffer, ref size))
                    {
                        return new string(buffer, 0, (int)size);
                    }

                    if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                    {
                        return null;
                    }
                }

                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        // FILETIME is two 32-bit halves in the header and eight bytes in memory,
        // so a long marshals it exactly and DateTime.FromFileTimeUtc consumes it
        // directly — no local-time hop anywhere in the path.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(
            IntPtr hProcess,
            out long lpCreationTime,
            out long lpExitTime,
            out long lpKernelTime,
            out long lpUserTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            int dwProcessId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            uint dwFlags,
            [Out] char[] lpExeName,
            ref uint lpdwSize);
    }
}
