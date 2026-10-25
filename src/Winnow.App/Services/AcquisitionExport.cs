using System.Text;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Winnow.App.Services;

public interface IAcquisitionExportDestination
{
    /// <summary>Returns false when the user cancels; failures propagate to the visible status.</summary>
    Task<bool> SaveAsync(string csv, CancellationToken ct = default);
}

public sealed class TopLevelAcquisitionExportDestination : IAcquisitionExportDestination
{
    public async Task<bool> SaveAsync(string csv, CancellationToken ct = default)
    {
        var file = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window } || !window.StorageProvider.CanSave)
            {
                throw new IOException("The save dialog is unavailable.");
            }

            return await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export acquisition CSV",
                SuggestedFileName = "winnow-acquisitions.csv",
                DefaultExtension = "csv",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }],
            });
        });
        if (file is null) return false;
        using (file)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            await writer.WriteAsync(csv.AsMemory(), ct);
        }
        return true;
    }
}
