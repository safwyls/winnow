using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Monitor;

/// <summary>
/// Production <see cref="IProcessSource"/>. Tier 1 walks the Windows process
/// snapshot directly and falls back to <c>System.Diagnostics.Process</c>
/// elsewhere; Tier 2 is <c>Process</c> plus Windows-specific path resolution.
/// </summary>
public sealed class SystemProcessSource : IProcessSource
{
    private const int InitialSnapshotBytes = 256 * 1024;
    private const int SnapshotHeadroomBytes = 64 * 1024;
    private const int MaximumSnapshotBytes = 128 * 1024 * 1024;
    private const int SnapshotAttempts = 8;
    private const int ListingHeadroom = 16;
    private const int IdleProcessId = 0;
    private const int SystemProcessId = 4;

    private readonly ILogger<SystemProcessSource> _logger;

    // Serializes the reusable snapshot buffer below. The watcher polls from one
    // loop, but this class is registered as a singleton and nothing in
    // IProcessSource promises a single caller.
    private readonly object _snapshotGate = new();

    // Reused across polls and allocated pinned, because the kernel writes each
    // entry's ImageName.Buffer as a raw address inside this block: a buffer the
    // GC could move would leave the walk reading somewhere else. It grows only
    // with the machine's process and thread count, so after the first poll it
    // is neither reallocated nor collected.
    private byte[] _snapshot = [];

    // The previous poll's result count, so the listing list is sized once
    // instead of doubling its way up to ~700 entries every five seconds.
    private int _listingCapacity = 256;

    public SystemProcessSource(ILogger<SystemProcessSource>? logger = null)
        => _logger = logger ?? NullLogger<SystemProcessSource>.Instance;

    /// <summary>Enumerates all processes (Tier 1). Reads pid, name and the Linux Proton marker; opens no handles.</summary>
    public IReadOnlyList<ProcessListing> List()
        => OperatingSystem.IsWindows() ? ListFromSnapshot() : ListFromProcessObjects();

    /// <summary>
    /// The Windows walk: one <c>NtQuerySystemInformation</c> snapshot, read for
    /// pid and image name and nothing else.
    ///
    /// <para><c>Process.GetProcesses()</c> issues the same syscall, but then
    /// materializes a <c>Process</c>, a <c>ProcessInfo</c> and a
    /// <c>ThreadInfo</c> for every thread on the machine — 1.29 MB of garbage
    /// across ~14,000 objects per five-second tick on the author's box, for two
    /// fields (docs/spikes/memory-footprint.md §2.3).</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private IReadOnlyList<ProcessListing> ListFromSnapshot()
    {
        lock (_snapshotGate)
        {
            try
            {
                if (!TryFillSnapshot(out var length))
                {
                    return [];
                }

                var entrySize = Unsafe.SizeOf<Win32.SystemProcessInformation>();
                var listings = new List<ProcessListing>(_listingCapacity);
                for (var offset = 0; offset >= 0 && offset <= length - entrySize;)
                {
                    var entry = MemoryMarshal.Read<Win32.SystemProcessInformation>(
                        _snapshot.AsSpan(offset));

                    // Tier 1 on Windows is exactly the pid and the name: the
                    // Proton marker is Linux-only and paths are Tier 2.
                    listings.Add(new ProcessListing((int)entry.UniqueProcessId, ReadProcessName(entry)));

                    // A zero offset ends the list. Anything shorter than one
                    // entry would re-read this one forever, so refuse it rather
                    // than spin inside a five-second poll.
                    if (entry.NextEntryOffset < (uint)entrySize)
                    {
                        break;
                    }

                    offset += (int)entry.NextEntryOffset;
                }

                _listingCapacity = listings.Count + ListingHeadroom;
                return listings;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // A failed enumeration costs one poll, never the watcher.
                _logger.LogDebug(ex, "Process enumeration failed; skipping this discovery pass.");
                return [];
            }
        }
    }

