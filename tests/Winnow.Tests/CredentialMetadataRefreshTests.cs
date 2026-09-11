using Winnow.App.Services;
using Xunit;

namespace Winnow.Tests;

public sealed class CredentialMetadataRefreshTests
{
    [Fact]
    public async Task ChangesWaitForStartupAndCoalesceIntoOnePass()
    {
        using var stop = new CancellationTokenSource();
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var queue = new CredentialMetadataRefresh(startup.Task, _ =>
        {
            Interlocked.Increment(ref calls);
            refreshed.SetResult();
            return Task.CompletedTask;
        }, _ => Assert.Fail("Refresh failed"), stop.Token);
        for (var i = 0; i < 10; i++) queue.Request();
        Assert.Equal(0, calls);
        startup.SetResult();
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await queue.Completion;
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChangesDuringPassQueueOneFollowupAndDoNotOverlap()
    {
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var queue = new CredentialMetadataRefresh(Task.CompletedTask, async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            else finished.SetResult();
        }, _ => Assert.Fail("Refresh failed"), stop.Token);
        queue.Request();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 10; i++) queue.Request();
        Assert.Equal(1, calls);
        release.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await queue.Completion;
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FailedPassDoesNotPreventLaterRefresh()
    {
        using var stop = new CancellationTokenSource();
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var success = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var queue = new CredentialMetadataRefresh(Task.CompletedTask, _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException();
            success.SetResult();
            return Task.CompletedTask;
        }, _ => failure.SetResult(), stop.Token);
        queue.Request();
        await failure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        queue.Request();
        await success.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await stop.CancelAsync();
        await queue.Completion;
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ShutdownDiscardsRefreshWaitingForStartup()
    {
        using var stop = new CancellationTokenSource();
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = new CredentialMetadataRefresh(startup.Task,
            _ => throw new InvalidOperationException("Must not run"),
            _ => Assert.Fail("Must not run"), stop.Token);
        queue.Request();
        await stop.CancelAsync();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
