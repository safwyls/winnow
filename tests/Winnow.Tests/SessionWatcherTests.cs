using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// §5.2 mechanism A end to end, on a real database, with a scripted process
/// source standing in for the OS. Every one of the four noise sources §5.2 names
/// has a test here, plus the two guarantees that make the design work at all:
/// the Tier 1 name filter running before any path resolution, and exit being
/// event-driven rather than polled.
///
/// <para>Nothing sleeps. Time moves only when a test moves it.</para>
/// </summary>
public sealed class SessionWatcherTests
{
    private static readonly DateTime T0 = SessionWatcherHarness.Origin;

    /// <summary>
    /// The one cost requirement in §5.2, and the reason
    /// <see cref="Winnow.Monitor.ProcessListing"/> carries no path. Three hundred
    /// processes are running and one of them is a game; the watcher must open
    /// exactly one.
    ///
    /// <para>A zero-cost assertion here rules the failure out rather than merely
    /// failing to observe it: the scripted source records every Tier 2 promotion,
    /// so "resolve everything then filter" would show up as 301 entries.</para>
    /// </summary>
    [Fact]
    public async Task Tier_1_resolves_a_path_only_for_processes_whose_name_is_already_a_known_game()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        for (var i = 0; i < 300; i++)
        {
            harness.Processes.Start(1000 + i, $"svchost{i}", $@"C:\Windows\System32\svchost{i}.exe", T0);
        }

        harness.Processes.Start(500, "enshrouded", game.Exe("enshrouded.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));

        Assert.Equal([(500, "enshrouded")], harness.Processes.TrackCalls);
    }

    /// <summary>
    /// "The interval governs when the app notices, not what it records." The
    /// game starts at T0 and is not discovered until T0+5s; the row still says
    /// T0, and the duration is the real one.
    /// </summary>
    [Fact]
    public async Task The_recorded_start_is_the_process_start_not_the_moment_of_discovery()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(100, T0.AddHours(2));
        await harness.TickAtAsync(T0.AddHours(2));
        var tick = await harness.TickAtAsync(T0.AddHours(2).AddSeconds(31));

        Assert.Equal(1, tick.Recorded);

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(T0.AddHours(2), session.EndedAt);
        Assert.Equal(7200, session.DurationSeconds);
        Assert.Equal(DetectionMethods.ProcessWatch, session.DetectionMethod);
    }

    /// <summary>
    /// §5.2: "Polling is for discovery only — never for exit detection." The
    /// scripted process counts reads of its exit state; one read at attach time
    /// is the documented race check, and a watcher that polled would show a
    /// count that grows with the tick count.
    /// </summary>
    [Fact]
    public async Task Exit_is_detected_by_the_event_and_never_by_polling()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        var process = harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        Assert.Equal(1, process.HasExitedReads);

        for (var i = 1; i <= 10; i++)
        {
            await harness.TickAtAsync(T0.AddSeconds(5 + (5 * i)));
        }

        Assert.Equal(1, process.HasExitedReads);

