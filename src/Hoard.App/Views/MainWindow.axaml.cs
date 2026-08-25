using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// The library shell's behaviour: the §8 keyboard floor (arrows across the
/// grid, <c>/</c> to search, <c>Enter</c> to open the selection, <c>Escape</c>
/// to close), tile selection, and list-view selection. The cover wall's own
/// geometry lives in <see cref="CoverWall"/>, which divides the width it is
/// measured with — the view no longer computes a cell size and pushes it into a
/// layout object that is measured separately.
/// </summary>
public partial class MainWindow : Window
{
    private LibraryViewModel? _library;
    private MainWindowViewModel? _shell;
    private bool _chromeReady;
    private DateTime _lastTitleBarPress = DateTime.MinValue;
    private PixelPoint _lastTitleBarPoint;

    public MainWindow()
    {
        InitializeComponent();

        DetailsPanel.CloseRequested += (_, _) => _library?.CloseDetailsCommand.Execute(null);

        // WindowState can be written before the caption's controls exist, so
        // the state handler stays inert until the tree is up.
        _chromeReady = true;
        UpdateWindowStateChrome();
    }

    // ══ Window chrome ═══════════════════════════════════════════════════════
    // The client area is extended over the decorations, so everything Windows
    // used to do for the caption is done here. Each of these is load-bearing:
    // drop one and the window reads as broken rather than as styled.

    /// <summary>
    /// Drag moves the window; a double press maximises or restores it.
    ///
    /// <para><see cref="Window.BeginMoveDrag"/> hands the press to the system's
    /// own move loop, which is what buys Aero Snap, the edge previews and
    /// Win+Arrow for free rather than reimplementing them badly. The cost is
    /// that the loop is modal: it owns the pointer until the button comes up,
    /// so the second press of a double click reaches us through this handler or
    /// not at all. Hence both tests below — the framework's click count when the
    /// move loop leaves it intact, and our own press clock when it does not.
    /// The two can never both fire for one press, so the window cannot toggle
    /// twice and appear to ignore the gesture.</para>
    ///
    /// <para>The gesture is deliberately NOT wired to <c>DoubleTapped</c> as
    /// well. Avalonia raises that from the tunnelling half of this same press,
    /// so a second handler would toggle the window a second time on the same
    /// click and land it back where it started.</para>
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var at = this.PointToScreen(e.GetPosition(this));
        var now = DateTime.UtcNow;

        // Screen coordinates, not window coordinates: after a drag the pointer
        // sits at the same place *in the title bar* it started from, and a
        // window-relative test would read the next click as a double.
        var repeat = now - _lastTitleBarPress < TimeSpan.FromMilliseconds(500)
            && Math.Abs(at.X - _lastTitleBarPoint.X) <= 8
            && Math.Abs(at.Y - _lastTitleBarPoint.Y) <= 8;

        if (e.ClickCount >= 2 || repeat)
        {
            _lastTitleBarPress = DateTime.MinValue;
            ToggleMaximised();
            e.Handled = true;
            return;
        }

        _lastTitleBarPress = now;
        _lastTitleBarPoint = at;

        BeginMoveDrag(e);
    }

    private void OnMinimisePressed(object? sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximisePressed(object? sender, RoutedEventArgs e)
        => ToggleMaximised();

    private void OnClosePressed(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximised()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_chromeReady && change.Property == WindowStateProperty)
        {
            UpdateWindowStateChrome();
        }
    }

    /// <summary>
    /// The middle caption button says what it will do, not what state the
    /// window is in: a maximised window offers "Restore down" and draws the
    /// two-square glyph, exactly as the system chrome it replaced did.
    /// </summary>
    private void UpdateWindowStateChrome()
    {
        var maximised = WindowState == WindowState.Maximized;

        MaximiseGlyph.IsVisible = !maximised;
        RestoreGlyph.IsVisible = maximised;
        ToolTip.SetTip(MaximiseButton, maximised ? "Restore down" : "Maximise");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_library is not null)
        {
            _library.PropertyChanged -= OnLibraryPropertyChanged;
        }

        _shell = DataContext as MainWindowViewModel;
        _library = _shell?.Library;

        if (_library is not null)
        {
            _library.PropertyChanged += OnLibraryPropertyChanged;
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_library is { } library)
        {
            await library.LoadCommand.ExecuteAsync(null);
        }

        // The rail's REVIEW count has to be right before the user looks at it,
        // so the queue loads with the window rather than on first visit.
        if (_shell?.MergeQueue is { } queue)
        {
            await queue.LoadCommand.ExecuteAsync(null);
        }

        // §8's dimming preference. After the library, deliberately: it is one
        // row out of a settings table and the wall is already built, so reading
        // it late costs a repaint of the visible tiles rather than delaying the
        // first paint of the grid behind an IO round trip.
        if (_shell?.Display is { } display)
        {
            await display.LoadAsync();
        }

