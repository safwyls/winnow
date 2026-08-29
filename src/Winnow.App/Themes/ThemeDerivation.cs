using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// Seeds plus proportions in, a complete <see cref="WinnowTheme"/> out; the same
/// arithmetic run backwards exports a built-in as a template. Ramps are walked
/// in HSV (not RGB) to preserve hue through the neutral family.
/// </summary>
public static class ThemeDerivation
{
    // ── The accent steps, measured across all four built-ins ───────────────
    // Value multipliers and chroma multipliers, applied in HSV. The spread
    // across the set is given beside each one; anything this tight is a
    // constant, not a setting.
    private const double VoltHoverValue = 1.03;     // 1.000 - 1.033
    private const double VoltHoverChroma = 0.75;    // 0.692 - 0.796 (Box art excepted; it overrides)
    private const double VoltPressValue = 0.89;     // 0.885 - 0.901
    private const double VoltPressChroma = 1.10;    // 1.074 - 1.117 (same exception)
    private const double DangerHoverValue = 1.055;  // 1.043 - 1.069
    private const double DangerHoverChroma = 0.865; // 0.849 - 0.877
    private const double DangerPressValue = 0.798;  // 0.793 - 0.802
    private const double DangerPressChroma = 1.027; // 1.017 - 1.045

    /// <summary>The ink that sits ON a Volt fill (the Play button, "Same
    /// game"), as a chroma multiplier and a contrast target rather than a
    /// colour: what it has to be is READABLE on the fill, and a ratio says that
    /// where a hex only implies it. §8 asks 7:1 of this pair; the derivation
    /// aims at 9 so a theme is not sitting on the floor.</summary>
    private const double VoltInkChroma = 1.15;

    /// <summary>The ink on a Danger fill is a near-white carrying a trace of the
    /// danger hue — 0.051 to 0.063 saturation at full value in all four.</summary>
    private const double DangerInkChroma = 0.06;

    /// <summary>The translucent inks desaturate slightly as they brighten, which
    /// is the same simultaneous-contrast correction §2 makes for the opaque
    /// pair. 0.81-0.94 and 0.72-1.01 across the set.</summary>
    private const double DimLiftChroma = 0.87;

    private const double FaintLiftChroma = 0.85;

    /// <summary>
    /// Exponent in <c>S' = S * r^-k</c> for darkening a neutral's chroma.
    /// Without it, deep tones lose their room colour and fade to black.
    /// Applies only downward; lifted tones keep their saturation.
    /// </summary>
    private const double DarkeningChroma = 0.42;

    /// <summary>The sixteen colours this can produce. In the order the exported
    /// template lists them, which is the order <see cref="WinnowTheme"/> declares
    /// them: the neutral family, then the inks, then the roles, then the
    /// translucent set.</summary>
    public static IReadOnlyList<string> DerivedFields { get; } =
    [
        "Well",
        "SurfaceRaised",
        "SurfaceHigh",
        "Line",
        "TextDim",
        "TextFaint",
        "VoltInk",
        "VoltHover",
        "VoltPress",
        "DangerHover",
        "DangerPress",
        "DangerInk",
        "TranslucentSurface",
        "TranslucentChromeGround",
        "TranslucentTextDim",
        "TranslucentTextFaint",
    ];

    /// <summary>The eight seed names, in the order the exported template lists
    /// them and the order <see cref="ThemeSeeds"/> declares them.</summary>
    public static IReadOnlyList<string> SeedFields { get; } =
        ["ground", "surface", "text", "flare", "volt", "amber", "azure", "danger"];

