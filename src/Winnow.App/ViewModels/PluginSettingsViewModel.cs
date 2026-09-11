using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

public partial class PluginSettingsViewModel(
    IPluginSettingsBackend? backend = null, IUriDispatcher? uris = null) : ObservableObject
{
    public string Title => "Plugins";
    public string SegmentLabel => "PLUGINS";
    public string SegmentTooltip => "Install and configure provider plugins";
    public string IntroMessage => "Manage plugins for library imports, metadata, artwork and recommendations.";
    public const string InstallationNote = "Place an unpacked plugin in the plugins folder, restart Winnow, then enable it here. Only enable plugins from authors you trust: plugins run with Winnow's access to this device.";
    public const string SecretNote = "Secrets are stored securely on this device and are never shown again. Leave a secret blank to keep its saved value.";
    public ObservableCollection<PluginCardViewModel> Plugins { get; } = [];
    public string UserPluginDirectory => backend?.UserPluginDirectory ?? string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy || backend is null) return;
        IsBusy = true;
        ClearSecrets();
        var drafts = Plugins.SelectMany(plugin => plugin.Fields).ToDictionary(editor => editor, editor => editor.Revision);
        try
        {
            var snapshots = await backend.LoadAsync(ct);
            foreach (var snapshot in snapshots)
            {
                var existing = Plugins.FirstOrDefault(plugin => plugin.Id == snapshot.Id);
                if (existing is null) Plugins.Add(new PluginCardViewModel(snapshot, backend, uris));
                else existing.Apply(snapshot, drafts);
            }
            foreach (var removed in Plugins.Where(plugin => snapshots.All(snapshot => snapshot.Id != plugin.Id)).ToArray())
            { removed.ClearSecrets(); Plugins.Remove(removed); }
            Status = Plugins.Count == 0 ? "No plugins found. Open the plugins folder to add one, then restart Winnow." : string.Empty;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Status = "Could not read plugins. Restart Winnow to try again."; }
        finally { IsBusy = false; }
    }

    public void ClearSecrets() { foreach (var plugin in Plugins) plugin.ClearSecrets(); }
    public void FolderOpenFailed() => Status = "Could not open the plugins folder. Check that Winnow's data folder is available, then try again.";
}

public partial class PluginCardViewModel : ObservableObject
{
    private readonly IPluginSettingsBackend _backend;
    private readonly IUriDispatcher? _uris;
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Version { get; }
    public string Capabilities { get; }
    public string? WebsiteUrl { get; }
    public bool HasWebsite => WebUri(WebsiteUrl) is not null;
    public bool CanConfigure { get; }
    public bool HasSettings => Fields.Count > 0;
    public bool HasSecrets => Fields.Any(candidate => candidate.IsSecret);
    public ObservableCollection<PluginSettingFieldViewModel> Fields { get; } = [];
    [ObservableProperty] public partial bool Enabled { get; private set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial bool RestartRequired { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    public string EnabledLabel => Enabled ? "Disable plugin" : "Enable plugin";
    public string EnabledStatus => Enabled ? "Enabled" : "Disabled";
    public string ToggleAccessibleName => $"{EnabledLabel}: {Name}";
    public string SaveAccessibleName => $"Save {Name} settings";
    public string RefreshAccessibleName => $"Refresh {Name}";
    public string WebsiteAccessibleName => $"Open {Name} website";

    public PluginCardViewModel(PluginSettingsSnapshot snapshot, IPluginSettingsBackend backend, IUriDispatcher? uris = null)
    {
        _backend = backend; _uris = uris;
        Id = snapshot.Id; Name = snapshot.Name; Description = snapshot.Description;
        Version = snapshot.Version; Capabilities = snapshot.Capabilities; WebsiteUrl = snapshot.WebsiteUrl;
        CanConfigure = snapshot.CanConfigure;
        foreach (var field in snapshot.Settings) Fields.Add(new(field, this));
        Apply(snapshot);
    }

    internal void Apply(PluginSettingsSnapshot snapshot, IReadOnlyDictionary<PluginSettingFieldViewModel, int>? drafts = null)
    {
        Enabled = snapshot.Enabled; IsLoaded = snapshot.IsLoaded; RestartRequired = snapshot.RestartRequired;
        Status = snapshot.Status;
        foreach (var field in Fields)
        {
            if (snapshot.Settings.FirstOrDefault(value => value.Key == field.Key) is { } value)
                field.Apply(value, drafts is null || !drafts.TryGetValue(field, out var revision) || revision == field.Revision);
        }
        NotifyCommands();
    }

    public void ClearSecrets() { foreach (var field in Fields.Where(field => field.IsSecret)) field.Value = string.Empty; }
    private bool CanEdit() => CanConfigure && !IsBusy;
    private bool CanRefresh() => CanConfigure && !IsBusy && Enabled && IsLoaded;
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        var missing = Fields.FirstOrDefault(field => field.IsRequired && !field.IsSecret && string.IsNullOrWhiteSpace(field.Value));
        if (missing is not null) { Status = $"Enter {missing.Label.ToLowerInvariant()} before saving."; return; }
        var values = Fields.Where(field => !field.IsSecret || !string.IsNullOrWhiteSpace(field.Value))
            .ToDictionary(field => field.Key, field => field.Value);
        if (values.Count == 0) { Status = "Enter a setting or secret before saving."; return; }
        await RunAsync(() => _backend.SaveAsync(Id, values),
            "Settings saved." + (Enabled && IsLoaded ? " Refresh queued." : " Enable the plugin and restart Winnow to use them."),
            "Could not save plugin settings. Check that secure storage is available and Winnow's data folder is writable, then try again.");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private Task ToggleEnabledAsync() => RunAsync(() => _backend.SetEnabledAsync(Id, !Enabled),
        "Plugin setting saved. Restart Winnow to apply the change.",
        "Could not change this plugin. Check that Winnow's data folder is writable, then try again.");

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => RunAsync(() => _backend.RefreshAsync(Id), "Refresh queued.",
        "Could not queue a plugin refresh. Restart Winnow to try again.");

    internal Task RemoveSecretAsync(PluginSettingFieldViewModel field) => RunAsync(() => _backend.RemoveSecretAsync(Id, field.Key),
        "Saved secret removed. Any configured fallback remains available.",
        "Could not remove the saved secret. Check that Winnow's data folder is writable, then try again.");

    private async Task RunAsync(Func<Task> action, string success, string failure)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action();
            ClearSecrets();
            var snapshot = (await _backend.LoadAsync()).FirstOrDefault(plugin => plugin.Id == Id);
            if (snapshot is not null) Apply(snapshot);
            Status = success;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { Status = failure; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task OpenWebsiteAsync() => OpenWebAsync(WebsiteUrl);
    internal async Task OpenWebAsync(string? url)
    {
        try
        {
            if (WebUri(url) is { } uri && _uris is not null && await _uris.OpenAsync(uri)) return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        Status = "Could not open the provider website. Check your default browser and try again.";
    }

    internal static Uri? WebUri(string? url) => !string.IsNullOrWhiteSpace(url)
        && !url.Any(char.IsControl) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledLabel)); OnPropertyChanged(nameof(EnabledStatus));
        OnPropertyChanged(nameof(ToggleAccessibleName)); RefreshCommand.NotifyCanExecuteChanged();
    }
    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged(); ToggleEnabledCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
        foreach (var field in Fields) field.NotifyCommands();
    }
}

