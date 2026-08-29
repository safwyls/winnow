using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Winnow.App.Services;

/// <summary>
/// The OS file dialog for choosing saved Steam account pages, behind an
/// interface so the saved-file route can be tested without a window, a
/// dialog or a real file.
/// </summary>
public interface ISteamAccountPageFilePicker
{
    /// <summary>
    /// Opens a multi-select open-file dialog and returns local paths. Returns
    /// an empty list when the user dismisses the dialog or when no picked item
    /// has a path on disk. Never throws.
    /// </summary>
    Task<IReadOnlyList<string>> PickAsync(string title, CancellationToken ct = default);
}

/// <summary>
/// The real file picker: goes through <c>TopLevel.StorageProvider</c>,
/// marshals to the UI thread, and filters to saved web pages with an
/// all-files entry because browsers save under many extensions. Never
/// throws; failure is an empty list.
/// </summary>
public sealed class TopLevelSteamAccountPageFilePicker : ISteamAccountPageFilePicker
{
    public async Task<IReadOnlyList<string>> PickAsync(string title, CancellationToken ct = default)
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (MainTopLevel()?.StorageProvider is not { } storage)
                {
                    return (IReadOnlyList<string>)[];
                }

                var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Saved web page")
                        {
                            Patterns = ["*.html", "*.htm", "*.mhtml"],
                            MimeTypes = ["text/html"],
                        },
                        FilePickerFileTypes.All,
                    ],
                });

                var paths = new List<string>(picked.Count);
                foreach (var file in picked)
                {
                    // A picked file that has no local path is one the user
                    // reached through a provider Winnow cannot read from disk.
                    // Dropped here rather than turned into an unreadable path.
                    if (file.TryGetLocalPath() is { Length: > 0 } path)
                    {
                        paths.Add(path);
                    }
                }

                return (IReadOnlyList<string>)paths;
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return [];
        }
    }

    private static TopLevel? MainTopLevel()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window }
            ? window
            : null;
}
