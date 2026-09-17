using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;

namespace Winnow.App.ViewModels;

/// <summary>One explicit metadata operation shared by desktop and fullscreen settings.</summary>
public partial class MetadataSyncViewModel(
    IManualMetadataSyncService? service = null, CancellationToken shutdown = default) : ObservableObject
{
    private int _run;
    public const string Explanation = "Match and update details for games already in your library. Uses cached metadata when available and keeps your manual choices.";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    private bool CanSync() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (IsBusy) return;
        if (service is null)
        {
            Status = "Metadata sync is unavailable. Restart Winnow and try again.";
            return;
        }
        IsBusy = true;
        Status = "Starting metadata sync…";
        var run = ++_run;
        var progress = new Progress<string>(message => { if (IsBusy && run == _run) Status = message; });
        try
        {
            var result = await service.SyncAsync(progress, shutdown);
            Status = result switch
            {
                MetadataSyncResult.MissingCredentials => "Add credentials in IGDB metadata, then try again.",
                MetadataSyncResult.PartialFailure => "Some metadata steps could not finish. Available updates were kept. Try again.",
                MetadataSyncResult.RefreshFailed => "Metadata sync finished, but the library could not refresh. Reopen the library to see updates.",
                _ => "Metadata sync finished. Games without a match may still need a manual match."
            };
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        { Status = "Metadata sync stopped. Updates already saved were kept."; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Status = "Metadata sync could not finish. Check your connection and IGDB credentials, then try again."; }
        finally { IsBusy = false; }
    }
}
