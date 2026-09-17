using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.PluginSdk;

namespace Winnow.App.ViewModels;

public partial class PluginSettingsViewModel(
    IPluginSettingsBackend? backend = null, IUriDispatcher? uris = null, TimeProvider? timeProvider = null,
    IOfficialPluginInstaller? installer = null, IGameLinkRouter? linkRouter = null) : ObservableObject
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private PluginInstallViewModel? _installation;
    public PluginInstallViewModel Installation => _installation ??= new(installer, backend, ShowInstalledSettingsAsync);
    public event Action<PluginCardViewModel>? PluginSettingsRequested;
    private async Task ShowInstalledSettingsAsync(string id)
    {
        await LoadAsync();
        var plugin = Plugins.FirstOrDefault(plugin => plugin.Id == id)
            ?? throw new InvalidOperationException("Installed plugin settings are unavailable.");
        PluginSettingsRequested?.Invoke(plugin);
    }
    public string Title => "Plugins";
    public string SegmentLabel => "PLUGINS";
    public string SegmentTooltip => "Install and configure provider plugins";
    public string IntroMessage => "Manage plugins for library imports, metadata, artwork and recommendations.";
    public const string InstallationNote = "Install official plugins from the Winnow website, or place a plugin ZIP or unpacked plugin in the plugins folder and restart. ZIPs unpack automatically. Manually added plugins need enabling and a restart. Only enable plugins from authors you trust: plugins run with Winnow's access to this device.";
    public const string SecretNote = "Secrets are stored securely on this device and are never shown again. Leave a secret blank to keep its saved value.";
    public ObservableCollection<PluginCardViewModel> Plugins { get; } = [];
    public string UserPluginDirectory => backend?.UserPluginDirectory ?? string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;
    [ObservableProperty] public partial string LoadedPluginSummary { get; private set; } = "Reading loaded plugins…";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (backend is null) return;
        await _loadGate.WaitAsync(ct);
        IsBusy = true;
        foreach (var plugin in Plugins) plugin.ClearSecrets();
        var drafts = Plugins.SelectMany(plugin => plugin.Fields).ToDictionary(editor => editor, editor => editor.Revision);
        try
        {
            var snapshots = await backend.LoadAsync(ct);
            var loaded = snapshots.Where(plugin => plugin.IsLoaded)
                .OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(plugin => $"{plugin.Name} · {plugin.Version}").ToArray();
            LoadedPluginSummary = loaded.Length == 0 ? "No plugins are loaded in this session."
                : string.Join(Environment.NewLine, loaded);
            foreach (var snapshot in snapshots)
            {
                var existing = Plugins.FirstOrDefault(plugin => plugin.Id == snapshot.Id);
                if (existing is null) Plugins.Add(new PluginCardViewModel(snapshot, backend, uris, timeProvider, linkRouter));
                else existing.Apply(snapshot, drafts);
            }
            foreach (var removed in Plugins.Where(plugin => snapshots.All(snapshot => snapshot.Id != plugin.Id)).ToArray())
            { removed.Deactivate(); Plugins.Remove(removed); }
            Status = Plugins.Count == 0 ? "No plugins found. Open the plugins folder to add one, then restart Winnow." : string.Empty;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            LoadedPluginSummary = "Loaded plugins could not be read.";
            Status = "Could not read plugins. Restart Winnow to try again.";
        }
        finally { IsBusy = false; _loadGate.Release(); }
    }

    public void ClearSecrets() { foreach (var plugin in Plugins) plugin.Deactivate(); }
    public void FolderOpenFailed() => Status = "Could not open the plugins folder. Check that Winnow's data folder is available, then try again.";
}

