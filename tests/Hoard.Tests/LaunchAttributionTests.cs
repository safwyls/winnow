using Hoard.Core.Domain;
using Hoard.Monitor;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// M3b's attribution seam, on the same harness §5.2's watcher is tested on: a
/// real database, real install directories, a scripted process source and a
/// clock that only moves when a test moves it. <b>No game is started and none is
/// needed</b> — which is the point of M3a having put process enumeration behind
/// an interface in the first place.
///
/// <para>The claim under test is narrow and worth stating exactly. A declared
/// launch does not make the watcher believe more things; it makes the watcher
/// stop guessing about ONE thing it was already looking at. Every test below is
/// either a case inference could not settle and a launch can, or a case a launch
/// must NOT be allowed to settle.</para>
/// </summary>
public sealed class LaunchAttributionTests
{
    private static readonly DateTime T0 = SessionWatcherHarness.Origin;

    /// <summary>
    /// The case §5.2 is honest about and M3a could not fix. Two owned games ship
    /// an executable with the same name — engines do this constantly — and the
    /// running one is outside both install roots, so there is no path evidence.
    /// The watcher's name fallback needs the name to belong to exactly one game
    /// and gives up; a declared launch says which one it is.
    /// </summary>
    [Fact]
    public async Task A_declared_launch_settles_a_name_two_owned_games_share()
    {
        using var harness = new SessionWatcherHarness();
        var mine = await harness.AddGameAsync("Bluebird", "Game.exe");
        await harness.AddGameAsync("Redwing", "Game.exe");

        harness.Declare(mine.OwnershipId);

        // Running from somewhere neither install root contains: a shortcut into
        // a shared runtime directory, an anti-cheat re-exec, a game that copies
        // itself out to run. Path evidence exists and is useless.
        harness.Processes.Start(700, "Game", harness.ElsewherePath("Game.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(700, T0.AddMinutes(30));
        await harness.TickAtAsync(T0.AddMinutes(31));

        var session = Assert.Single(await harness.SessionsForAsync(mine.OwnershipId));
        Assert.Equal(SessionAttributions.Launch, session.AttributedBy);
        Assert.Equal(DetectionMethods.ProcessWatch, session.DetectionMethod);
    }

    /// <summary>
    /// The same situation with nobody having clicked Play in Hoard: still
    /// ambiguous, still unattributed. This is the M3a behaviour the seam is
    /// measured against, and it is here so the previous test cannot pass for
    /// some reason other than the intent.
    /// </summary>
    [Fact]
    public async Task Without_a_launch_the_shared_name_is_still_nobodys()
    {
        using var harness = new SessionWatcherHarness();
        var mine = await harness.AddGameAsync("Bluebird", "Game.exe");
        var other = await harness.AddGameAsync("Redwing", "Game.exe");

        harness.Processes.Start(700, "Game", harness.ElsewherePath("Game.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(700, T0.AddMinutes(30));
        await harness.TickAtAsync(T0.AddMinutes(31));

        Assert.Empty(await harness.SessionsForAsync(mine.OwnershipId));
        Assert.Empty(await harness.SessionsForAsync(other.OwnershipId));
    }

    /// <summary>
    /// A process whose main module cannot be read — an elevated game, an
    /// anti-cheat driver host, a 32-bit reader looking at a 64-bit process.
    /// There is no path at all, so the install-root join has nothing to work
    /// with. The declared launch is the only thing that can answer.
    /// </summary>
    [Fact]
    public async Task A_process_with_no_readable_path_is_attributed_by_the_launch()
    {
        using var harness = new SessionWatcherHarness();
        var mine = await harness.AddGameAsync("Bluebird", "Bluebird.exe");
        await harness.AddGameAsync("Redwing", "Bluebird.exe");

        harness.Declare(mine.OwnershipId);
        harness.Processes.Start(701, "Bluebird", executablePath: null, startedAtUtc: T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(701, T0.AddMinutes(20));
        await harness.TickAtAsync(T0.AddMinutes(21));

        var session = Assert.Single(await harness.SessionsForAsync(mine.OwnershipId));
        Assert.Equal(SessionAttributions.Launch, session.AttributedBy);
    }

    /// <summary>
    /// <b>Evidence outranks intent.</b> While a launch of one game is pending,
    /// a process that resolves inside a DIFFERENT game's install directory is
    /// that other game — the user started it from Steam while waiting, and
    /// relabelling it would be the fabricated fact this whole design is trying
    /// not to produce.
    /// </summary>
    [Fact]
    public async Task A_pending_launch_never_relabels_a_process_the_filesystem_can_place()
    {
        using var harness = new SessionWatcherHarness();
        var declared = await harness.AddGameAsync("Bluebird", "Bluebird.exe");
        var other = await harness.AddGameAsync("Redwing", "Redwing.exe");

        harness.Declare(declared.OwnershipId);
        harness.Processes.Start(702, "Redwing", other.Exe("Redwing.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(702, T0.AddMinutes(10));
        await harness.TickAtAsync(T0.AddMinutes(11));

        Assert.Empty(await harness.SessionsForAsync(declared.OwnershipId));

        var session = Assert.Single(await harness.SessionsForAsync(other.OwnershipId));

        // Inferred, not launched: Hoard did not start this one, and saying it
        // did would poison exactly the signal M3b exists to create.
        Assert.Equal(SessionAttributions.Inferred, session.AttributedBy);
    }

    /// <summary>
    /// <b>A launch does not widen what counts as a game.</b> Another owned
    /// game's executable, running from outside every install root while a
    /// launch of a third game is pending, is claimed by nobody. The intent's
    /// rules require the process to already look like ITS game; there is no
    /// "believe whatever starts next" fallback behind them, and one wrong
    /// eight-hour session would cost more than ten missed ones.
    /// </summary>
    [Fact]
    public async Task A_pending_launch_does_not_claim_a_process_that_is_not_its_game()
    {
        using var harness = new SessionWatcherHarness();
        var declared = await harness.AddGameAsync("Bluebird", "Bluebird.exe");
        var other = await harness.AddGameAsync("Redwing", "Redwing.exe");

        harness.Declare(declared.OwnershipId);
        harness.Processes.Start(703, "Redwing", harness.ElsewherePath("Redwing.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(703, T0.AddMinutes(40));
        await harness.TickAtAsync(T0.AddMinutes(41));

        Assert.Empty(await harness.SessionsForAsync(declared.OwnershipId));
        Assert.Empty(await harness.SessionsForAsync(other.OwnershipId));
    }

    /// <summary>
    /// The window closes. A user who cancelled at Steam's own prompt and started
    /// something else an hour later must not find that evening filed under the
    /// game they did not play.
    /// </summary>
    [Fact]
    public async Task An_expired_launch_claims_nothing()
    {
        using var harness = new SessionWatcherHarness();
        var mine = await harness.AddGameAsync("Bluebird", "Game.exe");
        await harness.AddGameAsync("Redwing", "Game.exe");

        harness.Declare(mine.OwnershipId);

        // Past the 90-second window, with the process appearing afterwards.
        var late = T0.AddMinutes(30);
        harness.Processes.Start(704, "Game", harness.ElsewherePath("Game.exe"), late);

        await harness.TickAtAsync(late.AddSeconds(5));
        harness.Processes.Exit(704, late.AddMinutes(20));
        await harness.TickAtAsync(late.AddMinutes(21));

        Assert.Empty(await harness.SessionsForAsync(mine.OwnershipId));
    }

    /// <summary>
    /// A withdrawn intent is gone immediately, not at expiry. This is the path a
    /// refused dispatch takes: the URI never reached a handler, so nothing must
    /// be left sitting there able to claim a process.
    /// </summary>
    [Fact]
    public async Task An_abandoned_launch_claims_nothing()
    {
        using var harness = new SessionWatcherHarness();
        var mine = await harness.AddGameAsync("Bluebird", "Game.exe");
        await harness.AddGameAsync("Redwing", "Game.exe");

        harness.Declare(mine.OwnershipId);
        harness.Intents.Abandon(mine.OwnershipId);

        harness.Processes.Start(705, "Game", harness.ElsewherePath("Game.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(705, T0.AddMinutes(20));
        await harness.TickAtAsync(T0.AddMinutes(21));

        Assert.Empty(await harness.SessionsForAsync(mine.OwnershipId));
    }

    /// <summary>
    /// Attribution is decided when the session OPENS and holds for the sitting:
    /// a game Hoard launched that hands off to a second executable is still a
    /// launched session, not a half-launched one.
    /// </summary>
    [Fact]
    public async Task A_launched_session_stays_launched_across_a_handoff()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Bluebird", "launcher.exe", "bluebird.exe");

        harness.Declare(game.OwnershipId);
        harness.Processes.Start(710, "launcher", game.Exe("launcher.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));

        harness.Processes.Exit(710, T0.AddSeconds(8));
        harness.Processes.Start(711, "bluebird", game.Exe("bluebird.exe"), T0.AddSeconds(9));
        await harness.TickAtAsync(T0.AddSeconds(10));

        harness.Processes.Exit(711, T0.AddMinutes(45));
        await harness.TickAtAsync(T0.AddMinutes(46));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(SessionAttributions.Launch, session.AttributedBy);
        Assert.Equal(T0, session.StartedAt);
    }

    /// <summary>
    /// The signal the ambient indicator resolves off. It fires once — the
    /// handoff above would otherwise announce the same launch twice — and it
    /// fires when the watcher actually attaches, which is what makes "the strip
    /// disappearing" a fact about a running game rather than an animation
    /// finishing.
    /// </summary>
    [Fact]
    public async Task Observing_the_launch_is_announced_exactly_once()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Bluebird", "launcher.exe", "bluebird.exe");

        var announced = new List<long>();
        harness.Intents.Observed += (_, e) => announced.Add(e.OwnershipId);

        harness.Declare(game.OwnershipId);
        harness.Processes.Start(720, "launcher", game.Exe("launcher.exe"), T0);
        harness.Processes.Start(721, "bluebird", game.Exe("bluebird.exe"), T0.AddSeconds(1));
        await harness.TickAtAsync(T0.AddSeconds(5));

        Assert.Equal([game.OwnershipId], announced);
    }

    /// <summary>
    /// Nothing is announced for a game the user started somewhere else. The
    /// event means "the launch you asked for is running", and firing it for a
    /// game that appeared on its own would put a strip on screen about something
    /// the user did not do in Hoard.
    /// </summary>
    [Fact]
    public async Task A_game_started_elsewhere_announces_nothing_and_records_inference()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Bluebird", "bluebird.exe");

        var announced = 0;
        harness.Intents.Observed += (_, _) => announced++;

        harness.Processes.Start(730, "bluebird", game.Exe("bluebird.exe"), T0);
        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(730, T0.AddMinutes(12));
        await harness.TickAtAsync(T0.AddMinutes(13));

        Assert.Equal(0, announced);

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(SessionAttributions.Inferred, session.AttributedBy);
    }

    /// <summary>
    /// The one-game deep scan. The library-wide index stops at
    /// <c>ExecutableScanDepth</c>; the launch scan goes further, which is how a
    /// title whose real binary sits under <c>Engine/Binaries/Win64</c> gets
    /// watched at all when the user starts it from Hoard.
    /// </summary>
    [Fact]
    public async Task A_launch_reaches_an_executable_deeper_than_the_library_index_scans()
    {
        using var harness = new SessionWatcherHarness(o => o.ExecutableScanDepth = 1);
        var game = await harness.AddGameAsync("Bluebird", "a/b/c/deep.exe");

        harness.Declare(game.OwnershipId);

        // Outside the install root, so only the NAME can place it — and the name
        // is only known because the launch scan went deeper than the index.
        harness.Processes.Start(740, "deep", harness.ElsewherePath("deep.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(740, T0.AddMinutes(15));
        await harness.TickAtAsync(T0.AddMinutes(16));

        var session = Assert.Single(await harness.SessionsForAsync(game.OwnershipId));
        Assert.Equal(SessionAttributions.Launch, session.AttributedBy);
    }

    /// <summary>
    /// A second click while the first launch is still in flight is refused by
    /// the registry, which is what the UI turns into "do nothing" rather than a
    /// second store prompt.
    /// </summary>
    [Fact]
    public void Declaring_the_same_launch_twice_is_refused()
    {
        var intents = new LaunchIntents();

        Assert.True(intents.Declare(42, T0));
        Assert.False(intents.Declare(42, T0.AddSeconds(1)));

        // Once the window has passed it is a new launch, not a duplicate.
        Assert.True(intents.Declare(42, T0 + intents.Window));
    }

    /// <summary>
    /// A described intent with nothing to look for attributes nothing. This is
    /// the ownership with no recorded install path, and the answer is silence
    /// rather than a guess: with no idea what the executable is called, the only
    /// remaining rule would be "believe whatever starts next".
    /// </summary>
    [Fact]
    public void An_intent_with_no_names_and_no_root_attributes_nothing()
    {
        var intents = new LaunchIntents();
        intents.Declare(42, T0);
        intents.Describe(42, installRoot: null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Null(intents.Attribute(@"C:\Anything\anything.exe", "anything", T0.AddSeconds(5)));
        Assert.Null(intents.Attribute(null, "anything", T0.AddSeconds(5)));
    }

    /// <summary>
    /// A game installed since the last index rebuild still has a root the
    /// intent can use: the launch is described from the index the tick refreshed
    /// before discovery ran, not from the one that was current when the user
    /// clicked.
    /// </summary>
    [Fact]
    public async Task A_launch_declared_before_the_first_index_build_still_attributes()
    {
        using var harness = new SessionWatcherHarness();
        var mine = await harness.AddGameAsync("Bluebird", "Game.exe");
        await harness.AddGameAsync("Redwing", "Game.exe");

        // Declared before the watcher has ever ticked, so nothing has been
        // scanned and no index exists yet.
        harness.Declare(mine.OwnershipId);
        harness.Processes.Start(750, "Game", harness.ElsewherePath("Game.exe"), T0);

        await harness.TickAtAsync(T0.AddSeconds(5));
        harness.Processes.Exit(750, T0.AddMinutes(9));
        await harness.TickAtAsync(T0.AddMinutes(10));

        var session = Assert.Single(await harness.SessionsForAsync(mine.OwnershipId));
        Assert.Equal(SessionAttributions.Launch, session.AttributedBy);
    }
}
