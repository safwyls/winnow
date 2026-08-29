using System.Globalization;
using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// Runtime audit of a theme's contrast and Flare separation. Walks the
/// transparency range and warns; never refuses. Results are printed on the
/// theme's card in the Appearance screen.
/// </summary>
public static class ThemeAudit
{
    /// <summary>The separation §2 accepts between the unread marker and the
    /// destructive one, in degrees, so a red the size of a caption button is
    /// never taken for a 10px dot.</summary>
    private const double MinFlareDangerHue = 24;

    /// <summary>And from the selection colour, which is the other thing a dot
    /// could be mistaken for.</summary>
    private const double MinFlareVoltHue = 60;

    /// <summary>
    /// Everything worth saying about a theme that is not a parse failure.
    /// </summary>
    public static IReadOnlyList<ThemeDiagnostic> Inspect(WinnowTheme theme, string fileName)
    {
        var log = new List<ThemeDiagnostic>();

        void Warn(string field, string message)
            => log.Add(new ThemeDiagnostic(ThemeSeverity.Warning, fileName, field, message));

        // ── §2: Flare has exactly one job ───────────────────────────────────
        foreach (var (role, colour) in theme.Roles())
        {
            if (role != "Flare" && colour == theme.Flare)
            {
                Warn(
                    $"seeds.{role.ToLowerInvariant()}",
                    $"is the same colour as Flare. Flare means \"this game was patched since you played it\" and marks nothing else in the whole application - the moment a second thing wears it, the badge stops carrying that meaning and the unread count stops being readable at a glance. The theme still loads.");
            }
        }

        var dangerGap = HueGap(theme.Flare, theme.Danger);
        if (dangerGap < MinFlareDangerHue)
        {
            Warn(
                "seeds.flare",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"sits {dangerGap:0}° from Danger on the wheel, under the {MinFlareDangerHue:0}° the built-ins keep. Danger fills the window's close button, which is fifty times the area of an unread dot; at this separation a glance at the caption reads as an alarm. The theme still loads."));
        }

        var voltGap = HueGap(theme.Flare, theme.Volt);
        if (voltGap < MinFlareVoltHue)
        {
            Warn(
                "seeds.flare",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"sits {voltGap:0}° from Volt, under the {MinFlareVoltHue:0}° the built-ins keep. Volt is selection and recency and is everywhere; Flare has to be the one hue the room cannot produce. The theme still loads."));
        }

        // ── §8: the accessibility floor, on the opaque palette ──────────────
        var t = theme.Tokens(transparency: 0);
        Floor("TextDim on Surface", t["TextDim"], t["Surface"], Colorimetry.AaThreshold, "seeds.surface",
            "the rail's metadata ink");
        Floor("TextDim on SurfaceRaised", t["TextDim"], t["SurfaceRaised"], Colorimetry.AaThreshold, "structure.elevation",
            "the metadata ink on a selected row");
        Floor("TextDim on Ground", t["TextDim"], t["Ground"], Colorimetry.AaThreshold, "structure.dimValue",
            "the metadata ink on the art field");
        Floor("Text on Surface", t["Text"], t["Surface"], 7.0, "seeds.text", "the primary ink");
        Floor("VoltInk on Volt", t["VoltInk"], t["Volt"], 7.0, "overrides.VoltInk",
            "the ink on a Volt fill - the Play button, \"Same game\"");
        Floor("Flare on Surface", t["Flare"], t["Surface"], Colorimetry.AaThreshold, "seeds.flare",
            "the unread dot against the rail it sits on");

        // ── §14: the dark field is load-bearing, and here is what it holds ──
        // Not a refusal. A theme may go bright; §14.1.1 declines to SHIP a light
        // theme because it is a second pass over the tile scrim, the caption
        // order and the dormancy floor, not because a bright palette is
        // forbidden. What is owed is the list of what stops working.
        if (Colorimetry.Luminance(theme.Ground) > Colorimetry.Luminance(Colorimetry.DormantCapsule))
        {
            Warn(
                "seeds.ground",
                "is lighter than a dormant cover. §5.1's ramp is dark capsules on a dark field, so past this point a dimmed tile stops reading as faded art and starts reading as a hole punched in a lit field - which inverts the one encoding the product is built on. The tile hover scrim fades to this colour too, so it will read as a wash rather than as a shadow. The theme loads and everything else in it works.");
        }

        if (Colorimetry.Luminance(theme.Surface) < Colorimetry.Luminance(theme.Ground))
        {
            Warn(
                "seeds.surface",
                "is darker than the art field it surrounds. §9 asks that the covers be the first thing on screen with light in them and that the chrome be a bracket around a recess; with these two the other way round the wall reads as the recess's lid. Legal, and unlike anything the built-ins do.");
        }

        return log;

        void Floor(string what, Color ink, Color on, double threshold, string field, string why)
        {
            var ratio = Colorimetry.Contrast(ink, on);
            if (ratio < threshold)
            {
                Warn(
                    field,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{what} measures {ratio:0.00}:1, under the {threshold:0.0}:1 §8 sets - this is {why}. The theme loads; it will be hard to read."));
            }
        }
    }

    /// <summary>
    /// The numbers the Appearance screen prints for a theme.
    ///
    /// <para>Computed from <see cref="Colorimetry"/>, which is the same
    /// arithmetic the slider's own AA mark is drawn from — so a user theme's
    /// card and the mark on the track can never disagree about where the floor
    /// is.</para>
    /// </summary>
    public static ThemeReport Report(WinnowTheme theme)
    {
        var t = theme.Tokens(transparency: 0);
        return new ThemeReport(
            AaCeiling: Colorimetry.AaCeiling(theme),
            WallCeiling: Colorimetry.WallPolarityCeiling(theme),
            MetadataOnChrome: Colorimetry.WorstMetadataContrast(theme, 0, Colorimetry.White),
            PrimaryOnChrome: Colorimetry.Contrast(t["Text"], t["Surface"]),
            Edge: Colorimetry.Contrast(theme.Line, theme.Surface),
            // A LUMINANCE ratio, not a WCAG contrast ratio, because that is
            // what §14.1.1's value-structure column is: "Winnow 1.8x art→chrome,
            // Nightshift 1.4x, Box art 4.8x". Run through Contrast() the same
            // four themes come out at 1.13, 1.06 and 1.28 — true numbers about
            // a different question, and quoting §14's figures beside them would
            // be printing a measurement under someone else's caption.
            FieldToChrome: Colorimetry.Luminance(theme.Surface)
                / Math.Max(Colorimetry.Luminance(theme.Ground), 0.0001),
            FlareToDangerHue: HueGap(theme.Flare, theme.Danger),
            FlareToVoltHue: HueGap(theme.Flare, theme.Volt));
    }

    /// <summary>Degrees between two hues, the short way round.</summary>
    public static double HueGap(Color a, Color b)
    {
        var gap = Math.Abs(Hue(a) - Hue(b)) % 360;
        return gap > 180 ? 360 - gap : gap;
    }

    private static double Hue(Color c)
        => ThemeDerivation.Hsv.From(c).H * 360;
}