public partial class PluginCardViewModel : ObservableObject
{
    private readonly IPluginSettingsBackend _backend;
    private readonly IUriDispatcher? _uris;
    private readonly IGameLinkRouter? _linkRouter;
    private readonly TimeProvider _time;
    private readonly IReadOnlyList<string> _accountHosts;
    private CancellationTokenSource? _signInCancellation;
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string Version { get; }
    public string Capabilities { get; }
    public string? WebsiteUrl { get; }
    public bool HasWebsite => WebUri(WebsiteUrl) is not null;
    public bool CanConfigure { get; }
    public bool HasAccount { get; }
    public bool HasSettings => Fields.Count > 0;
    public bool HasSecrets => Fields.Any(candidate => candidate.IsSecret);
    public ObservableCollection<PluginSettingFieldViewModel> Fields { get; } = [];
    public IReadOnlyList<PluginSettingFieldViewModel> StandardFields { get; }
    public IReadOnlyList<PluginSettingFieldViewModel> AdvancedFields { get; }
    public bool HasAdvancedSettings => AdvancedFields.Count > 0;
    public bool HasVisibleSecrets => StandardFields.Any(candidate => candidate.IsSecret)
        || AdvancedSettingsExpanded && AdvancedFields.Any(candidate => candidate.IsSecret);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedSettingsLabel), nameof(AdvancedSettingsAccessibleName), nameof(AdvancedSettingsStatus), nameof(HasVisibleSecrets))]
    public partial bool AdvancedSettingsExpanded { get; set; }
    public string AdvancedSettingsLabel => AdvancedSettingsExpanded ? "Hide advanced settings" : "Show advanced settings";
    public string AdvancedSettingsAccessibleName => $"{AdvancedSettingsLabel}: {Name}";
    public string AdvancedSettingsStatus => AdvancedSettingsExpanded ? "Expanded" : "Collapsed";
    [ObservableProperty] public partial bool Enabled { get; private set; }
    [ObservableProperty] public partial bool ActivationSelected { get; set; }
    [ObservableProperty] public partial bool IsLoaded { get; private set; }
    [ObservableProperty] public partial bool RestartRequired { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial bool AccountConnected { get; private set; }
    [ObservableProperty] public partial bool IsConnecting { get; private set; }
    [ObservableProperty] public partial string AccountStatus { get; private set; } = string.Empty;
    [ObservableProperty] public partial string VerificationUrl { get; private set; } = string.Empty;
    [ObservableProperty] public partial string UserCode { get; private set; } = string.Empty;
    public bool HasSignInChallenge => IsConnecting && UserCode.Length > 0;
    public string ConnectAccessibleName => $"Sign in to {Name}";
    public string DisconnectAccessibleName => $"Sign out of {Name}";
    public string CancelSignInAccessibleName => $"Cancel {Name} sign-in";
    public string SignInPageAccessibleName => $"Open {Name} sign-in page";
    public string EnabledLabel => Enabled ? "Disable plugin" : "Enable plugin";
    public string EnabledStatus => Enabled ? "Enabled" : "Disabled";
    public string ToggleAccessibleName => $"{EnabledLabel}: {Name}";
    public string SaveAccessibleName => $"Save {Name} settings";
    public string RefreshAccessibleName => $"Refresh {Name}";
    public string WebsiteAccessibleName => $"Open {Name} website";

    public PluginCardViewModel(PluginSettingsSnapshot snapshot, IPluginSettingsBackend backend, IUriDispatcher? uris = null,
        TimeProvider? timeProvider = null, IGameLinkRouter? linkRouter = null)
    {
        _backend = backend; _uris = uris;
        _linkRouter = linkRouter;
        _time = timeProvider ?? TimeProvider.System;
        _accountHosts = snapshot.AccountHosts ?? [];
        Id = snapshot.Id; Name = snapshot.Name; Description = snapshot.Description;
        Version = snapshot.Version; Capabilities = snapshot.Capabilities; WebsiteUrl = snapshot.WebsiteUrl;
        CanConfigure = snapshot.CanConfigure;
        HasAccount = snapshot.HasAccount;
        foreach (var field in snapshot.Settings) Fields.Add(new(field, this));
        StandardFields = Fields.Where(field => !field.IsAdvanced).ToArray();
        AdvancedFields = Fields.Where(field => field.IsAdvanced).ToArray();
        Apply(snapshot);
    }

    internal void Apply(PluginSettingsSnapshot snapshot, IReadOnlyDictionary<PluginSettingFieldViewModel, int>? drafts = null)
    {
        Enabled = snapshot.Enabled; IsLoaded = snapshot.IsLoaded; RestartRequired = snapshot.RestartRequired;
        ActivationSelected = Enabled;
        Status = snapshot.Status;
        if (!IsConnecting)
        {
            AccountConnected = snapshot.AccountConnected;
            AccountStatus = AccountConnected ? "Signed in." : "Not signed in.";
        }
        foreach (var field in Fields)
        {
            if (snapshot.Settings.FirstOrDefault(value => value.Key == field.Key) is { } value)
                field.Apply(value, drafts is null || !drafts.TryGetValue(field, out var revision) || revision == field.Revision);
        }
        NotifyCommands();
    }

    public void ClearSecrets() { foreach (var field in Fields.Where(field => field.IsSecret)) field.Value = string.Empty; }
    public void Deactivate() { ClearSecrets(); CancelSignIn(); AdvancedSettingsExpanded = false; }
    [RelayCommand] private void ToggleAdvancedSettings() => AdvancedSettingsExpanded = !AdvancedSettingsExpanded;
    private bool CanEdit() => CanConfigure && !IsBusy;
    private bool CanRefresh() => CanConfigure && !IsBusy && Enabled && IsLoaded;
    private bool CanConnect() => CanRefresh() && HasAccount && !AccountConnected;
    private bool CanDisconnect() => CanRefresh() && HasAccount && AccountConnected;
    private bool CanCancelSignIn() => IsConnecting;
    private bool CanOpenSignInPage() => HasSignInChallenge;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (!CanConnect()) return;
        using var cancellation = new CancellationTokenSource();
        _signInCancellation = cancellation;
        IsBusy = true;
        IsConnecting = true;
        AccountStatus = "Preparing sign-in…";
        PluginSignInChallenge? challenge = null;
        var connected = false;
        try
        {
            challenge = await _backend.BeginSignInAsync(Id, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (challenge is null || !PluginSignInValidation.IsValid(challenge, _accountHosts, _time.GetUtcNow()))
            { AccountStatus = "Could not start sign-in. Check the saved settings and try again."; return; }
            VerificationUrl = challenge.VerificationUrl;
            UserCode = challenge.UserCode;
            AccountStatus = "Open the sign-in page and enter this code.";
            using var expiry = new CancellationTokenSource(challenge.ExpiresAt - _time.GetUtcNow(), _time);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, expiry.Token);
            var interval = challenge.PollIntervalSeconds;
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), _time, lifetime.Token);
                    var result = await _backend.PollSignInAsync(Id, challenge.AttemptId, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    switch (result.State)
                    {
                        case PluginSignInState.Connected:
                            connected = AccountConnected = true;
                            AccountStatus = "Signed in. Refresh queued.";
                            return;
                        case PluginSignInState.SlowDown:
                            interval = Math.Min(interval + 5, 300);
                            break;
                        case PluginSignInState.Pending:
                            break;
                        default:
                            AccountStatus = "Sign-in was not completed. Try again when you are ready.";
                            return;
                    }
                }
            }
            catch (OperationCanceledException) when (expiry.IsCancellationRequested && !cancellation.IsCancellationRequested)
            { AccountStatus = "The sign-in code expired. Start sign-in again for a new code."; }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { AccountStatus = "Sign-in cancelled."; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { AccountStatus = "Could not complete sign-in. Check the saved settings and try again."; }
        finally
        {
            VerificationUrl = UserCode = string.Empty;
            if (challenge is not null && !connected)
            {
                try { await _backend.CancelSignInAsync(Id, challenge.AttemptId); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
            }
            _signInCancellation = null;
            IsConnecting = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelSignIn))]
    private void CancelSignIn()
    {
        _signInCancellation?.Cancel();
        VerificationUrl = UserCode = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (!CanDisconnect()) return;
        IsBusy = true;
        try
        {
            await _backend.SignOutAsync(Id);
            AccountConnected = false;
            AccountStatus = "Signed out. Imported games remain in your library.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { AccountStatus = "Could not sign out. Try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanOpenSignInPage))]
    private async Task OpenSignInPageAsync()
    {
        try
        {
            if (HasSignInChallenge && PluginSignInValidation.VerificationUri(VerificationUrl, _accountHosts) is { } uri
                && _uris is not null && await _uris.OpenAsync(uri)) return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        AccountStatus = "Could not open the sign-in page. Open the displayed address in your browser.";
    }
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        var missing = Fields.FirstOrDefault(field => field.IsRequired && !field.IsSecret && string.IsNullOrWhiteSpace(field.Value));
        if (missing is not null)
        {
            if (missing.IsAdvanced) AdvancedSettingsExpanded = true;
            Status = $"Enter {missing.Label.ToLowerInvariant()} before saving.";
            return;
        }
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
        finally
        {
            IsBusy = false;
            // Restore the persisted state if an activation toggle could not be saved.
            ActivationSelected = Enabled;
        }
    }

    [RelayCommand] private Task OpenWebsiteAsync() => OpenWebAsync(WebsiteUrl);
    internal async Task OpenWebAsync(string? url)
    {
        try
        {
            if (WebUri(url) is { } uri && GameLink.Create("Provider website", uri.AbsoluteUri) is { } link
                && (_linkRouter is not null ? (await _linkRouter.OpenAsync(link, Name)).Opened
                    : _uris is not null && await _uris.OpenAsync(uri))) return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        Status = "Could not open the provider website. Try again.";
    }

    internal static Uri? WebUri(string? url) => !string.IsNullOrWhiteSpace(url)
        && !url.Any(char.IsControl) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
    partial void OnIsBusyChanged(bool value) => NotifyCommands();
    partial void OnAccountConnectedChanged(bool value) => NotifyCommands();
    partial void OnIsConnectingChanged(bool value)
    {
        OnPropertyChanged(nameof(HasSignInChallenge));
        CancelSignInCommand.NotifyCanExecuteChanged();
        OpenSignInPageCommand.NotifyCanExecuteChanged();
    }
    partial void OnUserCodeChanged(string value)
    {
        OnPropertyChanged(nameof(HasSignInChallenge));
        OpenSignInPageCommand.NotifyCanExecuteChanged();
    }
    partial void OnEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledLabel)); OnPropertyChanged(nameof(EnabledStatus));
        OnPropertyChanged(nameof(ToggleAccessibleName)); RefreshCommand.NotifyCanExecuteChanged();
    }
    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged(); ToggleEnabledCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
        ConnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged();
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
    public bool IsBoolean { get; }
    public bool IsAdvanced { get; }
    public bool IsText => !IsBoolean;
    public bool BooleanValue { get => bool.TryParse(Value, out var selected) && selected; set => Value = value ? "true" : "false"; }
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
        IsBoolean = snapshot.IsBoolean;
        IsAdvanced = snapshot.IsAdvanced;
        Apply(snapshot);
    }
    internal void Apply(PluginSettingSnapshot snapshot, bool updateValue = true)
    {
        if (updateValue) Value = IsSecret ? string.Empty : snapshot.Value ?? (IsBoolean ? "false" : string.Empty);
        HasStoredSecret = snapshot.HasStoredSecret;
        NotifyCommands();
    }
    partial void OnValueChanged(string value) { Revision++; OnPropertyChanged(nameof(BooleanValue)); }
    private bool CanRemove() => !_owner.IsBusy && IsSecret && HasStoredSecret;
    [RelayCommand(CanExecute = nameof(CanRemove))] private Task RemoveSecretAsync() => _owner.RemoveSecretAsync(this);
    [RelayCommand] private Task OpenSetupAsync() => _owner.OpenWebAsync(SetupUrl);
    internal void NotifyCommands() { OnPropertyChanged(nameof(IsEnabled)); RemoveSecretCommand.NotifyCanExecuteChanged(); }
}
