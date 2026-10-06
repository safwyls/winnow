using System.Net;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Companion;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class PhoneSyncSettingsTests
{
    [AvaloniaFact]
    public async Task Desktop_turns_sync_on_shows_a_scannable_code_and_removes_a_phone()
    {
        var service = new FakeCompanion();
        service.Devices.Add(new CompanionDevice("a", "Pixel 8", DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddHours(-3)));
        using var phoneSync = new PhoneSyncViewModel(service);
        var model = new ApplicationSettingsViewModel(phoneSync: phoneSync);
        await phoneSync.LoadAsync();
        var view = new ApplicationSettingsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 1400, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var card = view.FindControl<Border>("PhoneSyncCard")!;
            Assert.True(card.IsEffectivelyVisible);
            var start = view.FindControl<Button>("StartPairingButton")!;
            Assert.False(start.IsEffectivelyEnabled);

            var toggle = view.FindControl<ToggleSwitch>("PhoneSyncToggle")!;
            Assert.Equal("Sync with phones", AutomationProperties.GetName(toggle));
            toggle.IsChecked = true;
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            Assert.True(service.Enabled);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Address: 192.168.1.20:47630");

            Assert.True(start.IsEffectivelyEnabled);
            start.Command!.Execute(null);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            var qr = view.FindControl<QrCodeView>("PhoneSyncQr")!;
            Assert.True(qr.IsEffectivelyVisible);
            Assert.True(qr.ModuleCount >= 21);
            Assert.Equal("Pairing QR code", AutomationProperties.GetName(qr));
            Assert.Equal("ABCD EFGH JKLM NPQR", view.FindControl<SelectableTextBlock>("PhoneSyncCode")!.Text);
            Assert.StartsWith("Code expires in 4:", view.FindControl<TextBlock>("PhoneSyncExpiry")!.Text);
            Assert.False(start.IsEffectivelyVisible);

            view.FindControl<Button>("StopPairingButton")!.Command!.Execute(null);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            Assert.False(qr.IsEffectivelyVisible);

            var remove = view.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove Pixel 8");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "Last synced 3 hours ago");
            remove.Command!.Execute(remove.CommandParameter);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(service.Devices);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == "No phones are paired.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fullscreen_controller_turns_sync_on_pairs_and_removes_a_phone()
    {
        var service = new FakeCompanion();
        using var phoneSync = new PhoneSyncViewModel(service);
        var preview = PreviewData.Shell;
        var app = new ApplicationSettingsViewModel(phoneSync: phoneSync);
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            preview.Appearance, preview.Feed, preview.AccountStats, preview.LibrarySettings,
            applicationSettings: app);
        using var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        window.Show();
        try
        {
            // Settings › Application › Phone sync, by controller.
            context.Push(new FullscreenSettingsPage(context, "Application"));
            Dispatcher.UIThread.RunJobs();
            var settings = Assert.IsType<FullscreenSettingsPage>(view.CurrentPage);
            var entry = settings.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Phone sync");
            entry.Focus();
            settings.Handle(GamepadButtons.Accept);
            Dispatcher.UIThread.RunJobs();
            var page = Assert.IsType<FullscreenPhoneSyncPage>(view.CurrentPage);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();

            Button Find(string name) => page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            page.FocusInitial();
            Assert.Same(Find("Turn on phone sync"), TopLevel.GetTopLevel(page)!.FocusManager!.GetFocusedElement());
            page.Handle(GamepadButtons.Accept);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            Assert.True(service.Enabled);
            Assert.True(Find("Turn off phone sync").IsFocused);

            page.Handle(GamepadButtons.Down);
            Assert.True(Find("Start pairing").IsFocused);
            page.Handle(GamepadButtons.Accept);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            var qr = page.GetVisualDescendants().OfType<QrCodeView>().Single();
            Assert.True(qr.ModuleCount >= 21);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ABCD EFGH JKLM NPQR");

            service.Redeem("Pixel 8");
            await phoneSync.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(page.GetVisualDescendants().OfType<QrCodeView>());
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Pixel 8 is paired.");

            var remove = Find("Remove Pixel 8");
            remove.Focus();
            page.Handle(GamepadButtons.Accept);
            await phoneSync.Pending;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(service.Devices);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No phones are paired.");
            Assert.True(Find("Back").IsFocused);
        }
        finally { window.Close(); }
    }

    private sealed class FakeCompanion : ICompanionSettingsService
    {
        public bool Enabled { get; private set; }
        public List<CompanionDevice> Devices { get; } = [];
        private CompanionPairing? _pairing;

        public Task<CompanionStatus> StatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new CompanionStatus(Enabled, Enabled, 47630, null, Enabled ? "ab12" : null,
                Enabled ? ["192.168.1.20"] : [], [.. Devices], Enabled ? _pairing : null));

        public Task<CompanionStatus> SetEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            Enabled = enabled;
            return StatusAsync(ct);
        }

        public Task<CompanionStatus> OpenPairingAsync(CancellationToken ct = default)
        {
            if (!Enabled) throw new BackendApiException(HttpStatusCode.Conflict, "");
            _pairing = new("ABCDEFGHJKLMNPQR", DateTime.UtcNow.AddMinutes(5),
                "winnow-deck://pair?v=1&h=192.168.1.20&p=47630&f=ab12&c=ABCDEFGHJKLMNPQR&n=PC");
            return StatusAsync(ct);
        }

        public Task<CompanionStatus> ClosePairingAsync(CancellationToken ct = default)
        {
            _pairing = null;
            return StatusAsync(ct);
        }

        public Task RemoveDeviceAsync(string id, CancellationToken ct = default)
        {
            Devices.RemoveAll(d => d.Id == id);
            return Task.CompletedTask;
        }

        public void Redeem(string name)
        {
            _pairing = null;
            Devices.Add(new CompanionDevice("p-" + name, name, DateTime.UtcNow, null));
        }
    }
}
