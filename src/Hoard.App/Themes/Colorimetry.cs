using Avalonia.Media;

namespace Hoard.App.Themes;

/// <summary>
/// WCAG 2.x relative luminance and contrast ratio, plus an sRGB source-over
/// composite — which is what the GPU does, so it is what the window shows.
///
/// <para>This exists so the Appearance screen can state the consequence of the
/// transparency slider <i>live</i>, at the position the user is holding, rather
/// than quoting a number somebody measured once. §8's floor is a claim about the
/// running application; a settings screen that describes a different build of it
/// is worse than one that says nothing.</para>
///
/// <para><b>ThemeContrastTests deliberately carries its own copy of these
/// sums.</b> A test that calls the same arithmetic it is checking proves the
/// code agrees with itself. The two are kept separate on purpose.</para>
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
    /// A dark desktop: the other end of the bracket, and a real measurement
    /// rather than a round number.
    ///
    /// <para>Back-solved from the composite Windows put behind our chrome on this
    /// machine — the tone its own dark backdrop lands on, whatever the wallpaper
    /// under the window happens to be. Anyone running a dark wallpaper is at or
    /// near this; anyone running a bright one is somewhere between here and
    /// <see cref="White"/>, which is why the Appearance screen states both and
    /// claims neither is "the" number.</para>
    /// </summary>
    public static readonly Color DarkDesktop = Color.FromRgb(0x20, 0x1F, 0x1E);

    /// <summary>WCAG AA for normal text, and the floor §8 sets for metadata ink.</summary>
    public const double AaThreshold = 4.5;

    /// <summary>
    /// A dormant capsule, for judging the field it hangs on.
    ///
    /// <para>The §5.1 floor — saturation 0.22, hue −6°, brightness 0.68 — applied
    /// to an ordinary dark-blue cover (<c>#2B4C74</c>, the same stimulus the
    /// Appearance screen's theme miniatures use). Written down rather than
    /// recomputed because it is a fixed reference and not a live value:
    /// <c>PlaceholderArt.Floor</c> is the arithmetic, this is one answer from it.</para>
    ///
    /// <para>It is deliberately not the DARKEST capsule a library can hold. No
    /// field stays below a near-black cover, and asking it to would rule out
    /// every setting including the ones that look right. This is a middling dark
    /// one, which is what the wall is mostly made of.</para>
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
    /// What the rail's metadata ink measures at a given slider position, over a
    /// given backdrop. The one number the Appearance screen reports, because it
    /// is the one §8 names a floor for.
    /// </summary>
    public static double RailMetadataContrast(HoardTheme theme, double transparency, Color backdrop)
    {
        var tokens = theme.Tokens(transparency);
        return Contrast(tokens["TextDim"], Over(tokens["ChromeSurface"], backdrop));
    }

    /// <summary>
    /// The worst the metadata ink does anywhere on the chrome at a given slider
    /// position: the rail, and a hovered or selected row on it.
    ///
    /// <para>The SELECTED ROW is the one that binds, and it is not obvious — it
    /// is the rail with a veil of <c>Text</c> over it, so it is the lightest
    /// reading surface in the window and the first to lose its ink. §8 already
    /// singles it out for the same reason on the opaque palette.</para>
    ///
    /// <para><b>The command bar used to be a third term here and no longer is,
    /// because it is no longer chrome.</b> Its search box, its labels and its
    /// sort menu sit inside the library pane now (§15.1, revised), so they are
    /// measured where §14.7 measures every other pane ink — against a ground that
    /// admits <c>1 − MinWallAlpha</c> rather than the chrome's reach, and that
    /// fails at 59–73% of the slider against the chrome's 26–31%. It was never
    /// the binding term while it was here: <c>Ground</c> is darker than
    /// <c>Surface</c> at the same alpha, so a light ink on the bar always read
    /// better than the same ink on the rail. Dropping it moves no theme's
    /// ceiling — 27 / 31 / 30 / 26 before and after — which is asserted rather
    /// than claimed.</para>
    /// </summary>
    public static double ChromeMetadataContrast(HoardTheme theme, double transparency, Color backdrop)
    {
        var tokens = theme.Tokens(transparency);
        var rail = Over(tokens["ChromeSurface"], backdrop);
        var row = Over(tokens["ChromeRaised"], rail);
        var ink = tokens["TextDim"];

        return Math.Min(Contrast(ink, rail), Contrast(ink, row));
    }

    /// <summary>
    /// The last whole percent at which every reading surface in the chrome still
    /// clears AA against a white wallpaper, for this theme.
    ///
    /// <para>Walked rather than solved. The inks and the alpha move on different
    /// ramps, so the ratio is not monotone in any form worth inverting, and a
    /// hundred-step walk of a few multiplications is free.</para>
    /// </summary>
    public static int AaCeiling(HoardTheme theme)
    {
        var last = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            if (ChromeMetadataContrast(theme, percent / 100.0, White) < AaThreshold)
            {
                return last;
            }

            last = percent;
        }

        return 100;
    }

    /// <summary>
    /// The cover wall's field as it actually composites: the theme's ground at
    /// the wall's own alpha, over <paramref name="backdrop"/>.
    /// </summary>
    public static Color WallField(
        HoardTheme theme, double transparency, bool wallTranslucent, Color backdrop)
        => Over(theme.Tokens(transparency, wallTranslucent)["WallGround"], backdrop);

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
        HoardTheme theme, double transparency, bool wallTranslucent, Color backdrop)
        => Luminance(WallField(theme, transparency, wallTranslucent, backdrop))
            <= Luminance(DormantCapsule);

    /// <summary>
    /// The last whole percent at which the translucent field still sits below a
    /// dormant capsule against a white wallpaper.
    ///
    /// <para>Walked rather than solved, for the same reason
    /// <see cref="AaCeiling"/> is: it costs nothing and it cannot drift from the
    /// arithmetic it is describing. The number that matters is not this one on
    /// its own but its relation to <see cref="AaCeiling"/> — the wall must not
    /// be the thing that fails first. See <c>HoardTheme.MinWallAlpha</c>.</para>
    /// </summary>
    public static int WallPolarityCeiling(HoardTheme theme)
    {
        var last = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            if (!WallKeepsItsPolarity(theme, percent / 100.0, true, White))
            {
                return last;
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
