using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// The library shell's behaviour: the §8 keyboard floor (arrows across the
/// grid, <c>/</c> to search, <c>Enter</c> to launch), tile selection, and the
/// tile sizing that keeps the cover wall on its 2:3 geometry as the density
/// slider and the window width change.
/// </summary>
public partial class MainWindow : Window
{
    private const double GridPadding = 20;
    private const double Gutter = 16;

    private LibraryViewModel? _library;
    private MainWindowViewModel? _shell;

    /// <summary>Live column count — what up/down arrow moves selection by.</summary>
    private int _columns = 1;

    public MainWindow()
    {
        InitializeComponent();

        GridScroll.SizeChanged += (_, _) => UpdateTileGeometry();
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
#endif
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
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
                MoveSelection(-_columns);
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(_columns);
                e.Handled = true;
                break;

            case Key.Enter:
                // Launching arrives with sessions (M2); selection stays put.
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
        if (sender is Control { DataContext: GameTileViewModel tile })
        {
            _library?.SelectTile(tile);
        }
    }

    private void OnLibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // VisibleTiles matters as much as the size properties: on first open the
        // scroll viewer can still be zero-width when OnOpened measures, so the
        // repeater would otherwise keep the one-column fallback geometry it was
        // given before the library finished loading.
        if (e.PropertyName is nameof(LibraryViewModel.TileWidth)
            or nameof(LibraryViewModel.TileHeight)
            or nameof(LibraryViewModel.VisibleTiles))
        {
            UpdateTileGeometry();
        }
    }

    private void MoveSelection(int delta)
    {
        var index = _library?.MoveSelection(delta) ?? -1;
        if (index < 0)
        {
            return;
        }

        // Keep the newly selected tile on screen (§8: full keyboard grid nav).
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

        var available = GridScroll.Bounds.Width - (GridPadding * 2);
        var minWidth = _library.TileWidth;

        var columns = available > 0
            ? (int)Math.Floor((available + Gutter) / (minWidth + Gutter))
            : 1;
        _columns = Math.Max(1, columns);

        var width = available > 0
            ? Math.Max(minWidth, (available - ((_columns - 1) * Gutter)) / _columns)
            : minWidth;

        if (Math.Abs(layout.MinItemWidth - width) < 0.5)
        {
            return;
        }

        layout.MinItemWidth = width;
        layout.MinItemHeight = width * 1.5;

        // UniformGridLayout is an AvaloniaObject, not a Visual — writing its
        // properties does not invalidate the repeater that hosts it, so items
        // already realised keep the previous cell size until something else
        // forces a pass.
        TileRepeater.InvalidateMeasure();
    }
}
