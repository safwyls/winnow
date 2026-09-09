using System.Globalization;
using Winnow.App.Converters;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// <see cref="ScaledLength"/> at real window sizes. The card's width cap is half
/// the window between the 860 it shipped with and the 1582 ceiling; its height
/// is two-thirds and never below 720. The hero's own cap is gone: the
/// screenshot opens in the lightbox overlay, which is capped against the shot's
/// native size rather than against a fraction of the window
/// (design-system.md §10.7).
/// </summary>
public sealed class DetailsModalScaleTests
{
    private static readonly ScaledLength CardWidth = new() { Fraction = 0.5, Least = 860, Most = 1582 };

    private static readonly ScaledLength CardHeight = new() { Fraction = 0.667, Least = 720 };

    /// <summary>The card's width cap is half the window, floored at 860 and
    /// capped at 1582. The 1582 stays as a number but no longer has a
    /// derivation: it was the card width at which the in-modal hero reached
    /// IGDB's native 1280x720. The tabbed modal retains that ceiling, and prose
    /// remains bounded by the reading measure.</summary>
    [Theory]
    [InlineData(1200, 860)]
    [InlineData(1280, 860)]
    [InlineData(1600, 860)]
    [InlineData(1720, 860)]
    [InlineData(1920, 960)]
    [InlineData(2560, 1280)]
    [InlineData(3164, 1582)]
    [InlineData(3440, 1582)]
    [InlineData(3840, 1582)]
    [InlineData(7680, 1582)]
    public void The_card_takes_half_the_window_between_the_width_it_has_today_and_the_ceiling(
        double window, double expected)
        => Assert.Equal(expected, CardWidth.Apply(window), 2);

    /// <summary>
    /// The card's height cap is two-thirds of the window, floored at 720.
    /// No ceiling: the tabs are bounded scroll regions, so more height
    /// is more content on screen.
    /// </summary>
    [Theory]
    [InlineData(640, 720)]
    [InlineData(820, 720)]
    [InlineData(900, 720)]
    [InlineData(1080, 720.36)]
    [InlineData(1440, 960.48)]
    [InlineData(2160, 1440.72)]
    public void The_card_takes_two_thirds_of_the_window_height_and_never_less_than_it_has_today(
        double window, double expected)
        => Assert.Equal(expected, CardHeight.Apply(window), 2);

    /// <summary>
    /// The width floor must stay above the card's <c>MinWidth</c> of 700,
    /// so a capped modal remains wide enough for its controls.
    /// </summary>
    [Fact]
    public void The_width_floor_stays_above_the_cards_own_minimum()
    {
        Assert.True(CardWidth.Apply(0) > 700);
        Assert.True(CardWidth.Apply(1) > 700);
    }

    /// <summary>
    /// A window that has not laid out yet — zero, negative, NaN or
    /// infinity — yields the cap the modal shipped with rather than
    /// an uncapped card.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_window_with_no_measured_size_yields_the_shipped_cap(double window)
    {
        Assert.Equal(860, CardWidth.Apply(window), 2);
        Assert.Equal(720, CardHeight.Apply(window), 2);
    }

    /// <summary>
    /// A non-double value (null, a string, a boxed int) passes through
    /// <see cref="ScaledLength.Convert"/> as NaN and yields the floor.
    /// </summary>
    [Fact]
    public void A_value_that_is_not_a_length_yields_the_shipped_cap()
        => Assert.Equal(860d, CardWidth.Convert(null, typeof(double), null, CultureInfo.InvariantCulture));

    /// <summary>
    /// The converter is one-way: a card cannot resize the window it
    /// is capped against.
    /// </summary>
    [Fact]
    public void The_card_is_never_resized_back_into_the_window()
        => Assert.Throws<NotSupportedException>(
            () => CardWidth.ConvertBack(860d, typeof(double), null, CultureInfo.InvariantCulture));
}