    /// <summary>
    /// Builds a theme. <paramref name="overrides"/> wins over the derivation for
    /// any field it names; unnamed fields are derived.
    /// </summary>
    public static WinnowTheme Compose(
        string id,
        string name,
        string reason,
        ThemeSeeds seeds,
        ThemeShape shape,
        IReadOnlyDictionary<string, Color>? overrides = null,
        ThemeAppearanceDefaults? defaults = null,
        string? sourceFile = null)
    {
        var derived = Derive(seeds, shape, overrides);

        Color Pick(string field)
            => overrides is not null && overrides.TryGetValue(field, out var c) ? c : derived[field];

        return new WinnowTheme
        {
            Id = id,
            Name = name,
            Reason = reason,

            Well = Pick("Well"),
            Ground = seeds.Ground,
            Surface = seeds.Surface,
            SurfaceRaised = Pick("SurfaceRaised"),
            SurfaceHigh = Pick("SurfaceHigh"),
            Line = Pick("Line"),

            Text = seeds.Text,
            TextDim = Pick("TextDim"),
            TextFaint = Pick("TextFaint"),

            Flare = seeds.Flare,
            Volt = seeds.Volt,
            VoltInk = Pick("VoltInk"),
            VoltHover = Pick("VoltHover"),
            VoltPress = Pick("VoltPress"),
            Amber = seeds.Amber,
            Azure = seeds.Azure,
            Danger = seeds.Danger,
            DangerHover = Pick("DangerHover"),
            DangerPress = Pick("DangerPress"),
            DangerInk = Pick("DangerInk"),

            TranslucentSurface = Pick("TranslucentSurface"),
            TranslucentChromeGround = Pick("TranslucentChromeGround"),
            TranslucentTextDim = Pick("TranslucentTextDim"),
            TranslucentTextFaint = Pick("TranslucentTextFaint"),

            Defaults = defaults,
            SourceFile = sourceFile,
        };
    }

    /// <summary>
    /// The sixteen derived colours. <paramref name="overrides"/> is read at each
    /// link so translucent inks derive from the effective TextDim/TextFaint.
    /// </summary>
    public static Dictionary<string, Color> Derive(
        ThemeSeeds seeds,
        ThemeShape shape,
        IReadOnlyDictionary<string, Color>? overrides = null)
    {
        var ground = Hsv.From(seeds.Ground);
        var surface = Hsv.From(seeds.Surface);
        var volt = Hsv.From(seeds.Volt);
        var danger = Hsv.From(seeds.Danger);

        var textDim = new Hsv(surface.H, surface.S * shape.DimChroma, shape.DimValue);
        var textFaint = new Hsv(surface.H, surface.S * shape.FaintChroma, shape.FaintValue);

        // The elevation step is chained rather than doubled: SurfaceHigh is one
        // step above SurfaceRaised, which is what "one step above" means and
        // what keeps the two agreeing when an author states the middle one.
        var raised = Lift(surface, shape.Elevation);
        var effectiveRaised = Effective("SurfaceRaised", raised);

        var voltInk = new Hsv(volt.H, Math.Min(1, volt.S * VoltInkChroma), 0);
        voltInk = voltInk with { V = SolveDarker(voltInk, seeds.Volt, shape.VoltInkContrast) };

        var effectiveDim = Effective("TextDim", textDim);
        var effectiveFaint = Effective("TextFaint", textFaint);

        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["Well"] = Darken(ground, shape.WellDepth).ToColor(),
            ["SurfaceRaised"] = raised.ToColor(),
            ["SurfaceHigh"] = Lift(effectiveRaised, shape.Elevation).ToColor(),
            ["Line"] = (surface with { V = SolveLighter(surface, seeds.Surface, shape.Edge) }).ToColor(),

            ["TextDim"] = textDim.ToColor(),
            ["TextFaint"] = textFaint.ToColor(),

            ["VoltInk"] = voltInk.ToColor(),
            ["VoltHover"] = new Hsv(volt.H, volt.S * VoltHoverChroma, volt.V * VoltHoverValue).ToColor(),
            ["VoltPress"] = new Hsv(volt.H, volt.S * VoltPressChroma, volt.V * VoltPressValue).ToColor(),

            ["DangerHover"] = new Hsv(danger.H, danger.S * DangerHoverChroma, danger.V * DangerHoverValue).ToColor(),
            ["DangerPress"] = new Hsv(danger.H, danger.S * DangerPressChroma, danger.V * DangerPressValue).ToColor(),
            ["DangerInk"] = new Hsv(danger.H, DangerInkChroma, 1.0).ToColor(),

            ["TranslucentSurface"] = Darken(surface, shape.ChromeInk).ToColor(),
            ["TranslucentChromeGround"] = Darken(ground, shape.GroundInk).ToColor(),
            ["TranslucentTextDim"] = new Hsv(
                effectiveDim.H, effectiveDim.S * DimLiftChroma, effectiveDim.V * shape.DimLift).ToColor(),
            ["TranslucentTextFaint"] = new Hsv(
                effectiveFaint.H, effectiveFaint.S * FaintLiftChroma, effectiveFaint.V * shape.FaintLift).ToColor(),
        };

