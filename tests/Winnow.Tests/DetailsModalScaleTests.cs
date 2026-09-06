using System.Globalization;
using Winnow.App.Converters;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Tests <see cref="ScaledLength"/> at real window sizes: the card's cap
/// is half the window between the 860 it shipped with and the measured
/// 1582 ceiling, height is two-thirds and never below 720, and the hero
/// is three-tenths and never below 200.
/// </summary>
public sealed class DetailsModalScaleTests
{
    private static readonly ScaledLength CardWidth = new() { Fraction = 0.5, Least = 860, Most = 1582 };

    private static readonly ScaledLength CardHeight = new() { Fraction = 0.667, Least = 720 };

    private static readonly ScaledLength HeroHeight = new() { Fraction = 0.3, Least = 200 };

    /// <summary>
    /// The card's width cap is half the window, floored at 860 and
    /// capped at 1582 — the width at which the hero is drawn at the
    /// native resolution IGDB delivers.
    /// </summary>
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
    /// No ceiling: the bands are bounded scroll regions, so more height
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
    /// The hero's height cap is three-tenths of the window, floored at
    /// 200. The fraction reproduces roughly what shipped at the
    /// smallest window and grows from there.
    /// </summary>
    [Theory]
    [InlineData(640, 200)]
    [InlineData(667, 200.1)]
    [InlineData(820, 246)]
    [InlineData(1080, 324)]
    [InlineData(1440, 432)]
    [InlineData(2160, 648)]
    public void The_hero_takes_three_tenths_of_the_window_height_and_never_less_than_the_200_it_had(
        double window, double expected)
        => Assert.Equal(expected, HeroHeight.Apply(window), 2);

    /// <summary>
    /// The width floor must stay above the card's <c>MinWidth</c> of 700,
    /// which is what keeps the 420px right column the narrowest case any
    /// measurement has to hold for.
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
        Assert.Equal(200, HeroHeight.Apply(window), 2);
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
