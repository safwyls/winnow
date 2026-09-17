using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>Shared credential editing for both Settings surfaces.</summary>
public partial class IgdbSettingsViewModel(
    IIgdbSettingsService? settings = null,
    IUriDispatcher? uris = null, IGameLinkRouter? linkRouter = null) : ObservableObject
{
    [ObservableProperty] public partial string ClientId { get; set; } = string.Empty;
    [ObservableProperty] public partial string ClientSecret { get; set; } = string.Empty;
    [ObservableProperty] public partial bool HasSavedCredentials { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string Status { get; private set; } = "Add your Twitch application credentials to fetch IGDB game details.";

    private bool CanEdit() => !IsBusy;
    private const string FallbackNote = " Credentials from environment variables or configuration will be used when no saved pair is available.";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy || settings is null) return;
        IsBusy = true;
        try
        {
            var snapshot = await settings.LoadAsync(ct);
            ClientId = snapshot.ClientId;
            ClientSecret = string.Empty;
            HasSavedCredentials = snapshot.HasSavedCredentials;
            Status = snapshot.IsReadable
                ? "Credentials are saved on this device. Enter both fields to replace them."
                : HasSavedCredentials
                    ? "Saved credentials need to be re-entered. Enter your client ID and secret, then save."
                        + (snapshot.HasConfigurationCredentials ? FallbackNote : string.Empty)
                    : snapshot.HasConfigurationCredentials
                        ? "IGDB credentials are supplied by environment variables or configuration. A saved pair takes priority."
                        : "Add your Twitch application credentials to fetch IGDB game details.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Status = "Could not read IGDB settings. Restart Winnow to try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        if (IsBusy) return;
        if (settings is null)
        {
            Status = "Credential storage is unavailable. Reopen Winnow and try again.";
            return;
        }
        IsBusy = true;
        try
        {
            var id = ClientId.Trim();
            var result = await settings.SaveAsync(id, ClientSecret);
            if (result == IgdbSettingsSaveResult.MissingFields)
            {
                Status = "Enter both your client ID and client secret before saving.";
                return;
            }
            if (result == IgdbSettingsSaveResult.ProtectionUnavailable)
            {
                Status = "This device could not protect the secret, so nothing was saved. Use Igdb__ClientId and Igdb__ClientSecret environment variables instead.";
                return;
            }
            ClientId = id;
            ClientSecret = string.Empty;
            HasSavedCredentials = true;
            Status = "Credentials saved. Metadata refresh queued. IGDB will check them when fetching details.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Status = "Could not save IGDB credentials. Check that Winnow's data folder is writable, then try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task RemoveAsync()
    {
        if (IsBusy) return;
        if (settings is null)
        {
            Status = "Credential storage is unavailable. Reopen Winnow and try again.";
            return;
        }
        IsBusy = true;
        try
        {
            var hasConfiguration = await settings.RemoveAsync();
            ClientId = string.Empty;
            ClientSecret = string.Empty;
            HasSavedCredentials = false;
            Status = "Saved credentials removed. The change is active now."
                + (hasConfiguration ? FallbackNote : string.Empty);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Status = "Could not remove IGDB credentials. Check that Winnow's data folder is writable, then try again.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenSetupAsync()
    {
        try
        {
            var link = GameLink.Create("Twitch developer console", "https://dev.twitch.tv/console/apps")!;
            if (!(linkRouter is not null ? (await linkRouter.OpenAsync(link, link.Label)).Opened
                : uris is not null && await uris.OpenAsync(new Uri(link.Uri))))
                Status = "Could not open the page. Visit dev.twitch.tv/console/apps to create a Twitch application.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Status = "Could not open the page. Visit dev.twitch.tv/console/apps to create a Twitch application.";
        }
    }
}
