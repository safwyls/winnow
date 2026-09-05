using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Winnow.App.Services;

/// <summary>
/// Opens the OS file dialog and returns the local path of a chosen image,
/// or null when the dialog is dismissed.
/// </summary>
public interface IImageFilePicker
{
    /// <summary>
    /// Shows a file-picker dialog titled <paramref name="title"/> and returns the
    /// chosen file's local path, or null if the user dismissed the dialog.
    /// </summary>
    Task<string?> PickAsync(string title, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IImageFilePicker"/> that opens Avalonia's
/// <see cref="IStorageProvider"/> file dialog on the main window.
/// </summary>
public sealed class TopLevelImageFilePicker : IImageFilePicker
{
    /// <summary>Accepted image file extensions, offered as one group plus an all-files fallback.</summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        "jpg",
        "jpeg",
        "png",
        "webp",
        "gif",
        "bmp",
    ];

    /// <inheritdoc/>
    public async Task<string?> PickAsync(string title, CancellationToken ct = default)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (MainTopLevel()?.StorageProvider is not { } storage)
                {
                    return null;
                }

                var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType(ViewModels.GameMetadataEditorCopy.ImageFileTypeLabel)
                        {
                            Patterns = [.. Extensions.Select(e => $"*.{e}")],
                        },
                        FilePickerFileTypes.All,
                    ],
                });

                // A picked file with no local path came through a storage
                // provider Winnow cannot read from disk. Dropped rather than
                // turned into an unreadable path.
                return picked.Count > 0 && picked[0].TryGetLocalPath() is { Length: > 0 } path
                    ? path
                    : null;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }

    private static TopLevel? MainTopLevel()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window }
            ? window
            : null;
}
