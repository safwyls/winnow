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

public sealed class ManualMetadataSyncTests
{
    [AvaloniaFact]
    public async Task Desktop_enter_starts_one_shared_operation_and_fullscreen_reports_progress_and_completion()
    {
        var backend = new SyncService();
        var model = new MetadataSyncViewModel(backend);
        var settings = new EnrichmentSettingsViewModel(new(), metadataSync: model);
        var shell = Shell(settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Metadata & artwork");
        using var fullscreen = new FullscreenView(context);
        var window = new MainWindow { DataContext = shell, Width = 1280, Height = 900 };
        var television = new Window { Width = 1920, Height = 1080, Content = fullscreen };
        try
        {
            window.Show(); television.Show(); Flush();
            shell.ShowEnrichmentSettingsCommand.Execute(null); context.Push(page); Flush();
            var desktop = window.GetVisualDescendants().OfType<EnrichmentSettingsView>().Single();
            var button = SyncButton(desktop);
            button.Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Flush();
            Assert.Equal(1, backend.Calls);
            Assert.True(model.IsBusy);
            Assert.False(button.IsEffectivelyEnabled);
            Assert.False(SyncButton(page).IsEffectivelyEnabled);
            Assert.False(model.SyncCommand.CanExecute(null));
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            SyncButton(page).Focus(); page.Handle(GamepadButtons.Accept); Flush();
            Assert.Equal(1, backend.Calls);
            backend.Progress!.Report("Matching games with IGDB…"); Flush();
            Assert.Equal("Matching games with IGDB…", model.Status);
            AssertStatus(desktop, model.Status); AssertStatus(page, model.Status);
            Capture(window, "manual-metadata-desktop-running");
            Capture(television, "manual-metadata-fullscreen-running");
            backend.Complete(MetadataSyncResult.Completed);
            await model.SyncCommand.ExecutionTask!; Flush();
            Assert.False(model.IsBusy);
            Assert.True(button.IsEffectivelyEnabled); Assert.True(SyncButton(page).IsEffectivelyEnabled);
            Assert.Contains("finished", model.Status);
            AssertStatus(desktop, model.Status); AssertStatus(page, model.Status);
            var completed = model.Status;
            backend.Progress!.Report("Late progress from the completed operation"); Flush();
            Assert.Equal(completed, model.Status);
        }
        finally { backend.Complete(MetadataSyncResult.Completed); window.Close(); television.Close(); }
    }

    [AvaloniaFact]
    public async Task Controller_sync_retains_state_when_settings_close_and_reopen()
    {
        var backend = new SyncService();
        var model = new MetadataSyncViewModel(backend);
        var settings = new EnrichmentSettingsViewModel(new(), metadataSync: model);
        var shell = Shell(settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var original = new FullscreenSettingsPage(context, "Metadata & artwork");
        var window = new Window { Width = 1920, Height = 1080, Content = original };
        try
        {
            window.Show(); Flush();
            SyncButton(original).Focus(); original.Handle(GamepadButtons.Accept); Flush();
            Assert.Equal(1, backend.Calls);
            original.Handle(GamepadButtons.Down); Flush();
            Assert.Equal("IGDB metadata", AutomationProperties.GetName(
                Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement())));
            window.Content = null; original.Dispose();
            backend.Progress!.Report("Updating game details…"); Flush();
            using var reopened = new FullscreenSettingsPage(context, "Metadata & artwork");
            window.Content = reopened; Flush();
            Assert.False(SyncButton(reopened).IsEffectivelyEnabled);
            AssertStatus(reopened, "Updating game details…");
            backend.Complete(MetadataSyncResult.Completed);
            await model.SyncCommand.ExecutionTask!; Flush();
            Assert.True(SyncButton(reopened).IsEffectivelyEnabled);
            var desktop = new EnrichmentSettingsView { DataContext = settings };
            window.Content = desktop; Flush();
            AssertStatus(desktop, model.Status);
            Assert.Equal(1, backend.Calls);
        }
        finally { backend.Complete(MetadataSyncResult.Completed); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(MetadataSyncResult.MissingCredentials, "credentials in IGDB metadata")]
    [InlineData(MetadataSyncResult.PartialFailure, "Try again")]
    [InlineData(MetadataSyncResult.RefreshFailed, "Reopen the library")]
    public async Task Failure_guidance_is_shared_and_controller_can_retry(MetadataSyncResult result, string guidance)
    {
        var backend = new SyncService();
        var model = new MetadataSyncViewModel(backend);
        var settings = new EnrichmentSettingsViewModel(new(), metadataSync: model);
        var shell = Shell(settings);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var page = new FullscreenSettingsPage(context, "Metadata & artwork");
        var desktop = new EnrichmentSettingsView { DataContext = settings };
        var window = new Window { Width = 1100, Height = 800, Content = desktop };
        var television = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); television.Show(); Flush();
            SyncButton(desktop).Focus(); window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            backend.Complete(result); await model.SyncCommand.ExecutionTask!; Flush();
            Assert.Contains(guidance, model.Status);
            AssertStatus(desktop, model.Status); AssertStatus(page, model.Status);
            Assert.True(SyncButton(desktop).IsEffectivelyEnabled); Assert.True(SyncButton(page).IsEffectivelyEnabled);
            backend.Reset();
            SyncButton(page).Focus(); page.Handle(GamepadButtons.Accept); Flush();
            Assert.Equal(2, backend.Calls);
            Assert.True(model.IsBusy);
            backend.Complete(MetadataSyncResult.Completed);
            await model.SyncCommand.ExecutionTask!; Flush();
            Assert.Contains("finished", model.Status);
            AssertStatus(desktop, model.Status); AssertStatus(page, model.Status);
        }
        finally { backend.Complete(MetadataSyncResult.Completed); window.Close(); television.Close(); }
    }

