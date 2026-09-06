using System.Globalization;
using Avalonia.Data.Converters;

namespace Winnow.App.Converters;

/// <summary>
/// One-way <see cref="IValueConverter"/> from an available length (a window
/// dimension) to a maximum for the element bound to it. The three
/// details-modal caps — card width, card height, hero height — are the
/// only callers.
///
/// When the available length is not finite and positive — a window that
/// has not laid out yet, or a modal that has never been shown —
/// <see cref="Least"/> is returned, which is the cap the modal shipped
/// with, so the first frame is never uncapped.
/// </summary>
public sealed class ScaledLength : IValueConverter
{
    /// <summary>Multiplier applied to the available length.</summary>
    public double Fraction { get; set; }

    /// <summary>Floor: the cap never falls below this value.</summary>
    public double Least { get; set; }

    /// <summary>Ceiling: the cap never exceeds this value.
    /// <see cref="double.PositiveInfinity"/> means no ceiling.</summary>
    public double Most { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// The whole scaling rule. Public because the unit tests exercise it
    /// directly rather than going through <see cref="Convert"/>.
    /// </summary>
    public double Apply(double available)
    {
        if (!double.IsFinite(available) || available <= 0)
        {
            return Least;
        }

        var scaled = available * Fraction;
        return scaled < Least ? Least : scaled > Most ? Most : scaled;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Apply(value is double available ? available : double.NaN);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException(
            $"{nameof(ScaledLength)} is a one-way converter: a window is not resized by the card inside it.");
}
