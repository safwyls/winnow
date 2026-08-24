using System.Collections;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace Hoard.App.Views;

/// <summary>
/// The virtualizing cover wall (§4): a uniform 2:3 grid that reflows on
/// available width and realizes only the rows the viewport can see.
///
/// <para><b>Why this exists instead of <c>ItemsRepeater</c> +
/// <c>UniformGridLayout</c>.</b> That pair computes items-per-line two different
/// ways. <c>UniformGridLayout</c>'s anchor and extent math uses
/// <c>floor(available / (itemWidth + spacing))</c> — every item is charged for a
/// trailing gutter, including the last one in a row — while the flow algorithm
/// that actually places the tiles packs them greedily and fits one more whenever
/// the row ends flush. §4's geometry ("the row divides the remaining space
/// evenly") produces exactly that flush row at every window width, so the two
/// disagreed permanently: five tiles per line on screen, four per line in the
/// anchor arithmetic. The visible result was a tile orphaned alone in column 0
/// at the scroll anchor, every row below it displaced downward — measured at
/// 7,800px of drift over 606 tiles — and a scroll extent 22% longer than the
/// content. The only way to satisfy that layout is to leave a full gutter of
/// dead space at the right edge of every row, which is the geometry §4 exists to
/// prevent. See docs/spikes/avalonia-dormancy-rendering.md for the standing plan
/// to retire the package.</para>
///
/// <para><b>Why this is safe to own.</b> Every cell is the same size, so nothing
/// here is estimated: rows, extent, cell rect and the visible row range are all
/// closed-form in the item count. There is no realization window to go stale
/// while the wall is hidden behind list view, and no cached items-per-line that
/// can disagree with the arrangement — both come from one expression, evaluated
/// in <see cref="MeasureOverride"/>, every pass.</para>
/// </summary>
public class CoverWall : Panel
{
    /// <summary>
    /// Rows realized above and below the viewport. One is enough to cover a
    /// wheel notch landing between layout passes; more just holds bitmaps.
    /// </summary>
    private const int BufferRows = 1;

    /// <summary>
    /// Viewport height assumed for the very first measure, before the layout
    /// system has reported a real one. Overshooting costs one screenful of
    /// realization for one pass; undershooting would show a short wall, which
    /// §7 forbids ("never an empty grid").
    /// </summary>
    private const double AssumedViewportHeight = 1200;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<CoverWall, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<CoverWall, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>Density slider value: the *minimum* tile width, never the final one.</summary>
    public static readonly StyledProperty<double> MinCellWidthProperty =
        AvaloniaProperty.Register<CoverWall, double>(nameof(MinCellWidth), 148d);

    /// <summary>Height ÷ width. 1.5 keeps the portrait capsule geometry of §4.</summary>
    public static readonly StyledProperty<double> CellAspectProperty =
        AvaloniaProperty.Register<CoverWall, double>(nameof(CellAspect), 1.5d);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<CoverWall, double>(nameof(Spacing), 16d);

    private readonly List<Control> _pool = [];
    private readonly Dictionary<int, Control> _realized = [];

    private IList? _items;
    private INotifyCollectionChanged? _incc;
    private Rect _viewport;
    private double _cellWidth = 148;
    private double _cellHeight = 222;
    private double _lastWidth = 148;

    static CoverWall()
    {
        AffectsMeasure<CoverWall>(MinCellWidthProperty, CellAspectProperty, SpacingProperty);
    }

