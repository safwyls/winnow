using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Winnow.App.Views.Fullscreen;

/// <summary>A clipped row window that retains nearby covers while the selection moves.</summary>
internal sealed class FullscreenRowViewport : Panel, IDisposable
{
    private readonly FullscreenContext _context;
    private readonly Dictionary<int, Control> _rows = [];
    private Func<int, Control>? _createRow;
    private int _rowCount, _visibleRows = 1;
    private double _offset, _startOffset;
    private Size _layoutSize;
    private int _layoutFirstRow, _animationGeneration;
    private TimeSpan? _animationStart;
    private bool _subscribed, _attached, _disposed;

    public int FirstRow { get; private set; }
    public IReadOnlyDictionary<int, Control> RealizedRows => _rows;
    public new bool IsAnimating { get; private set; }
    public event EventHandler? RowsChanged;
    // Tests can own frame time while still exercising the production callback and generation checks.
    internal Action<Action<TimeSpan>>? FrameScheduler { get; set; }

    public FullscreenRowViewport(FullscreenContext context)
    {
        _context = context;
        ClipToBounds = true;
        // Focus belongs to the target row immediately, even while it is travelling into view.
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);
    }

    public void Configure(int rowCount, int visibleRows, Func<int, Control> createRow, int firstRow = 0)
    {
        if (_disposed) return;
        ArgumentOutOfRangeException.ThrowIfNegative(rowCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(visibleRows, 1);
        ArgumentNullException.ThrowIfNull(createRow);
        Stop();
        ClearRows();
        _rowCount = rowCount;
        _visibleRows = visibleRows;
        _createRow = createRow;
        FirstRow = Clamp(firstRow);
        _offset = FirstRow;
        Realize();
    }

    public void Show(int firstRow, bool animate = true)
    {
        if (_disposed || _createRow is null) return;
        var target = Clamp(firstRow);
        if (target == FirstRow)
        {
            if (IsAnimating && (!animate || _context.ReducedMotion)) Snap();
            return;
        }

        // Retarget from the last rendered position, so reversing never queues or jumps.
        FirstRow = target;
        if (!animate || !_attached || _context.ReducedMotion || Bounds.Height <= 0 ||
            Math.Abs(target - _offset) > _visibleRows + 1)
        {
            Snap();
            return;
        }
        _startOffset = _offset;
        _animationStart = null;
        IsAnimating = true;
        var generation = ++_animationGeneration;
        Realize();
        RequestFrame(generation);
    }

    public Control GetRow(int index) => _rows[index];

    public bool AppendRows(int rowCount)
    {
        if (_disposed || _createRow is null || rowCount <= _rowCount) return false;
        _rowCount = rowCount;
        Realize();
        return true;
    }

    public void InvalidateRow(int index)
    {
        if (_disposed || !_rows.Remove(index, out var row)) return;
        Children.Remove(row);
        Realize();
    }

    private int Clamp(int firstRow) => Math.Clamp(firstRow, 0, Math.Max(0, _rowCount - _visibleRows));

    private void Realize()
    {
        if (_createRow is null || _disposed) return;
        var changed = false;
        var first = Math.Max(0, FirstRow - 1);
        var last = Math.Min(_rowCount - 1, FirstRow + _visibleRows);
        if (IsAnimating)
        {
            first = Math.Min(first, Math.Max(0, (int)Math.Floor(_offset)));
            last = Math.Max(last, Math.Min(_rowCount - 1, (int)Math.Ceiling(_offset) + _visibleRows - 1));
        }
        foreach (var index in _rows.Keys.Where(i => i < first || i > last).ToArray())
        {
            Children.Remove(_rows[index]);
            _rows.Remove(index);
            changed = true;
        }
        for (var index = first; index <= last; index++)
        {
            if (!_rows.TryGetValue(index, out var row))
            {
                row = _createRow(index);
                row.RenderTransform = new TranslateTransform();
                _rows.Add(index, row);
                Children.Add(row);
                changed = true;
            }
            var active = index >= FirstRow && index < FirstRow + _visibleRows;
            row.IsEnabled = active;
            row.IsHitTestVisible = active;
        }
        if (changed) InvalidateMeasure();
        else InvalidateArrange();
        PositionRows();
        if (changed) RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var rowHeight = double.IsFinite(availableSize.Height) ? availableSize.Height / _visibleRows : double.PositiveInfinity;
        foreach (var row in _rows.Values) row.Measure(new Size(availableSize.Width, rowHeight));
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : _rows.Values.Select(r => r.DesiredSize.Width).DefaultIfEmpty().Max(),
            double.IsFinite(availableSize.Height) ? availableSize.Height : _rows.Values.Select(r => r.DesiredSize.Height).DefaultIfEmpty().Max() * _visibleRows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_layoutSize != finalSize)
        {
            _layoutSize = finalSize;
            if (IsAnimating) Snap();
        }
        var rowHeight = finalSize.Height / _visibleRows;
        _layoutFirstRow = FirstRow;
        foreach (var (index, row) in _rows)
            row.Arrange(new Rect(0, (index - FirstRow) * rowHeight, finalSize.Width, rowHeight));
        PositionRows();
        return finalSize;
    }

    private void PositionRows()
    {
        // Transforms must use the row positions already arranged, including between input and layout.
        var y = (_layoutFirstRow - _offset) * _layoutSize.Height / _visibleRows;
        foreach (var row in _rows.Values)
            if (row.RenderTransform is TranslateTransform translation) translation.Y = y;
    }

    private void RequestFrame(int generation)
    {
        void Frame(TimeSpan timestamp)
        {
            if (!IsAnimating || generation != _animationGeneration || !_attached) return;
            // Row creation and the input handler's other work precede the animation clock.
            _animationStart ??= timestamp;
            AdvanceAnimation(timestamp - _animationStart.Value);
            if (IsAnimating) RequestFrame(generation);
        }
        if (FrameScheduler is { } scheduler) scheduler(Frame);
        else TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
    }

    internal void AdvanceAnimation(TimeSpan elapsed)
    {
        if (!IsAnimating) return;
        var progress = Math.Clamp(elapsed.TotalMilliseconds / 220, 0, 1);
        if (progress >= 1 || _context.ReducedMotion) { Snap(); return; }
        var eased = 1 - Math.Pow(1 - progress, 3);
        _offset = _startOffset + (FirstRow - _startOffset) * eased;
        PositionRows();
    }

    private void Snap()
    {
        Stop();
        _offset = FirstRow;
        Realize();
    }

    private void Stop()
    {
        IsAnimating = false;
        _animationStart = null;
        _animationGeneration++;
    }

    private void PreferencesChanged(object? sender, EventArgs e)
    {
        if (_context.ReducedMotion) Snap();
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        _context.PreferencesChanged += PreferencesChanged;
        _subscribed = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_disposed) return;
        _attached = true;
        Subscribe();
        Snap();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Release();
    }

    private void ClearRows()
    {
        Children.Clear();
        _rows.Clear();
    }

    private void Release()
    {
        Stop();
        _context.PreferencesChanged -= PreferencesChanged;
        _subscribed = false;
        ClearRows();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
        _createRow = null;
    }
}
