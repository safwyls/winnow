using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class PluginInstallInteractionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Installation_reports_progress_retries_from_input_and_opens_settings(bool fullscreen, bool alreadyInstalled)
    {
        var backend = new Backend();
        var installer = new Installer(alreadyInstalled);
        var plugins = new PluginSettingsViewModel(backend, installer: installer);
        var preview = PreviewData.Shell;
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings,
            enrichmentSettings: new EnrichmentSettingsViewModel(new(), plugins));
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var presentation = fullscreen ? new FullscreenView(context) : null;
        using var installPage = fullscreen ? new FullscreenPluginInstallPage(context) : null;
        if (installPage is not null) context.Push(installPage);
        Control view = presentation is null ? new PluginSettingsView { DataContext = plugins } : presentation;
        var window = new Window { Width = fullscreen ? 1920 : 1200, Height = 1080, Content = view };
        window.Show();
        try
        {
            var install = plugins.Installation.InstallAsync(new("psn", "v0.2.0"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(plugins.Installation.IsBusy);
            Assert.Contains(view.GetVisualDescendants().OfType<ProgressBar>(), bar => bar.IsEffectivelyVisible);
            var status = Status(view);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Equal("Downloading the plugin…", status.Text);
            Capture(window, fullscreen ? "fullscreen-plugin-install-progress" : "desktop-plugin-install-progress");
            installer.First.TrySetResult(new(PluginInstallOutcome.Failed, "psn", "The download failed. Try again."));
            await install;
            Dispatcher.UIThread.RunJobs();
            Assert.False(plugins.Installation.IsBusy);
            Assert.Equal("The download failed. Try again.", Status(view).Text);
            var retry = view.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "Retry plugin installation");
            Assert.True(retry.IsEffectivelyVisible);
            Assert.True(retry.Focus());
            Capture(window, fullscreen ? "fullscreen-plugin-install-failed" : "desktop-plugin-install-failed");
            if (installPage is not null) Assert.True(installPage.Handle(GamepadButtons.Accept));
            else
            {
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            }
            await plugins.Installation.RetryCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, installer.Calls);
            Assert.Equal(alreadyInstalled ? 0 : 1, backend.Refreshes);
            Assert.Equal("Plugin settings are ready.", Status(view).Text);
            Assert.Equal("psn", Assert.Single(plugins.Plugins).Id);
            if (fullscreen) Assert.Contains(view.GetVisualDescendants(), control => control is FullscreenPluginSettingsPage);
            else Assert.Contains(view.GetVisualDescendants().OfType<Control>(), control =>
                ReferenceEquals(control.DataContext, plugins.Plugins[0]) && control.IsEffectivelyVisible);
            Assert.False(plugins.Installation.CanRetry);
            Capture(window, fullscreen ? "fullscreen-plugin-installed" : "desktop-plugin-installed");
        }
        finally { installer.First.TrySetCanceled(); window.Close(); }
    }

    private static TextBlock Status(Control view) => view.GetVisualDescendants().OfType<TextBlock>()
        .Single(control => control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == "PluginInstallStatus");

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class Installer(bool alreadyInstalled) : IOfficialPluginInstaller
    {
        public readonly TaskCompletionSource<PluginInstallResult> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Task<PluginInstallResult> InstallAsync(PluginInstallRequest request, IProgress<PluginInstallProgress>? progress = null, CancellationToken ct = default)
        {
            Calls++;
            progress?.Report(new("Downloading the plugin…"));
            return Calls == 1 ? First.Task : Task.FromResult(new PluginInstallResult(
                alreadyInstalled ? PluginInstallOutcome.AlreadyInstalled : PluginInstallOutcome.Installed, "psn", "Plugin settings are ready."));
        }
    }

    private sealed class Backend : IPluginSettingsBackend
    {
        public string UserPluginDirectory => "plugins";
        public int Refreshes { get; private set; }
        public Task<IReadOnlyList<PluginSettingsSnapshot>> LoadAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PluginSettingsSnapshot>>([new("psn", "PlayStation", "Import your PlayStation library.",
                "1.0.0", "Library sources", true, true, false, "", [])]);
        public Task RefreshAsync(string pluginId, CancellationToken ct = default) { Refreshes++; return Task.CompletedTask; }
        public Task SaveAsync(string pluginId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveSecretAsync(string pluginId, string key, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
    }
}