    public CoverWall()
    {
        // The wall is taller than its viewport by design, so its own effective
        // viewport is the scroll position — the only input realization needs.
        EffectiveViewportChanged += OnViewportChanged;
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public double MinCellWidth
    {
        get => GetValue(MinCellWidthProperty);
        set => SetValue(MinCellWidthProperty, value);
    }

    public double CellAspect
    {
        get => GetValue(CellAspectProperty);
        set => SetValue(CellAspectProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Live column count — what up/down arrow moves the selection by (§8).
    /// Valid after the first measure; 1 before it, which is also the honest
    /// answer for a wall that has not been given a width yet.
    /// </summary>
    public int Columns { get; private set; } = 1;

    /// <summary>
    /// Brings a tile on screen whether or not it is realized. Selection can move
    /// to an index hundreds of rows away, so this addresses the cell rather than
    /// a container that may not exist yet; the scroll it triggers is what makes
    /// the container exist.
    /// </summary>
    public void ScrollIntoView(int index)
    {
        if (_items is null || index < 0 || index >= _items.Count)
        {
            return;
        }

        // Inflated by one gutter so an edge row lands with its margin showing
        // rather than flush against the viewport edge, where it reads as
        // clipped.
        this.BringIntoView(CellRect(index).Inflate(Spacing));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            SetItems(change.GetNewValue<IEnumerable?>());
        }
        else if (change.Property == ItemTemplateProperty)
        {
            // Containers were built by the previous template; none of them can
            // be reused, so drop the lot rather than mixing two shapes.
            DiscardContainers();
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // The wall lives in a ScrollViewer with horizontal scrolling disabled,
        // so width is real and height is infinite. Keep the last real width for
        // the degenerate case, rather than laying out at infinity.
        if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
        {
            _lastWidth = availableSize.Width;
        }

        var width = _lastWidth;
        var spacing = Spacing;
        var minCell = Math.Max(1, MinCellWidth);

        // §4: the slider sets the minimum width, the row then divides the space
        // evenly and height follows at 2:3. Floor keeps every cell on a whole
        // pixel, so the arranged rects cannot round up into each other; the
        // remainder (< Columns px) stays at the right edge and is invisible.
        Columns = Math.Max(1, (int)Math.Floor((width + spacing) / (minCell + spacing)));
        _cellWidth = Math.Max(1, Math.Floor((width - ((Columns - 1) * spacing)) / Columns));
        _cellHeight = Math.Max(1, Math.Floor(_cellWidth * CellAspect));

        var count = _items?.Count ?? 0;
        var rows = (count + Columns - 1) / Columns;

        Realize(count);

        var cell = new Size(_cellWidth, _cellHeight);
        foreach (var container in _realized.Values)
        {
            container.Measure(cell);
        }

        return new Size(width, rows == 0 ? 0 : (rows * _cellHeight) + ((rows - 1) * spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, container) in _realized)
        {
            container.Arrange(CellRect(index));
        }

        return finalSize;
    }

    private void OnViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var viewport = e.EffectiveViewport;

        // Empty means hidden — list view is up, or the window is minimized.
        // Realizing against a zero viewport is what left the old repeater with
        // a realization window describing nothing; keeping the last real one
        // costs a handful of off-screen containers and means coming back is a
        // normal viewport change rather than a recovery.
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        if (Math.Abs(viewport.Y - _viewport.Y) < 0.5
            && Math.Abs(viewport.Height - _viewport.Height) < 0.5)
        {
            return;
        }

        _viewport = viewport;
        InvalidateMeasure();
    }

    private void SetItems(IEnumerable? source)
    {
        if (_incc is not null)
        {
            _incc.CollectionChanged -= OnItemsChanged;
            _incc = null;
        }

        _items = source switch
        {
            null => null,
            IList list => list,
            _ => source.Cast<object?>().ToList(),
        };

        if (source is INotifyCollectionChanged incc)
        {
            _incc = incc;
            _incc.CollectionChanged += OnItemsChanged;
        }

        // The set changed underneath every realized index, so no container is
        // showing what its index now means. Recycling them all is the whole
        // rebuild: the next measure realizes the new first rows from scratch.
        RecycleAll();
        InvalidateMeasure();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RecycleAll();
        InvalidateMeasure();
    }

    private void Realize(int count)
    {
        if (count == 0)
        {
            RecycleAll();
            return;
        }

        var pitch = _cellHeight + Spacing;
        var top = _viewport.Height > 0 ? Math.Max(0, _viewport.Y) : 0;
        var height = _viewport.Height > 0 ? _viewport.Height : AssumedViewportHeight;

        var firstRow = Math.Max(0, (int)Math.Floor(top / pitch) - BufferRows);
        var lastRow = (int)Math.Floor((top + height) / pitch) + BufferRows;

        var first = Math.Min(count - 1, firstRow * Columns);
        var last = Math.Min(count - 1, ((lastRow + 1) * Columns) - 1);

        foreach (var index in _realized.Keys.ToList())
        {
            if (index < first || index > last)
            {
                Recycle(index);
            }
        }

        for (var index = first; index <= last; index++)
        {
            if (!_realized.ContainsKey(index))
            {
                _realized[index] = Attach(_items![index]);
            }
        }

        // Keep the spare pool to a row's worth. Anything beyond that is a
        // container the wall will not need again at this size.
        while (_pool.Count > Columns)
        {
            var spare = _pool[^1];
            _pool.RemoveAt(_pool.Count - 1);
            Children.Remove(spare);
        }
    }

    private Control Attach(object? item)
    {
        Control container;
        if (_pool.Count > 0)
        {
            container = _pool[^1];
            _pool.RemoveAt(_pool.Count - 1);
        }
        else
        {
            container = ItemTemplate?.Build(item) ?? new ContentControl();
            Children.Add(container);
        }

        // DataContext, then visible: GameTileView releases the previous game's
        // bitmaps on the context swap and requests the new game's art, so the
        // container is never briefly showing one game's art under another's.
        container.DataContext = item;
        container.IsVisible = true;
        return container;
    }

    private void Recycle(int index)
    {
        if (!_realized.Remove(index, out var container))
        {
            return;
        }

        container.IsVisible = false;

        // Clearing the context is what releases the decoded bitmaps —
        // GameTileView.OnDataContextChanged calls ReleaseCover on the game it
        // was showing. That is what keeps the cover cache's memory bound honest
        // with 616 tiles and only a screenful realized.
        container.DataContext = null;
        _pool.Add(container);
    }

    private void RecycleAll()
    {
        foreach (var index in _realized.Keys.ToList())
        {
            Recycle(index);
        }
    }

    private void DiscardContainers()
    {
        RecycleAll();
        foreach (var spare in _pool)
        {
            Children.Remove(spare);
        }

        _pool.Clear();
    }

    private Rect CellRect(int index)
    {
        var row = index / Columns;
        var column = index % Columns;
        return new Rect(
            column * (_cellWidth + Spacing),
            row * (_cellHeight + Spacing),
            _cellWidth,
            _cellHeight);
    }
}
