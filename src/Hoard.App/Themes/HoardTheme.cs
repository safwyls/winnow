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
/// <para><b>Themes differ on four axes, and hue is the weakest of them.</b> The
/// first set shipped varied mostly in hue and value, and read as four settings
/// of one theme rather than as four themes. What actually separates a room is
/// its TEMPERATURE, its CHROMA STRATEGY (how much colour the chrome is allowed
/// at all), its VALUE STRUCTURE (where the contrast lives — whether surfaces
/// step apart or sit flat and let edges do the work) and its MATERIAL (whether
/// the chrome reads as ink, glass, felt or board). <see cref="HoardThemes"/>
/// records which axis each theme takes, and the test of the set is that a
/// thumbnail of the rail alone identifies the theme.</para>
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
    /// <summary>
    /// The alpha the chrome reaches at the far end of the transparency slider.
    ///
    /// <para>Shared by every theme rather than set per theme, because the slider
    /// is a statement about how much desktop the user wants and that should not
    /// mean a different thing after they change theme.</para>
    ///
    /// <para><b>It is this low on purpose.</b> The previous transparency mode ran
    /// at 86–91% and the verdict on it was that it "doesn't come across as
    /// transparency at all" — correctly, because Windows composes dark Mica by
    /// darkening the wallpaper hard before it ever reaches the window, so 14% of
    /// an already-dark backdrop is nothing anyone can see. At 0.30 the desktop
    /// supplies 70% of the chrome and is unmistakable.</para>
    /// </summary>
    public const double MinChromeAlpha = 0.30;

    /// <summary>
    /// How much of the slider the ink compensation is spent over.
    ///
    /// <para><b>The inks have to move faster than the alpha, and this is the
    /// whole reason why.</b> Alpha coming off lightens a dark surface over any
    /// backdrop brighter than it, immediately; the darker ink and the brighter
    /// metadata ink that pay for it were arriving in proportion, so the first
    /// few percent of travel cost contrast that the compensation had not yet
    /// delivered. Front-loading it — both inks fully converted by the first
    /// quarter of the track, alpha continuing to fall for the other three —
    /// moves the point where the worst case drops under AA from 18% to 27% on
    /// the default theme, and from single digits on the selected row to that same
    /// 27%. Measured; anything shorter than a quarter buys nothing more.</para>
    /// </summary>
    private const double InkRampSpan = 0.25;

    /// <summary>The strength of the hover/selection veil at full transparency.
    /// See the <c>ChromeRaised</c> note in <see cref="Tokens"/>.</summary>
    private const double FullRaisedVeil = 0.10;

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
    /// <summary>The scrollbar track and the detail modal's scrim. NOT the
    /// caption any more — see the <c>CaptionFill</c> note in <see cref="Tokens"/>.</summary>
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

    // ── Transparency ────────────────────────────────────────────────────────
    // NOT the opaque tokens with alpha subtracted. That is the thing that
    // measured 3.1:1 and was refused (§13 gap 7). As the chrome opens up it
    // takes a DARKER ink, and the dim ink brightens to pay for what is left, so
    // the whole range is a walk from the opaque token to these — continuous, and
    // exactly the opaque values at slider zero. Measured across the range in
    // ThemeContrastTests against three backdrops: white (the ceiling any
    // wallpaper can produce), the Mica composite measured on a real desktop, and
    // black.

    /// <summary>The rail and caption's ink at the far end of the slider.</summary>
    public required Color TranslucentSurface { get; init; }

    /// <summary>The command bar and cut bar's ink at the far end of the slider.
    /// Below the rail's, the way <c>Ground</c> is below <c>Surface</c> when
    /// opaque — the two surfaces keep their order the whole way across.</summary>
    public required Color TranslucentChromeGround { get; init; }

    public required Color TranslucentTextDim { get; init; }
    public required Color TranslucentTextFaint { get; init; }

    /// <summary>
    /// The veil of <see cref="Text"/> that, laid over <see cref="Surface"/>,
    /// reproduces this theme's own <see cref="SurfaceRaised"/> — the mean of the
    /// three channel ratios.
    ///
    /// <para>Derived rather than written down, because it is not a taste
    /// decision: it is the answer to "how strong is this theme's elevation step,
    /// expressed as a veil", and a theme whose steps were retuned would otherwise
    /// leave a stale number behind. It is the strength the hover veil starts at,
    /// so the moment the slider leaves zero the selected row does not move.</para>
    /// </summary>
    private double OpaqueVeilStrength
    {
        get
        {
            var r = Ratio(SurfaceRaised.R, Surface.R, Text.R);
            var g = Ratio(SurfaceRaised.G, Surface.G, Text.G);
            var b = Ratio(SurfaceRaised.B, Surface.B, Text.B);
            return Math.Clamp((r + g + b) / 3, 0.02, FullRaisedVeil);

            static double Ratio(byte raised, byte surface, byte text)
                => text == surface ? 0 : (raised - surface) / (double)(text - surface);
        }
    }

    /// <summary>
    /// Every token this theme writes, as a flat key → colour map, at the
    /// transparency given — <c>0</c> fully opaque, <c>1</c> the most desktop the
    /// slider offers.
    ///
    /// <para>The derived alphas are computed here rather than written into each
    /// theme by hand, because they are all "this role at N%" and a theme that
    /// had to restate seventeen of them would drift on the eighteenth.</para>
    /// </summary>
    public Dictionary<string, Color> Tokens(double transparency)
    {
        var t = Math.Clamp(transparency, 0, 1);
        var alpha = 1 - (t * (1 - MinChromeAlpha));

        // The inks walk toward their translucent selves as the alpha comes off,
        // so slider zero is bit-for-bit the opaque palette and there is no step
        // at the moment the window turns transparent — but they walk FASTER than
        // the alpha does, and finish in the first quarter. See InkRampSpan.
        var ink = Math.Min(1, t / InkRampSpan);

        var railInk = Mix(Surface, TranslucentSurface, ink);
        var barInk = Mix(Ground, TranslucentChromeGround, ink);

        var textDim = Mix(TextDim, TranslucentTextDim, ink);
        var textFaint = Mix(TextFaint, TranslucentTextFaint, ink);

        var chromeSurface = A(railInk, alpha);
        var chromeGround = A(barInk, alpha);

        // The rail's hover and selection fill, and the one token whose walk is a
        // switch rather than a slide — for a reason worth stating, because the
        // slide was tried first and was wrong.
        //
        // Opaque, this is the ordinary Surface → SurfaceRaised step: an ink that
        // REPLACES what is under it. Translucent, it has to be a VEIL, because a
        // darker ink over an already-translucent rail composites downwards and
        // the "raised" row comes out darker than the row beside it. Those are two
        // different operations, and interpolating between them in ARGB walks
        // through "mid grey at high alpha" — which is neither, and which crushed
        // the metadata ink on a selected row to 4.2:1 six percent into the track.
        //
        // Only one veil is backdrop-independent: solving a·(V − rail) = λ·(Text −
        // rail) for every rail gives V = Text and a = λ. So the veil IS Text, and
        // the only free parameter is its strength. It starts at exactly the
        // strength that reproduces this theme's own Surface → SurfaceRaised step
        // over an opaque rail, which is what makes leaving zero invisible rather
        // than a jump, and grows to 10% as the rail opens up and the step has
        // more to lift. §6's "elevation is the Surface → SurfaceRaised step"
        // holds as a RELATIVE claim, which is what it always was.
        var chromeRaised = t <= 0
            ? SurfaceRaised
            : A(Text, OpaqueVeilStrength + ((FullRaisedVeil - OpaqueVeilStrength) * t));

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
            // ShellGround backs the whole client area below the caption. The
            // moment any transparency is asked for it is nothing at all, because
            // the columns over it paint their own — that is what lets the rail
            // be translucent without the window painting an opaque field behind
            // it first, and why it is a step rather than a ramp: two stacked
            // alphas would multiply and the slider could never reach its end.
            ["ShellGround"] = t > 0 ? A(Ground, 0) : Ground,

            // WallGround is the cover wall, the merge queue, the Stores and
            // Appearance panes. OPAQUE AT EVERY SLIDER POSITION, deliberately:
            // §1 says the art is the interface, and a wallpaper behind six
            // hundred capsules is a second image competing with all of them. It
            // also keeps §5.4's two-layer dormancy cross-fade compositing over
            // exactly the ground it always did, so the ramp's floor is unchanged
            // by construction rather than by measurement.
            ["WallGround"] = Ground,

            // ── The caption takes the rail's colour, in every theme ─────────
            // It used to be Well, one step BELOW Ground — §9's "unlit lip". That
            // rule bought the right thing (no bright platform strip above the
            // art) by the wrong means: it made the chrome two tones meeting at a
            // corner, a dark lip across the top and a lighter column down the
            // side. Painting both in Surface makes the chrome one continuous
            // bracket around a recessed field of art, which is the same claim
            // stated in one material instead of three tones — and the lip is
            // still unlit, because Surface is a chrome tone that no cover art
            // comes near. Well survives on the scrollbar track and the modal
            // scrim, which are the two places a tone below Ground is still the
            // point. design-system.md §9 carries the amendment.
            //
            // Same ink AND same alpha as the rail, not merely the same colour: a
            // second alpha over the same backdrop would land on a second tone
            // and put the corner back.
            ["CaptionFill"] = chromeSurface,
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
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));
}
