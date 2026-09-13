using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Winnow.App.Services;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>SETTINGS › APPLICATION: window behavior, metadata credentials and updates.</summary>
public partial class ApplicationSettingsViewModel : ObservableObject
{
    internal const string MinimizeToTraySettingKey = "application.minimize_to_tray";
    internal const string CloseToTraySettingKey = "application.close_to_tray";
    internal const string StartInFullscreenSettingKey = "application.start_in_fullscreen";

    private readonly ISettingsRepository? _settings;
    private readonly IStartupRegistration? _startup;
    private readonly IApplicationUpdater? _updater;
    private readonly IUriDispatcher? _uris;
    private bool _refreshingUpdate;
    private bool _loading;
    private Task _linkWrite = Task.CompletedTask;

    public ApplicationSettingsViewModel(
        ISettingsRepository? settings = null,
        IStartupRegistration? startup = null,
        IApplicationUpdater? updater = null,
        IUriDispatcher? uris = null,
        IgdbSettingsViewModel? igdb = null,
        IStoreClientAvailability? storeClients = null)
    {
        _settings = settings;
        _startup = startup;
        _updater = updater;
        _uris = uris;
        LinkDestinationOptions = storeClients?.IsAvailable(GameLink.SteamScheme) == true
            ? ["In Winnow", "System browser", "Store client"] : ["In Winnow", "System browser"];
        Igdb = igdb ?? new IgdbSettingsViewModel();
        if (_updater is not null)
        {
            _updater.Changed += (_, _) =>
            {
                if (Dispatcher.UIThread.CheckAccess()) RefreshUpdate();
                else Dispatcher.UIThread.Post(RefreshUpdate);
            };
            RefreshUpdate();
        }
    }

    public string Title => "Application";
    public IgdbSettingsViewModel Igdb { get; }
    public event Action? SetupRequested;
    [RelayCommand]
    private void OpenSetup() => SetupRequested?.Invoke();
    public string ApplicationVersion => ApplicationBuildInfo.Current.Version;
    public string BuildCommit => ApplicationBuildInfo.Current.Commit;
    public string IntroMessage =>
        "Manage startup, links and updates.";
    public string SegmentLabel => "APPLICATION";
    public string SegmentTooltip => "Startup, links, metadata and updates";

    public IReadOnlyList<string> LinkDestinationOptions { get; }
    public string LinkDestinationNote => LinkDestinationOptions.Count == 3
        ? "Winnow reads supported patch notes. Store client opens Steam store pages. Other pages use your browser."
        : "Winnow reads supported patch notes. Other pages use your browser. Store client is available when Steam is installed on Windows.";
    [ObservableProperty]
    public partial int LinkDestinationIndex { get; set; }
    partial void OnLinkDestinationIndexChanged(int value)
    {
        if (_loading || _settings is null || value < 0 || value >= LinkDestinationOptions.Count) return;
        _linkWrite = SaveLinkDestinationAsync(_linkWrite, (LinkDestination)value);
        PendingSave = _linkWrite;
    }
    private async Task SaveLinkDestinationAsync(Task previous, LinkDestination destination)
    {
        await previous;
        try { await _settings!.SetAsync(GameLinkRouter.SettingKey, GameLinkRouter.Serialize(destination)); Problem = null; }
        catch { Problem = "Couldn't save the link destination. Try again."; }
    }

