using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the game detail modal. Its whole job is the platform reach
/// the view model must not have: dismissing, opening a target through the
/// shell, and asking the cover cache for art at detail resolution.
///
/// <para><b>Every outbound target arrives here already validated.</b> The
/// handlers below read a <see cref="GameLink"/> off the pressed control's data
/// context and hand its URI to <c>TopLevel.Launcher</c>; none of them builds,
/// concatenates or repairs a URL. A string that failed
/// <see cref="GameLink.Create"/> is a null link, which the view binds
/// <c>IsVisible</c> to — so an unopenable target is a button that was never
/// rendered rather than one that fails when pressed.</para>
/// </summary>
public partial class GameDetailsView : UserControl
{
    public GameDetailsView()
    {
        InitializeComponent();
    }

    /// <summary>Raised when the user dismisses — the shell owns what "closed" means.</summary>
    public event EventHandler? CloseRequested;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RequestCover();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestCover();
    }

    private void RequestCover()
    {
        if (DataContext is not GameDetailsViewModel details)
        {
            return;
        }

        // Display resolution, not source resolution (§5.4) — the cache snaps
        // this to a bucket, so this is one decode shared with nothing else.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        details.RequestCover(GameDetailsViewModel.CoverWidth * scaling);
    }

    /// <summary>
    /// Only a press that lands on the scrim itself closes. Without the source
    /// check, a press that started inside the card and drifted — selecting the
    /// install path, say, which is now a real gesture — would dismiss the panel
    /// out from under the user.
    /// </summary>
    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnClosePressed(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Play / Install. <c>steam://run/&lt;appid&gt;</c> hands the launch to
    /// Steam's own protocol handler, which is the difference between an
    /// affordance and a button that opens a web page about launching.
    /// </summary>
    private async void OnPrimaryActionPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameDetailsViewModel { PrimaryAction: { } action })
        {
            await LaunchAsync(action);
        }
    }

    /// <summary>Store page, patch-notes hub — whatever the links row holds.</summary>
    private async void OnLinkPressed(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: GameLink link })
        {
            await LaunchAsync(link);
        }
    }

    /// <summary>
    /// §5.2: "clicking the badge opens the patch notes for the updates you
    /// missed". The row's link is null unless the stored URL was absolute http(s)
    /// (update_events.url is captured from a network response, so it is
    /// untrusted), and a null link never rendered a button.
    /// </summary>
    private async void OnUpdateLinkPressed(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: UpdateEventViewModel { Link: { } link } })
        {
            await LaunchAsync(link);
        }
    }

    /// <summary>
    /// The install directory, through the launcher's own directory entry point
    /// rather than as a <c>file:</c> URI. <see cref="GameLink"/> refuses that
    /// scheme deliberately — it would let any stored string become a shell open
    /// — and this is the one local target the design actually wants, reached by
    /// a path the app read from Steam's manifests rather than by a URL.
    /// </summary>
    private async void OnOpenFolderPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameDetailsViewModel { OpenableFolder: { } folder })
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        try
        {
            var directory = new DirectoryInfo(folder);
            if (directory.Exists)
            {
                await launcher.LaunchDirectoryInfoAsync(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The path came from Steam's manifests and the drive may be gone.
            // Nothing to say about it that the user cannot see for themselves.
        }
    }

    /// <summary>
    /// The single place a URI reaches the platform. The launcher is the OS's own
    /// handler — the app never shells out to a browser or to steam.exe by name.
    /// </summary>
    private async Task LaunchAsync(GameLink link)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is not { } launcher)
        {
            return;
        }

        // Re-parse rather than trust the string that reached us: the only way
        // to build a GameLink is through its factory, but the launcher call is
        // the boundary and a boundary checks.
        if (!Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        await launcher.LaunchUriAsync(uri);
    }
}
