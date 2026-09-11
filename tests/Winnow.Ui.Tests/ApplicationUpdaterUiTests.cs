using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ApplicationUpdaterUiTests
{
    [AvaloniaFact]
    public async Task Beta_and_automatic_preferences_stay_shared_in_both_presentations()
    {
        var updater = new FakeUpdater();
        var settings = new ApplicationSettingsViewModel(updater: updater);
        var shell = Shell(settings);
        using var page = new FullscreenSettingsPage(new FullscreenContext(shell.Library, shell.Feed, shell));
        var desktop = new ApplicationSettingsView { DataContext = settings };
        var television = new Window { Width = 1920, Height = 1080, Content = page };
        var window = new Window { Width = 1000, Height = 900, Content = desktop };
        try
        {
            television.Show(); window.Show();
            FindButton(page, "Application").Focus();
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            var beta = FindButton(page, "Include beta releases");
            Assert.Equal("Off", AutomationProperties.GetItemStatus(beta));
            beta.Focus(); page.Handle(GamepadButtons.Accept);
            await settings.PendingSave;
            Assert.True(updater.Snapshot.IncludeBeta);
            Assert.True(FindSwitch(desktop, "Include beta releases").IsChecked);
            FindSwitch(desktop, "Include beta releases").IsChecked = false;
            await settings.PendingSave;
            Assert.Equal("Off", AutomationProperties.GetItemStatus(beta));
            FindSwitch(desktop, "Automatic background updates").IsChecked = false;
            await settings.PendingSave;
            Assert.False(updater.Snapshot.Automatic);
            Assert.Equal("Off", AutomationProperties.GetItemStatus(FindButton(page, "Automatic background updates")));
        }
        finally { window.Close(); television.Close(); }
    }

    [AvaloniaFact]
    public async Task Background_readiness_updates_both_surfaces_and_restart_requires_activation()
    {
        var updater = new FakeUpdater();
        var settings = new ApplicationSettingsViewModel(updater: updater);
        var shell = Shell(settings);
        using var page = new FullscreenSettingsPage(new FullscreenContext(shell.Library, shell.Feed, shell));
        var desktop = new ApplicationSettingsView { DataContext = settings };
        var television = new Window { Width = 1920, Height = 1080, Content = page };
        var window = new Window { Width = 1000, Height = 900, Content = desktop };
        try
        {
            television.Show(); window.Show();
            FindButton(page, "Application").Focus();
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            var beta = FindButton(page, "Include beta releases"); beta.Focus();
            await Task.Run(() => updater.Publish(updater.Snapshot with
            {
                CanRestart = true, Status = "Ready to update. Restart when you are ready.",
                AvailableVersion = "2.0.0-beta.2", ReleaseUrl = "https://github.com/test/releases/tag/v2.0.0-beta.2"
            }));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(beta, television.FocusManager!.GetFocusedElement());
            Assert.Equal(0, updater.Restarts);
            Assert.True(FindButton(page, "Restart to update").IsEnabled);
            Assert.True(FindButton(desktop, "Restart to update").IsVisible);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == settings.UpdateStatus);
            Assert.Contains(desktop.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == settings.UpdateStatus);
            var restart = FindButton(page, "Restart to update");
            restart.Focus(); page.Handle(GamepadButtons.Accept);
            await settings.RestartUpdateCommand.ExecutionTask!;
            Assert.Equal(1, updater.Restarts);
        }
        finally { window.Close(); television.Close(); }
    }

    [AvaloniaFact]
    public void Fullscreen_hides_unavailable_update_actions_and_reaches_available_links_vertically()
    {
        var updater = new FakeUpdater();
        var settings = new ApplicationSettingsViewModel(updater: updater);
        var shell = Shell(settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context);
        var television = new Window { Width = 1280, Height = 720, Content = page };
        try
        {
            television.Show();
            FindButton(page, "Application").Focus();
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            foreach (var label in new[] { "Download update", "Cancel download", "Restart to update", "Release notes", "Download in browser" })
                Assert.False(FindButton(page, label).IsVisible);

            updater.Publish(updater.Snapshot with
            {
                CanDownload = true,
                ReleaseUrl = "https://github.com/test/releases/tag/v2",
                DownloadUrl = "https://github.com/test/releases/download/v2/setup.exe"
            });
            Dispatcher.UIThread.RunJobs();
            FindButton(page, "Check for updates").Focus();
            foreach (var label in new[] { "Download update", "Release notes", "Download in browser" })
            {
                page.Handle(GamepadButtons.Down);
                Dispatcher.UIThread.RunJobs();
                var action = FindButton(page, label);
                Assert.Same(action, television.FocusManager!.GetFocusedElement());
                Assert.True(action.IsEffectivelyVisible);
                Assert.True(action.IsEffectivelyEnabled);
                var scroll = action.GetVisualAncestors().OfType<ScrollViewer>().First();
                var position = action.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(position.Y, -1, scroll.Viewport.Height - action.Bounds.Height + 1);
            }
            Assert.Contains(page.GetVisualDescendants().OfType<ScrollViewer>(), scroll => scroll.Offset.Y > 0);
            foreach (var label in new[] { "Release notes", "Download update", "Check for updates" })
            {
                page.Handle(GamepadButtons.Up);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(FindButton(page, label), television.FocusManager!.GetFocusedElement());
            }
        }
        finally { television.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_recovers_focus_when_download_actions_change_availability()
    {
        var updater = new FakeUpdater();
        updater.Publish(updater.Snapshot with { CanDownload = true });
        var settings = new ApplicationSettingsViewModel(updater: updater);
        var shell = Shell(settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context);
        var television = new Window { Width = 1280, Height = 720, Content = page };
        try
        {
            television.Show();
            FindButton(page, "Application").Focus();
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            FindButton(page, "Check for updates").Focus();
            page.Handle(GamepadButtons.Down);
            Assert.Same(FindButton(page, "Download update"), television.FocusManager!.GetFocusedElement());
            page.Handle(GamepadButtons.Accept);
            await settings.DownloadUpdateCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, updater.Downloads);
            AssertAvailableFocus();
            Reach("Cancel download");
            page.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            Assert.False(settings.UpdateBusy);
            AssertAvailableFocus();
            Reach("Download update");
            page.Handle(GamepadButtons.Accept);
            await settings.DownloadUpdateCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Reach("Cancel download");
            updater.Publish(updater.Snapshot with { Busy = false, CanCancel = false, CanDownload = false, CanRestart = true });
            Dispatcher.UIThread.RunJobs();
            AssertAvailableFocus();
            Reach("Restart to update");
            Assert.Equal(0, updater.Restarts);
            page.Handle(GamepadButtons.Accept);
            await settings.RestartUpdateCommand.ExecutionTask!;
            Assert.Equal(1, updater.Restarts);

            void AssertAvailableFocus()
            {
                var focused = Assert.IsAssignableFrom<Control>(television.FocusManager!.GetFocusedElement());
                Assert.True(focused.IsEffectivelyVisible);
                Assert.True(focused.IsEffectivelyEnabled);
                Assert.Contains(focused, page.GetVisualDescendants());
            }
            void Reach(string label)
            {
                var target = FindButton(page, label);
                for (var i = 0; i < 20 && !ReferenceEquals(target, television.FocusManager!.GetFocusedElement()); i++)
                {
                    page.Handle(GamepadButtons.Down);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.Same(target, television.FocusManager!.GetFocusedElement());
            }
        }
        finally { television.Close(); }
    }

    [AvaloniaFact]
    public async Task Download_cancellation_retry_and_browser_links_use_shared_commands()
    {
        var updater = new FakeUpdater();
        var uris = new FakeUris();
        var settings = new ApplicationSettingsViewModel(updater: updater, uris: uris);
        await settings.CheckUpdateCommand.ExecuteAsync(null);
        Assert.Equal(1, updater.Checks);
        await settings.DownloadUpdateCommand.ExecuteAsync(null);
        Assert.True(settings.UpdateBusy);
        Assert.True(settings.CanCancelUpdate);
        settings.CancelUpdateCommand.Execute(null);
        Assert.False(settings.UpdateBusy);
        Assert.False(settings.CanCancelUpdate);
        Assert.True(settings.CanDownloadUpdate);
        await settings.DownloadUpdateCommand.ExecuteAsync(null);
        Assert.Equal(2, updater.Downloads);
        updater.Publish(updater.Snapshot with { ReleaseUrl = "https://github.com/test/releases/tag/v2", DownloadUrl = "https://github.com/test/releases/download/v2/setup.exe" });
        await settings.OpenReleaseNotesCommand.ExecuteAsync(null);
        await settings.OpenManualDownloadCommand.ExecuteAsync(null);
        Assert.Equal(2, uris.Opened.Count);
        Assert.Equal(updater.Snapshot.DownloadUrl, uris.Opened[1].AbsoluteUri);
        Assert.Equal(0, updater.Restarts);
    }

    private static MainWindowViewModel Shell(ApplicationSettingsViewModel settings) => new(
        PreviewData.Library, PreviewData.MergeQueue, PreviewData.Stores, PreviewData.Appearance,
        PreviewData.Feed, PreviewData.AccountStats, PreviewData.LibrarySettings, applicationSettings: settings);

    private static Button FindButton(Control view, string label) => view.GetVisualDescendants().OfType<Button>()
        .Single(b => AutomationProperties.GetName(b) == label || Equals(b.Content, label));
    private static ToggleSwitch FindSwitch(Control view, string label) => view.GetVisualDescendants().OfType<ToggleSwitch>()
        .Single(b => AutomationProperties.GetName(b) == label);

    private sealed class FakeUris : IUriDispatcher
    {
        public List<Uri> Opened { get; } = [];
        public Task<bool> OpenAsync(Uri uri) { Opened.Add(uri); return Task.FromResult(true); }
    }

    private sealed class FakeUpdater : IApplicationUpdater
    {
        public event EventHandler? Changed;
        public UpdateSnapshot Snapshot { get; private set; } = new();
        public int Restarts { get; private set; }
        public int Downloads { get; private set; }
        public int Checks { get; private set; }
        public void Publish(UpdateSnapshot snapshot) { Snapshot = snapshot; Changed?.Invoke(this, EventArgs.Empty); }
        public Task CheckAsync(CancellationToken ct = default) { Checks++; Publish(Snapshot with { CanDownload = true }); return Task.CompletedTask; }
        public Task DownloadAsync(CancellationToken ct = default) { Downloads++; Publish(Snapshot with { Busy = true, CanCancel = true, Status = "Downloading update: 25%", Progress = 25 }); return Task.CompletedTask; }
        public void CancelDownload() => Publish(Snapshot with { Busy = false, CanCancel = false, CanDownload = true, Status = "Download cancelled. Try again when you are ready." });
        public Task RestartAsync(CancellationToken ct = default) { Restarts++; return Task.CompletedTask; }
        public Task SetAutomaticAsync(bool value, CancellationToken ct = default) { Publish(Snapshot with { Automatic = value }); return Task.CompletedTask; }
        public Task SetIncludeBetaAsync(bool value, CancellationToken ct = default) { Publish(Snapshot with { IncludeBeta = value }); return Task.CompletedTask; }
    }
}