        Hsv Effective(string field, Hsv fallback)
            => overrides is not null && overrides.TryGetValue(field, out var c) ? Hsv.From(c) : fallback;
    }

    /// <summary>A neutral one step up: value rises, saturation is untouched.
    /// The built-ins shed a little chroma as they climb; taking none is within
    /// a unit of every one of them and needs no constant.</summary>
    private static Hsv Lift(Hsv neutral, double step) => neutral with { V = neutral.V + step };

    /// <summary>A neutral taken down to <paramref name="ratio"/> of its value,
    /// keeping the room's colour rather than fading to black. See
    /// <see cref="DarkeningChroma"/>.</summary>
    private static Hsv Darken(Hsv neutral, double ratio)
    {
        var r = Math.Clamp(ratio, 0.001, 1);
        return new Hsv(
            neutral.H,
            Math.Min(1, neutral.S * Math.Pow(r, -DarkeningChroma)),
            neutral.V * r);
    }

    /// <summary>The seeds of an existing theme — the eight fields
    /// <see cref="Compose"/> reads straight through.</summary>
    public static ThemeSeeds SeedsOf(WinnowTheme theme) => new()
    {
        Ground = theme.Ground,
        Surface = theme.Surface,
        Text = theme.Text,
        Flare = theme.Flare,
        Volt = theme.Volt,
        Amber = theme.Amber,
        Azure = theme.Azure,
        Danger = theme.Danger,
    };

    /// <summary>
    /// The arithmetic run backwards: what proportions is this theme built to?
    /// </summary>
    public static ThemeShape Fit(WinnowTheme theme)
        => Refine(theme, RawFit(theme));

    /// <summary>
    /// Which colours each scalar is answerable for. Used to refine the fit, and
    /// in dependency order: a scalar is judged against the fields it produces
    /// and nothing else.
    /// </summary>
    /// <summary>Decimal places tried, shortest first, when writing a fitted
    /// scalar into an exported template.</summary>
    private static readonly int[] Precisions = [2, 3, 4, 5, 6];

    private static readonly (string Scalar, string[] Fields)[] ScalarTargets =
    [
        ("elevation", ["SurfaceRaised", "SurfaceHigh"]),
        ("wellDepth", ["Well"]),
        ("edge", ["Line"]),
        ("dimValue", ["TextDim"]),
        ("dimChroma", ["TextDim"]),
        ("voltInkContrast", ["VoltInk"]),
        ("faintValue", ["TextFaint"]),
        ("faintChroma", ["TextFaint"]),
        ("chromeInk", ["TranslucentSurface"]),
        ("groundInk", ["TranslucentChromeGround"]),
        ("dimLift", ["TranslucentTextDim"]),
        ("faintLift", ["TranslucentTextFaint"]),
    ];

    /// <summary>
    /// Rounds each fitted scalar to the shortest decimal that still reproduces
    /// the colours it governs, so the export stays legible. Each scalar is
    /// refined against upstream colours to avoid cascading misses.
    /// </summary>
    private static ThemeShape Refine(WinnowTheme theme, ThemeShape raw)
    {
        var seeds = SeedsOf(theme);
        var actual = ActualDerivedFields(theme);

        // The three links anything else derives THROUGH, pinned to what the
        // theme actually holds.
        var upstream = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["SurfaceRaised"] = theme.SurfaceRaised,
            ["TextDim"] = theme.TextDim,
            ["TextFaint"] = theme.TextFaint,
        };

        var shape = raw;
        foreach (var (scalar, fields) in ScalarTargets)
        {
            var exact = Read(raw, scalar);
            var best = exact;
            var target = Score(With(shape, scalar, exact), fields);

            foreach (var places in Precisions)
            {
                var candidate = Math.Round(exact, places);
                if (Score(With(shape, scalar, candidate), fields) >= target)
                {
                    best = candidate;
                    break;
                }
            }

            shape = With(shape, scalar, best);
        }

        return shape;

        int Score(ThemeShape candidate, string[] fields)
        {
            var derived = Derive(seeds, candidate, upstream);
            return fields.Count(f => derived[f] == actual[f]);
        }
    }

    private static double Read(ThemeShape s, string scalar) => scalar switch
    {
        "elevation" => s.Elevation,
        "wellDepth" => s.WellDepth,
        "edge" => s.Edge,
        "dimValue" => s.DimValue,
        "dimChroma" => s.DimChroma,
        "voltInkContrast" => s.VoltInkContrast,
        "faintValue" => s.FaintValue,
        "faintChroma" => s.FaintChroma,
        "chromeInk" => s.ChromeInk,
        "groundInk" => s.GroundInk,
        "dimLift" => s.DimLift,
        _ => s.FaintLift,
    };

    private static ThemeShape With(ThemeShape s, string scalar, double value) => scalar switch
    {
        "elevation" => s with { Elevation = value },
        "wellDepth" => s with { WellDepth = value },
        "edge" => s with { Edge = value },
        "dimValue" => s with { DimValue = value },
        "dimChroma" => s with { DimChroma = value },
        "voltInkContrast" => s with { VoltInkContrast = value },
        "faintValue" => s with { FaintValue = value },
        "faintChroma" => s with { FaintChroma = value },
        "chromeInk" => s with { ChromeInk = value },
        "groundInk" => s with { GroundInk = value },
        "dimLift" => s with { DimLift = value },
        _ => s with { FaintLift = value },
    };

    /// <summary>The ratios as measured, at full precision.</summary>
    private static ThemeShape RawFit(WinnowTheme theme)
    {
        var ground = Hsv.From(theme.Ground);
        var surface = Hsv.From(theme.Surface);
        var raised = Hsv.From(theme.SurfaceRaised);
        var dim = Hsv.From(theme.TextDim);
        var faint = Hsv.From(theme.TextFaint);

        return new ThemeShape
        {
            // Fitted off the FIRST step only. The step is chained, so the
            // second one is a consequence rather than a second measurement, and
            // averaging the two would put a theme whose steps differ a unit out
            // on both of them instead of on one.
            Elevation = raised.V - surface.V,
            WellDepth = Safe(Hsv.From(theme.Well).V, ground.V),
            Edge = Colorimetry.Contrast(theme.Line, theme.Surface),
            DimValue = dim.V,
            DimChroma = Safe(dim.S, surface.S),
            VoltInkContrast = Colorimetry.Contrast(theme.VoltInk, theme.Volt),
            FaintValue = faint.V,
            FaintChroma = Safe(faint.S, surface.S),
            ChromeInk = Safe(Hsv.From(theme.TranslucentSurface).V, surface.V),
            GroundInk = Safe(Hsv.From(theme.TranslucentChromeGround).V, ground.V),
            DimLift = Safe(Hsv.From(theme.TranslucentTextDim).V, dim.V),
            FaintLift = Safe(Hsv.From(theme.TranslucentTextFaint).V, faint.V),
        };

        // A ratio against a zero denominator is not an error worth throwing
        // over: a theme whose Ground is pure black is legal and its well depth
        // is simply not a ratio. The shape default stands.
        static double Safe(double numerator, double denominator)
            => denominator <= 0 ? 0.5 : numerator / denominator;
    }

    /// <summary>The derived fields whose value this theme does NOT agree with —
    /// exactly the set an exported template has to carry explicitly.</summary>
    /// <remarks>
    /// Walked in dependency order, re-deriving at each field, because four of
    /// the sixteen are derived THROUGH another one: whether
    /// <c>TranslucentTextDim</c> needs stating depends on whether
    /// <c>TextDim</c> was stated a moment ago. Judging them all against one
    /// override-free derivation would list fields that the reader, walking the
    /// same chain, would have got right on its own.
    /// </remarks>
    public static Dictionary<string, Color> ResidualOverrides(WinnowTheme theme, ThemeShape shape)
    {
        var seeds = SeedsOf(theme);
        var actual = ActualDerivedFields(theme);
        var residual = new Dictionary<string, Color>(StringComparer.Ordinal);

        foreach (var field in DerivedFields)
        {
            if (Derive(seeds, shape, residual)[field] != actual[field])
            {
                residual[field] = actual[field];
            }
        }

        return residual;
    }

    /// <summary>What the theme actually holds in each derived slot.</summary>
    public static Dictionary<string, Color> ActualDerivedFields(WinnowTheme theme)
        => new(StringComparer.Ordinal)
        {
            ["Well"] = theme.Well,
            ["SurfaceRaised"] = theme.SurfaceRaised,
            ["SurfaceHigh"] = theme.SurfaceHigh,
            ["Line"] = theme.Line,
            ["TextDim"] = theme.TextDim,
            ["TextFaint"] = theme.TextFaint,
            ["VoltInk"] = theme.VoltInk,
            ["VoltHover"] = theme.VoltHover,
            ["VoltPress"] = theme.VoltPress,
            ["DangerHover"] = theme.DangerHover,
            ["DangerPress"] = theme.DangerPress,
            ["DangerInk"] = theme.DangerInk,
            ["TranslucentSurface"] = theme.TranslucentSurface,
            ["TranslucentChromeGround"] = theme.TranslucentChromeGround,
            ["TranslucentTextDim"] = theme.TranslucentTextDim,
            ["TranslucentTextFaint"] = theme.TranslucentTextFaint,
        };

    /// <summary>The value at which a colour of this hue and chroma reaches
    /// <paramref name="ratio"/> against <paramref name="reference"/> from BELOW.
    /// Bisected rather than solved: the sRGB transfer curve is piecewise and a
    /// forty-step bisection of three multiplications costs nothing.</summary>
    private static double SolveDarker(Hsv ink, Color reference, double ratio)
        => Bisect(ink, reference, ratio, 0, Hsv.From(reference).V, darker: true);

    private static double SolveLighter(Hsv ink, Color reference, double ratio)
        => Bisect(ink, reference, ratio, ink.V, 1, darker: false);

    /// <summary>
    /// Walks the value axis for the point where the contrast against
    /// <paramref name="reference"/> reaches <paramref name="ratio"/>.
    ///
    /// <para>Contrast is monotone in value on either side of the reference, but
    /// it runs in opposite directions on the two sides — so which half of the
    /// interval to keep depends on which side we are walking. That is the whole
    /// of what <paramref name="darker"/> selects.</para>
    /// </summary>
    private static double Bisect(Hsv ink, Color reference, double ratio, double lo, double hi, bool darker)
    {
        for (var i = 0; i < 40; i++)
        {
            var mid = (lo + hi) / 2;
            var clears = Colorimetry.Contrast((ink with { V = mid }).ToColor(), reference) >= ratio;

            if (darker == clears)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) / 2;
    }

    /// <summary>
    /// Hue, saturation, value — carried as doubles rather than through
    /// Avalonia's <c>HsvColor</c> so the round trip is ours and cannot change
    /// under a framework update. Hue is a fraction of a turn, not degrees, which
    /// keeps the wrap arithmetic out of the way.
    /// </summary>
    public readonly record struct Hsv(double H, double S, double V)
    {
        public static Hsv From(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var span = max - min;

            var h = 0.0;
            if (span > 0)
            {
                if (max == r)
                {
                    h = ((((g - b) / span) % 6) + 6) % 6;
                }
                else if (max == g)
                {
                    h = ((b - r) / span) + 2;
                }
                else
                {
                    h = ((r - g) / span) + 4;
                }

                h /= 6;
            }

            return new Hsv(h, max <= 0 ? 0 : span / max, max);
        }

        public Color ToColor()
        {
            var h = ((H % 1) + 1) % 1;
            var s = Math.Clamp(S, 0, 1);
            var v = Math.Clamp(V, 0, 1);

            var sector = Math.Floor(h * 6);
            var i = (int)sector % 6;
            var f = (h * 6) - sector;
            var p = v * (1 - s);
            var q = v * (1 - (f * s));
            var t = v * (1 - ((1 - f) * s));

            var (r, g, b) = i switch
            {
                0 => (v, t, p),
                1 => (q, v, p),
                2 => (p, v, t),
                3 => (p, q, v),
                4 => (t, p, v),
                _ => (v, p, q),
            };

            return Color.FromRgb(Byte(r), Byte(g), Byte(b));

            static byte Byte(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
        }
    }
}
