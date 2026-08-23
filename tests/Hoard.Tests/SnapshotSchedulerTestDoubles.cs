using System.Threading.Channels;
using Hoard.App.Services;
using Hoard.Resolve;
using Microsoft.Extensions.Time.Testing;

namespace Hoard.Tests;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that says when the scheduler's
/// <see cref="PeriodicTimer"/> has been created.
///
/// <para>Needed because the host starts <c>ExecuteAsync</c> on the thread pool:
/// advancing the clock before the timer exists moves time past a tick that was
/// never scheduled, and the fake clock — unlike a real one — never comes back
/// round. Waiting on <see cref="TimerCreated"/> makes every advance land on a
/// timer that is actually armed, which is what keeps these tests from being
/// flaky rather than fast.</para>
/// </summary>
internal sealed class SchedulerClock : FakeTimeProvider
{
    private readonly TaskCompletionSource _timerCreated =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the scheduler has armed its periodic timer.</summary>
    public Task TimerCreated => _timerCreated.Task;

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _timerCreated.TrySetResult();
        return timer;
    }
}

/// <summary>
/// A stand-in for <see cref="SteamSyncService"/> that records when it was
/// called, how many calls were ever in flight at once, and lets a test hold a
/// call open to simulate a scan that overruns its interval.
///
/// <para>Every wait in these tests is on a channel written by this double, not
/// on wall-clock time: the <see cref="TimeSpan"/> below is a failure bound so a
/// broken scheduler fails instead of hanging, never a delay the passing path
/// pays.</para>
/// </summary>
internal sealed class FakeSteamSync : ISteamSync
{
    /// <summary>Only ever reached when an expected tick never happens.</summary>
    private static readonly TimeSpan FailureBound = TimeSpan.FromSeconds(20);

    private readonly Func<int, CancellationToken, Task<SteamSyncReport>> _body;
    private readonly Channel<int> _starts = Channel.CreateUnbounded<int>();
    private readonly Channel<int> _completions = Channel.CreateUnbounded<int>();
    private readonly TaskCompletionSource _overlapped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _calls;
    private int _inFlight;
    private int _maxInFlight;

    /// <param name="body">
    /// Runs in place of the real scan; receives the 1-based call ordinal.
    /// Defaults to an instant "nothing found" pass.
    /// </param>
    public FakeSteamSync(Func<int, CancellationToken, Task<SteamSyncReport>>? body = null)
        => _body = body ?? ((_, _) => Task.FromResult(NoChangeReport));

    /// <summary>What the real service returns for a machine with no Steam install.</summary>
    public static SteamSyncReport NoChangeReport { get; } =
        new(Candidates: 0, Result: null, Elapsed: TimeSpan.Zero);

    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Peak concurrent calls. Must stay 1: the scheduler is one sequential loop.</summary>
    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    /// <summary>Completes the instant a second call enters while one is still running.</summary>
    public Task Overlapped => _overlapped.Task;

    public async Task<SteamSyncReport> SyncAsync(CancellationToken ct = default)
    {
        var ordinal = Interlocked.Increment(ref _calls);
        var inFlight = Interlocked.Increment(ref _inFlight);
        if (inFlight > 1)
        {
            _overlapped.TrySetResult();
        }

        // Publish the high-water mark without a lock; the loop retries only
        // while some other call raised it in between.
        int seen;
        while (inFlight > (seen = Volatile.Read(ref _maxInFlight)))
        {
            Interlocked.CompareExchange(ref _maxInFlight, inFlight, seen);
        }

        _starts.Writer.TryWrite(ordinal);
        try
        {
            return await _body(ordinal, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            _completions.Writer.TryWrite(ordinal);
        }
    }

    /// <summary>Ordinal of the next call to enter <see cref="SyncAsync"/>.</summary>
    public Task<int> NextStartAsync() => NextAsync(_starts.Reader);

    /// <summary>Ordinal of the next call to leave <see cref="SyncAsync"/>, thrown or not.</summary>
    public Task<int> NextCompletionAsync() => NextAsync(_completions.Reader);

    private static async Task<int> NextAsync(ChannelReader<int> reader)
    {
        using var timeout = new CancellationTokenSource(FailureBound);
        return await reader.ReadAsync(timeout.Token);
    }
}
