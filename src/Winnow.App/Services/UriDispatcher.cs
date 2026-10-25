using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Winnow.App.Services;

/// <summary>
/// The real dispatcher: hands URIs to <c>TopLevel.Launcher</c> (ShellExecute on
/// Windows). Never throws; failures return <c>false</c>.
/// </summary>
public sealed class TopLevelUriDispatcher : IUriDispatcher
{
    public async Task<bool> OpenAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            // The launcher belongs to a TopLevel, which is UI-thread state. The
            // caller is a command handler that may already be on it — InvokeAsync
            // runs inline in that case rather than deferring a frame.
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (MainTopLevel()?.Launcher is not { } launcher)
                {
                    return false;
                }

                return await launcher.LaunchUriAsync(uri);
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }

    private static TopLevel? MainTopLevel()
        => Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window }
            ? window
            : null;
}