    /// <summary>
    /// Fills <see cref="_snapshot"/> with a fresh snapshot and reports how much
    /// of it the kernel wrote. False means this poll produces nothing.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private bool TryFillSnapshot(out int length)
    {
        for (var attempt = 0; attempt < SnapshotAttempts; attempt++)
        {
            if (_snapshot.Length == 0)
            {
                _snapshot = AllocateSnapshot(InitialSnapshotBytes);
            }

            var status = Win32.NtQuerySystemInformation(
                Win32.SystemProcessInformationClass,
                Marshal.UnsafeAddrOfPinnedArrayElement(_snapshot, 0),
                _snapshot.Length,
                out var required);

            if (status == Win32.StatusSuccess)
            {
                // The written length only bounds the walk; NextEntryOffset ends
                // it. Treating an unreported length as "all of it" therefore
                // costs nothing and loses no process.
                length = required > 0 ? Math.Min(required, _snapshot.Length) : _snapshot.Length;
                return true;
            }

            if (status != Win32.StatusInfoLengthMismatch)
            {
                _logger.LogDebug(
                    "Process enumeration failed with NTSTATUS 0x{Status}; skipping this discovery pass.",
                    status.ToString("X8", CultureInfo.InvariantCulture));
                length = 0;
                return false;
            }

            // The kernel reports what it needed, but processes and threads keep
            // starting, so ask for headroom. Doubling is the floor in case a
            // future kernel reports nothing on the mismatch, which would
            // otherwise retry the same size until the attempts run out.
            var grown = Math.Max((long)required + SnapshotHeadroomBytes, (long)_snapshot.Length * 2);
            if (grown > MaximumSnapshotBytes)
            {
                _logger.LogDebug(
                    "Process snapshot wanted {Bytes} bytes; skipping this discovery pass.", grown);
                length = 0;
                return false;
            }

            _snapshot = AllocateSnapshot((int)grown);
        }

        _logger.LogDebug(
            "Process snapshot still did not fit after {Attempts} attempts; skipping this discovery pass.",
            SnapshotAttempts);
        length = 0;
        return false;
    }

    private static byte[] AllocateSnapshot(int bytes)
        => GC.AllocateUninitializedArray<byte>(bytes, pinned: true);

    /// <summary>
    /// The name <c>Process.ProcessName</c> would report for this entry.
    ///
    /// <para>.NET derives that property from the same <c>ImageName</c> field, so
    /// agreeing with it means reproducing its rule rather than inventing one:
    /// see <see cref="TrimToProcessName"/> for the named case, and
    /// <c>SystemProcessSourceTests</c> for the assertion that the two snapshots
    /// still name every shared pid identically.</para>
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string ReadProcessName(in Win32.SystemProcessInformation entry)
    {
        var pid = (int)entry.UniqueProcessId;
        if (entry.ImageName.Buffer == 0)
        {
            // The kernel names neither the idle process nor a process it
            // protects. Measured on Windows 11 26200: pid 0 is the only such
            // entry on an ordinary machine, and System.Diagnostics calls it
            // "Idle", so these substitutions are its, not ours.
            return pid switch
            {
                IdleProcessId => "Idle",
                SystemProcessId => "System",
                _ => pid.ToString(CultureInfo.InvariantCulture),
            };
        }

        var image = Marshal.PtrToStringUni(entry.ImageName.Buffer, entry.ImageName.Length / sizeof(char));
        return image is null ? string.Empty : TrimToProcessName(image);
    }

    /// <summary>
    /// <c>System.Diagnostics</c>' own trimming rule: the text after the last
    /// backslash, minus a trailing <c>.exe</c>. Every other extension stays,
    /// because <see cref="GameExecutableIndex"/> is built from the same rule and
    /// a <c>launcher.bin</c> that lost its suffix here would match nothing.
    /// </summary>
    private static string TrimToProcessName(string imageName)
    {
        var name = imageName.AsSpan(imageName.LastIndexOf('\\') + 1);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == imageName.Length ? imageName : name.ToString();
    }

