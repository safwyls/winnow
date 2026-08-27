using Avalonia.Media;

namespace Hoard.App.Themes;

/// <summary>
/// A complete palette, and the rule that makes several of them one system.
///
/// <para><b>The role is the invariant, never the colour.</b> §2 gives every hue
/// a job — <c>Volt</c> is selection and recency, <c>Amber</c> is "you have been
/// here a lot", <c>Azure</c> does the boring informational work, <c>Danger</c>
/// is the one destructive affordance — and <c>Flare</c> marks unread updates and
/// the bucket that counts them and NOTHING else. A theme may change which colour
/// plays a role. It may not change what the role means, and it may not spend a
/// role's colour on a second job. <see cref="Flare"/> is therefore the one value
/// here that no other property may equal: the moment two roles share it the
/// badge stops meaning anything and the product loses its point.
/// <c>ThemeContrastTests</c> asserts exactly that, per theme.</para>
///
/// <para><b>Every theme's Volt is its own room at full voltage.</b> §2's
/// argument for a hued neutral rather than grey is that it makes Volt the
/// chrome's own colour intensified rather than a decoration sitting on top of
/// it. That reasoning is not specific to teal, so it is the rule the other
/// themes are built to as well — and <c>Flare</c> stays the one hue the room
/// cannot produce, which is what an unread marker has to be.</para>
///
/// <para><b>Why the fields are colours and not brushes.</b> Every view in the
/// app reaches its tokens with <c>StaticResource</c>, which resolves once at
/// parse time and never looks again, so swapping the dictionary would repaint
/// nothing. What every view DOES share is the brush OBJECT it resolved, so a
/// theme change is applied by writing <see cref="SolidColorBrush.Color"/> on the
/// brushes already in <c>Application.Resources</c> — <c>Brush</c> raises
/// <c>IAffectsRender.Invalidated</c>, which is the same path a colour animation
/// takes, so the window repaints without a single binding being re-evaluated.
/// See <c>ThemeService</c>.</para>
/// </summary>
public sealed record HoardTheme
{
    /// <summary>Stable id. Persisted; never localised, never renamed.</summary>
    public required string Id { get; init; }

    /// <summary>What the settings screen calls it.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Why this theme exists, in one sentence, written for the person choosing
    /// (§7). Not a mood: each one names a condition or a register a reader can
    /// recognise themselves in.
    /// </summary>
    public required string Reason { get; init; }

