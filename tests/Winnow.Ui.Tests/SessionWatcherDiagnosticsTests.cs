using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Monitor;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class SessionWatcherDiagnosticsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_shells_show_background_failure_without_taking_focus_and_clear_on_recovery(bool fullscreen)
    {
        var health = new SessionWatcherHealth();
        var directory = Path.Combine(Path.GetTempPath(), "winnow-health-" + Guid.NewGuid());
        string? opened = null;
        var diagnostics = new DiagnosticsViewModel(health, directory, path => opened = path);
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            preview.Appearance, preview.Feed, preview.AccountStats, preview.LibrarySettings,
            applicationSettings: new ApplicationSettingsViewModel(diagnostics: diagnostics));
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var full = fullscreen ? new FullscreenView(context) : null;
        var window = fullscreen ? new Window { Content = full, Width = 1920, Height = 1080 }
            : new MainWindow { DataContext = shell, Width = 1200, Height = 640 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var notice = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name ==
                (fullscreen ? "FullscreenSessionWatcherNotice" : "SessionWatcherNotice"));
            Assert.False(notice.IsVisible);
            var focus = window.FocusManager!.GetFocusedElement();
            await Task.Run(() => health.ReportFailure(SessionWatcherOperation.ExecutableIndex, new InvalidCastException()));
            Dispatcher.UIThread.RunJobs();
            Assert.True(notice.IsVisible);
            Assert.Same(focus, window.FocusManager.GetFocusedElement());
            Assert.True(notice.Bounds.Height > 0);
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } capture)
            {
                Directory.CreateDirectory(capture);
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(capture, fullscreen ? "watcher-fullscreen.png" : "watcher-desktop.png"));
            }
            if (fullscreen)
            {
                full!.Handle(GamepadButtons.Menu);
                Dispatcher.UIThread.RunJobs();
                var action = full.CurrentPage.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Open logs folder"));
                Assert.True(action.Focus());
                full.Handle(GamepadButtons.Accept);
            }
            else
            {
                var action = notice.GetVisualDescendants().OfType<Button>().Single();
                Assert.True(action.Focus());
                action.Command!.Execute(null);
            }
            Assert.Equal(Path.Combine(directory, "logs"), opened);
            health.ReportSuccess(SessionWatcherOperation.Tick);
            Assert.True(diagnostics.HasWatcherFailure);
            health.ReportSuccess(SessionWatcherOperation.ExecutableIndex);
            Dispatcher.UIThread.RunJobs();
            Assert.False(notice.IsVisible);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void Settings_keep_log_access_after_recovery_on_both_surfaces()
    {
        var app = new ApplicationSettingsViewModel();
        var desktop = new ApplicationSettingsView { DataContext = app };
        var desktopWindow = new Window { Content = desktop, Width = 1000, Height = 800 };
        desktopWindow.Show();
        try
        {
            Assert.Same(app.Diagnostics.OpenLogsCommand, desktop.FindControl<Button>("OpenLogsButton")!.Command);
            using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
            using var page = new FullscreenSettingsPage(context, "Application");
            desktopWindow.Content = page;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(page.GetVisualDescendants().OfType<Button>(), b =>
                AutomationProperties.GetName(b) == "Open logs folder" || Equals(b.Content, "Open logs folder") ||
                b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Open logs folder"));
        }
        finally { desktopWindow.Close(); }
    }

    [AvaloniaFact]
    public void Failed_folder_open_exposes_manual_path_and_retry_clears_it()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-health-" + Guid.NewGuid());
        var fail = true;
        var model = new DiagnosticsViewModel(dataDirectory: directory,
            openFolder: _ => { if (fail) throw new IOException(); });
        try
        {
            model.OpenLogsCommand.Execute(null);
            Assert.Contains(model.LogsDirectory, model.Problem);
            fail = false;
            model.OpenLogsCommand.Execute(null);
            Assert.Null(model.Problem);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
