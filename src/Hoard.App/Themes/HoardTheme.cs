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
    /// The alpha the WINDOW'S GROUND reaches at the far end of the slider — the
    /// gaps between the panes, and the caption, which is part of that same
    /// field.
    ///
    /// <para><b>There are two tiers now, not three, and this is the outer
    /// one.</b> The window used to run at three levels: a ground that admitted
    /// everything, a CHROME tier at <c>0.30</c> carrying the rail, the caption
    /// and the filter panel, and a PANE tier at <see cref="MinWallAlpha"/>
    /// carrying the art field and the screens that share its place. Three levels
    /// is one more than an eye sorts, and the middle one had no job left to do:
    /// the rail and the filter panel are content columns rather than window
    /// furniture, and the argument §14.7 used to move the merge queue and the
    /// list view onto the field's ramp applies to them word for word. So the
    /// chrome tier is gone. Every pane in the window sits at one level; the
    /// ground and the caption sit at the other.
    /// </para>
    ///
    /// <para><b>This is the one number in the stack that is FREE, and the
    /// caption is what fixes it.</b> Everything below it is forced by the
    /// identity in <see cref="MinPaneAlpha"/>. The ground itself is answerable to
    /// nothing except the one thing painted on it that has to be read — the
    /// wordmark and the three window glyphs — so the bar is the mirror image of
    /// the one <see cref="MinWallAlpha"/> is held to: <b>the restructure may not
    /// cost the user range they already have.</b> Walked per theme against white,
    /// the most open the ground can be while no theme's AA ceiling falls below
    /// where it stands today (27 / 31 / 30 / 26) is <c>0.14</c>:</para>
    ///
    /// <list type="table">
    ///   <item><term>Ground at 0.12</term><description>29 / 30 / 30 / 30 — Nightshift loses a point</description></item>
    ///   <item><term>Ground at 0.14</term><description>29 / 31 / 30 / 30 — the marginal value, two themes exactly at par</description></item>
    ///   <item><term>Ground at 0.15</term><description>30 / 31 / 31 / 31 — chosen</description></item>
    ///   <item><term>Ground at 0.20</term><description>32 / 33 / 33 / 33 — more range, less window</description></item>
    /// </list>
    ///
    /// <para><c>0.15</c> is taken over the marginal <c>0.14</c> for the reason
    /// <c>0.65</c> was taken over <c>0.62</c>: it is the round step past the
    /// boundary, it buys 1 to 5 points of margin on top, and it states as a pair
    /// of numbers the Appearance screen can print — <b>the ground admits 85% of
    /// the desktop, a pane admits 35%</b>.</para>
    ///
    /// <para><b>A second route lands within two points of it, which is why the
    /// round number is not a fudge.</b> The request that opened this asked for
    /// the ground and the caption to sit "somewhere between where the background
    /// is now and where the rail is now" — between admitting everything and
    /// admitting the old chrome's 70%. Transmittances COMPOSE by multiplying, so
    /// the midpoint between two of them is the geometric mean rather than the
    /// arithmetic one: <c>√(1.00 · 0.70) = 0.837</c>, an alpha of <c>0.163</c>.
    /// The legibility boundary and the honest reading of "halfway" agree to
    /// within a point and a half, and the more conservative of the two is
    /// taken.</para>
    /// </summary>
    public const double MinShellAlpha = 0.15;

    /// <summary>
    /// How much of the desktop a PANE finally admits at the far end of the
    /// slider — the art field, the rail, the filter panel, and every screen that
    /// takes the library pane's place.
    ///
    /// <para><b>The number and its derivation are unchanged, and that is the
    /// point of writing the tier this way round.</b> <c>1 − 0.65 = 0.35</c> is
    /// still exactly what reaches the eye through any pane, so §14.6's argument
    /// for it survives the restructure intact: the constraint is POLARITY, not
    /// contrast. §5.1's ramp encodes dormancy as dark capsules on a dark field
    /// and only reads that way while the field stays DARKER than the capsules
    /// hung on it. Over a white wallpaper the field climbs, and at some position
    /// it passes the dormancy floor of an ordinary dark cover — after which a
    /// dormant tile stops reading as dimmed art and starts reading as a hole
    /// punched in a lit field, which inverts the one encoding the product is
    /// built on.</para>
    ///
    /// <para>The bar is that the field must not fail FIRST. A field the user
    /// cannot read labels over is a place they are already told not to go
    /// (§14.3), so the wall does not have to hold past the AA mark — it has to
    /// hold to it. Walked per theme against white, with the dormancy floor of a
    /// mid-dark cover (sat 0.22, bright 0.68) as the target:</para>
    ///
    /// <list type="table">
    ///   <item><term>AA ceiling, two tiers</term><description>30 / 31 / 31 / 31</description></item>
    ///   <item><term>Field inverts, at 0.65</term><description>34 / 47 / 41 / 44 — clears all four, by 4 to 16 points</description></item>
    /// </list>
    ///
    /// <para><b>What DID change is that the number is an admission rather than a
    /// paint.</b> A pane is drawn ON the window's ground, so its own alpha and the
    /// ground's stack; <see cref="MinPaneAlpha"/> is what a pane paints, and this
    /// is what comes through. The ground darkens the field slightly on the way,
    /// which is why polarity improved from 29 / 46 / 38 / 44 to 34 / 47 / 41 / 44
    /// without the constant moving at all.</para>
    ///
    /// <para><b>The halving relation it used to state has gone with the tier it
    /// referred to.</b> "The wall admits exactly half what the chrome does" was
    /// true and is now vacuous — there is no chrome for it to be half of. What
    /// the Appearance screen prints instead is the relation that is left: the
    /// ground admits 85%, a pane admits 35%, and every field in the window admits
    /// exactly what the pane around it does.</para>
    ///
    /// <para><b>Over the measured dark desktop the question does not arise.</b>
    /// The composite is darker than <c>Ground</c>, so opening the field DEEPENS
    /// it and the polarity gets better rather than worse — the same asymmetry
    /// §14.3 records for the inks.</para>
    /// </summary>
    public const double MinWallAlpha = 0.65;

    /// <summary>
    /// What a PANE actually paints, over the window's ground.
    ///
    /// <para><b>Forced rather than chosen — and it is the same identity that has
    /// governed input fields since §14.7, promoted one level out.</b> A pane is a
    /// child of the ground it is laid on, so the two alphas stack: what the
    /// desktop finally contributes to a pane is
    /// <c>(1 − shellAlpha) · (1 − paneAlpha)</c>. Asking that a pane admit what
    /// the art field admits — which is the whole of the two-tier idea — fixes the
    /// number outright:</para>
    ///
    /// <code>
    ///   (1 − MinShellAlpha) · (1 − MinPaneAlpha) = 1 − MinWallAlpha
    ///          0.85         ·       0.4118       =        0.35
    /// </code>
    ///
    /// <para>So the window has ONE rule from the desktop inward, applied three
    /// times: <c>alpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)</c>, where
    /// the container of a pane is the ground and the container of a field is the
    /// pane it is cut into. The ground is the only surface with nothing above it,
    /// which is exactly why it is the only free quantity.</para>
    ///
    /// <para><b>It rides the INK ramp rather than the alpha's, and that is what
    /// keeps the stack honest across the slider rather than only at its end.</b>
    /// The ground's share is already linear in the slider position, so the moment
    /// this factor stops moving the product of the two is linear at exactly the
    /// wall's rate — a pane admits <c>0.35 · t</c> at EVERY position past the
    /// first quarter, which is the same claim <see cref="MinPaneFieldAlpha"/>
    /// makes one level further in. On the alpha's own linear ramp the product
    /// would be QUADRATIC: at the middle of the track a pane would admit 8.75%
    /// where it should admit 17.5%, so the two tiers would sit twice as far apart
    /// through the part of the slider anybody actually uses as they do at its
    /// end. Below the first quarter the product is sub-linear rather than
    /// super-linear, which is the safe direction, and it meets the linear part
    /// exactly at <see cref="InkRampSpan"/>.</para>
    ///
    /// <para><b>The ground's ink bleeds into the pane, and the amount is bounded
    /// and was measured rather than waved past.</b> A pane's composite ink is its
    /// own tone at this alpha plus the ground's showing through the rest, so some
    /// fraction of every pane is <c>Well</c> rather than its own tone: 9.5% at the
    /// far end, at most 34% in the middle of the track where the pane is still
    /// nearly opaque. Measured against the same pane painted straight onto the
    /// desktop, the worst tone difference that produces is 1.06 to 1.11:1 across
    /// the whole slider — under the <c>Well</c>-to-<c>Ground</c> step itself,
    /// which §15.7 already measures at 1.02 to 1.13:1 and calls nearly
    /// invisible.</para>
    /// </summary>
    public const double MinPaneAlpha =
        1 - ((1 - MinWallAlpha) / (1 - MinShellAlpha));

    /// <summary>
    /// The alpha an INPUT FIELD IN THE FILTER PANEL reaches at the far end of
    /// the slider, measured against the surface it is drawn on rather than
    /// against the desktop.
    ///
    /// <para><b>It is forced, not chosen, and this is the third time the number
    /// has moved while the identity has not.</b> A field is a child of the
    /// surface it sits in, so the two alphas stack: what the desktop finally
    /// contributes to a field is
    /// <c>(1 − containerAdmits) · (1 − fieldAlpha)</c>. Asking that a field admit
    /// exactly what the art field admits — which is the whole point, so the
    /// window has one translucency rather than three — fixes the number
    /// outright:</para>
    ///
    /// <code>
    ///   fieldAlpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha)
    /// </code>
    ///
    /// <para><b>The filter panel changed tiers, so its answer changed with
    /// it.</b> The panel was chrome, its container term was the chrome's
    /// <c>0.30</c>, and the answer was exactly a half. The panel is a PANE now,
    /// and a pane already admits <c>1 − MinWallAlpha</c> — there is no half left
    /// for a field to spend:</para>
    ///
    /// <code>
    ///   (1 − MinWallAlpha) · (1 − MinFieldAlpha) = 1 − MinWallAlpha
    ///          0.35        ·        1.00         =        0.35
    /// </code>
    ///
    /// <para><b>Zero, and it has to be zero</b> — the same answer, by the same
    /// route, that <see cref="MinPaneFieldAlpha"/> already gave when the command
    /// bar moved inside the library pane. The two fields are separate constants
    /// because the identity is per-container and they would part company again
    /// the moment either field moved; they agree today because both containers
    /// are panes, which is the two-tier collapse showing up in the arithmetic
    /// rather than a coincidence worth deleting.</para>
    ///
    /// <para><b>What it costs is small and already recorded.</b> §14.7 measures a
    /// field found by its border and lit by its ring, with the fill and the
    /// surface around it converging to 1.05:1 at the far end — the fill was
    /// nearly cosmetic there already. Slider zero is untouched, so the step a
    /// field cuts into the panel is exactly the step it always was at
    /// <c>SOLID</c>, and it fades out over <see cref="InkRampSpan"/> like every
    /// other compensation on this slider.</para>
    ///
    /// <para><b>And the ink stopped walking with it.</b> §14.3's ink ramp was a
    /// CHROME compensation — the chrome opened to 0.70 and paid for it with a
    /// darker ink — and there is no chrome. The panel is its own <c>Surface</c>
    /// at an alpha at every position, so the field cut into it is the theme's own
    /// <c>Ground</c> at an alpha, and the step between them is the opaque
    /// palette's step the whole way across.</para>
    /// </summary>
    public const double MinFieldAlpha =
        1 - ((1 - MinWallAlpha) / (1 - MinWallAlpha));

    /// <summary>
    /// The alpha an input field INSIDE THE LIBRARY PANE reaches at the far end
    /// of the slider — the command bar's search box and the cut bar's prompt.
    ///
    /// <para><b>Same identity as <see cref="MinFieldAlpha"/>, same container tier
    /// now, and it is written as the division rather than as its answer so that
    /// the derivation stays visible.</b> The command bar and the cut bar sit
    /// inside the library pane (§15.8), so a field on either is a child of the
    /// PANE, and a pane's ground already admits exactly
    /// <c>1 − MinWallAlpha</c>:</para>
    ///
    /// <code>
    ///   (1 − MinWallAlpha) · (1 − MinPaneFieldAlpha) = 1 − MinWallAlpha
    ///          0.35        ·            1.00         =        0.35
    /// </code>
    ///
    /// <para><b>Zero, and it has to be zero.</b> The surface the field sits on is
    /// already AT the window's declared translucency, so anything the field
    /// paints on top of it makes the field LESS open than the wall — a
    /// bolted-shut patch in an open pane, which is §14.7's own verdict arriving
    /// one level further in. The only fill that leaves the identity intact is no
    /// fill.</para>
    ///
    /// <para><b>It follows the WALL's setting, not the slider's</b>, and that is
    /// still the one thing separating it from the panel's field. With the art
    /// field solid the pane under it is solid, the identity is vacuous — nothing
    /// is admitted for the field to match — and a field that faded anyway would
    /// lose its step for no gain at all. So it is opaque exactly when the pane is
    /// opaque, which is the same condition <c>WallGround</c> and
    /// <c>PaneGround</c> answer.</para>
    /// </summary>
    public const double MinPaneFieldAlpha =
        1 - ((1 - MinWallAlpha) / (1 - MinWallAlpha));

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

    // ── Provenance, for themes that came out of a file ──────────────────────
    // Both null on the four built-ins, so nothing about them changes: the
    // record is still twenty-four colours and three strings, and every existing
    // construction site still compiles unchanged because neither is `required`.

    /// <summary>
    /// What this theme asks the rest of the Appearance screen to be set to when
    /// it is picked. <c>null</c> on every built-in, which is what keeps the
    /// shipped four behaving exactly as they did before the JSON engine
    /// existed. See <see cref="ThemeAppearanceDefaults"/>.
    /// </summary>
    public ThemeAppearanceDefaults? Defaults { get; init; }

    /// <summary>
    /// The file this theme was read out of, as a bare file name — <c>null</c>
    /// for the built-ins.
    ///
    /// <para>A NAME and not a path, deliberately. The Appearance screen prints
    /// the folder once and then prints this beside each theme, so what differs
    /// is what is shown; and nothing in the app ever dereferences it, which is
    /// the other half of "a theme file is data" — see <c>ThemeJson</c>.</para>
    /// </summary>
    public string? SourceFile { get; init; }

    /// <summary>True for anything that came out of the themes folder. Drives
    /// exactly two things on the settings screen: the file name under the card,
    /// and the contrast line the built-ins do not need because the slider's own
    /// AA mark already carries it.</summary>
    public bool IsUserTheme => SourceFile is not null;

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
    public Dictionary<string, Color> Tokens(
        double transparency,
        bool wallTranslucent = false,
        HoardLayout layout = HoardLayouts.Default)
    {
        var t = Math.Clamp(transparency, 0, 1);
        var floating = layout == HoardLayout.Floating;

        // ── The two tiers, and which ramp each is on ────────────────────────
        // THE WINDOW'S GROUND, and it is LINEAR because it is the outermost
        // surface in the stack: nothing is painted between it and the desktop,
        // so its alpha IS the translucency the slider is setting. It shows in
        // every gap between panes, and it is what the caption is made of.
        //
        // It is a RAMP now rather than the step it used to be, and the step's
        // reason is answered rather than ignored. ShellGround painted nothing
        // past zero because two stacked alphas MULTIPLY: a pane on a proportional
        // ground would admit a quadratic and the slider could never reach its own
        // end. What makes a fill safe here is that the layer above it finishes
        // early — see paneAlpha.
        var shellAlpha = 1 - (t * (1 - MinShellAlpha));

        // A PANE, painted on that ground: the rail, the filter panel, the art
        // field, and every screen that takes the library pane's place. One tier
        // for all of them, which is the whole change — the rail and the panel
        // used to sit at a chrome tier of their own between these two.
        //
        // ON THE INK'S RAMP, NOT THE ALPHA'S, and that is what keeps the product
        // of the two honest. What reaches the eye through a pane is
        // (1 − shellAlpha) · (1 − paneAlpha); the ground's half is already linear
        // in t, so the moment this factor STOPS moving the product is linear at
        // exactly the wall's rate — a pane admits 0.35·t at EVERY position past
        // the first quarter rather than only at the end of the track. On the
        // alpha's own linear ramp the product would be quadratic: 8.75% at the
        // middle of the slider where it should be 17.5%, so the two tiers would
        // read twice as far apart through the part anybody uses as they do at the
        // end. Under the first quarter the product is sub-linear instead, which
        // is the safe direction, and it meets the linear part exactly at
        // InkRampSpan. See MinPaneAlpha.
        var ink = Math.Min(1, t / InkRampSpan);
        var paneAlpha = 1 - (ink * (1 - MinPaneAlpha));

        // The art field and the screens beside it answer the reach setting as
        // well as the slider; the rail and the filter panel answer the slider
        // alone. That asymmetry is the "how far" qualifier doing its job and is
        // stated on the Appearance screen rather than smoothed over here: with
        // the reach off, the side panes open and the library pane does not.
        // Zero on the slider is opaque either way, so the reach setting cannot
        // produce a translucent window on its own.
        var wallAlpha = wallTranslucent ? paneAlpha : 1;

        // An input field's alpha, against the surface it is drawn ON rather than
        // against the desktop. The filter panel's field follows the SLIDER,
        // because the panel it is cut into does.
        //
        // Both fields solve to zero now. The panel used to be chrome, so its
        // container admitted 0.70 and a field in it spent the other half; the
        // panel is a pane, a pane already admits 1 − MinWallAlpha, and there is
        // no half left. Same identity, one fewer tier, and the two field
        // constants coincide for the first time — see MinFieldAlpha.
        var fieldAlpha = 1 - (ink * (1 - MinFieldAlpha));

        // The library pane's own field: the search box and the cut bar's prompt.
        // Same answer, and it follows the WALL's setting rather than the
        // slider's, because with the art field solid the pane under it is solid,
        // the identity is vacuous, and fading the field anyway would only cost it
        // the step it cuts. See MinPaneFieldAlpha.
        var paneFieldAlpha = wallTranslucent ? 1 - (ink * (1 - MinPaneFieldAlpha)) : 1;

        // ── The one ink ramp that survives, and the one that did not ────────
        // THE GROUND'S ink walks, and it is the surface that needs it most: it
        // is the most open thing in the window and it carries the wordmark and
        // three glyphs. Well in the floating layout — one step below Ground, the
        // tone §9 keeps for "under the art field", and what makes a gap read as a
        // recess rather than as a missing pane — and Ground in the flush layout,
        // where there are no gaps and the ground is simply what lies under the
        // columns. Its walked partner is TranslucentChromeGround in both.
        //
        // THE RAIL'S INK RAMP IS RETIRED, and it had to be. §14.3's ramp is a
        // CHROME compensation: the chrome opened to 0.70 and paid for it with a
        // darker ink. There is no chrome. Worse, the ink it walked toward —
        // TranslucentSurface — is BELOW Ground in three of the four themes, so at
        // the alpha the rail now shares with the art field the chrome would sink
        // under the field beside it: measured, the walked rail is at or below the
        // wall at 87 to 89 of the 101 slider positions in Hoard, Nightshift and
        // Tungsten, and the unwalked one at none of them in any theme. §14.2's
        // recess is the art hanging BELOW the chrome, so the rail takes the
        // treatment PaneGround has always had — the theme's own token at an
        // alpha, unwalked, at every position. TranslucentSurface is retired with
        // the tier it belonged to, exactly as ChromeGround was.
        var shellInk = Mix(floating ? Well : Ground, TranslucentChromeGround, ink);

        var textDim = Mix(TextDim, TranslucentTextDim, ink);
        var textFaint = Mix(TextFaint, TranslucentTextFaint, ink);

        var chromeSurface = A(Surface, paneAlpha);

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
            // ShellGround backs the WHOLE client area, the caption's 36px
            // included, and it is the outer of the window's two tiers. Every gap
            // between panes is this token showing, and so is the caption — in
            // the floating layout the caption paints nothing of its own and this
            // is what is seen through it, so the two are not merely the same
            // colour at the same alpha but literally the same paint.
            //
            // IT IS A RAMP NOW, WHERE IT USED TO BE A STEP, and the step's reason
            // is answered rather than dropped. The old rule was that past slider
            // zero this painted NOTHING, because two stacked alphas multiply and
            // a pane on a proportional ground would admit a quadratic. That is
            // still true of a proportional ground; it is not true of this one,
            // because the layer above it finishes early. paneAlpha rides
            // InkRampSpan, so past the first quarter (1 − shellAlpha)·(1 − paneAlpha)
            // is linear in t at exactly the wall's rate and a pane admits
            // 1 − MinWallAlpha at the end of the track, to the last decimal. The
            // panes still composite over the ground exactly ONCE, which was the
            // half of the old rule that mattered.
            //
            // WHAT IT BUYS is the thing §15.7 recorded as an honest cost and
            // could not fix: "at SOLID the ground is one field; past it, it is a
            // field with brighter slots cut in it." The gaps used to admit the
            // WHOLE desktop while the caption beside them admitted the chrome's
            // 70%, so the ground was one tone at zero and two everywhere else.
            // Now the gap and the caption carry the same fill at every position,
            // and the continuity claim is exact across the slider rather than
            // approximate above zero.
            //
            // The ink is Well in the floating layout and Ground in the flush one
            // — see shellInk. Flush has no gaps, so nothing of this is visible
            // there except through the surfaces on it; it is still painted,
            // because the panes' own alphas are derived assuming it is.
            ["ShellGround"] = A(shellInk, shellAlpha),

            // ── WallGround: the field the covers hang in ───────────────────
            // It used to be opaque at every setting, on a §1 argument: the art
            // is the interface, and a wallpaper behind six hundred capsules is a
            // second image competing with all of them. THAT ARGUMENT WAS
            // OVERRULED by the person looking at the result — a solid slab
            // bolted to translucent chrome reads as two windows, and the
            // aesthetic call is theirs.
            //
            // The half of the old reasoning that was NOT aesthetics still binds,
            // and it binds somewhere else: §5.4's dormancy ramp is a two-layer
            // opacity cross-fade whose layers are only opaque TOGETHER, so a
            // tile mid-decode over a translucent field would show the desktop
            // through the ramp's floor. That is a bug, not a preference. It is
            // answered by TileGround below rather than by keeping this opaque —
            // the FIELD may open up, the TILES may not.
            //
            // So: opaque unless asked for, opaque at slider zero either way, and
            // at half the chrome's reach when asked for. See MinWallAlpha.
            ["WallGround"] = wallAlpha >= 1 ? Ground : A(Ground, wallAlpha),

            // The panes that share the wall's position but not its job: the
            // merge queue, Stores, Appearance, the library's LIST view and the
            // empty state.
            //
            // THIS USED TO BE OPAQUE AT EVERY SETTING, and the argument for that
            // was wrong — not in its principle but in its arithmetic. §14.3 says
            // an ink chosen for an opaque ground cannot have alpha subtracted
            // from it, which is true, and the conclusion drawn from it was that
            // text may not sit on any translucent field. But the number that
            // conclusion was measured against was the CHROME's reach, and these
            // panes do not live on the chrome. The wall admits 0.35 of the
            // desktop against the chrome's 0.70 — LESS THAN HALF — and the rail
            // already carries labels at the chrome's own reach.
            //
            // Walked per theme against white, TextDim clears AA to 59 / 71 / 65
            // / 73 percent of the slider on the open field, against the chrome's
            // own 27 / 31 / 30 / 26. A pane header's Text clears it at 100. The
            // pane is not the thing that fails first — it fails somewhere over
            // twice as far up the track as the surface the app already ships
            // reading matter on, which is the same "must not fail first" test
            // MinWallAlpha is held to.
            //
            // So it is the wall's own ramp, and it follows the wall's SETTING
            // too: a translucent Appearance screen beside a solid grid is the
            // same "two windows" complaint in mirror image, and one setting that
            // means one thing is the point.
            //
            // What did NOT move: TileGround, which is construction rather than
            // measurement (§14.4), and the popovers, which never receive the
            // window's backdrop at all.
            ["PaneGround"] = wallAlpha >= 1 ? Ground : A(Ground, wallAlpha),

            // ── The caption belongs to the ground, and in flush to the rail ──
            // TWO TIERS, TWO ANSWERS, and the older amendment survives in the
            // layout it was written for rather than being reversed again.
            //
            // FLOATING. The caption is part of the window's ground and paints
            // NOTHING of its own — ShellGround is directly behind it and shows
            // through. That is the strongest available form of §9's "same ink AND
            // same alpha": the caption and every gap are not two surfaces that
            // agree, they are one surface. It also repeals §15.7's first honest
            // cost, which was that the gaps admitted the whole desktop while the
            // caption beside them admitted the chrome's 70%, so the ground was
            // one field at SOLID and a field with brighter slots cut in it
            // everywhere else. There are no slots now.
            //
            // It is a STEP for the reason ShellGround used to be one: painting
            // any fill here would composite over the ground it is supposed to BE,
            // and two alphas over one backdrop land on two tones. At SOLID the
            // ground is opaque Well, so painting opaque Well changes nothing and
            // the token keeps its opaque value — which is what makes slider zero
            // bit-for-bit the palette here as everywhere else.
            //
            // FLUSH. There is no ground to be part of: the panes meet edge to
            // edge and cover it, and the caption meets the rail at a corner.
            // §9's amendment therefore stands exactly as written — the caption is
            // the rail, same ink and same alpha — and it stands for the same
            // reason, that two tones meeting at a corner is a seam. What changed
            // underneath it is only which tier the rail is on, and the caption
            // went with it. Both surfaces are painted on the same ShellGround, so
            // the equality is true on the glass and not merely in the token map.
            //
            // AND §9's OLDER CLAIM — "the caption must not be the BRIGHTEST
            // thing, and the art must be the first thing on screen with light in
            // it" — is where the honesty is owed. In flush it holds outright: the
            // caption is a chrome tone at the pane tier, level with the rail and
            // above the art by exactly the palette's own step. In floating it
            // holds at SOLID and against a dark desktop, and over a BRIGHT
            // wallpaper it does not: the ground is the most open surface in the
            // window, so the caption is the brightest band in it, and the gaps
            // are exactly as bright. §15.7 already conceded that for the gaps and
            // the caption now joins them — which is the two-tier structure being
            // visible rather than a regression hiding in it. What is bought for
            // it is measured: the wordmark and the glyphs clear §8's floor to
            // 30 / 31 / 31 / 31 percent of the slider against white, against the
            // 27 / 31 / 30 / 26 the app ships today.
            ["CaptionFill"] = floating ? A(shellInk, t > 0 ? 0 : 1) : chromeSurface,
            ["ChromeSurface"] = chromeSurface,

            // ChromeGround IS GONE, and its absence is the change rather than a
            // tidy-up. It was the command bar's and the cut bar's own fill, a
            // chrome tone under a chrome strip; both strips are now inside the
            // library pane (§15.1, revised), so they have no fill of their own —
            // the pane paints its ground once and they sit on it. A token here
            // would be a second coat of the same ink at a second alpha over the
            // first, which is exactly the double composite ShellGround is a step
            // and not a ramp to avoid.
            //
            // Anything that used to measure "the bar" measures PaneGround now,
            // which is what the bar sits on and is a truer statement of the same
            // question.
            ["ChromeRaised"] = chromeRaised,

            // Half a step: a HOVERED row where ChromeRaised is a SELECTED one.
            // Opaque it is SurfaceRaised at 50% — the token the list view has
            // always used — and translucent it is the same veil at half
            // strength, because the two operations do not interpolate (see
            // ChromeRaised).
            ["ChromeRaisedHalf"] = t <= 0
                ? A(SurfaceRaised, 0.50)
                : A(Text, (OpaqueVeilStrength + ((FullRaisedVeil - OpaqueVeilStrength) * t)) * 0.5),

            // ── The two input fields, which share a number again ───────────
            // A field is one step from its container in the neutral family, in
            // the direction the opaque palette already took: the library pane is
            // Ground so a field on it is Surface; the filter panel is Surface so
            // a field in it is Ground.
            //
            // NEITHER OF THEM WALKS NOW, and that is the tier merge reaching one
            // level further in. §14.3's ink ramp was a CHROME compensation — the
            // chrome opened to 0.70 and paid for it with a darker ink, so a field
            // on the chrome had to walk with it or the step between them would
            // change size across the slider. The filter panel is a pane, panes do
            // not walk, and a field cut into an unwalked ground must be unwalked
            // too. Both fields are their own opaque token at an alpha, and the
            // step each cuts is the opaque palette's step the whole way across.
            //
            // AND THE ALPHA IDENTITY GIVES ONE ANSWER WHERE IT USED TO GIVE TWO.
            // The identity is (1 − containerAdmits)·(1 − fieldAlpha) =
            // 1 − MinWallAlpha, and the container is whatever the field is
            // painted on. The search box's container was already the library
            // pane, which admits 1 − MinWallAlpha, so its term solved to zero.
            // The panel's container was the CHROME, which admitted 0.70, so its
            // term was a half. The panel is a pane now — same admission, same
            // answer — so both fields paint nothing past the ink ramp, and each
            // is drawn by its Line border and lit by its Volt ring with its fill
            // spent entirely on the step it cuts at SOLID.
            //
            // They are still two constants rather than one. The identity is
            // per-container, and they would part company again the moment either
            // field moved; that they agree today is the two-tier collapse showing
            // up in the arithmetic. The one thing still separating them is WHICH
            // SETTING they answer — the panel's field follows the slider because
            // the panel does, and the search box follows the reach setting
            // because the library pane does.
            ["ChromeFieldOnGround"] = A(Surface, paneFieldAlpha),
            ["ChromeFieldOnSurface"] = A(Ground, fieldAlpha),

            // Under the art stack inside a tile, so a cover that has decoded one
            // of its two dormancy layers and not the other cannot show the
            // window through the gap between them.
            //
            // NEVER derived from wallAlpha, and now it is load-bearing rather
            // than belt-and-braces: with the field allowed to open up, this is
            // the ONE thing standing between the ramp's floor and the desktop.
            // A tile paints it under both dormancy layers, so the cross-fade
            // composites over exactly the ground it was calibrated against no
            // matter what the field is doing. Construction, not measurement.
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
