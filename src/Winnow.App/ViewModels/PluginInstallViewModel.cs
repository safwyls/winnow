using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>One installation state is presented by both desktop and fullscreen settings.</summary>
public partial class PluginInstallViewModel(
    IOfficialPluginInstaller? installer,
    IPluginSettingsBackend? backend,
    Func<string, Task> showSettings) : ObservableObject
{
    private readonly SemaphoreSlim _requests = new(1, 1);
    private PluginInstallRequest? _request;
    [ObservableProperty] public partial string Status { get; private set; } = string.Empty;
    [ObservableProperty] public partial string PluginId { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool HasRequest { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CanRetry { get; private set; }

    public async Task InstallAsync(PluginInstallRequest request, CancellationToken ct = default, Action? showProgress = null)
    {
        await _requests.WaitAsync(ct);
        _request = request;
        PluginId = request.PluginId;
        HasRequest = true;
        CanRetry = false;
        IsBusy = true;
        Status = "Preparing plugin installation…";
        var reporting = true;
        try
        {
            showProgress?.Invoke();
            if (installer is null) throw new InvalidOperationException();
            var progress = new Progress<PluginInstallProgress>(value =>
            {
                if (reporting && IsBusy && ReferenceEquals(_request, request)) Status = value.Message;
            });
            var result = await installer.InstallAsync(request, progress, ct);
            reporting = false;
            Status = result.Message;
            if (result.Outcome == PluginInstallOutcome.Failed) CanRetry = true;
            else
            {
                if (result.Outcome == PluginInstallOutcome.Installed && backend is not null)
                    await backend.RefreshAsync(result.PluginId, ct);
                await showSettings(result.PluginId);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Status = "Plugin installation cancelled. Try again when you’re ready.";
            CanRetry = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Status = "Could not finish the plugin installation. Try again.";
            CanRetry = true;
        }
        finally { reporting = false; IsBusy = false; _requests.Release(); }
    }

    private bool CanRetryInstall() => CanRetry && !IsBusy;
    [RelayCommand(CanExecute = nameof(CanRetryInstall))]
    private Task RetryAsync() => _request is { } request ? InstallAsync(request) : Task.CompletedTask;
}
