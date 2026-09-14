using Avalonia;
using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>A shelf of five curated cover slots, scrolling horizontally when they cannot fit.</summary>
public sealed class FeedGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(MinItemWidth), 180d);
    public static readonly StyledProperty<double> GutterProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(Gutter), 18d);
    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(ViewportWidth));

    static FeedGrid() => AffectsMeasure<FeedGrid>(MinItemWidthProperty, GutterProperty, ViewportWidthProperty);

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double Gutter { get => GetValue(GutterProperty); set => SetValue(GutterProperty, value); }
    public double ViewportWidth { get => GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public int Columns => Math.Max(1, Children.Count);

    private double _itemWidth = 180;

    protected override Size MeasureOverride(Size availableSize)
    {
        // ScrollViewer measures its content with infinite width; its viewport,
        // rather than that content extent, determines the cover size.
        var width = ViewportWidth > 0 ? ViewportWidth : availableSize.Width;
        (_, _itemWidth) = GeometryFor(width, MinItemWidth, Gutter);
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(_itemWidth, double.PositiveInfinity));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(Children.Count * _itemWidth + Math.Max(0, Children.Count - 1) * Gutter, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < Children.Count; i++)
            Children[i].Arrange(new Rect(i * (_itemWidth + Gutter), 0, _itemWidth, finalSize.Height));
        return finalSize;
    }

    public static (int Columns, double ItemWidth) GeometryFor(double width, double minItemWidth, double gutter)
    {
        var minimum = Math.Max(1, minItemWidth);
        if (!double.IsFinite(width) || width <= 0) return (5, minimum);
        return (5, Math.Clamp(Math.Floor((width - 4 * gutter) / 5), minimum, Math.Max(minimum, 240)));
    }
}
