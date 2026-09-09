using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Winnow.App.Views;

public partial class MainWindow
{
    private WindowState _beforeFullscreen = WindowState.Normal;
    private DispatcherTimer? _fullscreenClockTimer;
    private bool _fullscreenReady;

    internal bool IsFullscreen => WindowState == WindowState.FullScreen;

    private void InitializeFullscreen()
    {
        _fullscreenReady = true;
        _fullscreenClockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _fullscreenClockTimer.Tick += OnFullscreenClockTick;
        UpdateFullscreenPresentation();
    }

    private void DisposeFullscreen()
    {
        _fullscreenReady = false;
        if (_fullscreenClockTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnFullscreenClockTick;
        }
        _fullscreenClockTimer = null;
    }

    internal void ToggleFullscreen()
    {
        if (IsFullscreen)
        {
            WindowState = _beforeFullscreen;
        }
        else
        {
            _beforeFullscreen = WindowState == WindowState.Maximized
                ? WindowState.Maximized : WindowState.Normal;
            WindowState = WindowState.FullScreen;
        }
        UpdateFullscreenPresentation();
        if (FocusManager?.GetFocusedElement() is not Control focused ||
            !focused.IsEffectivelyVisible || ReferenceEquals(focused, this))
            FocusFirst(GamepadScope());
    }

    private void OnFullscreenPressed(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    internal void UpdateGamepadStatus(string? status)
    {
        GamepadStatus.Text = status;
        GamepadStatus.IsVisible = !string.IsNullOrWhiteSpace(status);
        FullscreenHints.Text = GamepadStatus.IsVisible
            ? "D-pad · Move    A · Select    B · Back    LB/RB · Controls    Y · Type    Right stick · Scroll"
            : "F11 · Exit fullscreen";
    }

    private void OnFullscreenClockTick(object? sender, EventArgs e) => UpdateFullscreenClock();

    private void UpdateFullscreenClock()
        => FullscreenClock.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);

    private void UpdateFullscreenPresentation()
    {
        if (!_fullscreenReady)
        {
            return;
        }

        var fullscreen = IsFullscreen;
        TitleBar.IsVisible = !fullscreen;
        ShellRows.RowDefinitions[0].Height = fullscreen ? new GridLength(0) : new GridLength(36);
        FullscreenBar.IsVisible = fullscreen;
        EnterFullscreenButton.IsVisible = !fullscreen;

        // Keep the command bar's measured minimum width and the modal's usable
        // height. Larger screens enlarge the whole interface, including focus.
        var scale = fullscreen ? CalculateFullscreenScale(Bounds.Width, Bounds.Height) : 1;
        FullscreenScaleHost.LayoutTransform = new ScaleTransform(scale, scale);
        if (fullscreen)
        {
            UpdateFullscreenClock();
            _fullscreenClockTimer?.Start();
        }
        else
        {
            _fullscreenClockTimer?.Stop();
        }
    }

    internal static double CalculateFullscreenScale(double width, double height)
        => Math.Clamp(Math.Min(width / 1200, height / 688), 1, 1.5);
}
