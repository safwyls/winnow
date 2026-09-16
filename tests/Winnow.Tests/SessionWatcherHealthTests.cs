using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Winnow.Monitor;
using Xunit;

namespace Winnow.Tests;

public sealed class SessionWatcherHealthTests
{
    [Fact]
    public void Failures_are_independent_and_repeated_logs_are_bounded()
    {
        var time = new FakeTimeProvider();
        var logger = new HealthLogger();
        var health = new SessionWatcherHealth(logger, time);
        var changed = 0;
        health.Changed += (_, _) => changed++;
        health.Changed += (_, _) => throw new InvalidOperationException("Bad subscriber");
        health.ReportFailure(SessionWatcherOperation.ExecutableIndex, new InvalidCastException());
        health.ReportFailure(SessionWatcherOperation.ExecutableIndex, new InvalidCastException());
        health.ReportFailure(SessionWatcherOperation.SessionPersistence, new IOException());
        health.ReportSuccess(SessionWatcherOperation.Tick);
        Assert.Equal(2, health.Failures.Count);
        Assert.Equal(2, logger.Warnings);
        time.Advance(TimeSpan.FromMinutes(5));
        health.ReportFailure(SessionWatcherOperation.ExecutableIndex, new InvalidCastException());
        Assert.Equal(3, logger.Warnings);
        health.ReportSuccess(SessionWatcherOperation.ExecutableIndex);
        Assert.True(health.HasFailures);
        health.ReportSuccess(SessionWatcherOperation.SessionPersistence);
        Assert.False(health.HasFailures);
        Assert.Equal(2, logger.Recoveries);
        Assert.Equal(6, changed);
    }

    [Fact]
    public async Task Failed_refresh_retains_index_and_tracking_while_retry_is_delayed()
    {
        using var h = new SessionWatcherHarness(o => o.IndexRefreshInterval = TimeSpan.FromSeconds(10));
        var game = await h.AddGameAsync("Game", "game.exe");
        await h.TickAtAsync(SessionWatcherHarness.Origin);
        var index = h.Watcher.Index;
        h.IndexReads.FailReads = true;
        h.Processes.Start(12, "game", game.Exe("game.exe"), SessionWatcherHarness.Origin);
        var failed = await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(15));
        Assert.Equal(1, failed.Started);
        Assert.Same(index, h.Watcher.Index);
        Assert.True(h.Watcher.Health.HasFailures);
        h.IndexReads.FailReads = false;
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(20));
        Assert.Equal(2, h.IndexReads.ReadAttempts);
        Assert.True(h.Watcher.Health.HasFailures);
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(75));
        Assert.Equal(3, h.IndexReads.ReadAttempts);
        Assert.False(h.Watcher.Health.HasFailures);
    }

    [Fact]
    public async Task Initial_index_failure_recovers_and_cancellation_is_not_a_failure()
    {
        using var h = new SessionWatcherHarness();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Watcher.TickAsync(cancellation.Token));
        Assert.False(h.Watcher.Health.HasFailures);
        h.IndexReads.FailReads = true;
        await h.Watcher.TickAsync();
        Assert.True(h.Watcher.Health.HasFailures);
        h.IndexReads.FailReads = false;
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddMinutes(1));
        Assert.False(h.Watcher.Health.HasFailures);
    }

    [Fact]
    public async Task Recovery_and_checkpoint_failure_clear_only_after_successful_retries()
    {
        using var h = new SessionWatcherHarness();
        var game = await h.AddGameAsync("Game", "game.exe");
        h.Processes.Start(12, "game", game.Exe("game.exe"), SessionWatcherHarness.Origin);
        h.SessionWrites.FailRecoveryReads = 1;
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(65));
        Assert.Equal(SessionWatcherOperation.SessionRecovery, Assert.Single(h.Watcher.Health.Failures).Operation);
        h.SessionWrites.FailNextInserts = 1;
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(70));
        Assert.Equal(SessionWatcherOperation.SessionPersistence, Assert.Single(h.Watcher.Health.Failures).Operation);
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(75));
        Assert.False(h.Watcher.Health.HasFailures);
        Assert.Single(await h.SessionsForAsync(game.OwnershipId));
    }

    [Fact]
    public async Task Cancelled_pending_write_keeps_existing_failure_and_queued_session()
    {
        using var h = new SessionWatcherHarness();
        var game = await h.AddGameAsync("Game", "game.exe");
        h.Processes.Start(12, "game", game.Exe("game.exe"), SessionWatcherHarness.Origin);
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(5));
        h.Processes.Exit(12, SessionWatcherHarness.Origin.AddSeconds(70));
        h.SessionWrites.FailNextInserts = 1;
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(105));
        Assert.True(h.Watcher.Health.HasFailures);
        using var cancellation = new CancellationTokenSource();
        h.SessionWrites.BeforeSave = cancellation.Cancel;
        await h.Watcher.TickAsync(cancellation.Token);
        Assert.True(h.Watcher.Health.HasFailures);
        Assert.Equal(1, h.Watcher.PendingCount);
        Assert.Equal(1, Assert.Single(h.Watcher.Health.Failures).FailureCount);
        h.SessionWrites.BeforeSave = null;
        await h.Watcher.TickAsync();
        Assert.False(h.Watcher.Health.HasFailures);
        Assert.Equal(0, h.Watcher.PendingCount);
    }

    [Fact]
    public async Task Discovery_failure_keeps_recording_exits_and_recovers_on_successful_enumeration()
    {
        using var h = new SessionWatcherHarness();
        var game = await h.AddGameAsync("Game", "game.exe");
        h.Processes.Start(12, "game", game.Exe("game.exe"), SessionWatcherHarness.Origin);
        await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(5));
        h.Processes.FailEnumeration = true;
        h.Processes.Exit(12, SessionWatcherHarness.Origin.AddSeconds(70));
        var tick = await h.TickAtAsync(SessionWatcherHarness.Origin.AddSeconds(105));
        Assert.Equal(1, tick.Recorded);
        Assert.Equal(SessionWatcherOperation.ProcessDiscovery, Assert.Single(h.Watcher.Health.Failures).Operation);
        h.Processes.FailEnumeration = false;
        await h.Watcher.TickAsync();
        Assert.False(h.Watcher.Health.HasFailures);
    }

    private sealed class HealthLogger : ILogger<SessionWatcherHealth>
    {
        public int Warnings { get; private set; }
        public int Recoveries { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings++;
            if (logLevel == LogLevel.Information) Recoveries++;
        }
    }
}
