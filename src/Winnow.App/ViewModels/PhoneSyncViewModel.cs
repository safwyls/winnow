using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Api.Contracts.Companion;

namespace Winnow.App.ViewModels;

/// <summary>One paired phone as both Settings surfaces show it.</summary>
public sealed record PhoneSyncDevice(string Id, string Name, string Detail);

/// <summary>Phone sync for both Settings surfaces: the on/off switch, pairing and paired phones.
/// The backend owns the listener; this view model only reads its status and sends commands.</summary>
public partial class PhoneSyncViewModel : ObservableObject, IDisposable
{
    private readonly ICompanionSettingsService? _service;
    private readonly TimeProvider _clock;
    private ITimer? _countdown;
    private bool _applying;
    private DateTime? _expiresAt;

    public PhoneSyncViewModel(ICompanionSettingsService? service = null, TimeProvider? clock = null)
    {
        _service = service;
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsAvailable => _service is not null;
    public string Intro => "Show your Winnow library in Winnow Deck on your phone. Phones read your library over your local network while Winnow is running. They cannot change it.";

    [ObservableProperty] public partial bool Enabled { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPairingCommand))]
    public partial bool Running { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartPairingCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPairingCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveDeviceCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty] public partial string Status { get; private set; } = "Phone sync is off.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }
    public bool HasProblem => Problem is not null;

    /// <summary>Where phones reach this PC, for a user checking their network.</summary>
    [ObservableProperty] public partial string Address { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPairing))]
    [NotifyCanExecuteChangedFor(nameof(StopPairingCommand))]
    public partial string? QrPayload { get; private set; }
    public bool IsPairing => QrPayload is not null;

    /// <summary>The code in groups of four, for reading it against the phone.</summary>
    [ObservableProperty] public partial string PairingCode { get; private set; } = string.Empty;
    [ObservableProperty] public partial string PairingExpiry { get; private set; } = string.Empty;

    /// <summary>What happened to the last pairing code: a phone used it, or it expired.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairingNote))]
    public partial string? PairingNote { get; private set; }
    public bool HasPairingNote => PairingNote is not null;

    public ObservableCollection<PhoneSyncDevice> Devices { get; } = [];
    [ObservableProperty] public partial bool HasDevices { get; private set; }

    /// <summary>The last status refresh or command, exposed so tests can wait for it.</summary>
    public Task Pending { get; private set; } = Task.CompletedTask;

    public Task LoadAsync(CancellationToken ct = default) => Pending = RunAsync(() => _service!.StatusAsync(ct),
        "Couldn't read phone sync status. Restart Winnow to try again.", busy: false);

    /// <summary>Called when another client or a phone changed phone sync.</summary>
    public Task RefreshAsync(CancellationToken ct = default) => LoadAsync(ct);

    partial void OnEnabledChanged(bool value)
    {
        if (_applying || _service is null) return;
        Pending = RunAsync(() => _service.SetEnabledAsync(value), value
            ? "Couldn't turn on phone sync. Try again."
            : "Couldn't turn off phone sync. Try again.");
    }

    private bool CanStartPairing() => Running && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanStartPairing))]
    private Task StartPairingAsync() => Pending = RunAsync(() => _service!.OpenPairingAsync(),
        "Couldn't start pairing. Check that phone sync is on, then try again.");

    private bool CanStopPairing() => IsPairing && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanStopPairing))]
    private Task StopPairingAsync() => Pending = RunAsync(() => _service!.ClosePairingAsync(),
        "Couldn't stop pairing. Try again.");

    private bool CanRemoveDevice(PhoneSyncDevice? device) => device is not null && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanRemoveDevice))]
    private Task RemoveDeviceAsync(PhoneSyncDevice? device) => Pending = RunAsync(async () =>
    {
        await _service!.RemoveDeviceAsync(device!.Id);
        return await _service.StatusAsync();
    }, $"Couldn't remove {device?.Name}. Try again.");

    private async Task RunAsync(Func<Task<CompanionStatus>> action, string failure, bool busy = true)
    {
        if (_service is null) return;
        if (busy) { IsBusy = true; PairingNote = null; }
        try
        {
            Apply(await action());
            Problem = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Problem = failure;
            // A failed command leaves the switch where the backend has it, not where the user put it.
            try { Apply(await _service.StatusAsync()); }
            catch (Exception again) when (again is not OutOfMemoryException and not StackOverflowException) { }
        }
        finally { if (busy) IsBusy = false; }
    }

    private void Apply(CompanionStatus status)
    {
        _applying = true;
        try { Enabled = status.Enabled; }
        finally { _applying = false; }
        Running = status.Running;
        Address = status.Running && status.Addresses.Count > 0
            ? string.Join(", ", status.Addresses.Select(a => $"{a}:{status.Port}"))
            : string.Empty;
        Status = status.Problem
            ?? (!status.Enabled ? "Phone sync is off."
                : !status.Running ? "Phone sync is starting."
                : "Phone sync is on. Paired phones on this network can read your library.");

        var now = _clock.GetUtcNow().UtcDateTime;
        var known = Devices.Select(d => d.Id).ToHashSet();
        Devices.Clear();
        foreach (var device in status.Devices.OrderByDescending(d => d.LastSyncAt ?? d.PairedAt))
            Devices.Add(new PhoneSyncDevice(device.Id, device.Name, device.LastSyncAt is { } synced
                ? "Last synced " + Ago(now, synced)
                : "Paired " + Ago(now, device.PairedAt) + ", not synced yet"));
        HasDevices = Devices.Count > 0;

        if (status.Pairing is { } pairing && pairing.ExpiresAt > now)
        {
            _expiresAt = pairing.ExpiresAt;
            PairingCode = string.Join(' ', pairing.Code.Chunk(4).Select(c => new string(c)));
            QrPayload = pairing.QrPayload;
            UpdateExpiry();
            _countdown ??= _clock.CreateTimer(_ => Dispatcher.UIThread.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        else if (_expiresAt is not null)
        {
            EndPairing();
            if (status.Devices.Where(d => !known.Contains(d.Id)).MaxBy(d => d.PairedAt) is { } paired)
                PairingNote = $"{paired.Name} is paired.";
        }
    }

    /// <summary>Advances the countdown; closes the code once it expires.</summary>
    internal void Tick()
    {
        if (_expiresAt is null) return;
        if (_expiresAt <= _clock.GetUtcNow().UtcDateTime)
        {
            EndPairing();
            PairingNote = "The code expired. Start pairing again for a new code.";
            // The backend may have paired a phone just before expiry.
            if (_service is not null && !IsBusy) Pending = RunAsync(() => _service.StatusAsync(), "Couldn't read phone sync status. Restart Winnow to try again.", busy: false);
        }
        else UpdateExpiry();
    }

    private void UpdateExpiry()
    {
        var left = _expiresAt!.Value - _clock.GetUtcNow().UtcDateTime;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        PairingExpiry = string.Create(CultureInfo.InvariantCulture, $"Code expires in {(int)left.TotalMinutes}:{left.Seconds:00}");
    }

    private void EndPairing()
    {
        _expiresAt = null;
        _countdown?.Dispose();
        _countdown = null;
        QrPayload = null;
        PairingCode = string.Empty;
        PairingExpiry = string.Empty;
    }

    private static string Ago(DateTime now, DateTime then)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return Plural((int)span.TotalMinutes, "minute") + " ago";
        if (span < TimeSpan.FromDays(1)) return Plural((int)span.TotalHours, "hour") + " ago";
        if (span < TimeSpan.FromDays(30)) return Plural((int)span.TotalDays, "day") + " ago";
        return "on " + then.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Plural(int count, string unit) => $"{count} {unit}{(count == 1 ? "" : "s")}";

    public void Dispose()
    {
        _countdown?.Dispose();
        _countdown = null;
        GC.SuppressFinalize(this);
    }
}
