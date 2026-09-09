using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenInteractionTests
{
    [AvaloniaFact]
    public void Fullscreen_footer_and_keyboard_fit_the_smallest_supported_content_area()
    {
        var shell = PreviewData.Shell;
        shell.Library.Lightbox.CloseCommand.Execute(null);
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.Library.Prompt?.CancelCommand.Execute(null);
        shell.Feed.ListPrompt?.CancelCommand.Execute(null);
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 688 };
        try
        {
            window.Show();
            shell.ShowLibraryCommand.Execute(null);
            window.ToggleFullscreen();
            window.UpdateGamepadStatus("Controller battery medium");
            Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<TextBox>("SearchBox")!;
            search.Focus(NavigationMethod.Tab);
            Assert.Same(search, window.FocusManager!.GetFocusedElement());
            window.HandleGamepad(App.Services.GamepadButtons.Keyboard);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetVisualDescendants().OfType<GamepadKeyboardView>());
            var footer = window.FindControl<Border>("FullscreenBar")!;
            var exit = window.FindControl<Button>("ExitFullscreenButton")!;
            var position = exit.TranslatePoint(default, window)!.Value;
            Assert.True(position.X + exit.Bounds.Width <= window.ClientSize.Width + 1);
            Assert.True(position.Y + exit.Bounds.Height <= window.ClientSize.Height + 1);
            Assert.True(footer.Bounds.Height <= 48);
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, "fullscreen-keyboard.png"));
            }
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Fullscreen_restores_each_window_state_and_the_caption()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                window.WindowState = state;
                window.ToggleFullscreen();
                Assert.Equal(WindowState.FullScreen, window.WindowState);
                Assert.False(window.FindControl<Border>("TitleBar")!.IsVisible);
                Assert.True(window.FindControl<Border>("FullscreenBar")!.IsVisible);
                Assert.False(string.IsNullOrWhiteSpace(window.FindControl<TextBlock>("FullscreenClock")!.Text));
                window.ToggleFullscreen();
                Assert.Equal(state, window.WindowState);
                Assert.True(window.FindControl<Border>("TitleBar")!.IsVisible);
                Assert.False(window.FindControl<Border>("FullscreenBar")!.IsVisible);
                Assert.Equal(1, ((ScaleTransform)window.FindControl<LayoutTransformControl>("FullscreenScaleHost")!.LayoutTransform!).ScaleX);
            }
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Buttons_and_F11_toggle_without_a_library_context()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.FindControl<Button>("EnterFullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.IsFullscreen);
            window.FindControl<Button>("ExitFullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(window.IsFullscreen);
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Assert.True(window.IsFullscreen);
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Assert.False(window.IsFullscreen);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void External_restore_clears_fullscreen_presentation_and_unknown_battery_is_not_invented()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            window.ToggleFullscreen();
            window.UpdateGamepadStatus("Controller connected");
            Assert.Equal("Controller connected", window.FindControl<TextBlock>("GamepadStatus")!.Text);
            window.UpdateGamepadStatus(null);
            Assert.False(window.FindControl<TextBlock>("GamepadStatus")!.IsVisible);
            window.WindowState = WindowState.Normal;
            Assert.False(window.FindControl<Border>("FullscreenBar")!.IsVisible);
            Assert.True(window.FindControl<Border>("TitleBar")!.IsVisible);
        }
        finally { window.ExitFromTray(); }
    }

    [Theory]
    [InlineData(1200, 688, 1)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(3840, 2160, 1.5)]
    [InlineData(1200, 1080, 1)]
    [InlineData(1920, 688, 1)]
    public void Scaling_reserves_usable_logical_space(double width, double height, double expected)
        => Assert.Equal(expected, MainWindow.CalculateFullscreenScale(width, height));
}
