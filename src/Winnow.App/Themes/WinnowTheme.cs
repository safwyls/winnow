using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// A complete palette, and the rule that makes several of them one system.
/// Roles are invariant (Volt = selection, Flare = unread, etc.); colours may
/// change per theme. Fields are colours so theme changes write
/// <see cref="SolidColorBrush.Color"/> on existing brushes in resources.
/// </summary>
public sealed record WinnowTheme
{
    /// <summary>
    /// The alpha the window's ground reaches at the far end of the slider
    /// (gaps between panes and caption). Chosen to keep caption ink above AA
    /// in every theme: ground admits 85%, a pane admits 35%.
    /// </summary>
    public const double MinShellAlpha = 0.15;

    /// <summary>
    /// How much of the desktop a pane finally admits at the far end of the
    /// slider. Constrained by polarity: the field must stay darker than a
    /// dormant capsule so the dormancy ramp keeps its encoding.
    /// </summary>
    public const double MinWallAlpha = 0.65;

    /// <summary>
    /// What a pane actually paints, over the window's ground. Forced by the
    /// stacking identity: <c>(1 - MinShellAlpha) * (1 - MinPaneAlpha) =
    /// 1 - MinWallAlpha</c>. Rides the ink ramp to keep the product linear.
    /// </summary>
    public const double MinPaneAlpha =
        1 - ((1 - MinWallAlpha) / (1 - MinShellAlpha));

    /// <summary>
    /// The alpha an input field in the filter panel reaches at the far end
    /// of the slider. Forced to zero: the pane already admits
    /// <c>1 - MinWallAlpha</c>, so there is no budget left for the field.
    /// </summary>
    public const double MinFieldAlpha =
        1 - ((1 - MinWallAlpha) / (1 - MinWallAlpha));

    /// <summary>
    /// The alpha an input field inside the library pane reaches at the far
    /// end of the slider. Same identity as <see cref="MinFieldAlpha"/>,
    /// same answer (zero); follows the wall's setting, not the slider's.
    /// </summary>
    public const double MinPaneFieldAlpha =
        1 - ((1 - MinWallAlpha) / (1 - MinWallAlpha));

    /// <summary>
    /// How much of the slider the ink compensation is spent over. Front-loading
    /// it avoids a contrast dip at the start of the track.
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
    // measured 3.1:1 and was refused. As a surface opens up it
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
    /// The file this theme was read out of, as a bare file name (null for
    /// built-ins). A name, not a path; nothing in the app dereferences it.
    /// </summary>
    public string? SourceFile { get; init; }

    /// <summary>True for anything that came out of the themes folder. Drives
    /// exactly two things on the settings screen: the file name under the card,
    /// and the contrast line the built-ins do not need because the slider's own
    /// AA mark already carries it.</summary>
    public bool IsUserTheme => SourceFile is not null;

    /// <summary>
    /// The veil strength that reproduces this theme's Surface-to-SurfaceRaised
    /// step, derived from the three channel ratios. Starting point for the
    /// hover veil so leaving slider zero is invisible.
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
    /// Every token this theme writes, as a flat key-to-colour map at the given
    /// transparency (0 fully opaque, 1 most open). Derived alphas are computed
    /// here rather than written into each theme by hand.
    /// </summary>
    public Dictionary<string, Color> Tokens(
        double transparency,
        bool wallTranslucent = false,
        WinnowLayout layout = WinnowLayouts.Default)
    {
        var t = Math.Clamp(transparency, 0, 1);
        var floating = layout == WinnowLayout.Floating;

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
        // wall at 87 to 89 of the 101 slider positions in Winnow, Nightshift and
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

            // The resting store pip's field on a multi-store tile: legible over
            // any capsule, never a second opaque block on the art.
            ["TileChipGround"] = A(Ground, 0.82),

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
