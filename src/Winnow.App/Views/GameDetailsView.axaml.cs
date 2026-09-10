using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the game detail modal. Handles dismissing, opening
/// validated links through the shell, and requesting cover art at detail
/// resolution.
/// </summary>
public partial class GameDetailsView : UserControl
{
    private GameDetailsViewModel? _observedDetails;
    private bool _wasFocusedView;
    private readonly List<MenuItem> _linkRows = [];

    public GameDetailsView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => RequestBackdrop();
        ScreenshotScroll.AddHandler(PointerWheelChangedEvent, OnScreenshotWheel, RoutingStrategies.Tunnel);
        WireMenuRows();
        MetadataEditorView.CloseRequested += OnSectionClosed;

        // The previewer gets the populated modal; runtime leaves the
        // DataContext to the shell. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.GameDetails;
        }
    }

    /// <summary>
    /// Both tool views return to the persistent More trigger when their own close
    /// control is used. The selected tab and its scroll position remain in place.
    /// </summary>
    private void OnSectionClosePressed(object? sender, RoutedEventArgs e)
        => OnSectionClosed(sender, EventArgs.Empty);

    private void OnSectionClosed(object? sender, EventArgs e) => MoreActionsButton.Focus();

    private void OnDetailsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or "" or nameof(GameDetailsViewModel.Links)) RefreshLinkRows();
        if (e.PropertyName != nameof(GameDetailsViewModel.IsFocusedView)
            || _observedDetails is not { } details) return;

        var wasFocused = _wasFocusedView;
        _wasFocusedView = details.IsFocusedView;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(DataContext, details) || !IsVisible) return;
            if (details.IsMatchFocused) MatchQueryField.Focus(NavigationMethod.Tab);
            else if (details.IsMetadataFocused)
            {
                // The editor may still be loading its first field rows. Back
                // remains an available keyboard destination throughout that load.
                var field = MetadataEditorView.GetVisualDescendants().OfType<TextBox>()
                    .FirstOrDefault(control => control.IsEffectivelyVisible && control.IsEnabled);
                if (field is not null) field.Focus(NavigationMethod.Tab);
                else BackToDetailsButton.Focus(NavigationMethod.Tab);
            }
            else if (wasFocused) MoreActionsButton.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Background);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape && DataContext is GameDetailsViewModel { IsFocusedView: true } details)
        {
            details.BackToDetailsCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void OnUpdatesShortcutPressed(object? sender, RoutedEventArgs e)
        => Dispatcher.UIThread.Post(() => UpdatesTab.Focus(NavigationMethod.Tab), DispatcherPriority.Background);

    private void OnDetailsTabsKeyDown(object? sender, KeyEventArgs e)
    {
        // Arrow keys use TabControl's native selection behavior. Home and End
        // apply only on the tab strip, so fields and timelines keep their keys.
        if (e.Handled || e.Source is not TabItem || e.Key is not (Key.Home or Key.End)
            || DataContext is not GameDetailsViewModel details) return;
        details.SelectedTabIndex = e.Key == Key.Home ? 0 : 4;
        (e.Key == Key.Home ? OverviewTab : LibraryTab).Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private void RefreshLinkRows()
    {
        if (MoreActionsButton?.Flyout is not MenuFlyout menu) return;
        foreach (var row in _linkRows) menu.Items.Remove(row);
        _linkRows.Clear();
        if (DataContext is not GameDetailsViewModel details) return;
        foreach (var link in details.Links)
        {
            var row = new MenuItem { Header = link.Label, DataContext = link };
            ToolTip.SetTip(row, link.Tooltip);
            row.AddHandler(MenuItem.ClickEvent, OnLinkPressed, handledEventsToo: true);
            menu.Items.Insert(_linkRows.Count, row);
            _linkRows.Add(row);
        }
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
                case "ManageInstallationItem":
                    row.AddHandler(MenuItem.ClickEvent, OnManageInstallationPressed, handledEventsToo: true);
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

        if (_observedDetails is not null) _observedDetails.PropertyChanged -= OnDetailsPropertyChanged;
        _observedDetails = DataContext as GameDetailsViewModel;
        if (_observedDetails is not null) _observedDetails.PropertyChanged += OnDetailsPropertyChanged;
        _wasFocusedView = _observedDetails?.IsFocusedView == true;
        RefreshLinkRows();

        // A new subject means the remembered thumbnail belongs to a game that
        // is no longer on screen. The strip's buttons are recycled containers,
        // so keeping the reference would hand focus to whatever shot now sits
        // in that slot.
        _lightboxOrigin = null;

        RequestCover();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestCover();
    }

    private void RequestBackdrop()
    {
        if (DataContext is not GameDetailsViewModel details) return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        details.RequestBackdrop(Card.Bounds.Width * scaling, Card.Bounds.Height * scaling);
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
        RequestBackdrop();

        // Candidate thumbnails decode at the width they are drawn at, and
        // the scaling is a fact of the window rather than of the view model.
        details.IgdbMatch?.SetCoverScaling(scaling);

        // Screenshot thumbnails decode at the width they are drawn at, for the
        // same reason and off the same fact about the window.
        details.Screenshots?.RequestThumbnails(scaling);
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
    /// Choosing an already-open tool restores keyboard focus without loading its
    /// fields again. Opening and closing are view-model state changes.
    /// </summary>
    private void OnEditDetailsPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameDetailsViewModel { MetadataEditor: not null })
        {
            return;
        }

        OnDetailsPropertyChanged(DataContext, new PropertyChangedEventArgs(nameof(GameDetailsViewModel.IsFocusedView)));
    }

    private void OnWrongGamePressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameDetailsViewModel { IgdbMatch: not null })
        {
            return;
        }

        OnDetailsPropertyChanged(DataContext, new PropertyChangedEventArgs(nameof(GameDetailsViewModel.IsFocusedView)));
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
    /// The screenshot strip scrolls sideways, so a thumbnail reached by Tab can
    /// be off the right edge of the region. Same mechanism as the IGDB candidate
    /// list: focus is never left where it cannot be seen.
    /// </summary>
    private void OnScreenshotGotFocus(object? sender, GotFocusEventArgs e)
    {
        (e.Source as Control)?.BringIntoView();
    }

    private void OnScreenshotWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.X != 0 || e.Delta.Y == 0 || e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            || e.Source is not Control source || TopLevel.GetTopLevel(this) is not { } root)
        {
            return;
        }

        // Use the presenter's native Shift-wheel handling so wheel distance and
        // trackpad precision stay consistent with the other scroll regions.
        e.Handled = true;
        source.RaiseEvent(new PointerWheelEventArgs(source, e.Pointer, root,
            e.GetPosition(root), e.Timestamp, e.GetCurrentPoint(root).Properties,
            e.KeyModifiers | KeyModifiers.Shift, e.Delta));
    }

    /// <summary>
    /// The thumbnail the lightbox was opened from. Remembered on the press
    /// rather than read back from the view model, because the overlay's own
    /// selection moves as the user navigates and focus goes back to where the
    /// user left rather than to where they got to.
    /// </summary>
    private Control? _lightboxOrigin;

    private void OnShotPressed(object? sender, RoutedEventArgs e)
        => _lightboxOrigin = sender as Control;

    /// <summary>
    /// Puts focus back on the thumbnail that opened the lightbox, once the
    /// overlay has gone. Nothing happens when the modal itself is on its way
    /// out: the lightbox closes with it, and the library owns where focus goes
    /// then. The thumbnail is scrolled back into view for the same reason
    /// <see cref="OnScreenshotGotFocus"/> exists — the strip may have been
    /// scrolled since.
    /// </summary>
    public void RestoreLightboxFocus()
    {
        if (!IsVisible || _lightboxOrigin is not { } origin)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                // Checked again here, not only above: the modal's own IsVisible
                // is written by a binding that runs after the overlay has gone,
                // so a lightbox closing because the modal closed reaches this
                // method while the modal still reports itself visible.
                if (!IsVisible)
                {
                    return;
                }

                origin.BringIntoView();
                origin.Focus(NavigationMethod.Tab);
            },
            DispatcherPriority.Input);
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

    private async void OnManageInstallationPressed(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GameDetailsViewModel { ManagementAction: { } link })
            await OpenAsync(link);
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
