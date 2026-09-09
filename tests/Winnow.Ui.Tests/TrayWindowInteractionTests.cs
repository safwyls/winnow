using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Winnow.App.Design;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class TrayWindowInteractionTests
{
    [AvaloniaFact]
    public void Fullscreen_startup_opens_tv_and_can_return_to_desktop_without_reapplying_preference()
    {
        var settings = PreviewData.ApplicationSettings;
        settings.StartInFullscreen = true;
        var window = new MainWindow { DataContext = PreviewData.Shell };
        try
        {
            window.Show();
            Assert.True(window.IsFullscreen);
            Assert.True(window.FindControl<Control>("TvHost")!.IsVisible);
            window.ToggleFullscreen();
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(window.FindControl<Control>("DesktopHost")!.IsVisible);
            window.Hide(); window.Show();
            Assert.False(window.IsFullscreen);
            settings.StartInFullscreen = false;
            settings.StartInFullscreen = true;
            Assert.False(window.IsFullscreen);
        }
        finally { settings.StartInFullscreen = false; window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public async Task Minimize_to_tray_hides_the_window_and_restore_returns_it_to_the_taskbar()
    {
        var settings = PreviewData.ApplicationSettings;
        settings.MinimizeToTray = true;
        settings.CloseToTray = false;
        var window = new MainWindow { DataContext = PreviewData.Shell };

        try
        {
            window.Show();
            window.WindowState = WindowState.Minimized;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.True(window.IsHiddenInTray);
            Assert.False(window.IsVisible);
            Assert.False(window.ShowInTaskbar);

            window.RestoreFromTray();

            Assert.False(window.IsHiddenInTray);
            Assert.True(window.IsVisible);
            Assert.True(window.ShowInTaskbar);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally
        {
            settings.MinimizeToTray = false;
            window.ExitFromTray();
        }
    }

    [AvaloniaFact]
    public void Close_to_tray_cancels_close_until_the_tray_exit_is_used()
    {
        var settings = PreviewData.ApplicationSettings;
        settings.MinimizeToTray = false;
        settings.CloseToTray = true;
        var window = new MainWindow { DataContext = PreviewData.Shell };

        try
        {
            window.Show();
            window.Close();

            Assert.True(window.IsHiddenInTray);
            Assert.False(window.IsVisible);
            Assert.False(window.ShowInTaskbar);
        }
        finally
        {
            settings.CloseToTray = false;
            window.ExitFromTray();
        }
    }

    [AvaloniaFact]
    public void Background_start_opens_directly_into_the_tray()
    {
        var settings = PreviewData.ApplicationSettings;
        settings.MinimizeToTray = false;
        settings.CloseToTray = false;
        var window = new MainWindow
        {
            DataContext = PreviewData.Shell,
            StartHidden = true,
        };

        try
        {
            settings.StartInFullscreen = true;
            window.Show();

            Assert.True(window.IsHiddenInTray);
            Assert.False(window.IsVisible);
            Assert.False(window.ShowInTaskbar);
            Assert.False(window.IsFullscreen);
            window.RestoreFromTray();
            Assert.True(window.IsVisible);
            Assert.False(window.IsFullscreen);
        }
        finally
        {
            settings.StartInFullscreen = false;
            window.ExitFromTray();
        }
    }
}
