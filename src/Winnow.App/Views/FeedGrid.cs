using Avalonia;
using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>
/// The Feed's items panel: a wrapping grid bound to the width it is given.
///
/// <para><b>Why this exists at all.</b> The shelves used to be horizontal rails
/// in a <c>ScrollViewer</c>, and a rail is a television convention. On a desktop
/// you cannot see how much a shelf holds, the wheel does the wrong thing, and
/// everything past the right edge is functionally invisible — the old markup's
/// own note that a rail shows "how much is off to the right" was an admission
/// that the content was hidden. So every section is now a grid: the cards wrap
/// to as many per row as fit, the page scrolls vertically, and nothing in the
/// Feed scrolls horizontally anywhere.</para>
///
/// <para><b>Rows are flush, which is the whole of the geometry.</b> The column
/// count is decided by <see cref="MinItemWidth"/> and then the row divides its
/// width evenly, so there is never a ragged right edge and never a trailing
/// gutter charged to a card that has no neighbour. This is the same arithmetic
/// <see cref="CoverWall"/> uses for the same reason — see its remarks for the
/// off-by-one that made <c>UniformGridLayout</c> unusable on that geometry.
/// Nothing here is virtualized and nothing needs to be: a shelf is capped at ten
/// cards and five shelves is the ceiling, against the six hundred that made
/// CoverWall necessary.</para>
///
/// <para><b>Row height is per row, not per section.</b> A reason runs 78 to 256
/// characters, so a section laid out at its longest card's height would carry
/// that card's whitespace on every other one. Each row takes its own tallest
/// card instead, which is what makes the action line land on one baseline
/// ACROSS a row — the property the old rail bought by being one row.</para>
///
/// <para><b><see cref="Columns"/> is what the arrow keys move by.</b> Up and
/// Down on this surface are a jump of one row, and a row is however many cards
/// the current width fits. See <see cref="FeedView.HandleNavigationKey"/>.</para>
/// </summary>
public sealed class FeedGrid : Panel
{
    /// <summary>
    /// The narrowest a card may be drawn; the row then divides its width evenly
    /// between however many of those fit.
    ///
    /// <para>It is a prose measure rather than a picture size. The card's text
    /// column is what is left after the cover, and at 420 that column runs about
    /// forty-four characters at the body size — the bottom of the readable band.
    /// Allowing a narrower card would buy a column at full width and spend the
    /// sentence to get it, on a screen whose entire payload is the
    /// sentence.</para>
    /// </summary>
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(MinItemWidth), 420d);

    /// <summary>Space between cards, both axes. One number, because this is a grid.</summary>
    public static readonly StyledProperty<double> GutterProperty =
        AvaloniaProperty.Register<FeedGrid, double>(nameof(Gutter), 14d);

    /// <summary>
    /// Last width the panel was measured at. A panel inside a horizontally
    /// scrolling parent would be handed infinity; the Feed's page scroller
    /// disables that axis, so this is belt to CoverWall's braces rather than a
    /// case that arises — but laying out at infinity would produce one column
    /// several thousand pixels wide, which is worse than a stale width.
    /// </summary>
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
    /// Columns, and the width each card gets — the whole of this panel's
    /// geometry, as a pure function so it can be asserted without a window.
    ///
    /// <para><b>A row is charged for exactly the gutters it has.</b> The column
    /// count divides <c>width + gutter</c> by <c>min + gutter</c>, which adds one
    /// notional trailing gutter to both sides of the division and so charges
    /// <c>n</c> columns for <c>n − 1</c> gaps. Getting that wrong in the other
    /// direction is the <c>UniformGridLayout</c> bug §5.4 records: it charged
    /// every item in a row for a trailing gutter when counting, then packed rows
    /// greedily when placing, and the two disagreed by one column at every
    /// window width.</para>
    ///
    /// <para><b>Floor on the item width</b> keeps every card on a whole pixel so
    /// two arranged rects cannot round into each other; the remainder is under
    /// one pixel per column and stays at the right edge, invisible.</para>
    /// </summary>
    public static (int Columns, double ItemWidth) GeometryFor(double width, double minItemWidth, double gutter)
    {
        var min = Math.Max(1, minItemWidth);
        var columns = Math.Max(1, (int)Math.Floor((width + gutter) / (min + gutter)));
        var itemWidth = Math.Max(1, Math.Floor((width - ((columns - 1) * gutter)) / columns));
        return (columns, itemWidth);
    }
}
