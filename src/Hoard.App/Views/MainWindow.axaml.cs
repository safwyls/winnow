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

        _library = (DataContext as MainWindowViewModel)?.Library;

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
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || _library is null)
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
