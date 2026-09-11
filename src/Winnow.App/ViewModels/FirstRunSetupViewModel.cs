using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

public enum FirstRunStep { Welcome, Igdb, Steam, Epic, Gog, Theme, Application, Library, Ready }

/// <summary>Shared setup navigation. Each editor owns its existing save and consent behavior.</summary>
public partial class FirstRunSetupViewModel : ObservableObject
{
    private readonly FirstRunSetupService _progress;
    private bool _navigating;
    private bool _loaded;

    public FirstRunSetupViewModel(StoresViewModel stores, AppearanceViewModel appearance,
        ApplicationSettingsViewModel application, LibrarySettingsViewModel librarySettings,
        FirstRunSetupService? progress = null)
    {
        Stores = stores;
        Appearance = appearance;
        Application = application;
        LibrarySettings = librarySettings;
        _progress = progress ?? new FirstRunSetupService();
        Application.SetupRequested += () => ReopenCommand.Execute(null);
        Application.Igdb.PropertyChanged += BusyChanged;
        Stores.PropertyChanged += BusyChanged;
        foreach (var command in new INotifyPropertyChanged[] { Stores.SignInToSteamCommand,
            Stores.SignInToEpicCommand, Stores.SaveSteamApiKeyCommand }) command.PropertyChanged += BusyChanged;
    }

    public StoresViewModel Stores { get; }
    public AppearanceViewModel Appearance { get; }
    public ApplicationSettingsViewModel Application { get; }
    public LibrarySettingsViewModel LibrarySettings { get; }
    [ObservableProperty] public partial bool IsOpen { get; private set; }
    [ObservableProperty] public partial FirstRunStep Step { get; private set; }
    [ObservableProperty] public partial string? Problem { get; private set; }

