using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// WCAG 2.x relative luminance, contrast ratio, and sRGB source-over composite.
/// Used by the Appearance screen to state the slider's consequence live.
/// </summary>
public static class Colorimetry
{
    /// <summary>
    /// The brightest backdrop a wallpaper can produce. Nothing needs to be
    /// assumed about how Windows composes Mica for a number measured against
    /// this to hold: it is the ceiling.
    /// </summary>
    public static readonly Color White = Color.FromRgb(255, 255, 255);

    /// <summary>
    /// A dark desktop, back-solved from the composite Windows puts behind the
    /// chrome. The other end of the bracket from <see cref="White"/>.
    /// </summary>
    public static readonly Color DarkDesktop = Color.FromRgb(0x20, 0x1F, 0x1E);

    /// <summary>WCAG AA for normal text, and the floor §8 sets for metadata ink.</summary>
    public const double AaThreshold = 4.5;

    /// <summary>
    /// A dormant capsule for judging the field it hangs on. The section 5.1 floor
    /// applied to a mid-dark cover; a fixed reference, not a live value.
    /// </summary>
    public static readonly Color DormantCapsule = Color.FromRgb(0x2C, 0x32, 0x37);

    /// <summary>
    /// <paramref name="ink"/> composited over <paramref name="backdrop"/>,
    /// honouring the ink's own alpha.
    /// </summary>
    public static Color Over(Color ink, Color backdrop)
    {
        var a = ink.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round((ink.R * a) + (backdrop.R * (1 - a))),
            (byte)Math.Round((ink.G * a) + (backdrop.G * (1 - a))),
            (byte)Math.Round((ink.B * a) + (backdrop.B * (1 - a))));
    }

    public static double Luminance(Color c)
        => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    public static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return la > lb
            ? (la + 0.05) / (lb + 0.05)
            : (lb + 0.05) / (la + 0.05);
    }

    /// <summary>
    /// The window's ground as it actually composites — the gaps between the
    /// panes, and the caption, which is the same paint.
    ///
    /// <para>It is the outer of the window's two tiers and the only surface with
    /// nothing painted between it and the desktop, which is why every other
    /// measurement here starts by asking for it.</para>
    /// </summary>
    public static Color Shell(
        WinnowTheme theme, double transparency, WinnowLayout layout, Color backdrop)
        => Over(theme.Tokens(transparency, layout: layout)["ShellGround"], backdrop);

    /// <summary>
    /// What the rail's metadata ink measures at a given slider position, over a
    /// given backdrop — the rail composited on the window's ground, which is
    /// where it actually sits.
    /// </summary>
    public static double RailMetadataContrast(
        WinnowTheme theme, double transparency, Color backdrop, WinnowLayout layout = WinnowLayouts.Default)
    {
        var tokens = theme.Tokens(transparency, layout: layout);
        return Contrast(
            tokens["TextDim"],
            Over(tokens["ChromeSurface"], Shell(theme, transparency, layout, backdrop)));
    }

    /// <summary>
    /// The worst the metadata ink does on any reading surface the slider
    /// reaches, taken across both layouts so the mark cannot be invalidated
    /// by a layout change.
    /// </summary>
    public static double WorstMetadataContrast(
        WinnowTheme theme, double transparency, Color backdrop)
    {
        var worst = double.MaxValue;

        foreach (var layout in WinnowLayouts.All)
        {
            var tokens = theme.Tokens(transparency, layout: layout);
            var ink = tokens["TextDim"];
            var shell = Shell(theme, transparency, layout, backdrop);

            // The caption. In floating it IS the ground; in flush it is the
            // rail's fill on the ground. Over(alpha 0, x) is x, so one line
            // covers both.
            worst = Math.Min(worst, Contrast(ink, Over(tokens["CaptionFill"], shell)));

            var rail = Over(tokens["ChromeSurface"], shell);
            worst = Math.Min(worst, Contrast(ink, rail));
            worst = Math.Min(worst, Contrast(ink, Over(tokens["ChromeRaised"], rail)));
        }

        return worst;
    }

    /// <summary>
    /// The last whole percent at which every reading surface the slider reaches
    /// still clears AA against a white wallpaper, for this theme.
    ///
    /// <para>Walked rather than solved. The inks and the alpha move on different
    /// ramps, so the ratio is not monotone in any form worth inverting, and a
    /// hundred-step walk of a few multiplications is free.</para>
    /// </summary>
    public static int AaCeiling(WinnowTheme theme)
    {
        var last = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            if (WorstMetadataContrast(theme, percent / 100.0, White) < AaThreshold)
            {
                return last;
            }

            last = percent;
        }

        return 100;
    }

    /// <summary>
    /// The cover wall's field as it actually composites: the theme's ground at
    /// the pane tier's alpha, over the WINDOW's ground, over
    /// <paramref name="backdrop"/>.
    ///
    /// <para>The middle term is what changed with the two-tier restructure. A
    /// pane is painted on the window's ground rather than straight on the
    /// desktop, so the field's real composite carries both alphas — and it is
    /// their product, <c>1 − MinWallAlpha</c>, that §14.6's polarity argument is
    /// about. Measuring the token alone would report a lighter field than the one
    /// on screen.</para>
    /// </summary>
    public static Color WallField(
        WinnowTheme theme,
        double transparency,
        bool wallTranslucent,
        Color backdrop,
        WinnowLayout layout = WinnowLayouts.Default)
        => Over(
            theme.Tokens(transparency, wallTranslucent, layout)["WallGround"],
            Shell(theme, transparency, layout, backdrop));

    /// <summary>
    /// Whether the field is still darker than the art hung on it — the one
    /// question the wall's translucency has to answer.
    ///
    /// <para>§5.1's ramp is dark capsules on a dark field. The moment the field
    /// rises past a dormant capsule the encoding inverts: dimmed art starts
    /// reading as a hole rather than as something faded, and the ramp is the
    /// product.</para>
    /// </summary>
    public static bool WallKeepsItsPolarity(
        WinnowTheme theme,
        double transparency,
        bool wallTranslucent,
        Color backdrop,
        WinnowLayout layout = WinnowLayouts.Default)
        => Luminance(WallField(theme, transparency, wallTranslucent, backdrop, layout))
            <= Luminance(DormantCapsule);

    /// <summary>
    /// The last whole percent at which the translucent field still sits below a
    /// dormant capsule against a white wallpaper, in either layout.
    ///
    /// <para>Walked rather than solved, for the same reason
    /// <see cref="AaCeiling"/> is: it costs nothing and it cannot drift from the
    /// arithmetic it is describing. The number that matters is not this one on
    /// its own but its relation to <see cref="AaCeiling"/> — the wall must not
    /// be the thing that fails first. See <c>WinnowTheme.MinWallAlpha</c>.</para>
    /// </summary>
    public static int WallPolarityCeiling(WinnowTheme theme)
    {
        var last = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            foreach (var layout in WinnowLayouts.All)
            {
                if (!WallKeepsItsPolarity(theme, percent / 100.0, true, White, layout))
                {
                    return last;
                }
            }

            last = percent;
        }

        return 100;
    }


    private static double Channel(byte c)
    {
        var v = c / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
