using System.Diagnostics;
using Winnow.TestFixtures;
using Xunit;

namespace Winnow.Update.Tests;

public sealed class UpdateParentProcessTests
{
    private static string Stat(int pid, string comm = "parent", string birth = "41641", char state = 'S')
        => $"{pid} ({comm}) {state} {string.Join(' ', Enumerable.Repeat("0", 18))} {birth} 0 0";

    [Theory]
    [InlineData("parent")]
    [InlineData("a parent (worker)")]
    [InlineData("name) with spaces\nand (parentheses")]
    public void ReadsExactKernelBirthAfterTheWholeCommField(string comm)
    {
        Assert.Equal(new UpdateParentProcess.LinuxIdentity(41641, 'S'), UpdateParentProcess.ParseLinuxStat(42, Stat(42, comm)));
        Assert.Equal(ulong.MaxValue, UpdateParentProcess.ParseLinuxStat(42, Stat(42, birth: ulong.MaxValue.ToString())).StartClockTicks);
    }

    [Theory]
    [InlineData("43 (parent) S 0 0")]
    [InlineData("42 parent S 0")]
    [InlineData("42 (parent) S 0")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("18446744073709551616")]
    public void RefusesMalformedOrNonpositiveIdentity(string value)
        => Assert.Throws<IOException>(() => UpdateParentProcess.ParseLinuxStat(42, value.Contains(' ') ? value : Stat(42, birth: value)));

    [Theory]
    [InlineData(false, "missing")]
    [InlineData(false, "reused")]
    [InlineData(false, "zombie")]
    [InlineData(false, "dead")]
    [InlineData(true, "missing")]
    [InlineData(true, "reused")]
    [InlineData(true, "zombie")]
    public async Task ExactAndLegacyParentsWaitUntilTheObservedKernelIdentityEnds(bool legacy, string ending)
    {
        var state = "live";
        var reads = 0;
        UpdateParentProcess.LinuxIdentity? Read(int pid)
        {
            Assert.Equal(42, pid);
            Interlocked.Increment(ref reads);
            return Volatile.Read(ref state) switch
            {
                "missing" => null,
                "reused" => new(41642, 'S'),
                "zombie" => new(41641, 'Z'),
                "dead" => new(41641, 'X'),
                _ => new(41641, 'S'),
            };
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var wait = UpdateParentProcess.WaitLinuxAsync(42, legacy ? null : 41641, Read, timeout.Token);
        Assert.Equal(1, reads);
        Assert.False(wait.IsCompleted);
        Volatile.Write(ref state, ending);
        await wait;
        Assert.True(reads >= 2);
    }

    [Fact]
    public async Task DifferentBirthDoesNotWaitForAnUnrelatedReplacementProcess()
        => await UpdateParentProcess.WaitLinuxAsync(42, 41641, _ => new(41642, 'S'), default);

    [Fact]
    public async Task UnreadableIdentityCannotAuthorizeReplacement()
        => await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            UpdateParentProcess.WaitLinuxAsync(42, 41641, _ => throw new UnauthorizedAccessException(), default));

    [Fact]
    public async Task CancellationStopsTheLiveParentWaitAndDoesNotReadAfterPreCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = UpdateParentProcess.WaitLinuxAsync(42, 41641, _ => new(41641, 'S'), cancellation.Token);
        Assert.False(wait.IsCompleted);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateParentProcess.WaitLinuxAsync(42, 41641, _ => throw new InvalidOperationException("Must not read"), cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealLinuxParentWaitsDespiteDifferentUtcConversionUntilItExits(bool legacy)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var parent = ProcessParent.Start("idle");
        try
        {
            Assert.Equal(parent.Id.ToString(), await ProcessParent.ReadLineAsync(parent, TimeSpan.FromSeconds(10)));
            var identity = UpdateParentProcess.ReadLinuxIdentity(parent.Id)!.Value;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var wait = UpdateParentProcess.WaitAsync(parent.Id, parent.StartTime.ToUniversalTime().Ticks + 4268,
                legacy ? null : identity.StartClockTicks, timeout.Token);
            Assert.False(wait.IsCompleted);
            Assert.False(parent.HasExited);
            parent.StandardInput.Close();
            await wait;
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, parent.ExitCode);
        }
        finally { if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task WindowsRetainsExactUtcComparisonAndWaitsForTheMatchingLiveParent()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var parent = ProcessParent.Start("idle");
        try
        {
            Assert.Equal(parent.Id.ToString(), await ProcessParent.ReadLineAsync(parent, TimeSpan.FromSeconds(10)));
            var ticks = parent.StartTime.ToUniversalTime().Ticks;
            await UpdateParentProcess.WaitAsync(parent.Id, ticks + 1, null, default);
            Assert.False(parent.HasExited);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var wait = UpdateParentProcess.WaitAsync(parent.Id, ticks, null, timeout.Token);
            Assert.False(wait.IsCompleted);
            parent.StandardInput.Close();
            await wait;
            Assert.True(parent.HasExited);
            Assert.Equal(0, parent.ExitCode);
        }
        finally { if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); } }
    }
}
