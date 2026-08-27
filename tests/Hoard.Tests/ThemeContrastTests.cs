using Avalonia.Media;
using Hoard.App.Services;
using Hoard.App.Themes;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// design-system.md §8's accessibility floor, held for every theme across the
/// whole transparency range — and §2's rule that Flare has exactly one job.
///
/// <para><b>Why this exists as a test and not as a note in a design file.</b>
/// §13 gap 7 recorded a translucent surface measuring 3.1:1 and concluded that
/// translucency could not reach a reading surface. The measurement was right;
/// what it actually proved was narrower — that an ink chosen for an opaque
/// ground cannot have alpha subtracted from it. The chrome therefore takes a
/// darker ink and a brighter dim ink as it opens up, and a claim like that is
/// only worth making if something re-checks it every build. A theme added later,
/// or an alpha nudged by eye until it "looked about right", fails here rather
/// than in somebody's rail.</para>
///
/// <para><b>Three backdrops, and the first one is the argument.</b> WHITE is the
/// ceiling: no wallpaper, and no compositor, can hand the window anything
/// brighter, so a number that holds against white holds everywhere without
/// assuming anything about how Windows composes Mica. MICA is the composite
/// measured on a real desktop and recorded in tokens.axaml — what actually
/// happens. BLACK is the other end. Light ink on a translucent dark surface gets
/// WORSE as the backdrop brightens, so white is the case that binds and the
/// other two are there to prove the range was walked rather than assumed.</para>
///
/// <para><b>The sums are re-implemented here on purpose.</b>
/// <c>Hoard.App.Themes.Colorimetry</c> carries the same arithmetic so the
/// Appearance screen can report a live number; a test that called it would prove
/// only that the code agrees with itself.</para>
/// </summary>
public class ThemeContrastTests
{
    /// <summary>The brightest thing any backdrop can be.</summary>
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    /// <summary>
    /// A dark desktop — back-solved from the composite Windows actually put
    /// behind our chrome, measured on a real machine. The other end of the
    /// bracket the Appearance screen reports, and the case most people are in.
    /// </summary>
    private static readonly Color DarkDesktop = Backsolve(
        composite: Color.FromRgb(0x0F, 0x16, 0x17),
        ink: Color.FromRgb(0x07, 0x12, 0x14),
        alpha: 0.685);

    /// <summary>Every whole percent the slider can be dragged to, as a fraction.</summary>
    private static IEnumerable<double> Range()
    {
        for (var percent = 0; percent <= 100; percent++)
        {
            yield return percent / 100.0;
        }
    }

