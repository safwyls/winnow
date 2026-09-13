using Winnow.App.Views;
using Xunit;

namespace Winnow.Tests;

public sealed class FeedGridTests
{
    [Theory]
    [InlineData(500, 180)]
    [InlineData(888, 180)]
    [InlineData(1170, 180)]
    [InlineData(1290, 200)]
    [InlineData(1530, 240)]
    [InlineData(3112, 240)]
    public void Six_slots_keep_covers_readable_and_bounded(double width, double expectedWidth)
    {
        var (columns, itemWidth) = FeedGrid.GeometryFor(width, 180, 18);
        Assert.Equal(6, columns);
        Assert.Equal(expectedWidth, itemWidth);
    }

    [Theory]
    [InlineData(1170)]
    [InlineData(1287)]
    [InlineData(1530)]
    public void Six_covers_fill_the_available_width_until_the_size_cap(double width)
    {
        var (columns, itemWidth) = FeedGrid.GeometryFor(width, 180, 18);
        var used = columns * itemWidth + (columns - 1) * 18;
        Assert.InRange(width - used, 0, columns - double.Epsilon);
    }

    [Fact]
    public void Narrow_windows_overflow_horizontally_instead_of_wrapping()
    {
        var (columns, itemWidth) = FeedGrid.GeometryFor(888, 180, 18);
        Assert.True(columns * itemWidth + (columns - 1) * 18 > 888);
        Assert.Equal(180, itemWidth);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    [InlineData(0)]
    public void Unmeasured_viewports_use_a_finite_readable_cover(double width)
    {
        Assert.Equal((6, 180d), FeedGrid.GeometryFor(width, 180, 18));
    }
}