/// <summary>
/// A theme's measurements, in the order the Appearance screen states them.
/// </summary>
/// <param name="AaCeiling">The last whole percent of the transparency slider at
/// which every reading surface in the chrome still clears AA against a white
/// wallpaper. The headline number: it is what the slider's own mark is drawn
/// from, and it is the one figure that tells an author whether the palette they
/// picked can carry the feature the palette exists to enable.</param>
/// <param name="WallCeiling">And the last percent at which the cover wall's
/// field still sits below a dormant capsule, so the dormancy ramp keeps its
/// polarity. Wants to be HIGHER than <paramref name="AaCeiling"/> — the wall
/// must not be the thing that fails first.</param>
/// <param name="MetadataOnChrome">What the metadata ink measures on the surface
/// that does worst anywhere in the window, solid. That surface used to be a
/// selected rail row and is the title bar now: the rail joined the panes and the
/// caption sits on the window's ground, which is the most open thing there
/// is.</param>
/// <param name="PrimaryOnChrome">And the primary ink, on the rail.</param>
/// <param name="Edge">The <c>Line</c> against <c>Surface</c> ratio — §14.1.1's
/// value-structure axis, stated as the number the format takes.</param>
/// <param name="FieldToChrome">The jump from the art field to the chrome, as a
/// ratio of relative luminances — §14.1.1's own measure, where Nightshift is
/// 1.4x and Box art 4.8x.</param>
/// <param name="FlareToDangerHue">Degrees between the unread marker and the
/// destructive one.</param>
/// <param name="FlareToVoltHue">And between the unread marker and
/// selection.</param>
public sealed record ThemeReport(
    int AaCeiling,
    int WallCeiling,
    double MetadataOnChrome,
    double PrimaryOnChrome,
    double Edge,
    double FieldToChrome,
    double FlareToDangerHue,
    double FlareToVoltHue)
{
    /// <summary>The one line that goes on a theme card. Names the setting the
    /// number is about, because "24%" on its own is a figure with no
    /// unit.</summary>
    public string Headline => AaCeiling >= 100
        ? "Labels stay over AA at every transparency."
        : AaCeiling <= 0
            ? "Labels drop under AA the moment transparency leaves zero."
            : string.Create(CultureInfo.InvariantCulture, $"Labels stay over AA to {AaCeiling}% transparency.");
}