        // And the event alone is enough to complete the session.
        harness.Processes.Exit(100, T0.AddHours(1));
        await harness.TickAtAsync(T0.AddHours(1).AddSeconds(31));

        Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(1, process.HasExitedReads);
    }

    /// <summary>§5.2's debounce. Ten seconds of runtime is not a play session.</summary>
    [Fact]
    public async Task A_run_under_the_debounce_floor_is_not_recorded()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(100, T0.AddSeconds(10));
        var tick = await harness.TickAtAsync(T0.AddSeconds(45));

        Assert.Equal(1, tick.Debounced);
        Assert.Equal(0, tick.Recorded);
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));
    }

    [Fact]
    public async Task The_debounce_floor_is_configurable()
    {
        using var harness = new SessionWatcherHarness(o => o.MinimumSessionDuration = TimeSpan.FromSeconds(5));
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(2));

        harness.Processes.Exit(100, T0.AddSeconds(10));
        await harness.TickAtAsync(T0.AddSeconds(45));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(10, session.DurationSeconds);
    }

    /// <summary>
    /// §5.2 noise source 2: "Some games relaunch through a second executable
    /// (the first exits immediately)." This is Palworld's real shape — a shim at
    /// the install root that starts the Unreal shipping binary and dies. Per-pid
    /// sessions would write a three-second bounce and a ninety-minute session;
    /// there was one sitting.
    /// </summary>
    [Fact]
    public async Task A_game_that_relaunches_through_a_second_executable_records_one_session()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync(
            "Palworld", "Palworld.exe", "Pal/Binaries/Win64/Palworld-Win64-Shipping.exe");

        harness.Processes.Start(100, "Palworld", game.Exe("Palworld.exe"), T0);
        harness.Processes.Start(101, "Palworld-Win64-Shipping", game.Exe("Palworld-Win64-Shipping.exe"), T0.AddSeconds(3));
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(100, T0.AddSeconds(6));
        await harness.TickAtAsync(T0.AddSeconds(10));
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));

        harness.Processes.Exit(101, T0.AddMinutes(90));
        await harness.TickAtAsync(T0.AddMinutes(90).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(T0.AddMinutes(90), session.EndedAt);
    }

    /// <summary>
    /// §5.2 noise source 1: "Launchers spawn child processes that outlive or
    /// precede the game." The harder version of the previous test — the
    /// successor does not exist yet when the launcher dies, so only the relaunch
    /// grace holds the session open across the gap.
    /// </summary>
    [Fact]
    public async Task A_successor_that_appears_after_the_first_process_dies_joins_the_same_session()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("GameWithLauncher", "Launcher.exe", "bin/Game.exe");

        harness.Processes.Start(100, "Launcher", game.Exe("Launcher.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(100, T0.AddSeconds(8));
        await harness.TickAtAsync(T0.AddSeconds(10));

        // Twenty-five seconds later — inside the thirty-second grace — the real
        // game appears. Nothing has been written yet, so there is a session to
        // rejoin.
        harness.Processes.Start(101, "Game", game.Exe("Game.exe"), T0.AddSeconds(25));
        await harness.TickAtAsync(T0.AddSeconds(30));

        harness.Processes.Exit(101, T0.AddMinutes(45));
        await harness.TickAtAsync(T0.AddMinutes(45).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(T0.AddMinutes(45), session.EndedAt);
        Assert.Equal(2700, session.DurationSeconds);
    }

    /// <summary>
    /// The control for the previous test: with the grace cut below the poll
    /// interval the same handoff splits, the launcher's eight seconds are
    /// debounced away, and the surviving row claims the session began when the
    /// <i>second</i> executable did.
    ///
    /// <para>This is what the default grace is buying, and the shape of the bug
    /// that appears if someone tunes it down to "reduce latency": not a missing
    /// record, but a record that is quietly twenty-five seconds short — and, for
    /// a launcher that takes a minute to hand off, quietly a minute short on
    /// every session that game ever records.</para>
    /// </summary>
    [Fact]
    public async Task Cutting_the_relaunch_grace_below_the_poll_interval_splits_the_handoff()
    {
        using var harness = new SessionWatcherHarness(o => o.RelaunchGrace = TimeSpan.FromSeconds(1));
        var game = await harness.AddGameAsync("GameWithLauncher", "Launcher.exe", "bin/Game.exe");

        harness.Processes.Start(100, "Launcher", game.Exe("Launcher.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(100, T0.AddSeconds(8));
        await harness.TickAtAsync(T0.AddSeconds(10));

        harness.Processes.Start(101, "Game", game.Exe("Game.exe"), T0.AddSeconds(25));
        await harness.TickAtAsync(T0.AddSeconds(30));
        harness.Processes.Exit(101, T0.AddMinutes(45));
        await harness.TickAtAsync(T0.AddMinutes(45).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0.AddSeconds(25), session.StartedAt);
        Assert.Equal(2700 - 25, session.DurationSeconds);
    }

    /// <summary>
    /// The other side of the grace window: a gap wider than the handoff window
    /// is two sittings, and merging them would be as wrong as splitting the
    /// handoff was.
    /// </summary>
    [Fact]
    public async Task A_relaunch_after_the_grace_window_records_a_second_session()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(100, T0.AddMinutes(70));
        await harness.TickAtAsync(T0.AddMinutes(70).AddSeconds(31));

        harness.Processes.Start(101, "enshrouded", game.Exe("enshrouded.exe"), T0.AddMinutes(80));
        await harness.TickAtAsync(T0.AddMinutes(80).AddSeconds(5));
        harness.Processes.Exit(101, T0.AddMinutes(140));
        await harness.TickAtAsync(T0.AddMinutes(140).AddSeconds(31));

        var sessions = await harness.SessionsForAsync(game.OwnershipId);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(T0, sessions[0].StartedAt);
        Assert.Equal(4200, sessions[0].DurationSeconds);
        Assert.Equal(T0.AddMinutes(80), sessions[1].StartedAt);
        Assert.Equal(3600, sessions[1].DurationSeconds);
    }

    /// <summary>
    /// §5.2 noise source 3: "Proton/Wine wraps everything in a process tree;
    /// match on the tree, not a single PID." Grouping by the install directory
    /// every member runs from is a stronger join than a parent pid and survives
    /// re-parenting — and it needs no parent-pid lookup at all.
    /// </summary>
    [Fact]
    public async Task A_process_tree_records_one_session_spanning_the_whole_tree()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync(
            "TreeGame", "wrapper.exe", "bin/game.exe", "bin/helper.exe");

        harness.Processes.Start(100, "wrapper", game.Exe("wrapper.exe"), T0);
        harness.Processes.Start(101, "game", game.Exe("game.exe"), T0.AddSeconds(2));
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Start(102, "helper", game.Exe("helper.exe"), T0.AddSeconds(7));
        await harness.TickAtAsync(T0.AddSeconds(10));

        // The children go first, in a different order than they started.
        harness.Processes.Exit(101, T0.AddMinutes(30));
        harness.Processes.Exit(102, T0.AddMinutes(31));
        await harness.TickAtAsync(T0.AddMinutes(31).AddSeconds(35));

        // The wrapper outlives them both — no session yet, it is still running.
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));

        harness.Processes.Exit(100, T0.AddMinutes(32));
        await harness.TickAtAsync(T0.AddMinutes(32).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(T0.AddMinutes(32), session.EndedAt);
    }

    /// <summary>
    /// The pid-reuse case. The scripted source refuses to recycle a pid whose
    /// handle the watcher still holds — modelling the OS guarantee that makes
    /// the retained handle worth holding — so reaching the second launch at all
    /// proves the first handle was released, and the two sittings must stay two
    /// rows with their own start times.
    /// </summary>
    [Fact]
    public async Task A_recycled_pid_starts_a_new_session_rather_than_extending_the_old_one()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        var first = harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(100, T0.AddMinutes(70));
        await harness.TickAtAsync(T0.AddMinutes(70).AddSeconds(31));

        Assert.True(first.Disposed);

        // The OS hands pid 100 to a fresh launch of the same game three hours
        // later. Same number, different process, different session.
        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0.AddHours(3));
        await harness.TickAtAsync(T0.AddHours(3).AddSeconds(5));
        harness.Processes.Exit(100, T0.AddHours(4));
        await harness.TickAtAsync(T0.AddHours(4).AddSeconds(31));

        var sessions = await harness.SessionsForAsync(game.OwnershipId);
        Assert.Equal(2, sessions.Count);
        Assert.Equal(T0, sessions[0].StartedAt);
        Assert.Equal(T0.AddHours(3), sessions[1].StartedAt);
        Assert.Equal(3600, sessions[1].DurationSeconds);
    }

    /// <summary>
    /// A pid the watcher is still tracking is never re-promoted to Tier 2, so a
    /// long session costs one <c>Track</c> and not one per poll.
    /// </summary>
    [Fact]
    public async Task A_process_already_being_tracked_is_not_re_opened_on_every_poll()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        for (var i = 1; i <= 20; i++)
        {
            await harness.TickAtAsync(T0.AddSeconds(5 * i));
        }

        Assert.Single(harness.Processes.TrackCalls);
    }

    /// <summary>
    /// A process whose name is in the Tier 1 set but which runs from outside
    /// every install directory: ignored, and — because the resolution is cached
    /// — resolved once rather than on every poll for as long as it runs.
    /// </summary>
    [Fact]
    public async Task A_same_named_process_from_elsewhere_is_ignored_and_resolved_only_once()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", harness.ElsewherePath("enshrouded.exe"), T0);
        for (var i = 1; i <= 5; i++)
        {
            await harness.TickAtAsync(T0.AddSeconds(5 * i));
        }

        Assert.Single(harness.Processes.TrackCalls);

        harness.Processes.Exit(100, T0.AddHours(3));
        await harness.TickAtAsync(T0.AddHours(3).AddSeconds(31));

        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));
    }

    /// <summary>
    /// A game that was already running before Winnow started records the truth,
    /// not the moment the watcher first saw it. Same guarantee as discovery
    /// latency, at a much larger scale — this is the difference between a
    /// six-hour session and a ten-minute one.
    /// </summary>
    [Fact]
    public async Task A_game_already_running_when_the_watcher_starts_records_its_true_start()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        var startedLongAgo = T0.AddHours(-6);
        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), startedLongAgo);

        await harness.TickAtAsync(T0);
        harness.Processes.Exit(100, T0.AddMinutes(10));
        await harness.TickAtAsync(T0.AddMinutes(10).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(startedLongAgo, session.StartedAt);
        Assert.Equal(6 * 3600 + 600, session.DurationSeconds);
    }

    [Fact]
    public async Task Two_games_running_at_once_record_two_sessions()
    {
        using var harness = new SessionWatcherHarness();
        var one = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");
        var two = await harness.AddGameAsync("Palworld", "Palworld.exe");

        harness.Processes.Start(100, "enshrouded", one.Exe("enshrouded.exe"), T0);
        harness.Processes.Start(101, "Palworld", two.Exe("Palworld.exe"), T0.AddMinutes(1));
        await harness.TickAtAsync(T0.AddMinutes(1).AddSeconds(5));

        harness.Processes.Exit(100, T0.AddMinutes(40));
        harness.Processes.Exit(101, T0.AddMinutes(50));
        await harness.TickAtAsync(T0.AddMinutes(50).AddSeconds(31));

        Assert.Equal(2400, Assert.Single(await harness.SessionsForAsync(one.OwnershipId)).DurationSeconds);
        Assert.Equal(2940, Assert.Single(await harness.SessionsForAsync(two.OwnershipId)).DurationSeconds);
    }

    /// <summary>
    /// A game installed while Winnow is resident becomes watchable at the next
    /// index rebuild, without a restart.
    /// </summary>
    [Fact]
    public async Task A_game_installed_after_startup_becomes_watchable_at_the_next_index_refresh()
    {
        using var harness = new SessionWatcherHarness();
        await harness.TickAtAsync(T0);

        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");
        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0.AddMinutes(1));

        // Before the rebuild the name is not in the Tier 1 set, so the process
        // is not even looked at.
        await harness.TickAtAsync(T0.AddMinutes(2));
        Assert.Empty(harness.Processes.TrackCalls);

        await harness.TickAtAsync(T0.AddMinutes(16));
        Assert.Single(harness.Processes.TrackCalls);

        harness.Processes.Exit(100, T0.AddMinutes(90));
        await harness.TickAtAsync(T0.AddMinutes(90).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));

        // Discovered fifteen minutes late; still recorded from its real start.
        Assert.Equal(T0.AddMinutes(1), session.StartedAt);
    }

    /// <summary>
    /// Shutdown with a game still running. See <c>SessionWatcher.FlushAsync</c>
    /// for why the row has no end rather than an invented one.
    /// </summary>
    [Fact]
    public async Task Shutdown_writes_an_in_flight_session_with_no_end_time()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Clock.SetUtcNow(T0.AddHours(2));
        Assert.Equal(1, await harness.Watcher.FlushAsync());

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);

        // The game did not stop when Winnow did. "Ended at 20:00" would be a
        // falsehood; null is the fact.
        Assert.Null(session.EndedAt);
        Assert.Null(session.DurationSeconds);
        Assert.Equal(DetectionMethods.ProcessWatch, session.DetectionMethod);
    }

    [Fact]
    public async Task Shutdown_drops_an_in_flight_run_still_under_the_debounce_floor()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Clock.SetUtcNow(T0.AddSeconds(30));
        Assert.Equal(0, await harness.Watcher.FlushAsync());
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));
    }

    /// <summary>
    /// Shutdown while a session is waiting out its relaunch grace: it already
    /// has a real end time, so it is written as a normal closed session.
    /// </summary>
    [Fact]
    public async Task Shutdown_finalises_a_session_that_is_only_waiting_out_its_grace()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(100, T0.AddMinutes(45));
        await harness.TickAtAsync(T0.AddMinutes(45).AddSeconds(5));

        harness.Clock.SetUtcNow(T0.AddMinutes(45).AddSeconds(10));
        Assert.Equal(1, await harness.Watcher.FlushAsync());

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0.AddMinutes(45), session.EndedAt);
        Assert.Equal(2700, session.DurationSeconds);
    }

    /// <summary>
    /// A clock that moved backwards between the OS recording the start and
    /// recording the exit. There is no honest repair, the schema's CHECK would
    /// reject the row anyway, and a negative duration in the history would be
    /// worse than a missing one.
    /// </summary>
    [Fact]
    public async Task A_session_that_ends_before_it_starts_is_discarded_rather_than_written()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(100, T0.AddHours(-1));
        await harness.TickAtAsync(T0.AddSeconds(10));

        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));
    }

    /// <summary>
    /// The handle is released once the game is gone — held long enough to pin
    /// the pid for the life of the session, not a moment longer.
    /// </summary>
    [Fact]
    public async Task Handles_are_released_once_the_process_has_exited()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        var process = harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        Assert.False(process.Disposed);

        harness.Processes.Exit(100, T0.AddHours(1));
        await harness.TickAtAsync(T0.AddHours(1).AddSeconds(5));

        Assert.True(process.Disposed);
    }

    /// <summary>
    /// Disposing the watcher with games running releases every handle. A leaked
    /// process handle would pin its pid for the life of the app.
    /// </summary>
    [Fact]
    public async Task Disposing_the_watcher_releases_every_tracked_handle()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("TreeGame", "wrapper.exe", "bin/game.exe");

        var wrapper = harness.Processes.Start(100, "wrapper", game.Exe("wrapper.exe"), T0);
        var inner = harness.Processes.Start(101, "game", game.Exe("game.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Watcher.Dispose();

        Assert.True(wrapper.Disposed);
        Assert.True(inner.Disposed);
    }

    /// <summary>
    /// A machine with nothing installed: the poll matches nothing and opens
    /// nothing, which is what makes registering the watcher unconditionally safe.
    /// </summary>
    [Fact]
    public async Task A_library_with_no_installed_games_opens_no_handles()
    {
        using var harness = new SessionWatcherHarness();

        for (var i = 0; i < 50; i++)
        {
            harness.Processes.Start(1000 + i, $"proc{i}", $@"C:\Windows\proc{i}.exe", T0);
        }

        var tick = await harness.TickAtAsync(T0.AddSeconds(5));

        Assert.Empty(harness.Processes.TrackCalls);
        Assert.Equal(0, tick.Started);
    }

    // ---------------------------------------------------------------------
    // Regressions. Each of these is a race or a failure path that shipped in
    // the first cut of this module and was caught in review — which is what the
    // scripted process source exists to make reachable from a test.
    // ---------------------------------------------------------------------

    /// <summary>
    /// The exit callback runs on a thread pool thread, so anything it throws is
    /// unhandled and takes the whole app down — a lost session would have been
    /// the mild outcome. The window: an OS callback whose invocation is already
    /// committed, and a shutdown that disposes the process before the handler
    /// reads its pid. <c>Process.Id</c> throws after <c>Close()</c>, and the
    /// handler read it first thing.
    ///
    /// <para>The scripted process models both halves — <c>Pid</c> throws once
    /// disposed, and <see cref="FakeProcess.WhileExitIsInFlight"/> is precisely
    /// the instant between capturing the handler list and running it.</para>
    /// </summary>
    [Fact]
    public async Task An_exit_callback_racing_disposal_does_not_throw()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        var process = harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        // The app closes at the same moment the game exits.
        process.WhileExitIsInFlight = harness.Watcher.Dispose;

        harness.Clock.SetUtcNow(T0.AddHours(1));
        harness.Processes.Exit(100, T0.AddHours(1));

        // Reaching this line at all is the assertion: the handler ran against a
        // process the watcher had already disposed.
        Assert.True(process.Disposed);
        Assert.Throws<InvalidOperationException>(() => process.Pid);
    }

    /// <summary>
    /// A long-lived executable under the install root — a resident updater, a
    /// dedicated server, a tool the user parked in the game folder — must not be
    /// able to drag the session start back to whenever <i>it</i> started.
    ///
    /// <para>Unbounded, this writes a row claiming the sitting began days ago,
    /// which is indistinguishable from a real week-long session and poisons
    /// every duration statistic built on the table. Bounded by the relaunch
    /// grace, the worst it can do is the width of the handoff window it already
    /// shares with legitimate successors.</para>
    /// </summary>
    [Fact]
    public async Task A_stranger_joining_a_later_pass_cannot_drag_the_session_start_backwards()
    {
        using var harness = new SessionWatcherHarness();
        // At the install root, deliberately: "tools/" is one of the pruned
        // subtrees, so an updater parked there would never be indexed and this
        // test would pass without exercising anything.
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe", "updater.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        // Five days old, and only matched now — an index rebuild picked it up,
        // or it simply had not been looked at before.
        harness.Processes.Start(101, "updater", game.Exe("updater.exe"), T0.AddDays(-5));
        await harness.TickAtAsync(T0.AddSeconds(10));

        harness.Processes.Exit(101, T0.AddSeconds(20));
        harness.Processes.Exit(100, T0.AddMinutes(50));
        await harness.TickAtAsync(T0.AddMinutes(50).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));

        // Pulled back by the grace, and not one second further.
        Assert.Equal(T0 - TimeSpan.FromSeconds(30), session.StartedAt);
        Assert.Equal(3000 + 30, session.DurationSeconds);
    }

    /// <summary>
    /// The control for the previous test: inside the pass that <i>opened</i> the
    /// session there is no clamp, because everything seen together in that first
    /// sweep is one launch. This is what keeps a launcher that started well
    /// before the game — and a whole tree already running when Winnow started —
    /// recording their real beginning.
    /// </summary>
    [Fact]
    public async Task Processes_found_together_in_the_opening_pass_still_take_the_earliest_start()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("GameWithLauncher", "Launcher.exe", "bin/Game.exe");

        harness.Processes.Start(100, "Launcher", game.Exe("Launcher.exe"), T0.AddHours(-2));
        harness.Processes.Start(101, "Game", game.Exe("Game.exe"), T0.AddHours(-1));
        await harness.TickAtAsync(T0);

        harness.Processes.Exit(100, T0.AddMinutes(1));
        harness.Processes.Exit(101, T0.AddMinutes(2));
        await harness.TickAtAsync(T0.AddMinutes(2).AddSeconds(31));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0.AddHours(-2), session.StartedAt);
    }

    /// <summary>
    /// A finished session survives a failed write. Finalising removes it from
    /// the tracking state, so before the write queue existed an insert that threw
    /// lost that sitting permanently — and SQLite has one writer, which the
    /// snapshot scheduler is also using.
    /// </summary>
    [Fact]
    public async Task A_session_whose_write_fails_is_retried_on_the_next_tick()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(100, T0.AddMinutes(50));

        harness.SessionWrites.FailNextInserts = 1;
        var failed = await harness.TickAtAsync(T0.AddMinutes(50).AddSeconds(31));

        Assert.Equal(0, failed.Recorded);
        Assert.Equal(1, failed.Queued);
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));

        var retried = await harness.TickAtAsync(T0.AddMinutes(50).AddSeconds(36));

        Assert.Equal(1, retried.Recorded);
        Assert.Equal(0, retried.Queued);

        // Retried, not re-derived: the timestamps are the ones observed at exit.
        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(T0, session.StartedAt);
        Assert.Equal(T0.AddMinutes(50), session.EndedAt);
    }

    /// <summary>
    /// The reachable-in-normal-operation version of the same bug: a host stop
    /// landing between reconciliation and the first insert. The tick must leave
    /// the session queued rather than throw it away, and the shutdown flush —
    /// which runs on its own token — must be able to pick it up.
    /// </summary>
    [Fact]
    public async Task A_session_finalised_as_the_host_stops_is_written_by_the_flush()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        harness.Processes.Start(100, "enshrouded", game.Exe("enshrouded.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(100, T0.AddSeconds(70));

        using var stopping = new CancellationTokenSource();
        await stopping.CancelAsync();

        // Inside the index-refresh interval on purpose: a tick that also
        // rebuilds the index cancels in the rebuild, before anything has been
        // finalised, which is a different (and harmless) path.
        harness.Clock.SetUtcNow(T0.AddSeconds(101));
        var tick = await harness.Watcher.TickAsync(stopping.Token);

        Assert.Equal(0, tick.Recorded);
        Assert.Equal(1, tick.Queued);
        Assert.Empty(await harness.SessionsForAsync(game.OwnershipId));

        Assert.Equal(1, await harness.Watcher.FlushAsync());

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(70, session.DurationSeconds);
        Assert.Equal(0, harness.Watcher.PendingCount);
    }

    /// <summary>
    /// A failed write must not take the sessions queued behind it down too —
    /// they are independent sittings, and only the head of the queue is in
    /// question.
    /// </summary>
    [Fact]
    public async Task A_failed_write_does_not_discard_the_sessions_queued_behind_it()
    {
        using var harness = new SessionWatcherHarness();
        var one = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");
        var two = await harness.AddGameAsync("Palworld", "Palworld.exe");

        harness.Processes.Start(100, "enshrouded", one.Exe("enshrouded.exe"), T0);
        harness.Processes.Start(101, "Palworld", two.Exe("Palworld.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(100, T0.AddMinutes(40));
        harness.Processes.Exit(101, T0.AddMinutes(41));

        harness.SessionWrites.FailNextInserts = 1;
        var failed = await harness.TickAtAsync(T0.AddMinutes(41).AddSeconds(31));

        Assert.Equal(0, failed.Recorded);
        Assert.Equal(2, failed.Queued);

        var retried = await harness.TickAtAsync(T0.AddMinutes(41).AddSeconds(36));
        Assert.Equal(2, retried.Recorded);
        Assert.Equal(0, retried.Queued);

        Assert.Single(await harness.SessionsForAsync(one.OwnershipId));
        Assert.Single(await harness.SessionsForAsync(two.OwnershipId));
    }
}