#if DEBUG
        // --open-queue lands on the merge confirm queue instead of the library,
        // so the screen can be captured and reviewed without driving the rail.
        // Debug-only, same convention as --seed-sample.
        if (_shell is not null && Environment.GetCommandLineArgs().Contains("--open-queue"))
        {
            _shell.IsMergeQueueVisible = true;
        }

        if (_library is not null && Environment.GetCommandLineArgs().Contains("--open-list"))
        {
            _library.ShowListViewCommand.Execute(null);
        }
#endif
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        // The detail modal is modal: it answers first, and it answers Escape
        // from anywhere — including from inside the search box (§8).
        if (_library is { IsDetailsOpen: true })
        {
            if (e.Key == Key.Escape)
            {
                _library.CloseDetailsCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (_shell is { IsMergeQueueVisible: true })
        {
            OnMergeQueueKeyDown(e);
            return;
        }

        if (_library is null)
        {
            return;
        }

        // While the caret is in the search box, arrows and "/" belong to it.
        if (SearchBox.IsFocused)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Text = string.Empty;
                e.Handled = true;
            }

            return;
        }

        // Up/down walks rows in list view and whole rows of tiles in the grid;
        // the two views are the same sequence read at different widths.
        var verticalStep = _library.IsGridView ? TileWall.Columns : 1;

        switch (e.Key)
        {
            case Key.Oem2 or Key.Divide:
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
                break;

            case Key.Left:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Right:
                MoveSelection(1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-verticalStep);
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(verticalStep);
                e.Handled = true;
                break;

            case Key.Enter:
                // §5.3 caps the tile at four facts; Enter is how you get the
                // rest. Launching is still M2's.
                _library.OpenDetailsCommand.Execute(_library.SelectedTile);
                e.Handled = true;
                break;

#if DEBUG
            // --grid-probe writes the wall's realized cell rects to
            // %TEMP%\hoard-grid-debug.txt. The dead-space bug was invisible in
            // a screenshot until you could read the anchor's cell position, so
            // the probe that found it stays reachable.
            case Key.F9 when Environment.GetCommandLineArgs().Contains("--grid-probe"):
                DumpGridDiagnostics();
                e.Handled = true;
                break;
#endif
        }
    }

#if DEBUG
    private void DumpGridDiagnostics()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"scroll offset={GridScroll.Offset} viewport={GridScroll.Viewport} extent={GridScroll.Extent}");
        text.AppendLine($"wall bounds={TileWall.Bounds} desired={TileWall.DesiredSize} columns={TileWall.Columns} tiles={_library?.VisibleTiles.Count}");

        foreach (var child in TileWall.Children)
        {
            if (child.IsVisible && child.DataContext is GameTileViewModel tile)
            {
                text.AppendLine($"  {child.Bounds} {tile.Title}");
            }
        }

        File.AppendAllText(
            Path.Combine(Path.GetTempPath(), "hoard-grid-debug.txt"),
            $"=== {DateTime.Now:HH:mm:ss.fff} ===\n{text}\n");
    }