    public bool IsBusy => _navigating || Application.Igdb.IsBusy || Stores.IsSigningInToSteam
        || Stores.SignInToEpicCommand.IsRunning || Stores.SaveSteamApiKeyCommand.IsRunning || Stores.IsAnyModalOpen;
    public int StepNumber => (int)Step + 1;
    public int StepCount => 9;
    public string ProgressText => $"SETUP · {StepNumber} OF {StepCount}";
    public bool CanGoBack => IsOpen && !IsBusy && Step != FirstRunStep.Welcome;
    public bool CanSkipStep => IsOpen && !IsBusy && Step is not (FirstRunStep.Welcome or FirstRunStep.Ready);
    public string NextLabel => Step switch { FirstRunStep.Welcome => "Get started", FirstRunStep.Ready => "Open my library", _ => "Continue" };
    public string MetadataNote => "Saved IGDB credentials take effect immediately. Metadata fills in in the background.";
    public string Title => Step switch
    {
        FirstRunStep.Welcome => "Welcome to Winnow",
        FirstRunStep.Igdb => "Fill in the details",
        FirstRunStep.Steam => "Your Steam library",
        FirstRunStep.Epic => "Your Epic library",
        FirstRunStep.Gog => "Your GOG library",
        FirstRunStep.Theme => "Make it yours",
        FirstRunStep.Application => "How Winnow fits your desktop",
        FirstRunStep.Library => "Choose what appears",
        _ => "Your library is ready to explore"
    };
    public string Description => Step switch
    {
        FirstRunStep.Welcome => "Bring your games together, choose a look, and make yourself comfortable. Every step is optional. You can change everything later in Settings.",
        FirstRunStep.Igdb => "IGDB adds game details and artwork. Save your credentials here, or skip this step and use the available store metadata.",
        FirstRunStep.Steam => "Installed Steam games are discovered locally. Connecting Steam can add games you own but have never installed.",
        FirstRunStep.Epic => "Installed Epic games are discovered locally. Sign in if you want Winnow to include the rest of your Epic library.",
        FirstRunStep.Gog => "Winnow reads your local Galaxy library automatically. No GOG sign-in is needed or offered. If Galaxy is not installed, you can skip this step.",
        FirstRunStep.Theme => "Choose a theme and adjust the look. Changes apply immediately and are kept if you skip ahead.",
        FirstRunStep.Application => "Choose startup and window behavior. Changes save as you make them; skipping keeps your current preferences.",
        FirstRunStep.Library => "Choose which games Winnow shows. Changes save as you make them, and you can adjust them later in Settings.",
        _ => "Your saved choices are in place. Library discovery and metadata may still be running. Anything you skipped is available in Settings, where you can also reopen setup."
    };

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_loaded) return;
        _loaded = true;
        _navigating = true;
        NotifyNavigation();
        try
        {
            var cursor = await _progress.LoadAsync(ct);
            if (cursor is null) return;
            Step = (FirstRunStep)cursor.Value;
            IsOpen = true;
            Problem = _progress.StartupProblem;
            await PrepareStepAsync();
        }
        catch (OperationCanceledException) { _loaded = false; throw; }
        catch (Exception)
        {
            IsOpen = true;
            Problem = "Could not read setup progress. Continue to try again, or skip setup.";
        }
        finally { _navigating = false; NotifyNavigation(); }
    }

    private bool CanNavigate() => IsOpen && !IsBusy;
    private bool CanReopen() => !IsOpen && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task NextAsync() => MoveAsync(Step == FirstRunStep.Ready ? null : (int)Step + 1, waitForPreferences: true);
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task BackAsync() => CanGoBack ? MoveAsync((int)Step - 1) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanSkipStep))]
    private Task SkipStepAsync() => CanSkipStep ? MoveAsync((int)Step + 1) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private Task SkipAllAsync() => MoveAsync(null);

    [RelayCommand(CanExecute = nameof(CanReopen))]
    private async Task ReopenAsync()
    {
        if (!CanReopen()) return;
        _loaded = true;
        await MoveAsync(0, reopening: true);
    }

    private async Task MoveAsync(int? cursor, bool reopening = false, bool waitForPreferences = false)
    {
        if (IsBusy || (!IsOpen && !reopening)) return;
        _navigating = true;
        Problem = null;
        NotifyNavigation();
        try
        {
            if (waitForPreferences)
            {
                try { await Task.WhenAll(Application.PendingSave, LibrarySettings.PendingSave, Appearance.Service.PendingSave); }
                catch (Exception)
                {
                    Problem = "A preference could not be saved. Try changing it again, or skip this step to continue with your saved settings.";
                    return;
                }
            }
            await _progress.SaveAsync(cursor);
            ClearDrafts();
            if (cursor is null) IsOpen = false;
            else
            {
                Step = (FirstRunStep)cursor.Value;
                IsOpen = true;
                await PrepareStepAsync();
            }
        }
        catch (Exception)
        {
            if (reopening) { Step = FirstRunStep.Welcome; IsOpen = true; }
            Problem = "Could not save setup progress. Check that Winnow's data folder is writable, then try again.";
        }
        finally { _navigating = false; NotifyNavigation(); }
    }

    private async Task PrepareStepAsync()
    {
        if (Step is FirstRunStep.Steam or FirstRunStep.Epic or FirstRunStep.Gog)
        {
            Stores.SelectedPlatform = Step switch
            {
                FirstRunStep.Steam => StorePlatform.Steam,
                FirstRunStep.Epic => StorePlatform.Epic,
                _ => StorePlatform.Gog
            };
            try
            {
                if (Stores.RefreshCommand.ExecutionTask is { IsCompleted: false } refresh) await refresh;
                else await Stores.RefreshCommand.ExecuteAsync(null);
            }
            catch (Exception) { Problem = "Could not refresh platform status. You can continue and reconnect later in Settings."; }
        }
    }

    private void ClearDrafts()
    {
        Application.Igdb.ClientSecret = "";
        Stores.SteamApiKeyInput = "";
    }

    partial void OnStepChanged(FirstRunStep value)
    {
        foreach (var name in new[] { nameof(StepNumber), nameof(ProgressText), nameof(Title), nameof(Description), nameof(NextLabel) }) OnPropertyChanged(name);
        NotifyNavigation();
    }
    partial void OnIsOpenChanged(bool value) => NotifyNavigation();
    private void BusyChanged(object? sender, PropertyChangedEventArgs e) => NotifyNavigation();
    private void NotifyNavigation()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanSkipStep));
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        SkipStepCommand.NotifyCanExecuteChanged();
        SkipAllCommand.NotifyCanExecuteChanged();
        ReopenCommand.NotifyCanExecuteChanged();
    }
}
