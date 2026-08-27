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
    /// The wall keeps its ground at every setting, and the tile keeps its own
    /// under the dormancy cross-fade. §5.4 composites two bitmap layers by
    /// opacity; between the first decoding and the second, a dimmed tile is a
    /// partly transparent tile, and on a translucent window that means the
    /// desktop showing through the ramp's floor.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_wall_and_the_tile_stay_opaque(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency);
            Assert.Equal(255, t["WallGround"].A);
            Assert.Equal(255, t["TileGround"].A);

            // Popovers: a flyout is its own popup root and never receives the
            // window's backdrop, so a translucent fill there would sample the
            // application instead of the desktop.
            Assert.Equal(255, t["SurfaceRaised"].A);

            Assert.Equal(theme.Ground, t["WallGround"]);
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

        // Everything a user could do on the Appearance screen.
        service.SelectTheme(HoardThemes.BoxArt);
        service.SetTransparency(12);
        service.SetTransparency(0);
        await service.PendingSave;

        Assert.Empty(settings.Writes);
        Assert.Same(HoardThemes.BoxArt, service.Theme);
        Assert.Equal(0, service.Transparency);
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