public partial class PluginSettingFieldViewModel : ObservableObject
{
    private readonly PluginCardViewModel _owner;
    public string Key { get; }
    public string Label { get; }
    public string? Description { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool IsSecret { get; }
    public bool IsRequired { get; }
    public char PasswordChar => IsSecret ? '●' : '\0';
    public string AccessibleName => $"{_owner.Name} {Label}";
    public string RemoveAccessibleName => $"Remove saved {_owner.Name} {Label}";
    public string SetupLabel => $"Get {Label.ToLowerInvariant()}";
    public string SetupAccessibleName => $"Get {_owner.Name} {Label}";
    public string? SetupUrl { get; }
    public bool HasSetup => PluginCardViewModel.WebUri(SetupUrl) is not null;
    public bool IsEnabled => !_owner.IsBusy;
    public string Watermark => IsSecret ? "Enter a secret to save or replace it" : string.Empty;
    [ObservableProperty] public partial string Value { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasStoredSecret { get; private set; }
    internal int Revision { get; private set; }

    public PluginSettingFieldViewModel(PluginSettingSnapshot snapshot, PluginCardViewModel owner)
    {
        _owner = owner; Key = snapshot.Key; Label = snapshot.Label; Description = snapshot.Description;
        IsSecret = snapshot.IsSecret; IsRequired = snapshot.IsRequired; SetupUrl = snapshot.SetupUrl;
        Apply(snapshot);
    }
    internal void Apply(PluginSettingSnapshot snapshot, bool updateValue = true)
    {
        if (updateValue) Value = IsSecret ? string.Empty : snapshot.Value ?? string.Empty;
        HasStoredSecret = snapshot.HasStoredSecret;
        NotifyCommands();
    }
    partial void OnValueChanged(string value) => Revision++;
    private bool CanRemove() => !_owner.IsBusy && IsSecret && HasStoredSecret;
    [RelayCommand(CanExecute = nameof(CanRemove))] private Task RemoveSecretAsync() => _owner.RemoveSecretAsync(this);
    [RelayCommand] private Task OpenSetupAsync() => _owner.OpenWebAsync(SetupUrl);
    internal void NotifyCommands() { OnPropertyChanged(nameof(IsEnabled)); RemoveSecretCommand.NotifyCanExecuteChanged(); }
}
