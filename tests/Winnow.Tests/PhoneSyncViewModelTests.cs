using System.Net;
using Microsoft.Extensions.Time.Testing;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Companion;
using Winnow.App.ViewModels;
using Xunit;

namespace Winnow.Tests;

public sealed class PhoneSyncViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Toggle_turns_sync_on_and_reports_the_address()
    {
        var clock = new FakeTimeProvider(Now);
        var service = new FakeCompanion(clock);
        using var model = new PhoneSyncViewModel(service, clock);
        await model.LoadAsync();
        Assert.False(model.Enabled);
        Assert.Equal("Phone sync is off.", model.Status);
        Assert.False(model.StartPairingCommand.CanExecute(null));

        model.Enabled = true;
        await model.Pending;

        Assert.True(service.Enabled);
        Assert.True(model.Running);
        Assert.Equal("192.168.1.20:47630", model.Address);
        Assert.StartsWith("Phone sync is on.", model.Status);
        Assert.True(model.StartPairingCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_refused_command_puts_the_switch_back_and_says_what_failed()
    {
        var clock = new FakeTimeProvider(Now);
        var service = new FakeCompanion(clock) { FailEnable = true };
        using var model = new PhoneSyncViewModel(service, clock);
        await model.LoadAsync();

        model.Enabled = true;
        await model.Pending;

        Assert.False(model.Enabled);
        Assert.Equal("Couldn't turn on phone sync. Try again.", model.Problem);
    }

    [Fact]
    public async Task Pairing_shows_the_code_counts_down_and_closes_when_it_expires()
    {
        var clock = new FakeTimeProvider(Now);
        var service = new FakeCompanion(clock) { Enabled = true };
        using var model = new PhoneSyncViewModel(service, clock);
        await model.LoadAsync();

        await model.StartPairingCommand.ExecuteAsync(null);

        Assert.True(model.IsPairing);
        Assert.Equal("ABCD EFGH JKLM NPQR", model.PairingCode);
        Assert.Contains("c=ABCDEFGHJKLMNPQR", model.QrPayload);
        Assert.Equal("Code expires in 5:00", model.PairingExpiry);

        clock.Advance(TimeSpan.FromSeconds(75));
        model.Tick();
        Assert.Equal("Code expires in 3:45", model.PairingExpiry);

        clock.Advance(TimeSpan.FromMinutes(4));
        model.Tick();
        await model.Pending;
        Assert.False(model.IsPairing);
        Assert.Null(model.QrPayload);
        Assert.Equal("The code expired. Start pairing again for a new code.", model.PairingNote);
    }

    [Fact]
    public async Task A_phone_that_pairs_is_named_and_listed()
    {
        var clock = new FakeTimeProvider(Now);
        var service = new FakeCompanion(clock) { Enabled = true };
        using var model = new PhoneSyncViewModel(service, clock);
        await model.LoadAsync();
        await model.StartPairingCommand.ExecuteAsync(null);

        service.Redeem("Pixel 8");
        await model.RefreshAsync();

        Assert.False(model.IsPairing);
        Assert.Equal("Pixel 8 is paired.", model.PairingNote);
        var device = Assert.Single(model.Devices);
        Assert.Equal("Pixel 8", device.Name);
        Assert.Equal("Paired just now, not synced yet", device.Detail);
    }

    [Fact]
    public async Task Removing_a_phone_revokes_it_and_lists_the_rest()
    {
        var clock = new FakeTimeProvider(Now);
        var service = new FakeCompanion(clock) { Enabled = true };
        service.Devices.Add(new CompanionDevice("a", "Pixel 8", Now.UtcDateTime.AddDays(-3), Now.UtcDateTime.AddHours(-2)));
        service.Devices.Add(new CompanionDevice("b", "iPad", Now.UtcDateTime.AddDays(-1), null));
        using var model = new PhoneSyncViewModel(service, clock);
        await model.LoadAsync();
        Assert.Equal(["Last synced 2 hours ago", "Paired 1 day ago, not synced yet"], model.Devices.Select(d => d.Detail));

        await model.RemoveDeviceCommand.ExecuteAsync(model.Devices[0]);

        Assert.Equal(["b"], service.Devices.Select(d => d.Id));
        Assert.Equal("iPad", Assert.Single(model.Devices).Name);
    }

    [Fact]
    public async Task Without_a_backend_it_stays_hidden_and_inert()
    {
        using var model = new PhoneSyncViewModel();
        await model.LoadAsync();
        model.Enabled = true;
        await model.Pending;
        Assert.False(model.IsAvailable);
        Assert.False(model.Running);
    }

    internal sealed class FakeCompanion(TimeProvider clock) : ICompanionSettingsService
    {
        public bool Enabled { get; set; }
        public bool FailEnable { get; init; }
        public List<CompanionDevice> Devices { get; } = [];
        private CompanionPairing? _pairing;

        public Task<CompanionStatus> StatusAsync(CancellationToken ct = default)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var pairing = _pairing is { } p && p.ExpiresAt > now && Enabled ? p : null;
            return Task.FromResult(new CompanionStatus(Enabled, Enabled, 47630, null, Enabled ? "ab12" : null,
                Enabled ? ["192.168.1.20"] : [], [.. Devices], pairing));
        }

        public Task<CompanionStatus> SetEnabledAsync(bool enabled, CancellationToken ct = default)
        {
            if (enabled && FailEnable) throw new BackendApiException(HttpStatusCode.InternalServerError, "");
            Enabled = enabled;
            if (!enabled) _pairing = null;
            return StatusAsync(ct);
        }

        public Task<CompanionStatus> OpenPairingAsync(CancellationToken ct = default)
        {
            if (!Enabled) throw new BackendApiException(HttpStatusCode.Conflict, "");
            const string code = "ABCDEFGHJKLMNPQR";
            _pairing = new(code, clock.GetUtcNow().UtcDateTime.AddMinutes(5),
                $"winnow-deck://pair?v=1&h=192.168.1.20&p=47630&f=ab12&c={code}&n=PC");
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
            Devices.Add(new CompanionDevice(Guid.NewGuid().ToString("N"), name, clock.GetUtcNow().UtcDateTime, null));
        }
    }
}