    public static TheoryData<string> ThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var theme in HoardThemes.All)
        {
            data.Add(theme.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Opaque_palette_clears_the_section_8_floor(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(transparency: 0);

        // §8: "TextDim on Surface measures 5.88:1, and on SurfaceRaised — which
        // is what a selected list row puts under the store and idle columns —
        // 5.04:1. Do not dim further."
        Assert.True(Contrast(t["TextDim"], t["Surface"]) >= 5.0, $"{id} TextDim/Surface");
        Assert.True(Contrast(t["TextDim"], t["SurfaceRaised"]) >= 5.0, $"{id} TextDim/SurfaceRaised");
        Assert.True(Contrast(t["TextDim"], t["Ground"]) >= 5.0, $"{id} TextDim/Ground");

        Assert.True(Contrast(t["Text"], t["Surface"]) >= 12.0, $"{id} Text/Surface");
        Assert.True(Contrast(t["Volt"], t["Ground"]) >= 7.0, $"{id} Volt/Ground");
        Assert.True(Contrast(t["Azure"], t["Surface"]) >= 4.5, $"{id} Azure/Surface");
        Assert.True(Contrast(t["Amber"], t["Surface"]) >= 4.5, $"{id} Amber/Surface");
        Assert.True(Contrast(t["Flare"], t["Surface"]) >= 4.5, $"{id} Flare/Surface");

        // The ink that sits ON a Volt fill (the Play button, "Same game").
        Assert.True(Contrast(t["VoltInk"], t["Volt"]) >= 7.0, $"{id} VoltInk/Volt");
        Assert.True(Contrast(t["DangerInk"], t["Danger"]) >= 2.9, $"{id} DangerInk/Danger");
    }

    /// <summary>
    /// §9 as amended: the caption takes the RAIL's colour, so the chrome is one
    /// continuous bracket rather than two tones meeting at a corner — and the
    /// art field is the recess inside it.
    ///
    /// <para>Same ink AND same alpha, checked at every slider position, because
    /// matching colours at differing alphas composite to two different tones over
    /// the same backdrop and put the corner straight back.</para>
    ///
    /// <para>The lip is still unlit in the sense that survives: the caption is a
    /// chrome tone, not a platform-bright strip, and the wall it sits above is
    /// darker than it is in every theme.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_caption_is_the_rail(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            Assert.Equal(t["ChromeSurface"], t["CaptionFill"]);
        }

        var opaque = theme.Tokens(transparency: 0);
        Assert.Equal(theme.Surface, opaque["CaptionFill"]);
        Assert.True(
            Luminance(opaque["WallGround"]) < Luminance(opaque["CaptionFill"]),
            $"{id}: the wall is not below the chrome");
    }

    /// <summary>
    /// The neutral family steps in one direction and keeps stepping. Well is
    /// still the bottom of it — it backs the scrollbar track and the modal scrim,
    /// which are the two places a tone below Ground is still the point.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_neutral_family_steps_one_way(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(transparency: 0);

        Assert.True(Luminance(t["Well"]) < Luminance(t["Ground"]), $"{id}: Well is not below Ground");
        Assert.True(Luminance(t["Ground"]) < Luminance(t["Surface"]), $"{id}: Surface is not above Ground");
        Assert.True(Luminance(t["Surface"]) < Luminance(t["SurfaceRaised"]), $"{id}: SurfaceRaised is not above Surface");
        Assert.True(Luminance(t["SurfaceRaised"]) < Luminance(t["SurfaceHigh"]), $"{id}: SurfaceHigh is not above SurfaceRaised");
    }

    /// <summary>
    /// Slider zero is not "transparency, off" — it is the opaque palette, exactly,
    /// with nothing carrying alpha. That is what makes zero a real position and
    /// the accessibility answer rather than a degenerate case of a feature.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Zero_is_the_opaque_palette_exactly(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(transparency: 0);

        Assert.Equal(theme.Surface, t["ChromeSurface"]);
        Assert.Equal(theme.Ground, t["ChromeGround"]);
        Assert.Equal(theme.Ground, t["ShellGround"]);
        Assert.Equal(theme.SurfaceRaised, t["ChromeRaised"]);
        Assert.Equal(theme.TextDim, t["TextDim"]);
        Assert.Equal(theme.TextFaint, t["TextFaint"]);

        foreach (var key in new[] { "ChromeSurface", "ChromeGround", "ChromeRaised", "CaptionFill", "ShellGround" })
        {
            Assert.Equal(255, t[key].A);
        }
    }

    /// <summary>
    /// Elevation stays elevation, at every position on the slider and against
    /// every backdrop. A darker ink over an already-translucent rail composites
    /// DOWNWARDS, which would make a selected row darker than its neighbours; the
    /// veil is what stops that, and the walk between the two has to stop it at
    /// every intermediate value as well.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_selected_row_is_never_below_the_rail(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            foreach (var backdrop in new[] { White, DarkDesktop, Black })
            {
                var rail = Over(t["ChromeSurface"], backdrop);
                var row = Over(t["ChromeRaised"], rail);
                Assert.True(
                    Luminance(row) > Luminance(rail),
                    $"{id}: selected row is not above the rail at {transparency:P0} on {backdrop}");
            }
        }
    }

    /// <summary>
    /// The range the accessibility floor covers, and the fact that it is a real
    /// range rather than a token one.
    ///
    /// <para><b>This is the honest shape of the trade.</b> The slider deliberately
    /// travels past the point where the WORST case — a wall of white behind the
    /// window — takes the rail's metadata ink under AA, because the user asked to
    /// be able to choose that and being protected from it is not a service. What
    /// the system owes is that the safe part of the range is not a sliver and that
    /// the Appearance screen can say exactly where it ends: so every theme must
    /// clear AA at 15% or better, and every theme's own ceiling is drawn on the
    /// track and reported live.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_worst_case_floor_covers_a_real_part_of_the_range(string id)
    {
        var theme = HoardThemes.ById(id);
        var ceiling = Colorimetry.AaCeiling(theme);

        Assert.True(ceiling >= 20, $"{id}: AA survives only to {ceiling}%");
        Assert.True(ceiling < 100, $"{id}: the white ceiling never bites, so the mark is a lie");

        for (var percent = 0; percent <= ceiling; percent++)
        {
            var t = theme.Tokens(percent / 100.0);
            var rail = Over(t["ChromeSurface"], White);
            var row = Over(t["ChromeRaised"], rail);
            var bar = Over(t["ChromeGround"], White);

            Assert.True(Contrast(t["TextDim"], rail) >= 4.5, $"{id} TextDim/rail at {percent}% on white");
            Assert.True(Contrast(t["TextDim"], row) >= 4.5, $"{id} TextDim/selected row at {percent}% on white");
            Assert.True(Contrast(t["TextDim"], bar) >= 4.5, $"{id} TextDim/command bar at {percent}% on white");
        }
    }

    /// <summary>
    /// What actually happens on a machine, as opposed to the ceiling: over the
    /// Mica composite measured on a real desktop the metadata ink comes out AHEAD
    /// of where it sits on a solid rail, everywhere on the slider.
    ///
    /// <para>Windows composes dark Mica by darkening the wallpaper hard, so the
    /// backdrop is DARKER than our own rail and admitting more of it deepens the
    /// ground the labels sit on. Asserted rather than described, because it is the
    /// claim the Appearance screen's first number makes.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void On_a_real_desktop_the_metadata_ink_beats_its_opaque_self(string id)
    {
        var theme = HoardThemes.ById(id);
        var solidRail = Contrast(
            theme.Tokens(0)["TextDim"],
            theme.Tokens(0)["Surface"]);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            var measured = Contrast(t["TextDim"], Over(t["ChromeSurface"], DarkDesktop));

            Assert.True(
                measured >= solidRail - 0.01,
                $"{id}: at {transparency:P0} the rail over a dark desktop reads {measured:0.00}:1, below the solid rail's {solidRail:0.00}:1");
        }
    }

    /// <summary>
    /// §2's discipline, and the one rule that survives every theme: Flare marks
    /// unread updates and the bucket that counts them and nothing else. A theme
    /// may change which colour plays that role. It may not hand that colour a
    /// second job — the instant it becomes a generic accent the badge stops
    /// meaning anything.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Flare_is_spent_on_nothing_else(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var (role, colour) in theme.Roles())
        {
            if (role == "Flare")
            {
                continue;
            }

            Assert.False(colour == theme.Flare, $"{id}: {role} is Flare");
        }

        // And it stays far enough from Danger that a red the size of a caption
        // button is never mistaken for a 10px dot (§2 puts the default pair 26°
        // apart and accepts it).
        Assert.True(
            HueGap(theme.Flare, theme.Danger) >= 24,
            $"{id}: Flare and Danger are {HueGap(theme.Flare, theme.Danger):0}° apart");

        // Same for the selection colour, which is the other one a dot could be
        // confused with.
        Assert.True(
            HueGap(theme.Flare, theme.Volt) >= 60,
            $"{id}: Flare and Volt are {HueGap(theme.Flare, theme.Volt):0}° apart");
    }

    /// <summary>
    /// The tile keeps its own ground under the dormancy cross-fade, whatever the
    /// field behind it is doing. §5.4 composites two bitmap layers by opacity;
    /// between the first decoding and the second, a dimmed tile is a partly
    /// transparent tile, and over a translucent field that means the desktop
    /// showing through the ramp's floor.
    ///
    /// <para>This was belt-and-braces while the wall was opaque at every
    /// setting. Now that the field can open up it is the ONLY thing holding, so
    /// it is asserted in both reach states rather than in the default one.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_tile_stays_opaque_whatever_the_field_does(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var wallTranslucent in new[] { false, true })
        {
            foreach (var transparency in Range())
            {
                var t = theme.Tokens(transparency, wallTranslucent);

                Assert.Equal(255, t["TileGround"].A);
                Assert.Equal(theme.Ground, t["TileGround"]);

                // Popovers: a flyout is its own popup root and never receives
                // the window's backdrop, so a translucent fill there would
                // sample the application instead of the desktop.
                Assert.Equal(255, t["SurfaceRaised"].A);
            }
        }
    }

    /// <summary>
    /// The wall's field is opaque unless it was asked for, opaque at slider zero
    /// either way, and never more open than the chrome it sits beside.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_field_opens_only_when_asked_and_never_past_the_chrome(string id)
    {
        var theme = HoardThemes.ById(id);

        byte previous = 255;
        foreach (var transparency in Range())
        {
            // Not asked for: exactly what it has always been, at every position.
            var solid = theme.Tokens(transparency)["WallGround"];
            Assert.Equal(255, solid.A);
            Assert.Equal(theme.Ground, solid);

            var open = theme.Tokens(transparency, wallTranslucent: true);
            var wall = open["WallGround"];

            // Zero is opaque in both reach states, so the reach setting cannot
            // produce a see-through window on its own.
            if (transparency <= 0)
            {
                Assert.Equal(255, wall.A);
            }

            // A field more open than the rail beside it would invert §14.2's
            // recess — the art hangs BELOW the chrome, in every theme.
            Assert.True(
                wall.A >= open["ChromeSurface"].A,
                $"{id}: the field is more open than the chrome at {transparency:P0}");

            Assert.True(wall.A <= previous, $"{id}: the field's alpha rose at {transparency:P0}");
            previous = wall.A;

            // The colour is the theme's own ground throughout; only the alpha
            // moves. The field has no ink ramp of its own — nothing reads on it.
            Assert.Equal(theme.Ground.R, wall.R);
            Assert.Equal(theme.Ground.G, wall.G);
            Assert.Equal(theme.Ground.B, wall.B);
        }

        // And the far end is exactly half the chrome's reach, which is the
        // relation the Appearance screen prints.
        var far = theme.Tokens(1, wallTranslucent: true);
        Assert.Equal(
            Math.Round(255 * HoardTheme.MinWallAlpha),
            (double)far["WallGround"].A);
        Assert.Equal(
            1 - HoardTheme.MinChromeAlpha,
            (1 - HoardTheme.MinWallAlpha) * 2,
            precision: 6);
    }

    /// <summary>
    /// The open field never fails before the labels do.
    ///
    /// <para>§5.1's ramp is dark capsules on a dark field, and it only reads that
    /// way while the field stays darker than the capsules. Over a white
    /// wallpaper the field climbs and at some position it passes the dormancy
    /// floor of a middling dark cover; past there a dimmed tile reads as a hole
    /// punched in a lit field rather than as faded art, and the ramp is the
    /// product.</para>
    ///
    /// <para>The wall does not have to hold across the whole slider — the range
    /// past the AA mark is already a place the user is told the labels stop
    /// clearing 4.5:1. What it must not do is fail FIRST, because then the
    /// translucent field would be quietly costing something the screen does not
    /// report. That is what fixes <c>MinWallAlpha</c>, and it is why the
    /// constant is 0.65 rather than something picked by eye.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_open_field_stays_under_the_art_at_least_as_far_as_the_labels_hold(string id)
    {
        var theme = HoardThemes.ById(id);

        // §5.1's floor — saturation 0.22, hue -6°, brightness 0.68 — applied to
        // an ordinary dark blue cover. Re-stated here rather than read off
        // Colorimetry, for this file's usual reason.
        var dormant = Color.FromRgb(0x2C, 0x32, 0x37);

        var aa = 0;
        while (aa < 100
            && WorstChromeContrast(theme, (aa + 1) / 100.0, White) >= 4.5)
        {
            aa++;
        }

        var polarity = 0;
        while (polarity < 100
            && Luminance(Over(
                theme.Tokens((polarity + 1) / 100.0, wallTranslucent: true)["WallGround"],
                White)) <= Luminance(dormant))
        {
            polarity++;
        }

        Assert.True(
            polarity >= aa,
            $"{id}: the field inverts the ramp at {polarity}%, before the labels drop under AA at {aa}%");

        // And over a dark desktop the question never arises: the composite is
        // darker than Ground, so opening the field deepens it.
        foreach (var transparency in Range())
        {
            var field = Over(
                theme.Tokens(transparency, wallTranslucent: true)["WallGround"], DarkDesktop);
            Assert.True(
                Luminance(field) <= Luminance(dormant),
                $"{id}: the field passed a dormant cover over a dark desktop at {transparency:P0}");
        }
    }

    /// <summary>
    /// Transparency has to be visible to be worth having, and the previous
    /// attempt's failure was exactly that it was not: 86% alpha over a backdrop
    /// Windows has already darkened hard is nothing anyone can see. So the far end
    /// of the slider admits most of the desktop, and the near end admits none.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_far_end_is_actually_transparent(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(transparency: 1);

        foreach (var key in new[] { "ChromeSurface", "ChromeGround", "CaptionFill" })
        {
            // The desktop supplies at least 60% of the chrome. The boolean this
            // replaced supplied 14%.
            Assert.True(t[key].A <= 102, $"{id}: {key} at the far end is still {t[key].A}/255");
        }

        Assert.Equal(0, t["ShellGround"].A);

        // And the alpha only ever comes off — no position on the track is denser
        // than the one before it.
        byte previous = 255;
        foreach (var transparency in Range())
        {
            var alpha = theme.Tokens(transparency)["ChromeSurface"].A;
            Assert.True(alpha <= previous, $"{id}: alpha rose at {transparency:P0}");
            previous = alpha;
        }
    }

    /// <summary>
    /// A theme change is applied by writing colours onto the brush objects the
    /// views already resolved, so a token the dictionary does not hold has to be
    /// skipped rather than added — an added key is one nothing reads, and a
    /// missing token that silently "works" is the failure this codebase keeps
    /// hitting.
    /// </summary>
    [Fact]
    public void Applying_a_theme_writes_the_brushes_in_place()
    {
        var resources = new Avalonia.Controls.ResourceDictionary
        {
            ["Surface"] = new SolidColorBrush(Colors.Black),
            ["Flare"] = new SolidColorBrush(Colors.Black),
        };
        var surface = (SolidColorBrush)resources["Surface"]!;

        ThemeService.ApplyTo(resources, HoardThemes.BoxArt, transparency: 0);

        Assert.Same(surface, resources["Surface"]);
        Assert.Equal(HoardThemes.BoxArt.Surface, surface.Color);
        Assert.Equal(HoardThemes.BoxArt.Flare, ((SolidColorBrush)resources["Flare"]!).Color);
        Assert.False(resources.ContainsKey("Volt"), "a key the dictionary lacks was added");
    }

    [Fact]
    public void An_unknown_stored_theme_reads_as_unset()
    {
        // A preference file written by a later version must not stop the app —
        // and neither must one written by an earlier one that named a theme
        // which has since been retired.
        Assert.Same(HoardThemes.Default, HoardThemes.ById("a-theme-from-the-future"));
        Assert.Same(HoardThemes.Default, HoardThemes.ById("cold-storage"));
        Assert.Same(HoardThemes.Default, HoardThemes.ById("phosphor"));
        Assert.Same(HoardThemes.Default, HoardThemes.ById(null));
        Assert.Same(HoardThemes.Hoard, HoardThemes.Default);
    }

    /// <summary>
    /// A session that was told what to look like never writes what it looks like.
    ///
    /// <para>The debug capture flags exist so every theme and every slider
    /// position can be reviewed in a screenshot without leaving a preference
    /// behind in somebody's real library. Suppressing the write only while the
    /// override was being applied was not enough: a capture run drove the
    /// Appearance screen, posted input reached the slider, and the row the run
    /// had promised not to touch was rewritten. The seal is for the whole
    /// session now, and this is the test that says so.</para>
    /// </summary>
    [Fact]
    public async Task An_overridden_session_never_writes_a_preference()
    {
        var settings = new RecordingSettings();
        var service = new ThemeService(settings);

        service.OverrideForSession(HoardThemes.Tungsten, 60);

        // Everything a user could do on the Appearance screen — all four
        // decisions, not just the two the flags existed for when it was written.
        service.SelectTheme(HoardThemes.BoxArt);
        service.SetTransparency(12);
        service.SetTransparency(0);
        service.SelectBackdrop(HoardBackdrop.Mica);
        service.SetWallTranslucent(true);
        service.SetWallTranslucent(false);
        await service.PendingSave;

        Assert.Empty(settings.Writes);
        Assert.Same(HoardThemes.BoxArt, service.Theme);
        Assert.Equal(0, service.Transparency);
        Assert.Equal(HoardBackdrop.Mica, service.Backdrop);
        Assert.False(service.WallTranslucent);
    }

    /// <summary>
    /// Asking for a material and getting the other one is a THIRD answer, and
    /// the screen has to be able to tell it from a refusal.
    ///
    /// <para>Falling through to the other backdrop is right — a machine that
    /// cannot do Mica is better off with acrylic than with a solid window — but
    /// a substitution nobody is told about is how a user concludes the choice
    /// does nothing at all.</para>
    /// </summary>
    [Fact]
    public void A_substituted_backdrop_is_not_the_same_answer_as_a_refused_one()
    {
        var service = new ThemeService();
        service.SetTransparency(40);
        service.SelectBackdrop(HoardBackdrop.Mica);

        // Refused outright.
        service.SetActiveBackdrop(HoardBackdrop.None);
        Assert.False(service.BackdropAvailable);
        Assert.False(service.BackdropSubstituted);
        Assert.Equal(0, service.ActiveTransparency);
        Assert.False(service.ActiveWallTranslucency);

        // Composited, but not the material that was asked for.
        service.SetActiveBackdrop(HoardBackdrop.Acrylic);
        Assert.True(service.BackdropAvailable);
        Assert.True(service.BackdropSubstituted);
        Assert.Equal(HoardBackdrop.Mica, service.Backdrop);
        Assert.Equal(0.40, service.ActiveTransparency, precision: 6);

        // And honoured.
        service.SetActiveBackdrop(HoardBackdrop.Mica);
        Assert.True(service.BackdropAvailable);
        Assert.False(service.BackdropSubstituted);
    }

    /// <summary>
    /// Changing the material clears what the last one got, so the screen can
    /// never report the old answer as the new one's.
    /// </summary>
    [Fact]
    public void Picking_a_new_material_forgets_the_old_ones_answer()
    {
        var service = new ThemeService();
        service.SetTransparency(40);
        service.SetActiveBackdrop(HoardBackdrop.Acrylic);
        Assert.True(service.BackdropAvailable);

        service.SelectBackdrop(HoardBackdrop.Mica);

        Assert.Equal(HoardBackdrop.None, service.ActiveBackdrop);
        Assert.False(service.BackdropAvailable);
        Assert.False(service.BackdropSubstituted);
    }

    /// <summary>
    /// The wall's field is painted translucent only when there is a desktop
    /// reaching the window for it to show. A see-through field over a window
    /// with nothing behind it is the failure the opaque token set exists to
    /// catch, and it is the same failure for the wall as for the rail.
    /// </summary>
    [Fact]
    public void The_field_opens_only_when_the_desktop_is_actually_arriving()
    {
        var service = new ThemeService();
        service.SetWallTranslucent(true);

        // Asked for, but the slider is at zero.
        Assert.False(service.ActiveWallTranslucency);

        service.SetTransparency(50);
        Assert.False(service.ActiveWallTranslucency);

        service.SetActiveBackdrop(HoardBackdrop.Acrylic);
        Assert.True(service.ActiveWallTranslucency);

        service.SetActiveBackdrop(HoardBackdrop.None);
        Assert.False(service.ActiveWallTranslucency);
    }

    [Fact]
    public void An_unknown_stored_backdrop_reads_as_unset()
    {
        // Same reasoning as the theme id: a preference written by a later
        // version must not stop the app. "none" lands here too — it is a report
        // about what the platform did, never something a user picked.
        Assert.Equal(HoardBackdrop.Acrylic, HoardBackdrops.ById("acrylic"));
        Assert.Equal(HoardBackdrop.Mica, HoardBackdrops.ById("mica"));
        Assert.Equal(HoardBackdrops.Default, HoardBackdrops.ById("none"));
        Assert.Equal(HoardBackdrops.Default, HoardBackdrops.ById("blur-behind-2029"));
        Assert.Equal(HoardBackdrops.Default, HoardBackdrops.ById(null));
        Assert.Equal(HoardBackdrop.Acrylic, HoardBackdrops.Default);
    }

    private sealed class RecordingSettings : Hoard.Core.Repositories.ISettingsRepository
    {
        public List<(string Key, string Value)> Writes { get; } = [];

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            Writes.Add((key, value));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// The setting the checkbox wrote is answered, not orphaned. Somebody who
    /// turned transparency on does not get silently returned to solid because the
    /// control that set it was replaced by a better one.
    /// </summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("55", 55)]
    [InlineData("100", 100)]
    [InlineData("240", 100)]
    [InlineData("-5", 0)]
    [InlineData("true", ThemeService.MigratedTransparency)]
    [InlineData("True", ThemeService.MigratedTransparency)]
    [InlineData("false", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void The_old_boolean_preference_migrates(string? stored, int expected)
    {
        Assert.Equal(expected, ThemeService.ParseTransparency(stored));
    }

    // ── The sums ────────────────────────────────────────────────────────────
    // WCAG 2.x relative luminance and contrast ratio, and an sRGB source-over
    // composite — which is what the GPU does, so it is what the window shows.

    /// <summary>
    /// The worst the metadata ink does anywhere on the chrome at one slider
    /// position: the rail, a selected row on it, and the command bar. The
    /// selected row is usually the one that binds — the rail with a veil over
    /// it, so the lightest reading surface in the window.
    /// </summary>
    /// <summary>
    /// The panes that share the wall's position take the wall's ramp — and the
    /// reason they are allowed to is a measurement, not a preference.
    ///
    /// <para><b>What this replaces.</b> <c>PaneGround</c> was opaque at every
    /// setting on the grounds that the merge queue, Stores, Appearance, the list
    /// view and the empty state are text sitting directly on the field, and that
    /// §14.3 rules that out. The principle was right; the number it was measured
    /// against was the CHROME's. The wall admits 0.35 of the desktop where the
    /// chrome admits 0.70, and the rail already carries labels at the chrome's
    /// full reach — so a pane on the wall's ramp is not a new risk, it is a
    /// smaller one than the app already ships.</para>
    ///
    /// <para>The bar, therefore, is the same one <c>MinWallAlpha</c> is held to:
    /// <b>a pane must not be the surface that fails first.</b> Its ink has to
    /// clear AA at least as far up the slider as the chrome's does, against
    /// white, which is the ceiling any wallpaper can reach.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_pane_carries_its_ink_further_than_the_chrome_does(string id)
    {
        var theme = HoardThemes.ById(id);

        var chrome = Ceiling(t => WorstChromeContrast(theme, t, White));

        // The inks that actually sit on PaneGround: a pane header and the empty
        // state are Text and TextDim, and the list view's store and idle columns
        // are TextDim. TextFaint never touches it — every use of it in
        // AppearanceView is inside an opaque Border.card.
        var text = Ceiling(t => Contrast(theme.Tokens(t)["Text"], Field(theme, t, White)));
        var dim = Ceiling(t => Contrast(theme.Tokens(t)["TextDim"], Field(theme, t, White)));

        // And a SELECTED list row, which is the pane's lightest reading surface
        // for the same reason a selected rail row is the chrome's.
        var row = Ceiling(t => Contrast(
            theme.Tokens(t)["TextDim"],
            Over(theme.Tokens(t)["ChromeRaised"], Field(theme, t, White))));

        Assert.True(dim > chrome, $"{id}: pane TextDim fails at {dim}%, chrome at {chrome}%");
        Assert.True(text > chrome, $"{id}: pane Text fails at {text}%, chrome at {chrome}%");
        Assert.True(row > chrome, $"{id}: a selected row fails at {row}%, chrome at {chrome}%");

        // Over a dark desktop the question never arises, for the same reason it
        // does not for the chrome: the composite is darker than Ground, so
        // opening the pane deepens the ground its labels sit on.
        foreach (var transparency in Range())
        {
            Assert.True(
                Contrast(theme.Tokens(transparency)["TextDim"], Field(theme, transparency, DarkDesktop))
                    >= 4.5,
                $"{id}: a pane label dropped under AA over a dark desktop at {transparency:P0}");
        }
    }

    /// <summary>
    /// A pane is the wall, in alpha and in ink, and it answers the same setting.
    ///
    /// <para>Two panes are never on screen at once, but a pane and the wall are
    /// one keystroke apart, and the complaint that started this was exactly that
    /// they did not match. Anything looser than equality here would let them
    /// drift apart again by a rounding step.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_pane_is_the_field(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var wallTranslucent in new[] { false, true })
        {
            foreach (var transparency in Range())
            {
                var t = theme.Tokens(transparency, wallTranslucent);
                Assert.Equal(t["WallGround"], t["PaneGround"]);
            }
        }

        // Not asked for: exactly what it has always been, at every position.
        foreach (var transparency in Range())
        {
            Assert.Equal(theme.Ground, theme.Tokens(transparency)["PaneGround"]);
        }
    }

    /// <summary>
    /// An input field admits half of what the surface around it admits — and
    /// that number is forced rather than chosen.
    ///
    /// <para>A field is a CHILD of its bar or its panel, so the two alphas
    /// stack: the desktop's share of a field is
    /// <c>(1 − containerAlpha) · (1 − fieldAlpha)</c>. Requiring a field to admit
    /// what the art field admits — so the window has one translucency rather
    /// than three — solves for <c>MinFieldAlpha</c> outright, and this asserts
    /// the identity rather than the constant, so retuning either end of the
    /// slider cannot leave a stale number behind.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_admits_what_the_art_field_admits(string id)
    {
        var theme = HoardThemes.ById(id);

        Assert.Equal(
            1 - HoardTheme.MinWallAlpha,
            (1 - HoardTheme.MinChromeAlpha) * (1 - HoardTheme.MinFieldAlpha),
            precision: 9);

        byte previousOnGround = 255;
        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            var onGround = t["ChromeFieldOnGround"];
            var onSurface = t["ChromeFieldOnSurface"];

            // Same alpha, both of them, and it never rises.
            Assert.Equal(onGround.A, onSurface.A);
            Assert.True(onGround.A <= previousOnGround, $"{id}: a field's alpha rose at {transparency:P0}");
            previousOnGround = onGround.A;

            // Never more open than the surface around it, and never more open
            // than the art field — a field is a step in the chrome, not a hole
            // through it. Asked of what the desktop actually contributes THROUGH
            // the field rather than of the field's own alpha, because a field is
            // a child of its container and the two alphas multiply: below the
            // first quarter the field's own alpha falls faster than the bar's
            // and the product is still smaller than either.
            // Alphas are stored as bytes, so every share here is quantised to
            // 1/255 and the product of two of them carries both errors.
            const double Rounding = 0.005;
            var wallShare = 1 - (theme.Tokens(transparency, wallTranslucent: true)["WallGround"].A / 255.0);
            var barShare = 1 - (t["ChromeGround"].A / 255.0);
            var fieldShare = barShare * (1 - (onGround.A / 255.0));

            Assert.True(
                fieldShare <= barShare + Rounding,
                $"{id}: the field admits more than the bar at {transparency:P0}");
            Assert.True(
                fieldShare <= wallShare + Rounding,
                $"{id}: the field admits more than the art field at {transparency:P0}");

            // The ink is the chrome's OTHER walked ink, not a fourth colour —
            // so a field is one step from its container the whole way across,
            // exactly as it was when everything was opaque.
            Assert.Equal(
                (t["ChromeSurface"].R, t["ChromeSurface"].G, t["ChromeSurface"].B),
                (onGround.R, onGround.G, onGround.B));
            Assert.Equal(
                (t["ChromeGround"].R, t["ChromeGround"].G, t["ChromeGround"].B),
                (onSurface.R, onSurface.G, onSurface.B));
        }

        // Slider zero is bit-for-bit the opaque palette, here as everywhere.
        var solid = theme.Tokens(transparency: 0);
        Assert.Equal(theme.Surface, solid["ChromeFieldOnGround"]);
        Assert.Equal(theme.Ground, solid["ChromeFieldOnSurface"]);

        // And the point of it: what the desktop contributes THROUGH a field is
        // the wall's own share, at every position past the first quarter — not
        // merely at the end of the track. That is what the early ramp buys, and
        // it is the whole claim the Appearance screen makes about fields.
        foreach (var transparency in Range())
        {
            if (transparency < 0.25)
            {
                continue;
            }

            var t = theme.Tokens(transparency);
            var throughBar = (1 - (t["ChromeGround"].A / 255.0))
                * (1 - (t["ChromeFieldOnGround"].A / 255.0));
            var throughPanel = (1 - (t["ChromeSurface"].A / 255.0))
                * (1 - (t["ChromeFieldOnSurface"].A / 255.0));
            var wall = 1 - (theme.Tokens(transparency, wallTranslucent: true)["WallGround"].A / 255.0);

            // Absolute tolerance rather than decimal places: every alpha here
            // is a byte, so a product of two of them carries both quantisations
            // and a rounding boundary is not a failure.
            Assert.True(
                Math.Abs(wall - throughBar) <= 0.006,
                $"{id}: a bar field admits {throughBar:P1}, the wall {wall:P1}, at {transparency:P0}");
            Assert.True(
                Math.Abs(wall - throughPanel) <= 0.006,
                $"{id}: a panel field admits {throughPanel:P1}, the wall {wall:P1}, at {transparency:P0}");
        }
    }

    /// <summary>
    /// An input is a TARGET, not a surface, so it is held to a stricter bar than
    /// a pane: it has to stay legible while somebody is typing in it, and its
    /// placeholder — the dimmest ink any field carries — has to stay readable
    /// too.
    ///
    /// <para>It clears that by a distance, because the field is the darkest
    /// thing in the chrome: half the reach of the bar it sits on, over an ink
    /// one step below it. The typed <c>Text</c> holds AA across the whole
    /// slider in every theme, and the placeholder holds it to roughly 85% —
    /// against the chrome's own 26 to 31.</para>
    ///
    /// <para><b>§8's floor is asserted for the placeholder specifically</b>,
    /// because that is where this was already broken before any of it was
    /// translucent: the year field's watermark was <c>TextFaint</c>, which
    /// measures 3.58 to 4.13 on the OPAQUE ground. A watermark ink is for
    /// watermarks and disabled arrows; a hint the user is meant to read is
    /// neither, so it is <c>TextDim</c> now and this is what keeps it there.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_stays_legible_further_than_the_chrome_around_it(string id)
    {
        var theme = HoardThemes.ById(id);
        var chrome = Ceiling(t => WorstChromeContrast(theme, t, White));

        foreach (var onBar in new[] { true, false })
        {
            Color Fill(double t)
            {
                var tok = theme.Tokens(t);
                var container = Over(tok[onBar ? "ChromeGround" : "ChromeSurface"], White);
                return Over(tok[onBar ? "ChromeFieldOnGround" : "ChromeFieldOnSurface"], container);
            }

            var where = onBar ? "on the bar" : "in the panel";

            var typed = Ceiling(t => Contrast(theme.Tokens(t)["Text"], Fill(t)));
            Assert.Equal(100, typed);

            var placeholder = Ceiling(t => Contrast(theme.Tokens(t)["TextDim"], Fill(t)));
            Assert.True(
                placeholder > chrome,
                $"{id}: the placeholder {where} fails at {placeholder}%, the chrome at {chrome}%");

            // §10.7 draws focus as a brush swap on a border whose thickness
            // never changes — the alternative reflows the command bar every time
            // the caret lands — so the ring is one pixel of Volt and the fill
            // under it is what decides whether it reads. Held to the same bar as
            // everything else here: not the thing that fails first.
            //
            // It is not a tighter bar than the ring already lives under. Opaque,
            // the ring on the bar's field reads slightly WORSE than on the bare
            // bar, because that field is a step UP from the bar (Surface on
            // Ground) — which is the palette as shipped and has nothing to do
            // with transparency. Past a few percent it reverses and stays
            // reversed, because the field is then the darker of the two.
            var ring = Ceiling(t => Contrast(theme.Tokens(t)["Volt"], Fill(t)));
            Assert.True(
                ring > chrome,
                $"{id}: the focus ring {where} fails at {ring}%, the chrome at {chrome}%");
        }
    }

    /// <summary>The pane's field as it composites, at a slider position, with
    /// the reach setting in — which is the only state in which any of this is a
    /// question.</summary>
    private static Color Field(HoardTheme theme, double transparency, Color backdrop)
        => Over(theme.Tokens(transparency, wallTranslucent: true)["PaneGround"], backdrop);

    /// <summary>The last whole percent at which <paramref name="measure"/> still
    /// clears AA. Walked, for <c>AaCeiling</c>'s reason: the inks and the alpha
    /// move on different ramps, so the ratio is not monotone in any form worth
    /// inverting.</summary>
    private static int Ceiling(Func<double, double> measure)
    {
        var last = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            if (measure(percent / 100.0) < 4.5)
            {
                return last;
            }

            last = percent;
        }

        return 100;
    }

    private static double WorstChromeContrast(HoardTheme theme, double transparency, Color backdrop)
    {
        var t = theme.Tokens(transparency);
        var rail = Over(t["ChromeSurface"], backdrop);
        var row = Over(t["ChromeRaised"], rail);
        var bar = Over(t["ChromeGround"], backdrop);
        var ink = t["TextDim"];

        return Math.Min(Contrast(ink, rail), Math.Min(Contrast(ink, row), Contrast(ink, bar)));
    }

    private static Color Over(Color ink, Color backdrop)
    {
        var a = ink.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round((ink.R * a) + (backdrop.R * (1 - a))),
            (byte)Math.Round((ink.G * a) + (backdrop.G * (1 - a))),
            (byte)Math.Round((ink.B * a) + (backdrop.B * (1 - a))));
    }

    private static Color Backsolve(Color composite, Color ink, double alpha) => Color.FromRgb(
        (byte)Math.Clamp(Math.Round((composite.R - (alpha * ink.R)) / (1 - alpha)), 0, 255),
        (byte)Math.Clamp(Math.Round((composite.G - (alpha * ink.G)) / (1 - alpha)), 0, 255),
        (byte)Math.Clamp(Math.Round((composite.B - (alpha * ink.B)) / (1 - alpha)), 0, 255));

    private static double Channel(byte c)
    {
        var v = c / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Color c)
        => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return la > lb
            ? (la + 0.05) / (lb + 0.05)
            : (lb + 0.05) / (la + 0.05);
    }

    private static double Hue(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        if (d == 0)
        {
            return 0;
        }

        var h = max == r ? ((g - b) / d) % 6
            : max == g ? ((b - r) / d) + 2
            : ((r - g) / d) + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static double HueGap(Color a, Color b)
    {
        var gap = Math.Abs(Hue(a) - Hue(b)) % 360;
        return gap > 180 ? 360 - gap : gap;
    }
}
