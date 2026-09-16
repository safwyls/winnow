using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DesktopStartupTests
{
    [AvaloniaFact]
    public async Task Restoring_a_hidden_window_during_preparation_uses_one_animation_clock()
    {
        var window = new MainWindow();
        var ready = new TaskCompletionSource();
        var loaded = false;
        try
        {
            window.Show(); window.Hide();
            var preparation = window.PrepareDesktopAsync(() => { loaded = true; return ready.Task; });
            await PumpUntilAsync(() => loaded);
            window.Show();
            ready.SetResult();
            await PumpUntilAsync(() => preparation.IsCompleted);
            Assert.True(await preparation);
            Assert.False(window.DesktopStartupVisible);
        }
        finally { ready.TrySetResult(); window.Close(); }
    }

    internal static Task WaitPreparedAsync(MainWindow window)
        => PumpUntilAsync(() => !window.DesktopStartupVisible);

    [AvaloniaFact]
    public async Task Automatic_startup_stays_covered_until_delayed_primary_feed_finishes()
    {
        var releases = new PreviewReleaseRepository();
        var links = new PreviewIdentityLinkRepository();
        var refusals = new PreviewExpansionRefusalRepository();
        using var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            releases, new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var service = new DelayedFeedService();
        using var feed = new FeedViewModel(service, library);
        var mergeQueue = new MergeQueueViewModel(new PreviewMergeCandidateRepository(), releases,
            new PreviewWorkRepository(), links, new PreviewOwnershipRepository(),
            new LibraryExpansionScan(releases, links, refusals), refusals, new PreviewLibraryQueryRepository());
        var shell = new MainWindowViewModel(library, mergeQueue,
            new StoresViewModel(new PreviewStoreConnections(), counts: library),
            new AppearanceViewModel(new ThemeService()), feed,
            new AccountStatsViewModel(new PreviewAccountStatsRepository()), new LibrarySettingsViewModel());
        var window = new MainWindow { DataContext = shell, EnableDesktopStartup = true };
        try
        {
            window.Show();
            await PumpUntilAsync(() => service.Called && !library.LoadCommand.IsRunning);
            Assert.NotEmpty(library.VisibleTiles);
            var hold = Stopwatch.StartNew();
            while (hold.Elapsed < TimeSpan.FromMilliseconds(700))
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Assert.True(feed.LoadCommand.IsRunning);
            Assert.True(window.DesktopStartupVisible);
            Assert.False(window.StartupLibraryReady.IsCompleted);
            service.Ready.SetResult();
            await PumpUntilAsync(() => !window.DesktopStartupVisible);
            Assert.True(await window.StartupLibraryReady);
            Assert.False(feed.LoadCommand.IsRunning);
            Assert.NotEmpty(feed.Shelves);
        }
        finally { service.Ready.TrySetResult(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Setup_keeps_library_disabled_after_startup_reveal()
    {
        var shell = PreviewData.Shell;
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            var preparation = window.PrepareDesktopAsync(() => shell.Setup.ReopenCommand.ExecuteAsync(null));
            await PumpUntilAsync(() => preparation.IsCompleted);
            Assert.True(await preparation);
            Assert.True(shell.Setup.IsOpen);
            Assert.False(window.FindControl<Grid>("ShellContent")!.IsEnabled);
            Assert.True(window.FindControl<LazyPane>("SetupPanel")!.IsVisible);
        }
        finally
        {
            await shell.Setup.SkipAllCommand.ExecuteAsync(null);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Motion_setting_controls_full_circuit_hold_and_continuous_trace_through_reveal(bool reducedMotion)
    {
        var shell = PreviewData.Shell;
        var oldMotion = shell.Library.Ramp.ReducedMotion;
        shell.Library.Ramp.ReducedMotion = reducedMotion;
        var window = new MainWindow { DataContext = shell };
        var frames = new List<Action<TimeSpan>>();
        window.DesktopStartupFrameScheduler = callback => frames.Add(callback);
        var time = TimeSpan.Zero;
        void Frame()
        {
            time += TimeSpan.FromMilliseconds(60);
            var current = frames.ToArray(); frames.Clear();
            foreach (var callback in current) callback(time);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        }
        try
        {
            window.Show();
            var dragon = window.GetVisualDescendants().OfType<LoadingDragon>().Single();
            dragon.FrameScheduler = callback => frames.Add(callback);
            var ready = new TaskCompletionSource();
            var preparation = window.PrepareDesktopAsync(() => ready.Task);
            Assert.Equal(!reducedMotion, dragon.IsTracing);
            Frame(); Capture(window, $"desktop-startup-{reducedMotion}.png");
            ready.SetResult(); Dispatcher.UIThread.RunJobs();
            Frame(); Frame();
            if (reducedMotion)
            {
                Assert.True(preparation.IsCompleted);
                Assert.False(window.DesktopStartupVisible);
            }
            else
            {
                Assert.False(preparation.IsCompleted);
                for (var i = 0; i < 26; i++) Frame();
                Assert.False(dragon.HasCompletedCircuit);
                Assert.Equal(1, window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DesktopStartup").Opacity);
                for (var i = 0; i < 10 && !dragon.HasCompletedCircuit; i++) Frame();
                Assert.True(dragon.HasCompletedCircuit);
                Assert.True(dragon.IsTracing);
                var veil = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DesktopStartup");
                for (var i = 0; i < 4 && veil.Opacity == 1; i++) Frame();
                Assert.True(dragon.IsTracing);
                Assert.InRange(window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "DesktopStartup").Opacity, .01, .99);
                for (var i = 0; i < 10 && !preparation.IsCompleted; i++) Frame();
            }
            Assert.True(await preparation);
            Assert.False(dragon.IsTracing);
        }
        finally { window.Close(); shell.Library.Ramp.ReducedMotion = oldMotion; }
    }

    [AvaloniaFact]
    public async Task Automatic_startup_waits_for_library_feed_and_real_layout_frames()
    {
        var window = new MainWindow { DataContext = PreviewData.Shell, EnableDesktopStartup = true };
        try
        {
            window.Show();
            Assert.True(window.DesktopStartupVisible);
            await PumpUntilAsync(() => !window.DesktopStartupVisible);
            Assert.True(await window.StartupLibraryReady);
            Assert.False(PreviewData.Shell.Feed.LoadCommand.IsRunning);
            Assert.False(window.GetVisualDescendants().OfType<LoadingDragon>().Single().IsTracing);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Startup_covers_pending_work_and_failure_can_retry_without_disabling_caption()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            var ready = new TaskCompletionSource();
            var preparation = window.PrepareDesktopAsync(() => ready.Task);
            await PumpUntilAsync(() => window.DesktopStartupVisible);
            Assert.True(window.FindControl<Border>("TitleBar")!.IsEnabled);
            Assert.DoesNotContain(window.FindControl<Grid>("DesktopHost")!.Children,
                c => Grid.GetRow(c) > 0 && c.Name != "DesktopStartup" && c.IsEnabled);
            ready.SetException(new InvalidOperationException("test startup failure"));
            await PumpUntilAsync(() => preparation.IsCompleted);
            Assert.False(await preparation);
            Assert.True(window.DesktopStartupVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Couldn't prepare your library. Try again.");
            Capture(window, "desktop-startup-failure.png");
            var retry = window.PrepareDesktopAsync(() => Task.CompletedTask);
            await PumpUntilAsync(() => retry.IsCompleted);
            Assert.True(await retry);
            Assert.False(window.DesktopStartupVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_cancels_pending_preparation_without_late_reveal()
    {
        var window = new MainWindow();
        window.Show();
        var ready = new TaskCompletionSource();
        var preparation = window.PrepareDesktopAsync(() => ready.Task);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        window.Close();
        Assert.False(await preparation);
        ready.SetResult(); Dispatcher.UIThread.RunJobs();
        Assert.True(window.DesktopStartupVisible);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<LoadingDragon>(), d => d.IsTracing);
    }

    [AvaloniaFact]
    public async Task Hidden_startup_does_not_wait_for_window_frames()
    {
        var window = new MainWindow { EnableDesktopStartup = true, StartHidden = true };
        try
        {
            window.Show();
            await PumpUntilAsync(() => window.StartupLibraryReady.IsCompleted && !window.DesktopStartupVisible);
            Assert.True(await window.StartupLibraryReady);
            Assert.False(window.IsVisible);
        }
        finally { window.ExitFromTray(); }
    }

    private static async Task PumpUntilAsync(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done() && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(done());
    }

    private static void Capture(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name));
    }

    private sealed class DelayedFeedService : IFeedService
    {
        private readonly PreviewFeedService _inner = new();
        public TaskCompletionSource Ready { get; } = new();
        public bool Called { get; private set; }
        public async Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default)
        {
            Called = true;
            await Ready.Task.WaitAsync(ct);
            return await _inner.GetShelvesAsync(ct);
        }
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default)
            => _inner.RecordSurfacedAsync(releaseId, shelfId, ct);
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => _inner.RecordVerdictAsync(releaseId, kind, ct);
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => _inner.RevokeVerdictAsync(releaseId, kind, ct);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => _inner.GetHistoryAsync(ct);
    }
}