    // ── The neutral family: one ink, stepped ────────────────────────────────
    public required Color Well { get; init; }
    public required Color Ground { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceRaised { get; init; }

    /// <summary>One step above SurfaceRaised: a selected menu item, a pressed
    /// caption button. SurfaceRaised on SurfaceRaised is no step at all.</summary>
    public required Color SurfaceHigh { get; init; }

    public required Color Line { get; init; }

    // ── Ink ─────────────────────────────────────────────────────────────────
    public required Color Text { get; init; }
    public required Color TextDim { get; init; }

    /// <summary>Watermarks, disabled arrows — the quietest ink that is still ink.</summary>
    public required Color TextFaint { get; init; }

    // ── Roles ───────────────────────────────────────────────────────────────
    /// <summary>Unread updates and the bucket counting them. Nothing else, ever.</summary>
    public required Color Flare { get; init; }

    public required Color Volt { get; init; }
    public required Color VoltInk { get; init; }
    public required Color VoltHover { get; init; }
    public required Color VoltPress { get; init; }
    public required Color Amber { get; init; }
    public required Color Azure { get; init; }
    public required Color Danger { get; init; }
    public required Color DangerHover { get; init; }
    public required Color DangerPress { get; init; }
    public required Color DangerInk { get; init; }

    // ── Transparency mode ───────────────────────────────────────────────────
    // NOT the opaque tokens with alpha subtracted. That is the thing that
    // measured 3.1:1 and was refused (§13 gap 7). A translucent surface takes
    // its own, DARKER ink, so the composite over the brightest backdrop a
    // desktop can reach lands no lighter than the opaque theme's SurfaceRaised —
    // and the dim ink brightens to pay for what is left. Measured in
    // ThemeContrastTests against three backdrops: white (the ceiling any
    // wallpaper can produce), the Mica composite measured on a real desktop, and
    // black.

    /// <summary>Alpha for the rail, the filter panel and the command bar.</summary>
    public required double ChromeAlpha { get; init; }

    /// <summary>Alpha for the caption strip. Higher than <see cref="ChromeAlpha"/>
    /// on the darker themes, because §9's "unlit lip" is a rule about the caption
    /// staying at or below Ground and a near-black Ground has less to give.</summary>
    public required double CaptionAlpha { get; init; }

    public required Color TranslucentWell { get; init; }
    public required Color TranslucentSurface { get; init; }

    /// <summary>The command bar and cut bar's ground — a step above the rail's,
    /// the way <c>Ground</c> is a step above <c>Surface</c> when opaque.</summary>
    public required Color TranslucentChromeGround { get; init; }

    public required Color TranslucentTextDim { get; init; }
    public required Color TranslucentTextFaint { get; init; }

    /// <summary>
    /// Every token this theme writes, as a flat key → colour map, for the
    /// transparency state given.
    ///
    /// <para>The derived alphas are computed here rather than written into each
    /// theme by hand, because they are all "this role at N%" and a theme that
    /// had to restate seventeen of them would drift on the eighteenth.</para>
    /// </summary>
    public Dictionary<string, Color> Tokens(bool translucent)
    {
        var chromeGround = translucent ? A(TranslucentChromeGround, ChromeAlpha) : Ground;
        var chromeSurface = translucent ? A(TranslucentSurface, ChromeAlpha) : Surface;
        var caption = translucent ? A(TranslucentWell, CaptionAlpha) : Well;

        var textDim = translucent ? TranslucentTextDim : TextDim;
        var textFaint = translucent ? TranslucentTextFaint : TextFaint;

        // The rail's hover and selection fill. Opaque, this is the ordinary
        // Surface → SurfaceRaised step. Translucent, a darker ink over an
        // already-translucent rail composites DOWNWARDS — the "raised" row would
        // come out darker than the row beside it — so the step becomes a veil of
        // the theme's own Text at 10%, which lifts whatever is under it by
        // 1.8x–5.8x on every backdrop measured. Elevation stays relative, which
        // is what §6 says it is.
        var chromeRaised = translucent ? A(Text, 0.10) : SurfaceRaised;

        return new Dictionary<string, Color>
        {
            ["Well"] = Well,
            ["Ground"] = Ground,
            ["Surface"] = Surface,
            ["SurfaceRaised"] = SurfaceRaised,
            ["SurfaceHigh"] = SurfaceHigh,
            ["Line"] = Line,
            ["Text"] = Text,
            ["TextDim"] = textDim,
            ["TextFaint"] = textFaint,

            ["Flare"] = Flare,
            ["Volt"] = Volt,
            ["VoltInk"] = VoltInk,
            ["VoltHover"] = VoltHover,
            ["VoltPress"] = VoltPress,
            ["Amber"] = Amber,
            ["Azure"] = Azure,
            ["Danger"] = Danger,
            ["DangerHover"] = DangerHover,
            ["DangerPress"] = DangerPress,
            ["DangerInk"] = DangerInk,

            // ── The grounds, and where the line between them falls ──────────
            // ShellGround backs the whole client area below the caption. In
            // transparency mode it is nothing at all, because the columns over
            // it paint their own — that is what lets the rail be translucent
            // without the window painting an opaque field behind it first.
            ["ShellGround"] = translucent ? A(Ground, 0) : Ground,

            // WallGround is the cover wall, the merge queue, the Stores and
            // Appearance panes. OPAQUE IN BOTH MODES, deliberately: §1 says the
            // art is the interface, and a wallpaper behind six hundred capsules
            // is a second image competing with all of them. It also keeps
            // §5.4's two-layer dormancy cross-fade compositing over exactly the
            // ground it always did, so the ramp's floor is unchanged by
            // construction rather than by measurement.
            ["WallGround"] = Ground,

            // The four tokens transparency mode actually moves. Everything else
            // in this map is identical in both states, which is the point: a
            // surface that carries reading matter is named apart from chrome
            // that may be translucent, so the boundary is a token rather than a
            // rule somebody has to remember. §13 gap 7 asked for exactly that.
            ["CaptionFill"] = caption,
            ["ChromeSurface"] = chromeSurface,
            ["ChromeGround"] = chromeGround,
            ["ChromeRaised"] = chromeRaised,

            // Under the art stack inside a tile, so a cover that has decoded one
            // of its two dormancy layers and not the other cannot show the
            // window through the gap between them.
            ["TileGround"] = Ground,

            // ── Derived: a role at N% ──────────────────────────────────────
            ["VoltSelection"] = A(Volt, 0.30),
            ["VoltSelectionSoft"] = A(Volt, 0.24),
            ["VoltEdgeSoft"] = A(Volt, 0.40),
            ["FlareSoft"] = A(Flare, 0.70),
            ["FlareGlow"] = A(Flare, 0.85),
            ["LineSoft"] = A(Line, 0.60),
            ["SurfaceRaisedHalf"] = A(SurfaceRaised, 0.50),
            ["SurfaceRaisedGhost"] = A(SurfaceRaised, 0.12),
            ["SurfaceRaisedFaint"] = A(SurfaceRaised, 0.08),
            ["GroundVeil"] = A(Ground, 0.30),
            ["ModalScrim"] = A(Well, 0.84),

            // ── Scrollbars (§9) ────────────────────────────────────────────
            // The thumb is neutral, never Volt: a scrollbar is chrome, and
            // spending the selection colour on it would make every scroll
            // position look like a selection.
            ["ScrollBarForeground"] = textDim,
            ["ScrollBarBackgroundPointerOver"] = A(Well, 0.85),
            ["ScrollBarTrackFillPointerOver"] = A(Well, 0.85),
            ["ScrollBarPanningThumbBackground"] = Mix(Line, TextDim, 0.35),
            ["ScrollBarThumbFillPointerOver"] = Mix(Line, TextDim, 0.62),
            ["ScrollBarThumbFillPressed"] = textDim,
            ["ScrollBarThumbFillDisabled"] = SurfaceRaised,
            ["ScrollBarButtonArrowForeground"] = textFaint,
            ["ScrollBarButtonArrowForegroundPointerOver"] = textDim,
            ["ScrollBarButtonArrowForegroundPressed"] = Text,
            ["ScrollBarButtonArrowForegroundDisabled"] = SurfaceRaised,
        };
    }

    /// <summary>The tile hover scrim's two stops (§5.3): transparent at the top,
    /// Ground at 92% across the bottom third. It rides the theme because the
    /// facts under it are read against the theme's own ground.</summary>
    public (Color Top, Color Bottom) TileScrim() => (A(Ground, 0), A(Ground, 0.92));

    /// <summary>The role colours, by the name of the role. Used by the settings
    /// screen's swatch row and by the test that holds Flare to one job.</summary>
    public IReadOnlyList<(string Role, Color Colour)> Roles() =>
    [
        ("Ground", Ground),
        ("Surface", Surface),
        ("Text", Text),
        ("Volt", Volt),
        ("Amber", Amber),
        ("Azure", Azure),
        ("Danger", Danger),
        ("Flare", Flare),
    ];

    private static Color A(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));
}
