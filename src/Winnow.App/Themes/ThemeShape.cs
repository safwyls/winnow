namespace Winnow.App.Themes;

/// <summary>
/// The scalars a theme's ramps are built from -- the numbers that answer
/// "how far apart", where the seed colours answer "which colour". Defaults
/// are the mean of the four built-ins.
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

    /// <summary><c>Line</c>'s contrast ratio against <c>Surface</c>. The most
    /// expressive number in the format: Nightshift 2.46, Tungsten 1.38.</summary>
    public double Edge { get; init; } = 1.60;

    /// <summary>The metadata ink's HSV value. §8 puts a floor under what this
    /// can be; <c>ThemeAudit</c> measures the result and says so.</summary>
    public double DimValue { get; init; } = 0.68;

    /// <summary>The metadata ink's saturation, as a fraction of the neutral's.
    /// §2: metadata reads as part of the room, so it carries the room's own
    /// chroma scaled down rather than a chroma of its own.</summary>
    public double DimChroma { get; init; } = 0.41;

    /// <summary>VoltInk's contrast ratio against Volt. A ratio so changing
    /// Volt does not require re-picking its ink.</summary>
    public double VoltInkContrast { get; init; } = 9.5;

    /// <summary>The quietest ink that is still ink — watermarks, disabled
    /// arrows.</summary>
    public double FaintValue { get; init; } = 0.50;

    /// <summary>And its share of the neutral's chroma. Higher than
    /// <see cref="DimChroma"/> in every built-in: as an ink darkens toward the
    /// room it takes more of the room's colour, not less.</summary>
    public double FaintChroma { get; init; } = 0.65;

    /// <summary><c>TranslucentSurface</c> as a fraction of <c>Surface</c>'s
    /// value. Part of the section 14.3 ink compensation that makes translucency
    /// work.</summary>
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
