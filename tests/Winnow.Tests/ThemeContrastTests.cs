using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.Themes;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Accessibility floor (§8) and Flare-discipline (§2) across every theme and
/// transparency level, with re-implemented sums.
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
        foreach (var theme in WinnowThemes.All)
        {
            data.Add(theme.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Opaque_palette_clears_the_section_8_floor(string id)
    {
        var theme = WinnowThemes.ById(id);
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
    /// The caption takes the rail's colour at every slider position (§9).
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_caption_is_the_rail(string id)
    {
        var theme = WinnowThemes.ById(id);

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
        var theme = WinnowThemes.ById(id);
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
        var theme = WinnowThemes.ById(id);
        var t = theme.Tokens(transparency: 0);

        Assert.Equal(theme.Surface, t["ChromeSurface"]);
        Assert.Equal(theme.Ground, t["PaneGround"]);
        Assert.Equal(theme.Ground, t["ShellGround"]);
        Assert.Equal(theme.Ground, t["ChromeFieldOnSurface"]);
        Assert.Equal(theme.Surface, t["ChromeFieldOnGround"]);
        Assert.Equal(theme.SurfaceRaised, t["ChromeRaised"]);
        Assert.Equal(theme.TextDim, t["TextDim"]);
        Assert.Equal(theme.TextFaint, t["TextFaint"]);

        foreach (var key in new[] { "ChromeSurface", "PaneGround", "ChromeRaised", "CaptionFill", "ShellGround" })
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
        var theme = WinnowThemes.ById(id);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            foreach (var backdrop in new[] { White, DarkDesktop, Black })
            {
                var rail = Over(t["ChromeSurface"], Shell(theme, transparency, backdrop));
                var row = Over(t["ChromeRaised"], rail);
                Assert.True(
                    Luminance(row) > Luminance(rail),
                    $"{id}: selected row is not above the rail at {transparency:P0} on {backdrop}");
            }
        }
    }

    /// <summary>
    /// The AA floor covers a real part of the slider range (at least 20%).
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_worst_case_floor_covers_a_real_part_of_the_range(string id)
    {
        var theme = WinnowThemes.ById(id);
        var ceiling = Colorimetry.AaCeiling(theme);

        Assert.True(ceiling >= 20, $"{id}: AA survives only to {ceiling}%");
        Assert.True(ceiling < 100, $"{id}: the white ceiling never bites, so the mark is a lie");

        for (var percent = 0; percent <= ceiling; percent++)
        {
            var t = theme.Tokens(percent / 100.0);
            var shell = Shell(theme, percent / 100.0, White);
            var rail = Over(t["ChromeSurface"], shell);
            var row = Over(t["ChromeRaised"], rail);

            // The CAPTION, which is what the ceiling is now made of: it sits on
            // the window's ground, so it is the most open reading surface there
            // is. Measured in the floating layout, where the ground is what shows
            // through it; flush paints it at the pane tier and it never binds.
            var floating = theme.Tokens(percent / 100.0, layout: WinnowLayout.Floating);
            var caption = Over(
                floating["CaptionFill"],
                Shell(theme, percent / 100.0, White, WinnowLayout.Floating));

            // The command bar is inside the library pane now, so it is measured
            // where every other pane ink is: on the pane's own ground, with the
            // reach setting in, which is the only state in which the question
            // arises. It is not what sets the ceiling - S14.7 puts the pane at
            // 59-73% against the chrome's 26-31% - but the ceiling is the range
            // the app promises is safe, so every reading surface inside it has
            // to hold, the bar's DENSITY label included.
            var bar = Field(theme, percent / 100.0, White);

            Assert.True(Contrast(t["TextDim"], caption) >= 4.5, $"{id} TextDim/caption at {percent}% on white");
            Assert.True(Contrast(t["TextDim"], rail) >= 4.5, $"{id} TextDim/rail at {percent}% on white");
            Assert.True(Contrast(t["TextDim"], row) >= 4.5, $"{id} TextDim/selected row at {percent}% on white");
            Assert.True(Contrast(t["TextDim"], bar) >= 4.5, $"{id} TextDim/command bar at {percent}% on white");
        }
    }

    /// <summary>
    /// Over a dark desktop the metadata ink beats its opaque self at every slider
    /// position.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void On_a_real_desktop_the_metadata_ink_beats_its_opaque_self(string id)
    {
        var theme = WinnowThemes.ById(id);
        var solidRail = Contrast(
            theme.Tokens(0)["TextDim"],
            theme.Tokens(0)["Surface"]);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            var measured = Contrast(
                t["TextDim"],
                Over(t["ChromeSurface"], Shell(theme, transparency, DarkDesktop)));

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
        var theme = WinnowThemes.ById(id);

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
    /// The tile ground stays opaque in both reach states, whatever the field does.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_tile_stays_opaque_whatever_the_field_does(string id)
    {
        var theme = WinnowThemes.ById(id);

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
    /// either way, and never more open than the panes beside it — which, since
    /// the tiers collapsed, means never more open than exactly equal to them.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_field_opens_only_when_asked_and_never_past_the_chrome(string id)
    {
        var theme = WinnowThemes.ById(id);

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
            // recess — the art hangs BELOW the chrome, in every theme. With one
            // pane tier this is equality when the reach is in, and the recess is
            // carried by the INK rather than by the alpha: Surface over Ground,
            // both unwalked, at the same alpha, on the same ground.
            Assert.True(
                wall.A >= open["ChromeSurface"].A,
                $"{id}: the field is more open than the chrome at {transparency:P0}");

            Assert.True(
                Luminance(Over(open["ChromeSurface"], Shell(theme, transparency, White)))
                    > Luminance(Over(wall, Shell(theme, transparency, White))),
                $"{id}: the chrome sank to or below the art field at {transparency:P0}");

            Assert.True(wall.A <= previous, $"{id}: the field's alpha rose at {transparency:P0}");
            previous = wall.A;

            // The colour is the theme's own ground throughout; only the alpha
            // moves. The field has no ink ramp of its own — nothing reads on it.
            Assert.Equal(theme.Ground.R, wall.R);
            Assert.Equal(theme.Ground.G, wall.G);
            Assert.Equal(theme.Ground.B, wall.B);
        }

        // And at the far end a pane paints MinPaneAlpha and ADMITS
        // 1 − MinWallAlpha, which are two different numbers because a pane is
        // painted on the window's ground rather than straight on the desktop.
        // The identity is asserted rather than either constant, so retuning
        // either end of the slider cannot leave a stale figure behind — this is
        // the same rule MinFieldAlpha is held to, one level out.
        var far = theme.Tokens(1, wallTranslucent: true);
        Assert.Equal(
            Math.Round(255 * WinnowTheme.MinPaneAlpha),
            (double)far["WallGround"].A);
        Assert.Equal(
            1 - WinnowTheme.MinWallAlpha,
            (1 - WinnowTheme.MinShellAlpha) * (1 - WinnowTheme.MinPaneAlpha),
            precision: 9);

        // Every pane is at ONE tier: the rail, the filter panel and the art
        // field paint the same alpha at every position, and the settings screens
        // beside them are the same token again.
        foreach (var transparency in Range())
        {
            var open = theme.Tokens(transparency, wallTranslucent: true);
            Assert.Equal(open["WallGround"].A, open["ChromeSurface"].A);
            Assert.Equal(open["WallGround"], open["PaneGround"]);
        }

        // And what actually reaches the eye through a pane is linear in the
        // slider past the first quarter, at exactly the wall's rate. That is
        // what the pane's alpha riding the INK ramp buys: on the alpha's own
        // ramp the product of the two layers would be quadratic and a pane would
        // be half as open as it should be through the middle of the track.
        for (var percent = 25; percent <= 100; percent++)
        {
            var open = theme.Tokens(percent / 100.0, wallTranslucent: true);
            var admitted = (1 - (open["ShellGround"].A / 255.0))
                * (1 - (open["WallGround"].A / 255.0));

            Assert.True(
                Math.Abs(admitted - ((1 - WinnowTheme.MinWallAlpha) * (percent / 100.0))) <= 0.006,
                $"{id}: a pane admits {admitted:P1} at {percent}%, not the wall's linear share");
        }
    }

    /// <summary>
    /// The open field stays under the art at least as far as the labels hold.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_open_field_stays_under_the_art_at_least_as_far_as_the_labels_hold(string id)
    {
        var theme = WinnowThemes.ById(id);

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

        // Through the WINDOW'S GROUND, which is the term that did not exist
        // when this was written. A pane is painted on that ground, and its own
        // alpha is derived assuming so; measuring the token against white alone
        // would report a field lighter than the one on screen and would put this
        // ceiling five points low.
        var polarity = 0;
        while (polarity < 100
            && Luminance(Over(
                theme.Tokens((polarity + 1) / 100.0, wallTranslucent: true)["WallGround"],
                Shell(theme, (polarity + 1) / 100.0, White))) <= Luminance(dormant))
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
                theme.Tokens(transparency, wallTranslucent: true)["WallGround"],
                Shell(theme, transparency, DarkDesktop));
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
        var theme = WinnowThemes.ById(id);
        var t = theme.Tokens(transparency: 1);

        // ASKED OF WHAT THE SURFACE ADMITS, not of the alpha it paints, because
        // a surface is painted on the window's ground and the two alphas
        // multiply. A pane paints 0.59 and admits 0.35; the alpha on its own
        // says nothing.
        var shell = 1 - (t["ShellGround"].A / 255.0);

        Assert.True(
            shell >= 0.80,
            $"{id}: the window's ground admits only {shell:P0} at the far end");

        foreach (var key in new[] { "ChromeSurface", "CaptionFill" })
        {
            var admits = shell * (1 - (t[key].A / 255.0));
            Assert.True(
                admits >= 0.30,
                $"{id}: {key} at the far end admits only {admits:P0}");
        }

        // The caption in the FLOATING layout is not merely as open as the ground
        // — it IS the ground, painting nothing of its own, which is the strongest
        // form of §9's "same ink and same alpha" available.
        var floating = theme.Tokens(transparency: 1, layout: WinnowLayout.Floating);
        Assert.Equal(0, floating["CaptionFill"].A);

        // The command bar is NOT on that list any more, and its absence is the
        // change rather than an omission. It is inside the library pane (S15.1,
        // revised), so it opens exactly as far as the pane does and no further:
        // opaque while the art field is solid, at the wall's half reach when it
        // is not. A bar that kept the chrome's alpha inside a solid island would
        // be S14.7's "half a translucent window" arriving one level in - a
        // see-through strip glued to the top of a solid card.
        Assert.Equal(255, t["PaneGround"].A);
        Assert.Equal(
            theme.Tokens(transparency: 1, wallTranslucent: true)["WallGround"],
            theme.Tokens(transparency: 1, wallTranslucent: true)["PaneGround"]);

        // ShellGround IS A RAMP NOW, where it used to paint nothing at all past
        // slider zero. The step existed because two stacked alphas multiply and a
        // pane on a proportional ground would admit a quadratic; that is answered
        // by the pane's alpha finishing on the ink ramp rather than by the ground
        // painting nothing. What the step was really protecting — that a pane
        // composites over the ground exactly ONCE — is asserted where it belongs,
        // in FloatingLayoutTests.
        Assert.Equal(Math.Round(255 * WinnowTheme.MinShellAlpha), (double)t["ShellGround"].A);

        // And the alpha only ever comes off — no position on the track is denser
        // than the one before it, on either tier.
        byte previousGround = 255;
        byte previous = 255;
        foreach (var transparency in Range())
        {
            var tokens = theme.Tokens(transparency);
            var ground = tokens["ShellGround"].A;
            var alpha = tokens["ChromeSurface"].A;
            Assert.True(ground <= previousGround, $"{id}: the ground's alpha rose at {transparency:P0}");
            Assert.True(alpha <= previous, $"{id}: alpha rose at {transparency:P0}");
            previousGround = ground;
            previous = alpha;
        }
    }

    /// <summary>Applying a theme writes existing brushes in place, never adds keys.</summary>
    [Fact]
    public void Applying_a_theme_writes_the_brushes_in_place()
    {
        var resources = new Avalonia.Controls.ResourceDictionary
        {
            ["Surface"] = new SolidColorBrush(Colors.Black),
            ["Flare"] = new SolidColorBrush(Colors.Black),
        };
        var surface = (SolidColorBrush)resources["Surface"]!;

        ThemeService.ApplyTo(resources, WinnowThemes.BoxArt, transparency: 0);

        Assert.Same(surface, resources["Surface"]);
        Assert.Equal(WinnowThemes.BoxArt.Surface, surface.Color);
        Assert.Equal(WinnowThemes.BoxArt.Flare, ((SolidColorBrush)resources["Flare"]!).Color);
        Assert.False(resources.ContainsKey("Volt"), "a key the dictionary lacks was added");
    }

    [Fact]
    public void An_unknown_stored_theme_reads_as_unset()
    {
        // A preference file written by a later version must not stop the app —
        // and neither must one written by an earlier one that named a theme
        // which has since been retired.
        Assert.Same(WinnowThemes.Default, WinnowThemes.ById("a-theme-from-the-future"));
        Assert.Same(WinnowThemes.Default, WinnowThemes.ById("cold-storage"));
        Assert.Same(WinnowThemes.Default, WinnowThemes.ById("phosphor"));
        Assert.Same(WinnowThemes.Default, WinnowThemes.ById(null));
        Assert.Same(WinnowThemes.Winnow, WinnowThemes.Default);
    }

    /// <summary>An overridden session never writes a preference.</summary>
    [Fact]
    public async Task An_overridden_session_never_writes_a_preference()
    {
        var settings = new RecordingSettings();
        var service = new ThemeService(settings);

        service.OverrideForSession(WinnowThemes.Tungsten, 60);

        // Everything a user could do on the Appearance screen — all four
        // decisions, not just the two the flags existed for when it was written.
        service.SelectTheme(WinnowThemes.BoxArt);
        service.SetTransparency(12);
        service.SetTransparency(0);
        service.SelectBackdrop(WinnowBackdrop.Mica);
        service.SetWallTranslucent(true);
        service.SetWallTranslucent(false);
        await service.PendingSave;

        Assert.Empty(settings.Writes);
        Assert.Same(WinnowThemes.BoxArt, service.Theme);
        Assert.Equal(0, service.Transparency);
        Assert.Equal(WinnowBackdrop.Mica, service.Backdrop);
        Assert.False(service.WallTranslucent);
    }

    /// <summary>A substituted backdrop is distinguishable from a refused one.</summary>
    [Fact]
    public void A_substituted_backdrop_is_not_the_same_answer_as_a_refused_one()
    {
        var service = new ThemeService();
        service.SetTransparency(40);
        service.SelectBackdrop(WinnowBackdrop.Mica);

        // Refused outright.
        service.SetActiveBackdrop(WinnowBackdrop.None);
        Assert.False(service.BackdropAvailable);
        Assert.False(service.BackdropSubstituted);
        Assert.Equal(0, service.ActiveTransparency);
        Assert.False(service.ActiveWallTranslucency);

        // Composited, but not the material that was asked for.
        service.SetActiveBackdrop(WinnowBackdrop.Acrylic);
        Assert.True(service.BackdropAvailable);
        Assert.True(service.BackdropSubstituted);
        Assert.Equal(WinnowBackdrop.Mica, service.Backdrop);
        Assert.Equal(0.40, service.ActiveTransparency, precision: 6);

        // And honoured.
        service.SetActiveBackdrop(WinnowBackdrop.Mica);
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
        service.SetActiveBackdrop(WinnowBackdrop.Acrylic);
        Assert.True(service.BackdropAvailable);

        service.SelectBackdrop(WinnowBackdrop.Mica);

        Assert.Equal(WinnowBackdrop.None, service.ActiveBackdrop);
        Assert.False(service.BackdropAvailable);
        Assert.False(service.BackdropSubstituted);
    }

    /// <summary>The field opens only when the desktop is actually arriving.</summary>
    [Fact]
    public void The_field_opens_only_when_the_desktop_is_actually_arriving()
    {
        var service = new ThemeService();
        service.SetWallTranslucent(true);

        // Asked for, but the slider is at zero.
        Assert.False(service.ActiveWallTranslucency);

        service.SetTransparency(50);
        Assert.False(service.ActiveWallTranslucency);

        service.SetActiveBackdrop(WinnowBackdrop.Acrylic);
        Assert.True(service.ActiveWallTranslucency);

        service.SetActiveBackdrop(WinnowBackdrop.None);
        Assert.False(service.ActiveWallTranslucency);
    }

    [Fact]
    public void An_unknown_stored_backdrop_reads_as_unset()
    {
        // Same reasoning as the theme id: a preference written by a later
        // version must not stop the app. "none" lands here too — it is a report
        // about what the platform did, never something a user picked.
        Assert.Equal(WinnowBackdrop.Acrylic, WinnowBackdrops.ById("acrylic"));
        Assert.Equal(WinnowBackdrop.Mica, WinnowBackdrops.ById("mica"));
        Assert.Equal(WinnowBackdrops.Default, WinnowBackdrops.ById("none"));
        Assert.Equal(WinnowBackdrops.Default, WinnowBackdrops.ById("blur-behind-2029"));
        Assert.Equal(WinnowBackdrops.Default, WinnowBackdrops.ById(null));
        Assert.Equal(WinnowBackdrop.Acrylic, WinnowBackdrops.Default);
    }

    private sealed class RecordingSettings : Winnow.Core.Repositories.ISettingsRepository
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

    /// <summary>The old boolean transparency preference migrates to a slider value.</summary>
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

    /// <summary>A pane's ink clears AA further than the chrome's does.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_pane_carries_its_ink_further_than_the_chrome_does(string id)
    {
        var theme = WinnowThemes.ById(id);

        var chrome = Ceiling(t => WorstChromeContrast(theme, t, White));

        // The inks that actually sit on PaneGround: a pane header and the empty
        // state are Text and TextDim, and the list view's store and idle columns
        // are TextDim. TextFaint never touches it — every use of it in
        // AppearanceView is inside an opaque Border.card.
        var text = Ceiling(t => Contrast(theme.Tokens(t)["Text"], Field(theme, t, White)));
        var dim = Ceiling(t => Contrast(theme.Tokens(t)["TextDim"], Field(theme, t, White)));

        // And the rail, which is a pane now and is measured as one. It fails
        // later than the ceiling too — 40 / 54 / 47 / 41 against 30 / 31 / 31 / 31
        // — because the thing that sets the ceiling is the caption on the ground,
        // not anything inside a pane.
        var rail = Ceiling(t => Math.Min(
            Contrast(
                theme.Tokens(t)["TextDim"],
                Over(theme.Tokens(t)["ChromeSurface"], Shell(theme, t, White))),
            Contrast(
                theme.Tokens(t)["TextDim"],
                Over(
                    theme.Tokens(t)["ChromeRaised"],
                    Over(theme.Tokens(t)["ChromeSurface"], Shell(theme, t, White))))));

        // And a SELECTED list row, which is the pane's lightest reading surface
        // for the same reason a selected rail row is the chrome's.
        var row = Ceiling(t => Contrast(
            theme.Tokens(t)["TextDim"],
            Over(theme.Tokens(t)["ChromeRaised"], Field(theme, t, White))));

        Assert.True(rail > chrome, $"{id}: the rail fails at {rail}%, the window at {chrome}%");
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

    /// <summary>A pane equals the wall in alpha and ink at every position.</summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_pane_is_the_field(string id)
    {
        var theme = WinnowThemes.ById(id);

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
    /// A field admits what the art field admits, with its alpha forced by its
    /// container.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_admits_what_the_art_field_admits(string id)
    {
        var theme = WinnowThemes.ById(id);

        // ONE FORMULA, APPLIED AT EVERY LEVEL OF THE STACK, and written as the
        // product rather than as its answer so that retuning either end of the
        // slider cannot leave a stale number behind anywhere in it.
        //
        // The ground is the only free quantity — it has nothing painted between
        // it and the desktop. A pane's alpha is forced by it; a field's alpha is
        // forced by the pane it is cut into. That is three applications of
        // alpha = 1 − (1 − MinWallAlpha) / (1 − containerAlpha).
        Assert.Equal(
            1 - WinnowTheme.MinWallAlpha,
            (1 - WinnowTheme.MinShellAlpha) * (1 - WinnowTheme.MinPaneAlpha),
            precision: 9);
        Assert.Equal(
            1 - WinnowTheme.MinWallAlpha,
            (1 - WinnowTheme.MinWallAlpha) * (1 - WinnowTheme.MinFieldAlpha),
            precision: 9);
        Assert.Equal(
            1 - WinnowTheme.MinWallAlpha,
            (1 - WinnowTheme.MinWallAlpha) * (1 - WinnowTheme.MinPaneFieldAlpha),
            precision: 9);

        // The two field constants coincide for the first time, and that is the
        // tier collapse showing up in the arithmetic rather than a duplication:
        // the filter panel used to be chrome, so its container admitted 0.70 and
        // its field spent the other half. The panel is a pane now, both
        // containers admit the same thing, and both fields solve to nothing.
        Assert.Equal(WinnowTheme.MinFieldAlpha, WinnowTheme.MinPaneFieldAlpha);
        Assert.Equal(0, WinnowTheme.MinFieldAlpha);

        byte previousOnGround = 255;
        byte previousOnSurface = 255;
        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency, wallTranslucent: true);
            var onGround = t["ChromeFieldOnGround"];
            var onSurface = t["ChromeFieldOnSurface"];

            // Neither alpha ever rises. They are no longer the same number — the
            // two fields sit on two different surfaces — so they are walked
            // separately.
            Assert.True(onGround.A <= previousOnGround, $"{id}: a pane field's alpha rose at {transparency:P0}");
            Assert.True(onSurface.A <= previousOnSurface, $"{id}: a panel field's alpha rose at {transparency:P0}");
            previousOnGround = onGround.A;
            previousOnSurface = onSurface.A;

            // Never more open than the surface around it, and never more open
            // than the art field — a field is a step CUT INTO a surface, not a
            // hole through it. Asked of what the desktop actually contributes
            // THROUGH the field rather than of the field's own alpha, because a
            // field is a child of its container and the two alphas multiply.
            // Alphas are stored as bytes, so every share here is quantised to
            // 1/255 and the product of two of them carries both errors.
            const double Rounding = 0.005;
            var ground = 1 - (t["ShellGround"].A / 255.0);
            var wallShare = ground * (1 - (t["WallGround"].A / 255.0));
            var paneShare = ground * (1 - (t["PaneGround"].A / 255.0));
            var panelShare = ground * (1 - (t["ChromeSurface"].A / 255.0));
            var paneFieldShare = paneShare * (1 - (onGround.A / 255.0));
            var panelFieldShare = panelShare * (1 - (onSurface.A / 255.0));

            Assert.True(
                paneFieldShare <= paneShare + Rounding,
                $"{id}: the search box admits more than the pane at {transparency:P0}");
            Assert.True(
                panelFieldShare <= panelShare + Rounding,
                $"{id}: a panel field admits more than the panel at {transparency:P0}");
            Assert.True(
                paneFieldShare <= wallShare + Rounding,
                $"{id}: the search box admits more than the art field at {transparency:P0}");
            Assert.True(
                panelFieldShare <= wallShare + Rounding,
                $"{id}: a panel field admits more than the art field at {transparency:P0}");

            // The ink is one step from the container in the neutral family, and
            // NEITHER FIELD WALKS ANY MORE. §14.3's ink ramp was a CHROME
            // compensation — the chrome opened to 0.70 and paid for it with a
            // darker ink, so a field on the chrome had to walk with it or the
            // step between them would change size across the slider. There is no
            // chrome: the filter panel is a pane, panes are their own opaque
            // token at an alpha at every position, and a field cut into an
            // unwalked ground must be unwalked too.
            Assert.Equal(
                (theme.Surface.R, theme.Surface.G, theme.Surface.B),
                (onGround.R, onGround.G, onGround.B));
            Assert.Equal(
                (theme.Ground.R, theme.Ground.G, theme.Ground.B),
                (onSurface.R, onSurface.G, onSurface.B));
            Assert.Equal(
                (theme.Ground.R, theme.Ground.G, theme.Ground.B),
                (t["PaneGround"].R, t["PaneGround"].G, t["PaneGround"].B));
            Assert.Equal(
                (theme.Surface.R, theme.Surface.G, theme.Surface.B),
                (t["ChromeSurface"].R, t["ChromeSurface"].G, t["ChromeSurface"].B));
        }

        // Slider zero is bit-for-bit the opaque palette, here as everywhere: the
        // step a field cuts is untouched by any of this at SOLID.
        var solid = theme.Tokens(transparency: 0);
        Assert.Equal(theme.Surface, solid["ChromeFieldOnGround"]);
        Assert.Equal(theme.Ground, solid["ChromeFieldOnSurface"]);

        // And the point of it: what the desktop contributes THROUGH a field is
        // the wall's own share, at every position past the first quarter — not
        // merely at the end of the track. That is what the early ramp buys, and
        // it is the whole claim the Appearance screen makes about fields. Both
        // fields land on that one number by two different routes, which is the
        // strongest form of the claim available.
        foreach (var transparency in Range())
        {
            if (transparency < 0.25)
            {
                continue;
            }

            var t = theme.Tokens(transparency, wallTranslucent: true);
            var ground = 1 - (t["ShellGround"].A / 255.0);
            var throughPaneField = ground
                * (1 - (t["PaneGround"].A / 255.0))
                * (1 - (t["ChromeFieldOnGround"].A / 255.0));
            var throughPanelField = ground
                * (1 - (t["ChromeSurface"].A / 255.0))
                * (1 - (t["ChromeFieldOnSurface"].A / 255.0));
            var wall = ground * (1 - (t["WallGround"].A / 255.0));

            // Absolute tolerance rather than decimal places: every alpha here is
            // a byte, so a product of two of them carries both quantisations and
            // a rounding boundary is not a failure.
            Assert.True(
                Math.Abs(wall - throughPaneField) <= 0.006,
                $"{id}: the search box admits {throughPaneField:P1}, the wall {wall:P1}, at {transparency:P0}");
            Assert.True(
                Math.Abs(wall - throughPanelField) <= 0.006,
                $"{id}: a panel field admits {throughPanelField:P1}, the wall {wall:P1}, at {transparency:P0}");
        }

        // With the art field SOLID the pane under the search box is solid, the
        // identity is vacuous — nothing is admitted for the field to match — and
        // the field stays opaque rather than fading out for no gain at all. That
        // is the one thing separating it from the panel's fields, which sit on
        // chrome and follow the slider whatever the wall is doing.
        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            Assert.Equal(255, t["ChromeFieldOnGround"].A);
            Assert.Equal(theme.Surface, t["ChromeFieldOnGround"]);

            if (transparency >= 0.25)
            {
                Assert.True(
                    t["ChromeFieldOnSurface"].A < 255,
                    $"{id}: a panel field stopped following the slider at {transparency:P0}");
            }
        }
    }

    /// <summary>
    /// A field stays legible further than the chrome around it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_stays_legible_further_than_the_chrome_around_it(string id)
    {
        var theme = WinnowThemes.ById(id);
        var chrome = Ceiling(t => WorstChromeContrast(theme, t, White));

        foreach (var onBar in new[] { true, false })
        {
            Color Fill(double t)
            {
                var tok = theme.Tokens(t, wallTranslucent: true);
                var container = onBar
                    ? Field(theme, t, White)
                    : Over(tok["ChromeSurface"], Shell(theme, t, White));
                return Over(tok[onBar ? "ChromeFieldOnGround" : "ChromeFieldOnSurface"], container);
            }

            var where = onBar ? "on the bar" : "in the panel";

            // WHAT YOU ARE TYPING, and the one figure the two-tier restructure
            // cost rather than bought. It used to hold AA across the whole
            // slider on both fields. It still does in the LIBRARY pane, whose
            // ground is Ground; in the FILTER PANEL it now runs out at 96 and 97
            // percent on Winnow and Box art, because the panel's field paints no
            // fill at all — the identity forces it to zero, since the panel is a
            // pane and a pane already admits the wall's share — so the ink under
            // the caret is the panel's own Surface rather than a Ground step cut
            // into it, and Surface is the lighter of the two.
            //
            // Four points at the very top of the track, on a pure white
            // wallpaper, three times past the mark the Appearance screen draws.
            // It is recorded rather than engineered away: the only fill that
            // would buy it back is one that makes the field less open than the
            // pane around it, which is the bolted-shut patch §14.7 refused.
            var typed = Ceiling(t => Contrast(theme.Tokens(t, wallTranslucent: true)["Text"], Fill(t)));
            Assert.True(
                onBar ? typed == 100 : typed >= 96,
                $"{id}: what you are typing {where} fails at {typed}%");

            var placeholder = Ceiling(t => Contrast(theme.Tokens(t, wallTranslucent: true)["TextDim"], Fill(t)));
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
            var ring = Ceiling(t => Contrast(theme.Tokens(t, wallTranslucent: true)["Volt"], Fill(t)));
            Assert.True(
                ring > chrome,
                $"{id}: the focus ring {where} fails at {ring}%, the chrome at {chrome}%");
        }
    }

    /// <summary>The window's ground as it composites over a backdrop.</summary>
    private static Color Shell(
        WinnowTheme theme, double transparency, Color backdrop, WinnowLayout layout = WinnowLayouts.Default)
        => Over(theme.Tokens(transparency, layout: layout)["ShellGround"], backdrop);

    /// <summary>The pane's field as it composites, at a slider position, with
    /// the reach setting in — which is the only state in which any of this is a
    /// question.</summary>
    private static Color Field(WinnowTheme theme, double transparency, Color backdrop)
        => Over(
            theme.Tokens(transparency, wallTranslucent: true)["PaneGround"],
            Shell(theme, transparency, backdrop));

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

    /// <summary>Worst chrome contrast at a slider position, across both layouts.</summary>
    private static double WorstChromeContrast(WinnowTheme theme, double transparency, Color backdrop)
    {
        var worst = double.MaxValue;

        foreach (var layout in WinnowLayouts.All)
        {
            var t = theme.Tokens(transparency, layout: layout);
            var ink = t["TextDim"];
            var shell = Shell(theme, transparency, backdrop, layout);

            // The caption. Floating it paints nothing and this IS the ground;
            // flush it is the rail's fill on the ground. Over(alpha 0, x) is x,
            // so one line covers both.
            worst = Math.Min(worst, Contrast(ink, Over(t["CaptionFill"], shell)));

            var rail = Over(t["ChromeSurface"], shell);
            worst = Math.Min(worst, Contrast(ink, rail));
            worst = Math.Min(worst, Contrast(ink, Over(t["ChromeRaised"], rail)));
        }

        return worst;
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