    public bool HasUpdater => _updater is not null;
    public string AutomaticUpdatesNote => "Check GitHub Releases and download updates in the background. Restart when you are ready.";
    public string BetaUpdatesNote => "Include preview releases. Turn off to receive stable releases only.";
    [ObservableProperty] public partial bool AutomaticUpdates { get; set; }
    [ObservableProperty] public partial bool IncludeBetaReleases { get; set; }
    [ObservableProperty] public partial string UpdateStatus { get; private set; } = "Updates are unavailable in this build.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateRecoveryStatus))]
    public partial string? UpdateRecoveryStatus { get; private set; }
    public bool HasUpdateRecoveryStatus => !string.IsNullOrWhiteSpace(UpdateRecoveryStatus);
    [ObservableProperty] public partial string? AvailableVersion { get; private set; }
    [ObservableProperty] public partial double UpdateProgress { get; private set; }
    [ObservableProperty] public partial bool UpdateBusy { get; private set; }
    [ObservableProperty] public partial bool CanCancelUpdate { get; private set; }
    [ObservableProperty] public partial bool CanCheckUpdate { get; private set; }
    [ObservableProperty] public partial bool CanDownloadUpdate { get; private set; }
    [ObservableProperty] public partial bool CanRestartUpdate { get; private set; }
    [ObservableProperty] public partial bool HasUpdateAction { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateAndRestartCommand))]
    public partial bool CanUpdateAndRestart { get; private set; }
    [ObservableProperty] public partial bool HasReleaseNotes { get; private set; }
    [ObservableProperty] public partial bool HasManualDownload { get; private set; }

    private void RefreshUpdate()
    {
        if (_updater is null) return;
        var snapshot = _updater.Snapshot;
        _refreshingUpdate = true;
        try { AutomaticUpdates = snapshot.Automatic; IncludeBetaReleases = snapshot.IncludeBeta; }
        finally { _refreshingUpdate = false; }
        UpdateStatus = snapshot.Status;
        UpdateRecoveryStatus = snapshot.RecoveryStatus;
        AvailableVersion = snapshot.AvailableVersion;
        UpdateProgress = snapshot.Progress;
        UpdateBusy = snapshot.Busy;
        CanCancelUpdate = snapshot.CanCancel;
        CanCheckUpdate = !snapshot.Busy;
        CanDownloadUpdate = snapshot.CanDownload && !snapshot.Busy;
        CanRestartUpdate = snapshot.CanRestart && !snapshot.Busy;
        HasUpdateAction = snapshot.CanDownload || snapshot.CanRestart || snapshot.CanCancel;
        CanUpdateAndRestart = HasUpdateAction && !snapshot.Busy;
        HasReleaseNotes = snapshot.ReleaseUrl is not null;
        HasManualDownload = snapshot.DownloadUrl is not null;
    }

    partial void OnAutomaticUpdatesChanged(bool value)
    {
        if (!_refreshingUpdate && _updater is not null)
            PendingSave = RunUpdateAsync(() => _updater.SetAutomaticAsync(value));
    }
    partial void OnIncludeBetaReleasesChanged(bool value)
    {
        if (!_refreshingUpdate && _updater is not null)
            PendingSave = RunUpdateAsync(() => _updater.SetIncludeBetaAsync(value));
    }

    [RelayCommand] private Task CheckUpdateAsync() => RunUpdateAsync(() => _updater?.CheckAsync() ?? Task.CompletedTask);
    [RelayCommand] private Task DownloadUpdateAsync() => RunUpdateAsync(() => _updater?.DownloadAsync() ?? Task.CompletedTask);
    [RelayCommand] private Task RestartUpdateAsync() => RunUpdateAsync(() => _updater?.RestartAsync() ?? Task.CompletedTask);
    [RelayCommand(CanExecute = nameof(CanUpdateAndRestart))]
    private Task UpdateAndRestartAsync() => RunUpdateAsync(async () =>
    {
        if (_updater is null || _updater.Snapshot.Busy) return;
        if (_updater.Snapshot.CanDownload) await _updater.DownloadAsync();
        // A cancelled or failed download must never turn this click into a restart.
        if (_updater.Snapshot is { CanRestart: true, Busy: false })
            await _updater.RestartAsync();
    });
    [RelayCommand] private void CancelUpdate() => _updater?.CancelDownload();
    [RelayCommand] private Task OpenReleaseNotesAsync() => OpenUpdateLinkAsync(_updater?.Snapshot.ReleaseUrl);
    [RelayCommand] private Task OpenManualDownloadAsync() => OpenUpdateLinkAsync(_updater?.Snapshot.DownloadUrl);

    private async Task RunUpdateAsync(Func<Task> action)
    {
        try { await action(); }
        catch { UpdateStatus = "Couldn't complete the update action. Try again."; }
    }

    private Task OpenUpdateLinkAsync(string? url) => RunUpdateAsync(async () =>
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || _uris is null || !await _uris.OpenAsync(uri))
            UpdateStatus = "Couldn't open your browser. Try again.";
    });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayIconWanted))]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayIconWanted))]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool StartInFullscreen { get; set; }

    public string FullscreenStartupNote =>
        "Open fullscreen next time you launch Winnow. Windows sign-in still starts quietly in the notification area.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string? Problem { get; private set; }

    /// <summary>The tray icon is normally absent until a tray behavior needs it.</summary>
    public bool TrayIconWanted => MinimizeToTray || CloseToTray;

    public bool IsStartupSupported => _startup?.IsSupported == true;

    public string StartupNote => IsStartupSupported
        ? "Starts quietly in the notification area after you sign in."
        : "Start with Windows is not available on this system.";

    public bool HasProblem => Problem is not null;

    /// <summary>In-flight persistence, exposed so tests can observe toggle completion.</summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    /// <summary>Reads preferences without writing their values back.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var stored = await Task.Run(async () =>
        {
            var minimize = _settings is null
                ? null
                : await _settings.GetAsync(MinimizeToTraySettingKey, ct);
            var close = _settings is null
                ? null
                : await _settings.GetAsync(CloseToTraySettingKey, ct);
            var startup = _startup?.IsSupported == true && _startup.IsEnabled();
            var fullscreen = _settings is null
                ? null
                : await _settings.GetAsync(StartInFullscreenSettingKey, ct);
            var links = _settings is null ? null : await _settings.GetAsync(GameLinkRouter.SettingKey, ct);
            return (minimize, close, startup, fullscreen, links);
        }, ct);

        _loading = true;
        try
        {
            MinimizeToTray = Parse(stored.minimize);
            CloseToTray = Parse(stored.close);
            StartWithWindows = stored.startup;
            StartInFullscreen = Parse(stored.fullscreen);
            var linkIndex = (int)GameLinkRouter.Parse(stored.links);
            LinkDestinationIndex = linkIndex < LinkDestinationOptions.Count ? linkIndex : 1;
            Problem = null;
        }
        finally
        {
            _loading = false;
        }
        await Igdb.LoadAsync(ct);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (!_loading && _settings is not null)
        {
            PendingSave = SavePreferenceAsync(MinimizeToTraySettingKey, value);
        }
    }

    partial void OnCloseToTrayChanged(bool value)
    {
        if (!_loading && _settings is not null)
        {
            PendingSave = SavePreferenceAsync(CloseToTraySettingKey, value);
        }
    }

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (!_loading && _startup?.IsSupported == true)
        {
            PendingSave = SaveStartupAsync(value);
        }
    }

    partial void OnStartInFullscreenChanged(bool value)
    {
        if (!_loading && _settings is not null)
        {
            PendingSave = SavePreferenceAsync(StartInFullscreenSettingKey, value);
        }
    }

    private async Task SavePreferenceAsync(string key, bool value)
    {
        try
        {
            await _settings!.SetAsync(key, Format(value));
            Problem = null;
        }
        catch
        {
            Problem = "Couldn't save that setting just now.";
        }
    }

    private async Task SaveStartupAsync(bool value)
    {
        try
        {
            await Task.Run(() => _startup!.SetEnabled(value));
            Problem = null;
        }
        catch
        {
            _loading = true;
            try
            {
                StartWithWindows = !value;
            }
            finally
            {
                _loading = false;
            }

            Problem = "Windows startup could not be changed.";
        }
    }

    private static bool Parse(string? value)
        => bool.TryParse(value, out var parsed) && parsed;

    private static string Format(bool value) => value ? "true" : "false";
}