#endif

    /// <summary>
    /// The §8 keyboard floor for the merge confirm queue: arrows walk the
    /// pairs, <c>S</c>/<c>Enter</c> answers "Same game", <c>D</c> answers
    /// "Different games", <c>Escape</c> goes back to the library. Both answers
    /// are one key because the queue's whole job is to be cleared — but they
    /// are different keys, never one key with a modifier, because "different
    /// games" is permanent.
    /// </summary>
    private void OnMergeQueueKeyDown(KeyEventArgs e)
    {
        if (_shell?.MergeQueue is not { } queue)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                MergeQueue.ScrollIntoView(queue.MoveSelection(-1));
                e.Handled = true;
                break;

            case Key.Down:
                MergeQueue.ScrollIntoView(queue.MoveSelection(1));
                e.Handled = true;
                break;

            case Key.S or Key.Enter:
                queue.SameGameCommand.Execute(queue.SelectedCandidate);
                e.Handled = true;
                break;

            case Key.D:
                queue.DifferentGamesCommand.Execute(queue.SelectedCandidate);
                e.Handled = true;
                break;

            case Key.Escape:
                _shell.ShowLibraryCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GameTileViewModel tile })
        {
            return;
        }

        _library?.SelectTile(tile);

        if (e.ClickCount >= 2)
        {
            _library?.OpenDetailsCommand.Execute(tile);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Puts focus on the display preference when its popover opens. §8 asks for
    /// a keyboard-reachable control with a visible focus ring, and a flyout that
    /// opens without moving focus leaves the only control inside it reachable by
    /// pointer alone — the button that opened it still holds focus, and Tab from
    /// there walks the command bar behind the popup.
    ///
    /// <para>Posted at input priority rather than run inline: the presenter is
    /// still being attached when Opened fires, so the content has no visual tree
    /// to focus into yet.</para>
    /// </summary>
    private void OnDisplayFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content })
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                var toggle = content as CheckBox
                    ?? content.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault();
                toggle?.Focus(NavigationMethod.Tab);
            },
            DispatcherPriority.Input);
    }

    /// <summary>Sort menu row. Sets the shared order, then closes the flyout.</summary>
    private void OnSortItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: SortOptionViewModel option })
        {
            _library?.SelectSortCommand.Execute(option);
        }

        SortButton.Flyout?.Hide();
    }

    /// <summary>
    /// The list is the one view that can hold more than one selection (§6), so
    /// the per-item flag the Volt edge reads has to follow the whole set rather
    /// than only the anchor the view model tracks. This runs after the anchor
    /// has been written, so it is the last word on which rows are marked.
    /// </summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        foreach (var removed in e.RemovedItems)
        {
            if (removed is GameTileViewModel tile)
            {
                tile.IsSelected = false;
            }
        }

        foreach (var added in e.AddedItems)
        {
            if (added is GameTileViewModel tile)
            {
                tile.IsSelected = true;
            }
        }

        if (_library is not null && sender is ListBox list)
        {
            _library.SelectedCount = list.SelectedItems?.Count ?? 0;
        }
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_library is not null && e.Source is Control { DataContext: GameTileViewModel tile })
        {
            _library.OpenDetailsCommand.Execute(tile);
            e.Handled = true;
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // The visible set changed under a viewport that is still scrolled
            // to wherever the previous, longer set had been left. Nothing
            // re-anchors it, so a filter down to three tiles would leave the
            // user looking at empty space below the content.
            case nameof(LibraryViewModel.VisibleTiles):
            case nameof(LibraryViewModel.IsGridView):
                ResetScroll();
                break;

            case nameof(LibraryViewModel.Details):
                // Focus follows the modal, so Escape and Tab reach it wherever
                // the user's focus happened to be (§8).
                if (_library is { IsDetailsOpen: true })
                {
                    Dispatcher.UIThread.Post(() => DetailsPanel.Focus(), DispatcherPriority.Input);
                }

                break;
        }
    }

    /// <summary>
    /// Sends both views back to the top. The offset belongs to the scroll
    /// viewer, not to the list it happens to be showing: filter 616 tiles down
    /// to three and nothing re-anchors it, so the viewport stays where the long
    /// set left it — far past the end of the short one — and the user sees empty
    /// space. Switching views is the same case, since the two panes scroll
    /// independently and only one of them is ever measured.
    ///
    /// <para>This is the whole of what the view has to do about a set change.
    /// <see cref="CoverWall"/> recomputes its geometry and its realized rows
    /// from the item count on the next measure pass, so there is nothing to
    /// re-seat and nothing that can be left describing the previous set.</para>
    /// </summary>
    private void ResetScroll()
    {
        GridScroll.Offset = new Vector(0, 0);

        if (ListRows.Scroll is { } listScroll)
        {
            listScroll.Offset = new Vector(0, 0);
        }
    }

    private void MoveSelection(int delta)
    {
        var index = _library?.MoveSelection(delta) ?? -1;
        if (index < 0)
        {
            return;
        }

        // Keep the newly selected item on screen (§8: full keyboard navigation).
        if (_library is { IsGridView: false })
        {
            ListRows.ScrollIntoView(index);
            return;
        }

        // The target is usually not realized — selection can jump a hundred
        // rows — so the wall scrolls to the cell, and the scroll is what
        // realizes the container.
        TileWall.ScrollIntoView(index);
    }
}
