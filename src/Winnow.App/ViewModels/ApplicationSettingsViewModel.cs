using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>SETTINGS › APPLICATION: window lifetime and Windows sign-in behavior.</summary>
public partial class ApplicationSettingsViewModel : ObservableObject
{
    internal const string MinimizeToTraySettingKey = "application.minimize_to_tray";
    internal const string CloseToTraySettingKey = "application.close_to_tray";

    private readonly ISettingsRepository? _settings;
    private readonly IStartupRegistration? _startup;
    private bool _loading;

    public ApplicationSettingsViewModel(
        ISettingsRepository? settings = null,
        IStartupRegistration? startup = null)
    {
        _settings = settings;
        _startup = startup;
    }

    public string Title => "Application";
    public string ApplicationVersion => ApplicationBuildInfo.Current.Version;
    public string BuildCommit => ApplicationBuildInfo.Current.Commit;
    public string IntroMessage =>
        "Choose where Winnow waits when you are not browsing your library.";
    public string SegmentLabel => "APPLICATION";
    public string SegmentTooltip => "Window and startup behavior";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayIconWanted))]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrayIconWanted))]
    public partial bool CloseToTray { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

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
            return (minimize, close, startup);
        }, ct);

        _loading = true;
        try
        {
            MinimizeToTray = Parse(stored.minimize);
            CloseToTray = Parse(stored.close);
            StartWithWindows = stored.startup;
            Problem = null;
        }
        finally
        {
            _loading = false;
        }
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
