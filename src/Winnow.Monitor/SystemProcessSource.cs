using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Monitor;

/// <summary>Production <see cref="IProcessSource"/> using <c>System.Diagnostics.Process</c> with Windows-specific path resolution.</summary>
public sealed class SystemProcessSource : IProcessSource
{
    private readonly ILogger<SystemProcessSource> _logger;

    public SystemProcessSource(ILogger<SystemProcessSource>? logger = null)
        => _logger = logger ?? NullLogger<SystemProcessSource>.Instance;

    /// <summary>Enumerates all processes (Tier 1). Reads only pid and name; opens no handles.</summary>
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

    /// <summary>Opens the process, reads start time and path, arms the exit callback (Tier 2).</summary>
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
    /// True process creation time, UTC. Reads the kernel FILETIME directly to
    /// avoid DST ambiguity in <c>Process.StartTime.ToUniversalTime()</c>.
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
    /// Full executable path, or null when the OS refused. Uses
    /// <c>QueryFullProcessImageName</c> on Windows (works with anti-cheat),
    /// falling back to <c>MainModule.FileName</c>.
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

    /// <summary>Wraps a live <c>Process</c>, holding the handle until <see cref="Dispose"/>.</summary>
    private sealed class SystemTrackedProcess : ITrackedProcess
    {
        private readonly Process _process;

        // Cached at construction: Process.Id throws after disposal, and exit
        // callbacks on the thread pool would crash the app if they hit that.
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

        /// <summary>OS exit time (UTC FILETIME), or null when unavailable. Immune to late callback delivery.</summary>
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

        /// <summary>Kernel creation/exit times as UTC FILETIMEs. <paramref name="exitedUtc"/> is null while running.</summary>
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
