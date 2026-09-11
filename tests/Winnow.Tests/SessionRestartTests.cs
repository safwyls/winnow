using Winnow.Core.Domain;
using Winnow.Data.Repositories;
using Winnow.Recommend;
using Xunit;

namespace Winnow.Tests;

public sealed class SessionRestartTests
{
    private static readonly DateTime T0 = SessionWatcherHarness.Origin;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restart_during_play_completes_one_sitting_and_preserves_note_and_launch_attribution(bool flush)
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Game", "game.exe");
        Assert.True(harness.Declare(game.OwnershipId));
        harness.Processes.Start(100, "game", game.Exe("game.exe"), T0);
        var announcements = new List<Session>();
        harness.Watcher.SessionRecorded += (_, s) => announcements.Add(s);
        await harness.TickAtAsync(T0.AddSeconds(5));
        await harness.TickAtAsync(T0.AddMinutes(2));
        var checkpoint = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Null(checkpoint.EndedAt);
        Assert.Null(checkpoint.DurationSeconds);
        await harness.Sessions.SetNoteAsync(new SessionNote { SessionId = checkpoint.Id, Note = "Keep this note", Rating = 4 });
        await harness.RestartAsync(flush);
        Assert.Empty(announcements);
        harness.Watcher.SessionRecorded += (_, s) => announcements.Add(s);
        await harness.TickAtAsync(T0.AddMinutes(5));
        harness.Processes.Exit(100, T0.AddMinutes(10));
        await harness.TickAtAsync(T0.AddMinutes(11));
        var completed = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(checkpoint.Id, completed.Id);
        Assert.Equal(checkpoint.MonitorKey, completed.MonitorKey);
        Assert.Equal(T0, completed.StartedAt);
        Assert.Equal(600, completed.DurationSeconds);
        Assert.Equal(SessionAttributions.Launch, completed.AttributedBy);
        Assert.Equal(completed, Assert.Single(announcements));
        Assert.Equal("Keep this note", (await harness.Sessions.GetNoteAsync(completed.Id))!.Note);
        Assert.Equal(completed.Id, Assert.Single(await harness.Sessions.GetJournalEntriesByOwnershipAsync(game.OwnershipId)).SessionId);
        var stats = new LibraryHistoryStatsRepository(harness.Factory);
        Assert.Equal(1, (await stats.GetAsync()).SessionCount);
        var engine = new RecommendationEngine(new LibraryQueryRepository(harness.Factory),
            new PlaytimeSnapshotRepository(harness.Factory), harness.Sessions,
            new UpdateEventRepository(harness.Factory), new FacetRepository(harness.Factory), stats);
        var feed = await engine.GetFeedAsync(new RecommendationRequest
        {
            AsOfUtc = T0.AddMinutes(11),
            Tuning = RecommendationTuning.Default with { Tier2MinSessions = 2, Tier2MinSpanDays = 0 },
        });
        Assert.Equal(DataTier.Settling, feed.Tier);
    }

    [Fact]
    public async Task A_persisted_young_child_recovers_the_older_sitting_before_debounce()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Tree", "wrapper.exe", "child.exe");
        harness.Processes.Start(100, "wrapper", game.Exe("wrapper.exe"), T0);
        await harness.TickAtAsync(T0.AddMinutes(2));
        harness.Processes.Start(101, "child", game.Exe("child.exe"), T0.AddMinutes(9));
        await harness.TickAtAsync(T0.AddMinutes(9).AddSeconds(5));
        harness.Processes.Exit(100, T0.AddMinutes(9).AddSeconds(6));
        await harness.RestartAsync(flush: false);
        await harness.TickAtAsync(T0.AddMinutes(9).AddSeconds(10));
        harness.Processes.Exit(101, T0.AddMinutes(9).AddSeconds(20));
        await harness.TickAtAsync(T0.AddMinutes(10));
        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(560, session.DurationSeconds);
    }

    [Fact]
    public async Task An_exit_while_Winnow_is_closed_stays_unknown_and_a_reused_pid_opens_another_sitting()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Game", "game.exe");
        harness.Processes.Start(100, "game", game.Exe("game.exe"), T0);
        await harness.TickAtAsync(T0.AddMinutes(2));
        var old = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        await harness.RestartAsync(flush: false);
        harness.Processes.Exit(100, T0.AddMinutes(3));
        await harness.TickAtAsync(T0.AddMinutes(4));
        Assert.Equal(old, Assert.Single(await harness.SessionsForAsync(game.OwnershipId)));
        harness.Processes.Start(100, "game", game.Exe("game.exe"), T0.AddMinutes(5));
        await harness.TickAtAsync(T0.AddMinutes(7));
        harness.Processes.Exit(100, T0.AddMinutes(8));
        await harness.TickAtAsync(T0.AddMinutes(9));
        var rows = await harness.SessionsForAsync(game.OwnershipId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(old, rows[0]);
        Assert.Equal(180, rows[1].DurationSeconds);
        Assert.NotEqual(old.MonitorKey, rows[1].MonitorKey);
    }

    [Fact]
    public async Task Checkpoints_obey_the_floor_and_do_not_write_on_every_unchanged_tick()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Game", "game.exe");
        harness.Processes.Start(100, "game", game.Exe("game.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(59));
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));
        await harness.TickAtAsync(T0.AddSeconds(60));
        Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        var writes = harness.SessionWrites.InsertAttempts;
        await harness.TickAtAsync(T0.AddSeconds(65));
        await harness.TickAtAsync(T0.AddMinutes(2));
        Assert.Equal(writes, harness.SessionWrites.InsertAttempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lost_write_response_retries_the_same_row_for_checkpoint_and_completion(bool checkpoint)
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Game", "game.exe");
        harness.Processes.Start(100, "game", game.Exe("game.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        if (!checkpoint) harness.Processes.Exit(100, T0.AddMinutes(2));
        harness.SessionWrites.FailAfterCommit = 1;
        await harness.TickAtAsync(T0.AddMinutes(3));
        var first = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        await harness.TickAtAsync(T0.AddMinutes(4));
        Assert.Equal(first, Assert.Single(await harness.SessionsForAsync(game.OwnershipId)));
        Assert.Equal(0, harness.Watcher.PendingCount);
    }

    [Fact]
    public async Task Failed_recovery_defers_finalization_until_the_original_sitting_is_known()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Game", "game.exe");
        harness.Processes.Start(100, "game", game.Exe("game.exe"), T0);
        await harness.TickAtAsync(T0.AddMinutes(2));
        var first = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        await harness.RestartAsync(flush: false);
        harness.SessionWrites.FailRecoveryReads = 2;
        await harness.TickAtAsync(T0.AddMinutes(3));
        harness.Processes.Exit(100, T0.AddMinutes(4));
        await harness.TickAtAsync(T0.AddMinutes(5));
        Assert.Equal(first, Assert.Single(await harness.SessionsForAsync(game.OwnershipId)));
        await harness.TickAtAsync(T0.AddMinutes(6));
        var completed = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(first.Id, completed.Id);
        Assert.Equal(240, completed.DurationSeconds);
    }
}
