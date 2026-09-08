using System.ComponentModel;
using System.Diagnostics;
using Winnow.Monitor;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The Windows Tier 1 walk against the live machine. <c>List()</c> reads the
/// <c>NtQuerySystemInformation</c> snapshot itself instead of going through
/// <c>Process.GetProcesses()</c>, so these tests pin the two things that buys
/// and the one thing it must not change: the names session detection matches on.
/// </summary>
public sealed class SystemProcessSourceTests
{
    /// <summary>
    /// The reason the walk exists. Every process on this machine is named
    /// identically by both snapshots, including the one entry the kernel leaves
    /// unnamed: measured on Windows 11 Pro 26200, pid 0 is that entry and
    /// <c>System.Diagnostics</c> calls it "Idle".
    /// </summary>
    [Fact]
    public void Every_shared_pid_is_named_the_same_as_Process_GetProcesses_names_it()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var listings = new SystemProcessSource().List();
        var reference = Process.GetProcesses();
        try
        {
            var expected = new Dictionary<int, string>(reference.Length);
            foreach (var process in reference)
            {
                try
                {
                    expected[process.Id] = process.ProcessName;
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // Exited between the two snapshots; it simply is not compared.
                }
            }

            Assert.NotEmpty(listings);

            var shared = 0;
            foreach (var listing in listings)
            {
                if (expected.TryGetValue(listing.Pid, out var name))
                {
                    shared++;
                    Assert.Equal(name, listing.ProcessName, ignoreCase: true);
                }
            }

            // Processes start and exit between the two calls, so only the
            // intersection can be compared — but it has to be nearly all of the
            // smaller set, or an implementation that returned a single entry
            // would satisfy the comparison above.
            var comparable = Math.Min(listings.Count, expected.Count);
            Assert.True(
                shared * 10 >= comparable * 9,
                $"only {shared} of {comparable} pids appeared in both snapshots");
        }
        finally
        {
            foreach (var process in reference)
            {
                process.Dispose();
            }
        }
    }

    [Fact]
    public void The_calling_process_appears_once_under_its_own_name()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var current = Process.GetCurrentProcess();
        var listings = new SystemProcessSource().List();

        var mine = new List<ProcessListing>();
        foreach (var listing in listings)
        {
            if (listing.Pid == current.Id)
            {
                mine.Add(listing);
            }
        }

        var only = Assert.Single(mine);
        Assert.Equal(current.ProcessName, only.ProcessName, ignoreCase: true);
        Assert.Null(only.SteamCompatibilityDataPath);
    }

    /// <summary>
    /// The measurement TASK-152.5 exists for. Asserted as a ratio and a
    /// per-process ceiling rather than a byte budget, because the absolute
    /// figures scale with the machine's process and thread count while the gap
    /// between the two paths does not.
    /// </summary>
    [Fact]
    public void A_poll_allocates_a_fraction_of_what_Process_GetProcesses_allocates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var source = new SystemProcessSource();

        // Warm up both paths first: the first call JITs the walk and sizes the
        // pinned snapshot buffer, and a five-second poll pays neither again.
        for (var i = 0; i < 3; i++)
        {
            source.List();
            DisposeAll(Process.GetProcesses());
        }

        var listed = 0;
        var walked = long.MaxValue;
        var materialized = long.MaxValue;
        for (var i = 0; i < 5; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var listings = source.List();
            walked = Math.Min(walked, GC.GetAllocatedBytesForCurrentThread() - before);
            listed = listings.Count;

            // Disposal is outside the measured window so this is the cost of the
            // call the old implementation made, and nothing else. It does not
            // read ProcessName either, which only understates the old figure.
            before = GC.GetAllocatedBytesForCurrentThread();
            var processes = Process.GetProcesses();
            materialized = Math.Min(materialized, GC.GetAllocatedBytesForCurrentThread() - before);
            DisposeAll(processes);
        }

        Assert.True(listed > 0, "the walk listed no processes");

        // Measured 79 KB against 1.29 MB at 700 processes and 14,121 threads on
        // the author's machine (16.7x). The factor here is well under that: the
        // old path's per-thread objects dominate, so a host with unusually few
        // threads per process narrows the ratio without regressing anything.
        Assert.True(
            walked * 8 < materialized,
            $"the walk allocated {walked} bytes against {materialized} for Process.GetProcesses() at {listed} processes");

        // A pid and a name cost a listing plus its two short strings. Anything
        // per-thread is orders of magnitude above this, so reintroducing it
        // fails here rather than merely eroding the ratio above.
        Assert.True(
            walked / listed < 400,
            $"the walk allocated {walked / listed} bytes per listing ({walked} bytes at {listed} processes)");
    }

    private static void DisposeAll(Process[] processes)
    {
        foreach (var process in processes)
        {
            process.Dispose();
        }
    }
}
