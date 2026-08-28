using Hoard.App.Views;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The Feed's wrapping grid — the arithmetic, not the rendering.
///
/// <para><b>Why this is worth a test file at all.</b> §5.4 of the design system
/// records the one layout bug this codebase has already paid for: Avalonia's
/// <c>UniformGridLayout</c> charged every item in a row for a trailing gutter
/// when it computed items-per-line, then packed rows greedily when it placed
/// them, and the two disagreed by one column at every window width — an orphaned
/// tile and a scroll extent 22% too long. <see cref="FeedGrid"/> does the same
/// job on the same geometry, so it does the same arithmetic, and the arithmetic
/// is asserted rather than eyeballed.</para>
///
/// <para><b>The property under test is "flush":</b> whatever the width, the row
/// exactly fills it — the columns plus the gutters between them come back to the
/// width given, to within the sub-pixel remainder the floor deliberately leaves
/// at the right edge. Nothing here constructs a control or needs a window.</para>
/// </summary>
public sealed class FeedGridTests
{
    private const double Min = 420;
    private const double Gutter = 14;

    /// <summary>
    /// The two widths that were measured on the running window: the 1200px
    /// minimum, and the author's 3440px ultrawide. The grid's width is the
    /// window's less the rail, the pane's padding and the section's — 328px, read
    /// off the probe rather than derived.
    /// </summary>
    [Theory]
    [InlineData(888, 2)]     // 1200px window, the documented minimum
    [InlineData(1272, 2)]    // 1600
    [InlineData(1592, 3)]    // 1920
    [InlineData(3112, 7)]    // 3440, the ultrawide this was reviewed on
    public void The_column_count_is_what_the_width_fits(double width, int expected)
    {
        var (columns, _) = FeedGrid.GeometryFor(width, Min, Gutter);
        Assert.Equal(expected, columns);
    }

    /// <summary>
    /// A row's cards plus the gutters BETWEEN them fill the width. The slack is
    /// the floor's remainder and must stay under one pixel per column — anything
    /// larger means a gutter was charged to a card that has no neighbour, which
    /// is the failure §5.4 names.
    /// </summary>
    [Theory]
    [InlineData(888)]
    [InlineData(1272)]
    [InlineData(1592)]
    [InlineData(3112)]
    [InlineData(419)]
    [InlineData(1287)]
    public void A_row_fills_the_width_it_was_given(double width)
    {
        var (columns, itemWidth) = FeedGrid.GeometryFor(width, Min, Gutter);

        var used = (columns * itemWidth) + ((columns - 1) * Gutter);

        Assert.True(used <= width, $"{columns} x {itemWidth} overflowed {width}.");
        Assert.True(width - used < columns, $"{width - used}px of slack at {width} is a lost column.");
    }

    /// <summary>
    /// No card is ever drawn narrower than the floor. The floor is a prose
    /// measure — the sentence is this screen's payload — so a width that cannot
    /// fit two of them gets one card at the full width rather than two cramped
    /// ones.
    /// </summary>
    [Theory]
    [InlineData(400)]
    [InlineData(700)]
    [InlineData(853)]
    public void A_width_that_fits_one_card_gets_one_column(double width)
    {
        var (columns, itemWidth) = FeedGrid.GeometryFor(width, Min, Gutter);

        Assert.Equal(1, columns);
        Assert.Equal(Math.Floor(width), itemWidth);
    }

    /// <summary>
    /// The column count only ever goes up with the width. A count that dipped as
    /// the window grew would be a rounding fault, and it is exactly what an
    /// off-by-one gutter produces at the boundary widths.
    /// </summary>
    [Fact]
    public void Widening_the_window_never_costs_a_column()
    {
        var previous = 0;

        for (var width = 200d; width <= 4000d; width += 1d)
        {
            var (columns, itemWidth) = FeedGrid.GeometryFor(width, Min, Gutter);

            Assert.True(columns >= previous, $"{width}px dropped from {previous} to {columns} columns.");
            Assert.True(
                columns == 1 || itemWidth >= Min,
                $"{width}px drew {columns} columns at {itemWidth}px, under the {Min}px floor.");

            previous = columns;
        }
    }
}
