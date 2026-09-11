using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

public partial class SteamGridDbSettingsViewModel(
    ISteamGridDbSettingsService? settings = null, IUriDispatcher? uris = null) : ObservableObject
{
    public const string Intro = "SteamGridDB adds community artwork for game backgrounds. Enter an API key from your SteamGridDB account.";
    public const string StorageNote = "The key is stored securely on this device. Changes take effect immediately.";
    private const string EmptyStatus = "Add a SteamGridDB API key to fetch community artwork.";
    [ObservableProperty] public partial string ApiKey { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasSavedKey { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = EmptyStatus;
    private bool CanEdit() => !IsBusy;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        ApiKey = string.Empty;
        if (IsBusy || settings is null) return;
        IsBusy = true;
        try
        {
            var state = await settings.LoadAsync(ct);
            HasSavedKey = state.HasStoredKey;
            Status = state.HasStoredKey && state.IsConfigured && state.Source == "saved"
                ? "An API key is saved on this device. Enter a key to replace it."
                : state.IsConfigured
                    ? "A SteamGridDB API key is supplied by environment variables or configuration. A saved key takes priority."
                    : state.HasStoredKey
                        ? "The saved API key needs to be re-entered. Enter your key, then save."
                        : EmptyStatus;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Status = "Could not read SteamGridDB settings. Reopen Winnow to try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        if (settings is null) { Status = "API key storage is unavailable. Reopen Winnow and try again."; return; }
        IsBusy = true;
        try
        {
            var result = await settings.SaveAsync(ApiKey);
            Status = result switch
            {
                SteamGridDbSettingsSaveResult.MissingKey => "Enter your API key before saving.",
                SteamGridDbSettingsSaveResult.InvalidKey => "Check the API key from your SteamGridDB account and try again.",
                SteamGridDbSettingsSaveResult.ProtectionUnavailable => "This device could not protect the key, so nothing was saved. Use the SteamGridDb__ApiKey environment variable instead.",
                _ => "API key saved. Artwork refresh queued. SteamGridDB will check the key when fetching artwork."
            };
            if (result == SteamGridDbSettingsSaveResult.Saved) { ApiKey = string.Empty; HasSavedKey = true; }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Status = "Could not save the SteamGridDB API key. Check that Winnow's data folder is writable, then try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RemoveAsync()
    {
        if (IsBusy) return;
        if (settings is null) { Status = "API key storage is unavailable. Reopen Winnow and try again."; return; }
        IsBusy = true;
        try
        {
            var configured = await settings.RemoveAsync();
            ApiKey = string.Empty;
            HasSavedKey = false;
            Status = "Saved API key removed. The change is active now."
                + (configured ? " The API key from environment variables or configuration remains available." : string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Status = "Could not remove the SteamGridDB API key. Check that Winnow's data folder is writable, then try again."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenSetupAsync()
    {
        try
        {
            if (uris is not null && await uris.OpenAsync(new Uri("https://www.steamgriddb.com/profile/preferences/api"))) return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        Status = "Could not open the browser. Visit steamgriddb.com/profile/preferences/api to get an API key.";
    }
}
