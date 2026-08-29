using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Winnow.App.Services;

/// <summary>
/// The one place a URI leaves this application, behind an interface so the
/// launch path can be tested without a window, a shell, or a store client.
///
/// <para>An interface rather than a static call because M3b's whole test story
/// depends on it: the brief for this milestone forbids actually starting a game,
/// and "did Winnow hand the right URI to the OS, declare the right intent, and
/// recover from a refusal" is exactly what needs proving. A fake dispatcher
/// answers all three without a 60GB download.</para>
/// </summary>
public interface IUriDispatcher
{
    /// <summary>
    /// Hands the URI to the operating system's own handler. Returns false when
    /// the platform declined it; never throws.
    /// </summary>
    Task<bool> OpenAsync(Uri uri);
}

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
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window }
            ? window
            : null;
}
