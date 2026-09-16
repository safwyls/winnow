using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Winnow.App.Services;

namespace Winnow.App.Views.Fullscreen;

public sealed partial class FullscreenView
{
    private Border? _startup;
    private LoadingDragon? _startupMark;
    private TextBlock? _startupMessage;
    private Button? _startupBack, _startupRetry;
    private CancellationTokenSource? _startupPresentation;
    private Task? _startupLoad;
    private Func<Task>? _prepare;
    private bool _startupWaiting;
    public bool IsPrepared { get; private set; }
    internal bool StartupVisible => _startup?.IsVisible == true;
    internal Action<Action<TimeSpan>>? StartupFrameScheduler { get; set; }

    private void InitializeStartup(bool preparing)
    {
        IsPrepared = !preparing;
        _startupMark = new LoadingDragon { Width = 160, Height = 160 };
        _startupMark.Name = "FullscreenStartupMark";
        _startupMark.HorizontalAlignment = HorizontalAlignment.Center;
        var title = FullscreenUi.Text("WINNOW", 48);
        title[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("DisplayFont");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _startupMessage = FullscreenUi.Text("Preparing fullscreen…", 28, "TextDim");
        _startupMessage.Name = "FullscreenStartupStatus";
        _startupMessage.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetLiveSetting(_startupMessage, AutomationLiveSetting.Polite);
        _startupRetry = FullscreenUi.Button("Try again", () => { if (_prepare is { } prepare) _ = PrepareAsync(prepare); });
        _startupRetry.Name = "FullscreenStartupRetry";
        _startupRetry.IsVisible = false;
        _startupBack = FullscreenUi.Button("Back to desktop", () => ExitRequested?.Invoke());
        _startupBack.Name = "FullscreenStartupBack";
        _startupBack.HorizontalAlignment = HorizontalAlignment.Center;
        _startupRetry.HorizontalAlignment = HorizontalAlignment.Center;
        var panel = new StackPanel { Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 1000,
            Children = { _startupMark, title, _startupMessage, _startupRetry, _startupBack } };
        _startup = new Border { Name = "FullscreenStartup", Child = panel, IsVisible = preparing, Padding = new Thickness(48) };
        _startup[!Border.BackgroundProperty] = new DynamicResourceExtension("Ground");
        KeyboardNavigation.SetTabNavigation(_startup, KeyboardNavigationMode.Cycle);
        _canvas.Children.Add(_startup);
        _safe.IsEnabled = _overlay.IsEnabled = !preparing;
    }

    public async Task PrepareAsync(Func<Task> prepare)
    {
        if (_disposed || _startup is null) return;
        CancelStartupPresentation();
        var presentation = new CancellationTokenSource();
        _startupPresentation = presentation;
        var token = presentation.Token;
        var timing = System.Diagnostics.Stopwatch.StartNew();
        _prepare = prepare;
        _startupWaiting = true;
        IsPrepared = false;
        _startup.IsVisible = true;
        _startup.Opacity = 1;
        _startupMark!.Opacity = 1;
        _startupMessage!.Text = "Preparing fullscreen…";
        _startupRetry!.IsVisible = false;
        _safe.IsEnabled = _overlay.IsEnabled = false;
        FocusStartup();
        try
        {
            // Frame callbacks run before rendering. Resume at Background so the loading
            // presentation gets its first paint before any synchronously completing reads.
            await StartupFrameAsync(token);
            StartStartupTrace();
            if (_startupLoad is null || _startupLoad.IsCompleted)
                _startupLoad = prepare();
            await _startupLoad.WaitAsync(token);
            // Library and primary feed are ready. Their queued layout/text-scaling work
            // must be presented beneath the veil; optional artwork and shelves can follow.
            await StartupFrameAsync(token);
            await StartupFrameAsync(token);
            // Count a circuit from the animation's first rendered frame, not the
            // start of data loading; preferences may have delayed the trace.
            while (!_context.ReducedMotion && !_startupMark.HasCompletedCircuit)
                await StartupFrameAsync(token);
            if (!_context.ReducedMotion)
            {
                var start = await StartupFrameAsync(token);
                while (true)
                {
                    var now = await StartupFrameAsync(token);
                    var progress = Math.Clamp((now - start).TotalMilliseconds / 180, 0, 1);
                    _startup.Opacity = 1 - progress;
                    if (progress >= 1 || _context.ReducedMotion) break;
                }
            }
            token.ThrowIfCancellationRequested();
            _startupWaiting = false;
            StopStartupTrace();
            // Re-entry shares only unfinished preparation. A later entry must join
            // a fresh refresh, including changes queued while this view was detached.
            _startupLoad = null;
            IsPrepared = true;
            _startup.IsVisible = false;
            _safe.IsEnabled = CurrentPage is not FullscreenActionsPage;
            _overlay.IsEnabled = true;
            FocusPage();
            _context.Services?.GetService<ILogger<FullscreenView>>()?.LogInformation(
                "Fullscreen preparation completed in {ElapsedMilliseconds} ms", timing.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _disposed) return;
            _startupWaiting = false;
            StopStartupTrace();
            if (_context.Services?.GetService<ILogger<FullscreenView>>() is { } logger)
                logger.LogError(ex, "Fullscreen preparation failed after {ElapsedMilliseconds} ms", timing.ElapsedMilliseconds);
            else System.Diagnostics.Trace.TraceError($"Fullscreen preparation failed: {ex}");
            _startupMessage.Text = "Couldn't prepare fullscreen. Try again or return to desktop.";
            _startupRetry.IsVisible = true;
            _startup.Opacity = 1;
            _startupRetry.Focus(NavigationMethod.Directional);
        }
    }

    private Task<TimeSpan> StartupFrameAsync(CancellationToken token)
    {
        var completion = new TaskCompletionSource<TimeSpan>();
        EventHandler<VisualTreeAttachmentEventArgs>? attached = null;
        var registration = token.Register(() =>
        {
            if (attached is not null) AttachedToVisualTree -= attached;
            completion.TrySetCanceled(token);
        });
        void Frame(TimeSpan timestamp) => Dispatcher.UIThread.Post(() =>
        {
            registration.Dispose();
            if (token.IsCancellationRequested) completion.TrySetCanceled(token);
            else completion.TrySetResult(timestamp);
        }, DispatcherPriority.Background);
        if (StartupFrameScheduler is { } scheduler) scheduler(Frame);
        else if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Frame);
        else
        {
            // Assigning Content can precede the presenter's first template/layout pass.
            attached = (_, _) =>
            {
                AttachedToVisualTree -= attached;
                if (!token.IsCancellationRequested) TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
            };
            AttachedToVisualTree += attached;
        }
        return completion.Task;
    }

