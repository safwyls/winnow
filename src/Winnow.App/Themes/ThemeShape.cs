namespace Winnow.App.Themes;

/// <summary>
/// The scalars a theme's ramps are built out of — the numbers that answer
/// "how far apart", where the seed colours answer "which colour".
///
/// <para><b>Why these are numbers in the format and the accent ramps are
/// not.</b> Every value here was measured across the four built-ins and came
/// back SPREAD: the edge runs 1.38:1 to 2.46:1, the well sits at 47% to 63% of
/// the ground, the chrome's translucent ink at 41% to 85% of its opaque one.
/// §14.1.1 says why — value structure and edge weight are two of the four axes
/// that separate a room, so a format that fixed them would only be able to
/// express one theme in four different hues, which is exactly the mistake the
/// first set of themes made.</para>
///
/// <para><b>What is NOT here was measured too, and came back flat.</b> The
/// hover and press steps on Volt and Danger land within a couple of percent of
/// each other in every built-in — Danger's press is 0.798, 0.793, 0.802, 0.799
/// of its value across the four — so they are engine constants in
/// <see cref="ThemeDerivation"/> rather than schema surface. An author who
/// wants a different one writes the colour into <c>overrides</c>, which is one
/// field instead of eight.</para>
///
/// <para>Every default here is the mean of the four built-ins, so a theme that
/// declares nothing but its eight seeds gets the house proportions.</para>
/// </summary>
public sealed record ThemeShape
{
    /// <summary>What a hover or a selection lifts a surface by, in HSV value.
    /// <c>SurfaceRaised</c> is one step up from <c>Surface</c> and
    /// <c>SurfaceHigh</c> is two. The flattest built-in and the starkest agree
    /// on this to within 0.012, which is why it has a default worth
    /// having.</summary>
    public double Elevation { get; init; } = 0.050;

    /// <summary>Where <c>Well</c> sits, as a fraction of <c>Ground</c>'s value.
    /// The tone under everything: the scrollbar track, the modal scrim, and —
    /// in the floating layout — every gap between panes.</summary>
    public double WellDepth { get; init; } = 0.55;

    /// <summary>
    /// <c>Line</c>'s contrast ratio against <c>Surface</c>, stated as the ratio
    /// rather than as a colour.
    ///
    /// <para>This is the single most expressive number in the format. §14.1.1's
    /// value-structure axis is almost entirely this: Nightshift is 2.46 and
    /// reads as one sheet of glass with the layout scribed on it, Tungsten is
    /// 1.38 and reads as felt where nothing has a hard boundary. Writing it as
    /// a ratio rather than a hex means an author changing the neutral does not
    /// have to re-pick the edge to keep the same room.</para>
    /// </summary>
    public double Edge { get; init; } = 1.60;

    /// <summary>The metadata ink's HSV value. §8 puts a floor under what this
    /// can be; <c>ThemeAudit</c> measures the result and says so.</summary>
    public double DimValue { get; init; } = 0.68;

    /// <summary>The metadata ink's saturation, as a fraction of the neutral's.
    /// §2: metadata reads as part of the room, so it carries the room's own
    /// chroma scaled down rather than a chroma of its own.</summary>
    public double DimChroma { get; init; } = 0.41;

    /// <summary>
    /// How dark the ink that sits ON a Volt fill goes, as its contrast ratio
    /// against Volt — the Play button's label, the "Same game" button's.
    ///
    /// <para><b>A ratio because that is the thing being decided.</b> What this
    /// ink has to be is readable on the fill; §8 asks 7:1 of the pair. It is a
    /// field rather than a constant because Volt's own brightness varies hugely
    /// across the set — Box art's is a near-white at 0.96 value and Winnow's a
    /// mint at 0.91 — and a fixed ratio put Box art's ink 45 units off. Stated
    /// as a ratio, an author who changes Volt does not have to re-pick the ink
    /// that goes on it.</para>
    /// </summary>
    public double VoltInkContrast { get; init; } = 9.5;

    /// <summary>The quietest ink that is still ink — watermarks, disabled
    /// arrows.</summary>
    public double FaintValue { get; init; } = 0.50;

    /// <summary>And its share of the neutral's chroma. Higher than
    /// <see cref="DimChroma"/> in every built-in: as an ink darkens toward the
    /// room it takes more of the room's colour, not less.</summary>
    public double FaintChroma { get; init; } = 0.65;

    /// <summary>
    /// <c>TranslucentSurface</c> as a fraction of <c>Surface</c>'s value — how
    /// much darker the chrome's ink goes at the far end of the slider.
    ///
    /// <para>§14.3: an ink chosen for an opaque ground cannot have alpha
    /// subtracted from it, so the chrome takes a darker ink as it opens up and
    /// the metadata ink brightens to pay for what is left. These two numbers
    /// and the two lifts below are that compensation, and they are the reason
    /// a user theme can be translucent at all rather than being forced solid.</para>
    /// </summary>
    public double ChromeInk { get; init; } = 0.48;

    /// <summary>The same for the art field's ink — <c>TranslucentChromeGround</c>,
    /// which the filter panel's fields and the floating layout's gaps both walk
    /// toward.</summary>
    public double GroundInk { get; init; } = 0.44;

    /// <summary>What the metadata ink brightens BY over the same walk, as a
    /// multiple of its value. All four built-ins sit between 1.131 and 1.145.</summary>
    public double DimLift { get; init; } = 1.14;

    /// <summary>And the faint ink's, which lifts slightly harder because it
    /// starts with less to lose.</summary>
    public double FaintLift { get; init; } = 1.20;

    /// <summary>The house proportions: what a theme that declares only its
    /// seeds is built to.</summary>
    public static ThemeShape Default { get; } = new();
}
