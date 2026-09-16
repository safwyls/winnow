using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.App.Views;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenStartupTests
{
    [AvaloniaFact]
    public async Task Finishing_load_after_a_circuit_keeps_trace_running_through_the_fade()
    {
        using var fixture = new Fixture(false);
        var ready = new TaskCompletionSource();
        var preparation = fixture.View.PrepareAsync(() => ready.Task);
        var dragon = fixture.View.GetVisualDescendants().OfType<LoadingDragon>().Single();
        for (var i = 0; i < 36; i++) fixture.Frame();
        Assert.True(dragon.HasCompletedCircuit);
        Assert.False(preparation.IsCompleted);
        var before = dragon.Phase;
        ready.SetResult(); Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 4; i++) fixture.Frame();
        Assert.True(dragon.IsTracing);
        Assert.True(dragon.Phase > before);
        Assert.InRange(fixture.View.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "FullscreenStartup").Opacity, .01, .99);
        for (var i = 0; i < 10 && !preparation.IsCompleted; i++) fixture.Frame();
        await preparation;
        Assert.False(dragon.IsTracing);
    }

    [AvaloniaFact]
    public async Task Leaving_before_attachment_cancels_first_paint_wait_without_loading()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context, preparing: true);
        var loaded = false;
        var preparation = view.PrepareAsync(() => { loaded = true; return Task.CompletedTask; });
        view.CancelStartupPresentation();
        await preparation;
        var window = new Window { Width = 1280, Height = 720, Content = view };
        try
        {
            window.Show(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            Assert.False(loaded);
            Assert.False(view.IsPrepared);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_motion_preference_is_known_before_tracing_and_trace_continues_through_reveal(bool reducedMotion)
    {
        var settings = new DelayedSettings(reducedMotion);
        using var services = new ServiceCollection().AddSingleton<ISettingsRepository>(settings).BuildServiceProvider();
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var context = new FullscreenContext(library, new FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell, services);
        using var fixture = new Fixture(false, context: context);
        var ready = new TaskCompletionSource();
        var preferences = new TaskCompletionSource();
        var preparation = fixture.View.PrepareAsync(async () =>
        {
            await context.LoadAsync(); preferences.SetResult(); await ready.Task;
        });
        var mark = fixture.View.GetVisualDescendants().OfType<LoadingDragon>().Single(c => c.Name == "FullscreenStartupMark");
        fixture.Frame(); fixture.Frame();
        Assert.False(context.HasLoadedPreferences);
        Assert.False(mark.IsTracing);
        settings.Ready.SetResult();
        await preferences.Task;
        fixture.Frame(); fixture.Frame();
        Assert.Equal(reducedMotion, context.ReducedMotion);
        Assert.Equal(!reducedMotion, mark.IsTracing);
        context.TextScale = 1.4;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(28 * 1.4, fixture.View.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Name == "FullscreenStartupStatus").FontSize);
        fixture.Capture($"fullscreen-startup-large-{reducedMotion}.png");
        ready.SetResult(); Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 60 && !preparation.IsCompleted; i++) fixture.Frame();
        await preparation;
        Assert.False(mark.IsTracing);
        Assert.True(fixture.View.IsPrepared);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_paints_before_load_and_reveals_only_after_ready_frames(bool reducedMotion)
    {
        using var fixture = new Fixture(reducedMotion);
        var ready = new TaskCompletionSource();
        var loaded = false;
        var preparation = fixture.View.PrepareAsync(() => { loaded = true; return ready.Task; });
        Assert.False(loaded);
        Assert.True(fixture.View.StartupVisible);
        fixture.Frame();
        Assert.True(loaded);
        fixture.View.Handle(GamepadButtons.Next | GamepadButtons.Menu | GamepadButtons.Play);
        Assert.Equal("For you", fixture.View.CurrentPage.Title);
        var exit = 0;
        fixture.View.ExitRequested += () => exit++;
        fixture.View.Handle(GamepadButtons.Back);
        Assert.Equal(1, exit);
        Assert.True(fixture.View.HandleKey(new KeyEventArgs { Key = Key.Space }));
        Assert.Equal(2, exit);
        Assert.False(fixture.View.HandleKey(new KeyEventArgs { Key = Key.F11 }));
        Assert.False(fixture.View.HandleKey(new KeyEventArgs { Key = Key.F4, KeyModifiers = KeyModifiers.Alt }));
        fixture.Capture($"fullscreen-startup-{reducedMotion}.png");
        ready.SetResult(); Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.View.StartupVisible);
        fixture.Frame();
        Assert.True(fixture.View.StartupVisible);
        for (var i = 0; i < 60 && !preparation.IsCompleted; i++) fixture.Frame();
        await preparation;
        Assert.True(fixture.View.IsPrepared);
        Assert.False(fixture.View.StartupVisible);
        Assert.NotNull(fixture.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warm_entry_refreshes_under_cover_and_preserves_the_page(bool reducedMotion)
    {
        using var fixture = new Fixture(reducedMotion);
        var first = fixture.View.PrepareAsync(() => Task.CompletedTask);
        for (var i = 0; i < 60 && !first.IsCompleted; i++) fixture.Frame();
        await first;
        fixture.View.Handle(GamepadButtons.Next);
        var page = fixture.View.CurrentPage;
        fixture.Window.Content = null;
        var refreshed = false;
        var ready = new TaskCompletionSource();
        var second = fixture.View.PrepareAsync(() => { refreshed = true; return ready.Task; });
        Assert.True(fixture.View.StartupVisible);
        Assert.False(fixture.View.IsPrepared);
        fixture.Window.Content = fixture.View;
        fixture.Frame();
        Assert.True(refreshed);
        Assert.True(fixture.View.StartupVisible);
        ready.SetResult();
        Dispatcher.UIThread.RunJobs();
        fixture.Frame(); fixture.Frame();
        if (!reducedMotion)
        {
            // Three 60ms frames cannot complete a glow circuit.
            Assert.True(fixture.View.StartupVisible);
            Assert.False(second.IsCompleted);
        }
        else Assert.True(second.IsCompleted);
        for (var i = 0; i < 60 && !second.IsCompleted; i++) fixture.Frame();
        await second;
        Assert.True(fixture.View.IsPrepared);
        Assert.Same(page, fixture.View.CurrentPage);
        Assert.False(fixture.View.StartupVisible);
    }

    [AvaloniaFact]
    public async Task Failure_stays_actionable_and_retry_loads_again()
    {
        using var fixture = new Fixture(true);
        var attempts = 0;
        Task Load() => ++attempts == 1 ? Task.FromException(new InvalidOperationException("test failure")) : Task.CompletedTask;
        var preparation = fixture.View.PrepareAsync(Load);
        fixture.Frame(); await preparation;
        Assert.False(fixture.View.IsPrepared);
        Assert.True(fixture.View.StartupVisible);
        var retry = fixture.View.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "FullscreenStartupRetry");
        Assert.True(retry.IsVisible);
        Assert.True(retry.IsFocused);
        fixture.Capture("fullscreen-startup-failure.png");
        fixture.View.Handle(GamepadButtons.Accept);
        for (var i = 0; i < 8 && !fixture.View.IsPrepared; i++) fixture.Frame();
        Assert.Equal(2, attempts);
        Assert.True(fixture.View.IsPrepared);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exit_and_reentry_share_inflight_load_without_a_stale_reveal(bool completeBeforeReentry)
    {
        using var fixture = new Fixture(true);
        var ready = new TaskCompletionSource();
        var attempts = 0;
        Task Load() { attempts++; return ready.Task; }
        var first = fixture.View.PrepareAsync(Load);
        fixture.Frame();
        fixture.Window.Content = null;
        await first;
        if (completeBeforeReentry) ready.SetResult();
        fixture.Frame();
        Assert.False(fixture.View.IsPrepared);
        Assert.True(fixture.View.StartupVisible);
        fixture.Window.Content = fixture.View;
        Dispatcher.UIThread.RunJobs();
        var second = fixture.View.PrepareAsync(Load);
        fixture.Frame();
        if (!completeBeforeReentry) ready.SetResult();
        for (var i = 0; i < 8 && !second.IsCompleted; i++) fixture.Frame();
        await second;
        Assert.Equal(completeBeforeReentry ? 2 : 1, attempts);
        Assert.True(fixture.View.IsPrepared);
    }

    [AvaloniaFact]
    public async Task Disposing_during_load_cannot_reveal_the_page()
    {
        using var fixture = new Fixture(true);
        var ready = new TaskCompletionSource();
        var preparation = fixture.View.PrepareAsync(() => ready.Task);
        fixture.Frame();
        fixture.View.Dispose();
        ready.SetResult(); fixture.Frame();
        await preparation;
        Assert.False(fixture.View.IsPrepared);
    }

    [AvaloniaFact]
    public async Task Real_render_frames_finish_initial_preparation()
    {
        using var fixture = new Fixture(false, scheduled: false);
        var loaded = false;
        var preparation = fixture.View.PrepareAsync(() => { loaded = true; return Task.CompletedTask; });
        Assert.False(loaded);
        var timeout = Stopwatch.StartNew();
        while (!preparation.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(4))
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(preparation.IsCompleted);
        await preparation;
        Assert.True(loaded);
        Assert.True(fixture.View.IsPrepared);
    }

    internal static async Task WaitPreparedAsync(FullscreenView view)
    {
        var timeout = Stopwatch.StartNew();
        while (!view.IsPrepared && timeout.Elapsed < TimeSpan.FromSeconds(4))
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(view.IsPrepared, view.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Name == "FullscreenStartupStatus")?.Text);
    }

    private sealed class Fixture : IDisposable
    {
        public FullscreenView View { get; }
        public Window Window { get; }
        private readonly List<Action<TimeSpan>> _frames = [];
        private TimeSpan _time;
        public Fixture(bool reducedMotion, bool scheduled = true, FullscreenContext? context = null)
        {
            context ??= new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell)
                { ReducedMotion = reducedMotion };
            View = new FullscreenView(context, preparing: true);
            if (scheduled) View.StartupFrameScheduler = callback => _frames.Add(callback);
            Window = new Window { Width = 1280, Height = 720, Content = View };
            Window.Show(); Dispatcher.UIThread.RunJobs();
            if (scheduled)
                View.GetVisualDescendants().OfType<LoadingDragon>().Single().FrameScheduler = callback => _frames.Add(callback);
        }
        public void Frame()
        {
            _time += TimeSpan.FromMilliseconds(60);
            var callbacks = _frames.ToArray(); _frames.Clear();
            foreach (var callback in callbacks) callback(_time);
            Dispatcher.UIThread.RunJobs(); Window.UpdateLayout();
        }
        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
            Directory.CreateDirectory(directory);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            using var frame = Window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(directory, name));
        }
        public void Dispose() { Window.Close(); View.Dispose(); }
    }

    private sealed class DelayedSettings(bool reducedMotion) : ISettingsRepository
    {
        public TaskCompletionSource Ready { get; } = new();
        public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        {
            await Ready.Task;
            return key == "fullscreen.reduced-motion" ? reducedMotion.ToString() : null;
        }
        public Task SetAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    }
}