    private void StartStartupTrace()
    {
        if (_startupMark is not null)
            _startupMark.IsTracing = _startupWaiting && _context.HasLoadedPreferences && !_context.ReducedMotion &&
                _startupPresentation is { IsCancellationRequested: false } && StartupVisible;
    }

    private void StopStartupTrace()
    {
        if (_startupMark is not null) _startupMark.IsTracing = false;
    }

    private void FocusStartup()
    {
        if (_startupRetry?.IsVisible == true) _startupRetry.Focus(NavigationMethod.Directional);
        else _startupBack?.Focus(NavigationMethod.Directional);
    }

    private void HandleStartup(GamepadButtons buttons)
    {
        if (buttons.HasFlag(GamepadButtons.Back)) { ExitRequested?.Invoke(); return; }
        if (buttons.HasFlag(GamepadButtons.Up) || buttons.HasFlag(GamepadButtons.Down) ||
            buttons.HasFlag(GamepadButtons.Left) || buttons.HasFlag(GamepadButtons.Right))
        {
            if (_startupRetry?.IsVisible == true && _startupBack?.IsFocused == true)
                _startupRetry.Focus(NavigationMethod.Directional);
            else _startupBack?.Focus(NavigationMethod.Directional);
        }
        if (buttons.HasFlag(GamepadButtons.Accept))
        {
            if (_startupRetry?.IsFocused == true && _prepare is { } prepare) _ = PrepareAsync(prepare);
            else ExitRequested?.Invoke();
        }
    }

    internal void CancelStartupPresentation()
    {
        _startupWaiting = false;
        StopStartupTrace();
        _startupPresentation?.Cancel();
        _startupPresentation?.Dispose();
        _startupPresentation = null;
    }
}