    /// <summary>
    /// The portable walk, used on Linux. <c>/proc</c> enumeration is already a
    /// directory listing, and the Proton marker has to be read per pid anyway,
    /// so there is nothing here for a snapshot to save.
    /// </summary>
    private IReadOnlyList<ProcessListing> ListFromProcessObjects()
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
                // The pid and name come out of the snapshot. Linux also reads
                // its one bounded Proton marker; MainModule remains Tier 2.
                listings.Add(new ProcessListing(
                    process.Id,
                    process.ProcessName,
                    ReadSteamCompatibilityDataPath(process.Id)));
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
                process,
                expectedName,
                ResolveExecutablePath(process),
                ReadSteamCompatibilityDataPath(process.Id),
                startedAt);
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

    /// <summary>Reads the one Proton attribution variable without retaining process environment data.</summary>
    private static string? ReadSteamCompatibilityDataPath(int pid)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            // A process environment is untrusted input. Only this one marker
            // matters, so cap the Tier 1 read rather than letting a hostile
            // process turn every five-second poll into an arbitrary allocation.
            const int maximumEnvironmentBytes = 64 * 1024;
            var bytes = new byte[maximumEnvironmentBytes];
            int length;
            using (var stream = new FileStream(
                $"/proc/{pid}/environ", FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                length = stream.Read(bytes, 0, bytes.Length);
            }

            var prefix = "STEAM_COMPAT_DATA_PATH="u8;
            for (var offset = 0; offset < length;)
            {
                var end = Array.IndexOf(bytes, (byte)0, offset);
                if (end < 0 || end > length)
                {
                    end = length;
                }

                var entry = bytes.AsSpan(offset, end - offset);
                if (entry.StartsWith(prefix))
                {
                    return System.Text.Encoding.UTF8.GetString(entry[prefix.Length..]);
                }

                offset = end + 1;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A process can exit or be protected between List and Track. No
            // prefix then means no Proton-specific attribution, never a guess.
        }

        return null;
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
            Process process,
            string processName,
            string? executablePath,
            string? steamCompatibilityDataPath,
            DateTime startedAtUtc)
        {
            _process = process;
            _pid = process.Id;
            ProcessName = processName;
            ExecutablePath = executablePath;
            SteamCompatibilityDataPath = steamCompatibilityDataPath;
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

        public string? SteamCompatibilityDataPath { get; }

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

        internal const int StatusSuccess = 0;
        internal const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        /// <summary><c>SystemProcessInformation</c>: every process, each followed by its threads.</summary>
        internal const int SystemProcessInformationClass = 5;

        /// <summary><c>UNICODE_STRING</c>. <see cref="Length"/> counts bytes, and <see cref="Buffer"/> is null for a process the kernel will not name.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct UnicodeString
        {
            public ushort Length;
            public ushort MaximumLength;
            public nint Buffer;
        }

        /// <summary>
        /// The head of <c>SYSTEM_PROCESS_INFORMATION</c>, down to the last field
        /// this module reads.
        ///
        /// <para>The reserved fields are the kernel's working-set and timing
        /// values. They are declared rather than skipped so the runtime computes
        /// where <see cref="ImageName"/> and <see cref="UniqueProcessId"/> sit,
        /// which keeps the layout right on x86, x64 and ARM64 alike; they are
        /// public and never read because a private field nothing assigns is a
        /// warning, and warnings are errors here.</para>
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct SystemProcessInformation
        {
            public uint NextEntryOffset;
            public uint NumberOfThreads;
            public long WorkingSetPrivateSize;
            public long ReservedLarge1;
            public long ReservedLarge2;
            public long CreateTime;
            public long UserTime;
            public long KernelTime;
            public UnicodeString ImageName;
            public int BasePriority;
            public nint UniqueProcessId;
        }

        // Returns an NTSTATUS, so there is no Win32 last error to carry. The
        // buffer is passed as an address rather than as a byte[]: the kernel
        // writes ImageName pointers into it, and the caller's array stays pinned
        // for the walk that follows the call.
        [DllImport("ntdll.dll")]
        internal static extern int NtQuerySystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            int SystemInformationLength,
            out int ReturnLength);

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