    [AvaloniaFact]
    public async Task Unexpected_failure_is_actionable_and_does_not_leave_sync_disabled()
    {
        var backend = new SyncService();
        var model = new MetadataSyncViewModel(backend);
        var run = model.SyncCommand.ExecuteAsync(null);
        backend.Fail(new HttpRequestException("private diagnostic detail"));
        await run;
        Assert.Contains("Check your connection and IGDB credentials", model.Status);
        Assert.DoesNotContain("private diagnostic detail", model.Status);
        Assert.False(model.IsBusy); Assert.True(model.SyncCommand.CanExecute(null));
        backend.Reset();
        run = model.SyncCommand.ExecuteAsync(null);
        backend.Complete(MetadataSyncResult.Completed); await run;
        Assert.Equal(2, backend.Calls);
        Assert.Contains("finished", model.Status);
    }

    private static Button SyncButton(Control view) => view.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == "Sync metadata now");

    private static void AssertStatus(Control view, string expected)
    {
        var status = view.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "MetadataSyncStatus"
            || AutomationProperties.GetAutomationId(text) == "MetadataSyncStatus");
        Assert.True(status.IsEffectivelyVisible);
        Assert.Equal(expected, status.Text);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
    }

    private static MainWindowViewModel Shell(EnrichmentSettingsViewModel settings)
    {
        var preview = PreviewData.Shell;
        return new(preview.Library, preview.MergeQueue, preview.Stores, preview.Appearance,
            preview.Feed, preview.AccountStats, preview.LibrarySettings, enrichmentSettings: settings);
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Flush();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }

    private sealed class SyncService : IManualMetadataSyncService
    {
        private TaskCompletionSource<MetadataSyncResult> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public IProgress<string>? Progress { get; private set; }
        public Task<MetadataSyncResult> SyncAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        { Calls++; Progress = progress; return _pending.Task; }
        public void Complete(MetadataSyncResult result) => _pending.TrySetResult(result);
        public void Fail(Exception error) => _pending.TrySetException(error);
        public void Reset() => _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
