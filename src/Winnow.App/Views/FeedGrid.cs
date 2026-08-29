using Avalonia;
using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>
/// The Feed's items panel: a wrapping grid whose column count is decided by
/// <see cref="MinItemWidth"/> and whose rows divide the available width evenly.
/// </summary>
public sealed class FeedGrid : Panel
{
    /// <summary>
    /// The narrowest a card may be drawn; the row then divides its width evenly
    /// between however many of those fit.
    /// </summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(MinItemWidth), 420d);

    /// <summary>Space between cards, both axes. One number, because this is a grid.</summary>
    public static readonly StyledProperty<double> GutterProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(Gutter), 14d);

    /// <summary>Last width the panel was measured at; used as fallback if given infinity.</summary>
    private double _lastWidth = 420d;

    static FeedGrid()
    {
        AffectsMeasure<FeedGrid>(MinItemWidthProperty, GutterProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Gutter
    {
        get => GetValue(GutterProperty);
        set => SetValue(GutterProperty, value);
    }

    /// <summary>
    /// Live column count. Valid after the first measure; 1 before it, which is
    /// also the honest answer for a grid that has not been given a width.
    /// </summary>
    public int Columns { get; private set; } = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width) && availableSize.Width > 0)
        {
            _lastWidth = availableSize.Width;
        }

        var (columns, itemWidth) = Geometry(_lastWidth);
        Columns = columns;

        var cell = new Size(itemWidth, double.PositiveInfinity);
        var gutter = Gutter;
        var height = 0d;
        var rowHeight = 0d;

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(cell);

            if (i % columns == 0 && i > 0)
            {
                height += rowHeight + gutter;
                rowHeight = 0d;
            }

            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        return new Size(_lastWidth, Children.Count == 0 ? 0 : height + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Geometry(finalSize.Width);
        Columns = columns;

        var gutter = Gutter;
        var y = 0d;

        for (var start = 0; start < Children.Count; start += columns)
        {
            // The row's height is its tallest card, and every card in it is
            // arranged to that height — so the Play button and the store badge,
            // which dock to the bottom of a card, land on one line across the
            // row however long the reasons above them ran.
            var rowHeight = 0d;
            var end = Math.Min(start + columns, Children.Count);
            for (var i = start; i < end; i++)
            {
                rowHeight = Math.Max(rowHeight, Children[i].DesiredSize.Height);
            }

            for (var i = start; i < end; i++)
            {
                var x = (i - start) * (itemWidth + gutter);
                Children[i].Arrange(new Rect(x, y, itemWidth, rowHeight));
            }

            y += rowHeight + gutter;
        }

        return new Size(finalSize.Width, Math.Max(0, y - gutter));
    }

    private (int Columns, double ItemWidth) Geometry(double width)
        => GeometryFor(
            double.IsInfinity(width) || width <= 0 ? _lastWidth : width,
            MinItemWidth,
            Gutter);

    /// <summary>
    /// Columns and the width each card gets, as a pure function so it can be
    /// asserted without a window.
    /// </summary>
    public static (int Columns, double ItemWidth) GeometryFor(double width, double minItemWidth, double gutter)
    {
        var min = Math.Max(1, minItemWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + gutter) / (min + gutter)));
        var itemWidth = Math.Max(1, Math.Floor((width - ((columns - 1) * gutter)) / columns));
        return (columns, itemWidth);
    }
}
