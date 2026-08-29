using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Monitor;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Launch path from the Play button to the URI dispatcher and the ambient strip.
/// </summary>
public sealed class GameLaunchTests
{
    private static readonly DateTime T0 = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);

    private static readonly GameLink Play = GameLink.Create(
        "Play", "steam://run/620", "Launch through Steam", GameLinkKind.Play)!;

    private static readonly GameLink Install = GameLink.Create(
        "Install", "steam://install/620", "Start the download in Steam", GameLinkKind.Install)!;

    /// <summary>
    /// The happy path, and the ordering that is the whole milestone: the intent
    /// exists BEFORE the URI is fired. A warm store client can have the game
    /// running before the dispatch call returns, and an intent declared after
    /// that race would have missed its own launch.
    /// </summary>
    [Fact]
    public async Task Play_declares_the_intent_before_it_fires_the_uri()
    {
        var intents = new LaunchIntents();
        var dispatcher = new RecordingDispatcher
        {
            OnOpen = _ => Assert.True(intents.IsLive(7, T0), "the intent must already exist"),
        };

        var service = new GameLaunchService(dispatcher, intents, new FakeTimeProvider(T0));

        Assert.Equal(LaunchDispatch.HandedOff, await service.LaunchAsync(7, Play));
        Assert.Equal(["steam://run/620"], dispatcher.Opened);
        Assert.True(intents.IsLive(7, T0));
    }

    /// <summary>
    /// The store client not being installed, its protocol registration being
    /// broken, or the shell's own "open this application?" prompt being declined
    /// all surface identically: the platform says no. Nothing throws, and the
    /// intent is withdrawn immediately rather than left to expire — an intent
    /// whose URI never reached a handler must not be sitting there ninety
    /// seconds later ready to claim whatever the user starts instead.
    /// </summary>
    [Fact]
    public async Task A_refused_dispatch_withdraws_the_intent_and_does_not_throw()
    {
        var intents = new LaunchIntents();
        var service = new GameLaunchService(
            new RecordingDispatcher { Answer = false }, intents, new FakeTimeProvider(T0));

        Assert.Equal(LaunchDispatch.Refused, await service.LaunchAsync(7, Play));
        Assert.False(intents.IsLive(7, T0));
        Assert.Equal(0, intents.PendingCount(T0));
    }

    /// <summary>
    /// A dispatcher that throws is still not an exception the UI sees, and still
    /// not a stuck state. The real dispatcher promises never to throw; this
    /// proves the launch path does not DEPEND on that promise, which matters
    /// because the caller is an async command handler and an exception escaping
    /// one of those is unobserved at best.
    /// </summary>
    [Fact]
    public async Task A_throwing_dispatcher_is_a_refusal_not_a_crash()
    {
        var intents = new LaunchIntents();
        var service = new GameLaunchService(
            new ThrowingDispatcher(), intents, new FakeTimeProvider(T0));

        Assert.Equal(LaunchDispatch.Refused, await service.LaunchAsync(7, Play));
        Assert.False(intents.IsLive(7, T0));
    }

    /// <summary>
    /// The impatient double click. The second press dispatches NOTHING — two
    /// dispatches is two store prompts — and reports the state rather than an
    /// error, so the UI leaves the first click's strip alone.
    /// </summary>
    [Fact]
    public async Task A_second_click_while_the_first_launch_is_in_flight_fires_nothing()
    {
        var intents = new LaunchIntents();
        var dispatcher = new RecordingDispatcher();
        var service = new GameLaunchService(dispatcher, intents, new FakeTimeProvider(T0));

        Assert.Equal(LaunchDispatch.HandedOff, await service.LaunchAsync(7, Play));
        Assert.Equal(LaunchDispatch.AlreadyRunning, await service.LaunchAsync(7, Play));

        Assert.Single(dispatcher.Opened);
    }

    /// <summary>
    /// An Install starts a download that produces no process for minutes or
    /// hours. Declaring an attribution window across that would be a window in
    /// which anything the user starts becomes this game.
    /// </summary>
    [Fact]
    public async Task An_install_dispatches_and_declares_nothing()
    {
        var intents = new LaunchIntents();
        var dispatcher = new RecordingDispatcher();
        var service = new GameLaunchService(dispatcher, intents, new FakeTimeProvider(T0));

        Assert.Equal(LaunchDispatch.HandedOff, await service.LaunchAsync(7, Install));

        Assert.Equal(["steam://install/620"], dispatcher.Opened);
        Assert.Equal(0, intents.PendingCount(T0));
    }

    /// <summary>
    /// The strip's ordinary life: it says the game is starting, and it stops
    /// saying so because the watcher SAW the game — not because a timer ran out.
    /// That is the one thing a launcher can report that a spinner cannot.
    /// </summary>
    [Fact]
    public void The_strip_resolves_when_the_watcher_sees_the_game()
    {
        var clock = new FakeTimeProvider(T0);
        var intents = new LaunchIntents();
        using var status = new LaunchStatusViewModel(intents, clock, post: a => a());

        status.Waiting(7, "Portal 2");

        Assert.True(status.IsOpen);
        Assert.True(status.IsWaiting);
        Assert.Equal("Starting Portal 2…", status.Message);

        intents.Declare(7, T0);
        intents.Fulfil(7, T0.AddSeconds(20));

        Assert.True(status.IsOpen);
        Assert.False(status.IsWaiting);
        Assert.Equal("Portal 2 is running.", status.Message);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(status.IsOpen);
    }

    /// <summary>
    /// <b>A launch nobody completed says nothing at all.</b> The user cancelled
    /// at Steam's own prompt, or thought better of it; they did not fail at
    /// anything, and being told off for it is exactly the friction this
    /// milestone is spending its budget to avoid.
    /// </summary>
    [Fact]
    public void A_launch_that_never_starts_disappears_without_a_complaint()
    {
        var clock = new FakeTimeProvider(T0);
        using var status = new LaunchStatusViewModel(
            intents: null, clock, post: a => a(), patience: TimeSpan.FromSeconds(90));

        status.Waiting(7, "Portal 2");
        Assert.True(status.IsOpen);

        clock.Advance(TimeSpan.FromSeconds(91));

        Assert.False(status.IsOpen);
        Assert.False(status.IsProblem);
        Assert.Equal(string.Empty, status.Message);
    }

    /// <summary>
    /// The one case Winnow can actually diagnose gets the one message with a
    /// negative tone — and it is still a line of text that fades, not a box to
    /// dismiss.
    /// </summary>
    [Fact]
    public void A_refusal_is_the_only_thing_that_reads_as_a_problem()
    {
        var clock = new FakeTimeProvider(T0);
        using var status = new LaunchStatusViewModel(intents: null, clock, post: a => a());

        status.Refused("Portal 2", "Steam");

        Assert.True(status.IsOpen);
        Assert.True(status.IsProblem);
        Assert.False(status.IsWaiting);
        Assert.Contains("Steam", status.Message, StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.False(status.IsOpen);
    }

    /// <summary>
    /// A launch of a different game confirming while this one is still waiting
    /// must not close this one's strip.
    /// </summary>
    [Fact]
    public void Another_games_launch_does_not_resolve_this_ones_strip()
    {
        var clock = new FakeTimeProvider(T0);
        var intents = new LaunchIntents();
        using var status = new LaunchStatusViewModel(intents, clock, post: a => a());

        status.Waiting(7, "Portal 2");

        intents.Declare(9, T0);
        intents.Fulfil(9, T0.AddSeconds(3));

        Assert.True(status.IsWaiting);
        Assert.Equal("Starting Portal 2…", status.Message);
    }

    /// <summary>
    /// The strip unsubscribes. A view model that outlives its window and keeps a
    /// handler on a process-lifetime singleton is the leak this codebase already
    /// avoids one subscription per wall for.
    /// </summary>
    [Fact]
    public void Disposing_the_strip_stops_it_listening()
    {
        var clock = new FakeTimeProvider(T0);
        var intents = new LaunchIntents();
        var status = new LaunchStatusViewModel(intents, clock, post: a => a());

        status.Waiting(7, "Portal 2");
        status.Dispose();

        intents.Declare(7, T0);
        intents.Fulfil(7, T0.AddSeconds(5));

        Assert.Equal("Starting Portal 2…", status.Message);
    }

    private sealed class RecordingDispatcher : IUriDispatcher
    {
        public List<string> Opened { get; } = [];

        public bool Answer { get; init; } = true;

        public Action<Uri>? OnOpen { get; init; }

        public Task<bool> OpenAsync(Uri uri)
        {
            OnOpen?.Invoke(uri);
            Opened.Add(uri.ToString());
            return Task.FromResult(Answer);
        }
    }

    private sealed class ThrowingDispatcher : IUriDispatcher
    {
        public Task<bool> OpenAsync(Uri uri)
            => throw new InvalidOperationException("no handler for this scheme");
    }
}
