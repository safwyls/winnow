using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// The library shell's behaviour: the §8 keyboard floor (arrows across the
/// grid, <c>/</c> to search, <c>Enter</c> to open the selection, <c>Escape</c>
/// to close), tile selection, list-view selection, and the tile sizing that
/// keeps the cover wall on its 2:3 geometry as the density slider and the
/// window width change.
/// </summary>
public partial class MainWindow : Window
{
    private const double GridPadding = 20;
    private const double Gutter = 16;

    private LibraryViewModel? _library;
    private MainWindowViewModel? _shell;

    /// <summary>Live column count — what up/down arrow moves selection by in the grid.</summary>
    private int _columns = 1;

    /// <summary>
    /// Set while the cover wall is hidden. A hidden ScrollViewer measures to
    /// zero, so any geometry computed during that time is meaningless and the
    /// repeater's realization state was built against a zero viewport — the
    /// first pass after it comes back has to be forced, not skipped because the
    /// numbers happen to match.
    /// </summary>
    private bool _gridGeometryStale = true;

    public MainWindow()
    {
        InitializeComponent();

        GridScroll.SizeChanged += (_, _) => UpdateTileGeometry();
        DetailsPanel.CloseRequested += (_, _) => _library?.CloseDetailsCommand.Execute(null);
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

        UpdateTileGeometry();
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        UpdateTileGeometry();

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
        var verticalStep = _library.IsGridView ? _columns : 1;

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
        }
    }

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
            // VisibleTiles matters as much as the size properties: on first open
            // the scroll viewer can still be zero-width when OnOpened measures,
            // so the repeater would otherwise keep the one-column fallback
            // geometry it was given before the library finished loading.
            case nameof(LibraryViewModel.TileWidth):
            case nameof(LibraryViewModel.TileHeight):
                UpdateTileGeometry();
                break;

            // The visible set changed under a viewport that is still scrolled
            // to wherever the previous, longer set had been left. Nothing
            // re-anchors it, so a filter down to three tiles leaves the user
            // looking at empty space below the content.
            case nameof(LibraryViewModel.VisibleTiles):
                RefreshGrid();
                break;

            case nameof(LibraryViewModel.IsGridView):
                // A hidden ScrollViewer measured to zero while the other view
                // was up; force the wall to lay out again now that it has a
                // viewport, and start it at the top like any other view change.
                _gridGeometryStale = true;
                RefreshGrid();
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
    /// Rebuilds the cover wall for a new visible set — the fix for both halves
    /// of the "blank grid" report, and the reason <c>ItemsSource</c> is not
    /// simply bound in XAML.
    ///
    /// <para><b>Scroll offset.</b> The offset belongs to the scroll viewer, not
    /// to the list it happens to be showing. Filter 616 tiles down to three and
    /// nothing re-anchors it: the viewport stays where the long set left it,
    /// far past the end of the short one, and the user sees empty space.</para>
    ///
    /// <para><b>Realization state.</b> An <c>ItemsRepeater</c> hidden behind
    /// list view is measured against a zero viewport and unrealizes; when the
    /// grid comes back, invalidating measure is not enough to rebuild what it
    /// threw away, and the wall comes back with a partial last row and
    /// containers still holding the previous set's items. Re-seating
    /// <c>ItemsSource</c> forces a clean rebuild. It costs one re-realization of
    /// the ~15 containers actually on screen — virtualization means the other
    /// 600 were never built either way.</para>
    /// </summary>
    private void RefreshGrid()
    {
        // Geometry first: the cell size has to be right before anything is
        // realized into it, or the rebuild lays out at the previous width.
        UpdateTileGeometry();

        GridScroll.Offset = new Vector(0, 0);

        if (ListRows.Scroll is { } listScroll)
        {
            listScroll.Offset = new Vector(0, 0);
        }

        ReseatItems();
    }

    /// <summary>
    /// Hands the repeater its items again from scratch. Null first: assigning
    /// the same collection instance would be a no-op, and assigning a different
    /// one still lets the repeater try to reuse a realization window that no
    /// longer describes anything.
    /// </summary>
    private void ReseatItems()
    {
        TileRepeater.ItemsSource = null;
        TileRepeater.ItemsSource = _library?.VisibleTiles;
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

        if (TileRepeater.TryGetElement(index) is { } element)
        {
            element.BringIntoView();
        }
    }

    /// <summary>
    /// Reflows on available width rather than a fixed column count (§4). The
    /// density slider sets the *minimum* tile width; the row then divides the
    /// remaining space evenly and height follows at 2:3, so the wall keeps its
    /// portrait capsule geometry at every window size.
    /// </summary>
    private void UpdateTileGeometry()
    {
        if (_library is null || TileRepeater.Layout is not UniformGridLayout layout)
        {
            return;
        }

        // Viewport, not Bounds: Bounds includes the vertical scrollbar, and
        // overstating the width by its thickness pushes the last column past
        // the right edge of the visible area, where the presenter clips it.
        var width = GridScroll.Viewport.Width > 0 ? GridScroll.Viewport.Width : GridScroll.Bounds.Width;
        var available = width - (GridPadding * 2);
        if (available <= 0)
        {
            // Hidden, or not laid out yet. Writing a one-column fallback here
            // is what used to leave the wall wrong until something else moved;
            // leaving the last good geometry alone costs nothing, because the
            // SizeChanged that follows a real layout will call back in.
            _gridGeometryStale = true;
            return;
        }

        var minWidth = _library.TileWidth;

        var columns = (int)Math.Floor((available + Gutter) / (minWidth + Gutter));
        _columns = Math.Max(1, columns);

        var tileWidth = Math.Max(minWidth, (available - ((_columns - 1) * Gutter)) / _columns);

        var wasStale = _gridGeometryStale;
        if (!wasStale && Math.Abs(layout.MinItemWidth - tileWidth) < 0.5)
        {
            return;
        }

        _gridGeometryStale = false;

        // The first real measurement after the wall was hidden. RefreshGrid's
        // re-seat ran while the viewport was still zero, so the repeater had
        // nothing to realize against; do it again now that it has.
        if (wasStale)
        {
            Dispatcher.UIThread.Post(ReseatItems, DispatcherPriority.Loaded);
        }

        layout.MinItemWidth = tileWidth;
        layout.MinItemHeight = tileWidth * 1.5;

        // UniformGridLayout is an AvaloniaObject, not a Visual — writing its
        // properties does not invalidate the repeater that hosts it, so items
        // already realised keep the previous cell size until something else
        // forces a pass.
        TileRepeater.InvalidateMeasure();
    }
}
