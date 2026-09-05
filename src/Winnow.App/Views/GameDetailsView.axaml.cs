using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the game detail modal. Handles dismissing, opening
/// validated links through the shell, and requesting cover art at detail
/// resolution.
/// </summary>
public partial class GameDetailsView : UserControl
{
    public GameDetailsView()
    {
        InitializeComponent();
        WireMenuRows();
    }

    /// <summary>
    /// Wires the three action-menu rows that need view work on top of their
    /// commands. Two Avalonia facts force this into code, both measured
    /// (docs/spikes/details-action-band-menu.md):
    /// <see cref="MenuItem"/> raises <see cref="MenuItem.ClickEvent"/>
    /// already marked handled, so a XAML <c>Click="..."</c> handler never
    /// fires — only <c>AddHandler</c> with <c>handledEventsToo</c> sees it;
    /// and a name inside a flyout does not reach a code-behind field, so the
    /// rows are found through the trigger's own <c>Flyout</c>.
    /// </summary>
    private void WireMenuRows()
    {
        if (MoreActionsButton.Flyout is not MenuFlyout menu)
        {
            return;
        }

        foreach (var row in menu.Items.OfType<MenuItem>())
        {
            switch (row.Name)
            {
                case "OpenFolderItem":
                    row.AddHandler(MenuItem.ClickEvent, OnOpenFolderPressed, handledEventsToo: true);
                    break;
                case "WrongGameItem":
                    row.AddHandler(MenuItem.ClickEvent, OnWrongGamePressed, handledEventsToo: true);
                    break;
                case "EditDetailsItem":
                    row.AddHandler(MenuItem.ClickEvent, OnEditDetailsPressed, handledEventsToo: true);
                    break;
            }
        }
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

        // Candidate thumbnails decode at the width they are drawn at, and
        // the scaling is a fact of the window rather than of the view model.
        details.IgdbMatch?.SetCoverScaling(scaling);
    }

    /// <summary>
    /// Enter runs the title search, so the field answers the way every
    /// other field in the application does. Handled here rather than by a
    /// KeyBinding because a KeyBinding on the field would fire while the
    /// query is blank, and the command refuses that case regardless.
    /// </summary>
    private void OnMatchQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter
            || DataContext is not GameDetailsViewModel { IgdbMatch: { } match })
        {
            return;
        }

        if (match.SearchCommand.CanExecute(null))
        {
            match.SearchCommand.Execute(null);
        }

        e.Handled = true;
    }

    /// <summary>
    /// The same arrangement <see cref="OnWrongGamePressed"/> uses. The
    /// editor opens in the right column's bounded rest band, below the
    /// fold, so without <c>BringIntoView</c> the disclosure would appear
    /// to do nothing. The scroll is posted at Background priority so it
    /// runs after the command has flipped <c>IsOpen</c> and the surface
    /// has been laid out; a closing press scrolls nothing.
    /// </summary>
    private void OnEditDetailsPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameDetailsViewModel { MetadataEditor: { } editor })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!editor.IsOpen)
                {
                    return;
                }

                MetadataEditorHost.BringIntoView();
            },
            DispatcherPriority.Background);
    }

    private void OnWrongGamePressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameDetailsViewModel { IgdbMatch: { } match })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (!match.IsOpen)
                {
                    return;
                }

                IgdbMatchDisclosure.BringIntoView();
                MatchQueryField.Focus();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// A candidate row reached by Tab may sit outside the bounded list's
    /// viewport. BringIntoView scrolls it in so the focus ring is visible
    /// where the user is. The handler is on the ScrollViewer because
    /// GotFocus bubbles from the rows.
    /// </summary>
    private void OnCandidateGotFocus(object? sender, GotFocusEventArgs e)
    {
        (e.Source as Control)?.BringIntoView();
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

    // Play / Install is a command now, bound straight to the tile's own
    // PrimaryActionCommand (M3b). It left this class for the reason the tile's
    // copy did: a launch has to name the ownership it is launching so the
    // session watcher does not have to infer it, and a handler holding a
    // GameLink knows a URI and nothing else. The links below are unaffected —
    // a store page is a page, and nothing needs to be attributed to it.

    /// <summary>Store page, patch-notes hub — whatever the links row holds.</summary>
    private async void OnLinkPressed(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: GameLink link })
        {
            await OpenAsync(link);
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
            await OpenAsync(link);
        }
    }

    /// <summary>
    /// Tries the embedded panel first; if the reader is unavailable or the
    /// policy refuses the URL, falls back to the system browser silently.
    /// The fallback is silent because both routes open the same page.
    /// </summary>
    private async Task OpenAsync(GameLink link)
    {
        if (DataContext is GameDetailsViewModel details && details.TryReadNotes(link))
        {
            return;
        }

        await LaunchAsync(link);
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
