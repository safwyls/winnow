using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;

namespace Winnow.App.Views;

public partial class MainWindow
{
    private Border _desktopStartup = null!;
    private LoadingDragon _desktopStartupDragon = null!;
    private TextBlock _desktopStartupStatus = null!;
    private Button _desktopStartupRetry = null!;
    private CancellationTokenSource? _desktopPreparation;
    private Func<Task>? _desktopPrepare;
    private bool _desktopClosed;
    private readonly long _desktopClockOrigin = Stopwatch.GetTimestamp();
    internal bool EnableDesktopStartup { get; init; } = Program.AppHost is not null;
    internal bool DesktopStartupVisible => _desktopStartup?.IsVisible == true;
    internal Action<Action<TimeSpan>>? DesktopStartupFrameScheduler { get; set; }

    private void InitializeDesktopStartup()
    {
        _desktopStartupDragon = new LoadingDragon { Width = 144, Height = 144, HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock { Text = "WINNOW", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center };
        title[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("DisplayFont");
        title[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Text");
        _desktopStartupStatus = new TextBlock { Text = "Preparing your library…", FontSize = 18,
            HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _desktopStartupStatus[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextDim");
        _desktopStartupStatus[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension("BodyFont");
        AutomationProperties.SetLiveSetting(_desktopStartupStatus, AutomationLiveSetting.Polite);
        _desktopStartupRetry = new Button { Content = "Try again", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center };
        _desktopStartupRetry.Classes.Add("act");
        _desktopStartupRetry.Classes.Add("quiet");
        _desktopStartupRetry.Click += async (_, _) => { if (_desktopPrepare is { } prepare) await PrepareDesktopAsync(prepare); };
        _desktopStartup = new Border { Name = "DesktopStartup", IsVisible = false, Focusable = true, Padding = new Thickness(32),
            Child = new StackPanel { Spacing = 18, MaxWidth = 600, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Children = { _desktopStartupDragon, title, _desktopStartupStatus, _desktopStartupRetry } } };
        _desktopStartup[!Border.BackgroundProperty] = new DynamicResourceExtension("Ground");
        Grid.SetRow(_desktopStartup, 1); Grid.SetRowSpan(_desktopStartup, 2);
        DesktopHost.Children.Add(_desktopStartup);
        Closed += (_, _) =>
        {
            _desktopClosed = true;
            _startupLibraryReady.TrySetResult(false);
            _desktopPreparation?.Cancel();
            _desktopStartupDragon.IsTracing = false;
        };
    }

    private async Task LoadInitialDesktopAsync()
    {
        await LoadOnOpenAsync();
        if (_desktopClosed) return;
        // TilesChanged starts the feed synchronously and coalesces further library
        // passes in that execution. Await it instead of starting a competing score.
        if (_shell?.Feed.LoadCommand.ExecutionTask is { } feed) await feed;
        if (_desktopClosed) return;
        _startupLibraryReady.TrySetResult(true);
        if (_shell is not null) await _shell.Setup.LoadAsync();
    }

    internal async Task<bool> PrepareDesktopAsync(Func<Task> prepare)
    {
        if (_desktopClosed) return false;
        _desktopPreparation?.Cancel();
        _desktopPreparation?.Dispose();
        _desktopPreparation = new CancellationTokenSource();
        var token = _desktopPreparation.Token;
        _desktopPrepare = prepare;
        _desktopStartup.IsVisible = true;
        _desktopStartup.Opacity = 1;
        _desktopStartupRetry.IsVisible = false;
        _desktopStartupStatus.Text = "Preparing your library…";
        SetDesktopContentEnabled(false);
        if (!IsFullscreen) _desktopStartup.Focus();
        _desktopStartupDragon.IsTracing = _library?.Ramp.ReducedMotion != true;
        try
        {
            await DesktopFrameAsync(token);
            await prepare().WaitAsync(token);
            await DesktopFrameAsync(token);
            var now = await DesktopFrameAsync(token);
            if (_library?.Ramp.ReducedMotion != true && IsVisible && DesktopHost.IsVisible)
            {
                while (!_desktopStartupDragon.HasCompletedCircuit && IsVisible && DesktopHost.IsVisible &&
                    _library?.Ramp.ReducedMotion != true)
                    now = await DesktopFrameAsync(token);
                var fadeStart = now;
                while (IsVisible && DesktopHost.IsVisible && _library?.Ramp.ReducedMotion != true &&
                    now - fadeStart < TimeSpan.FromMilliseconds(180))
                {
                    now = await DesktopFrameAsync(token);
                    _desktopStartup.Opacity = 1 - Math.Clamp((now - fadeStart).TotalMilliseconds / 180, 0, 1);
                }
            }
            token.ThrowIfCancellationRequested();
            _desktopStartupDragon.IsTracing = false;
            _desktopStartup.IsVisible = false;
            SetDesktopContentEnabled(true);
            if (!IsFullscreen && IsVisible && _shell?.Setup.IsOpen != true) Focus();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || _desktopClosed) return false;
            LogStartupLoadFailure(ex);
            _desktopStartupDragon.IsTracing = false;
            _desktopStartup.Opacity = 1;
            _desktopStartupStatus.Text = "Couldn't prepare your library. Try again.";
            _desktopStartupRetry.IsVisible = true;
            if (!IsFullscreen && IsVisible) _desktopStartupRetry.Focus(NavigationMethod.Tab);
        }
        return false;
    }

    private void SetDesktopContentEnabled(bool enabled)
    {
        foreach (var child in DesktopHost.Children)
            if (child != _desktopStartup && Grid.GetRow(child) > 0) child.IsEnabled = enabled;
        UpdateSetupPresentation();
    }

    private Task<TimeSpan> DesktopFrameAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Hidden tray launches and startup fullscreen still prepare desktop data;
        // neither can depend on a render callback from the invisible desktop.
        if (!IsVisible || !DesktopHost.IsVisible)
            return HiddenDesktopFrameAsync();
        var completion = new TaskCompletionSource<TimeSpan>();
        EventHandler<AvaloniaPropertyChangedEventArgs>? visibility = null;
        var registration = token.Register(() =>
        {
            if (visibility is not null) PropertyChanged -= visibility;
            completion.TrySetCanceled(token);
        });
        void Frame(TimeSpan time) => Dispatcher.UIThread.Post(() =>
        {
            registration.Dispose();
            PropertyChanged -= visibility;
            if (token.IsCancellationRequested) completion.TrySetCanceled(token);
            else completion.TrySetResult(time);
        }, DispatcherPriority.Background);
        visibility = (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !IsVisible)
                Frame(DesktopTimestamp());
        };
        PropertyChanged += visibility;
        if (DesktopStartupFrameScheduler is { } scheduler) scheduler(Frame);
        // Use one clock for visible and hidden frames. Renderer timestamps can
        // have a different origin when a tray launch is restored during loading.
        else RequestAnimationFrame(_ => Frame(DesktopTimestamp()));
        return completion.Task;
    }

    private TimeSpan DesktopTimestamp() => Stopwatch.GetElapsedTime(_desktopClockOrigin);

    private async Task<TimeSpan> HiddenDesktopFrameAsync()
        => await Dispatcher.UIThread.InvokeAsync(DesktopTimestamp, DispatcherPriority.Background);
}
