using Winnow.App.Services;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class MergeSuggestionRefreshTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task Requests_are_serial_and_a_later_request_runs_again_after_the_first_snapshot()
    {
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var service = new MergeSuggestionRefresh(async ct =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task.WaitAsync(ct);
            }
            return SoftMatchSweepReport.Empty;
        });
        var first = service.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.RefreshAsync();
        Assert.False(second.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(0, service.Revision);

        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, calls);
        Assert.Equal(2, service.Revision);
    }

    [Fact]
    public async Task Failed_and_cancelled_passes_release_the_gate_without_advancing_revision()
    {
        var calls = 0;
        var entered = Signal();
        var service = new MergeSuggestionRefresh(async ct =>
        {
            switch (Interlocked.Increment(ref calls))
            {
                case 1: throw new InvalidOperationException("Synthetic failure");
                case 2:
                    entered.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    break;
            }
            return SoftMatchSweepReport.Empty;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync());
        Assert.Equal(0, service.Revision);
        using var stop = new CancellationTokenSource();
        var cancelled = service.RefreshAsync(stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(0, service.Revision);

        await service.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, service.Revision);
    }

    [Fact]
    public async Task Cancelling_a_waiter_does_not_cancel_another_surfaces_active_pass()
    {
        var entered = Signal();
        var release = Signal();
        var calls = 0;
        var service = new MergeSuggestionRefresh(async ct =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return SoftMatchSweepReport.Empty;
        });
        var active = service.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource();
        var waiter = service.RefreshAsync(stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(active.IsCompleted);
        release.SetResult();
        await active.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, calls);
        Assert.Equal(1, service.Revision);
    }

    [Fact]
    public async Task Synchronous_matching_work_is_dispatched_away_from_the_requesting_thread()
    {
        var requester = 0;
        var worker = 0;
        var service = new MergeSuggestionRefresh(_ =>
        {
            worker = Environment.CurrentManagedThreadId;
            return Task.FromResult(SoftMatchSweepReport.Empty with { Truncated = true });
        });
        var result = await Task.Factory.StartNew(() =>
        {
            requester = Environment.CurrentManagedThreadId;
            return service.RefreshAsync();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        Assert.NotEqual(requester, worker);
        Assert.True(result.Truncated);
        Assert.Equal(1, service.Revision);
    }
}
