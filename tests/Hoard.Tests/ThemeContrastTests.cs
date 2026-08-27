using Avalonia.Media;
using Hoard.App.Services;
using Hoard.App.Themes;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// design-system.md §8's accessibility floor, held for every theme in both
/// transparency states — and §2's rule that Flare has exactly one job.
///
/// <para><b>Why this exists as a test and not as a note in a design file.</b>
/// §13 gap 7 recorded a translucent surface measuring 3.1:1 and concluded that
/// translucency could not reach a reading surface. The measurement was right;
/// what it actually proved was narrower — that an ink chosen for an opaque
/// ground cannot have alpha subtracted from it. Transparency mode therefore has
/// its own token set, and a claim like that is only worth making if something
/// re-checks it every build. A theme added later, or an alpha nudged by eye
/// until it "looked about right", fails here rather than in somebody's rail.</para>
///
/// <para><b>Three backdrops, and the first one is the argument.</b> WHITE is the
/// ceiling: no wallpaper, and no compositor, can hand the window anything
/// brighter, so a number that holds against white holds everywhere without
/// assuming anything about how Windows composes Mica. MICA is the composite
/// measured on a real desktop and recorded in tokens.axaml — what actually
/// happens. BLACK is the other end. Light ink on a translucent dark surface gets
/// WORSE as the backdrop brightens, so white is the case that binds and the
/// other two are there to prove the range was walked rather than assumed.</para>
/// </summary>
public class ThemeContrastTests
{
    /// <summary>The brightest thing any backdrop can be.</summary>
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    /// <summary>
    /// The backdrop Avalonia's dark Mica actually delivered, back-solved from
    /// the composite tokens.axaml recorded on a real desktop: <c>Well</c> at 85%
    /// came out <c>#061112</c>, so the backdrop behind it was this. Windows tints
    /// dark Mica hard toward #202020 by design, which is why the real case sits
    /// so far below the white ceiling.
    /// </summary>
    private static readonly Color Mica = Backsolve(
        composite: Color.FromRgb(0x06, 0x11, 0x12),
        ink: Color.FromRgb(0x05, 0x0D, 0x0E),
        alpha: 0.85);

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
        var t = theme.Tokens(translucent: false);

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
    /// §9: the caption is the window's UNLIT LIP — at or below Ground, never
    /// above it. Every platform puts a lighter strip above a darker body, and
    /// inverting that is what makes the cover wall the first thing on screen
    /// with any light in it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Caption_is_darker_than_ground(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(translucent: false);

        Assert.True(
            Luminance(t["Well"]) < Luminance(t["Ground"]),
            $"{id}: caption is not below Ground");
        Assert.True(
            Luminance(t["Ground"]) < Luminance(t["Surface"]),
            $"{id}: Surface is not above Ground");
        Assert.True(
            Luminance(t["Surface"]) < Luminance(t["SurfaceRaised"]),
            $"{id}: SurfaceRaised is not above Surface");
        Assert.True(
            Luminance(t["SurfaceRaised"]) < Luminance(t["SurfaceHigh"]),
            $"{id}: SurfaceHigh is not above SurfaceRaised");
    }

    /// <summary>
    /// The claim the transparency mode is sold on: it lands ABOVE the opaque
    /// numbers on reading matter, even against the brightest backdrop a desktop
    /// can produce, because it has its own inks rather than the opaque ones with
    /// alpha taken off.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Translucent_chrome_clears_AA_against_every_backdrop(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(translucent: true);

        foreach (var backdrop in new[] { White, Mica, Black })
        {
            var rail = Over(t["ChromeSurface"], backdrop);
            var bar = Over(t["ChromeGround"], backdrop);
            var caption = Over(t["CaptionFill"], backdrop);

            // A selected or hovered rail row: the 10% Text veil over the rail.
            var row = Over(t["ChromeRaised"], rail);

            Assert.True(Contrast(t["TextDim"], rail) >= 4.5, $"{id} TextDim/rail on {backdrop}");
            Assert.True(Contrast(t["TextDim"], row) >= 4.5, $"{id} TextDim/selected row on {backdrop}");
            Assert.True(Contrast(t["TextDim"], bar) >= 4.5, $"{id} TextDim/command bar on {backdrop}");
            Assert.True(Contrast(t["TextDim"], caption) >= 4.5, $"{id} TextDim/caption on {backdrop}");
            Assert.True(Contrast(t["Text"], rail) >= 7.0, $"{id} Text/rail on {backdrop}");
            Assert.True(Contrast(t["Text"], row) >= 7.0, $"{id} Text/selected row on {backdrop}");
            Assert.True(Contrast(t["Volt"], rail) >= 4.5, $"{id} Volt/rail on {backdrop}");

            // Elevation stays elevation. A darker ink over an already
            // translucent rail composites DOWNWARDS, which would make a selected
            // row darker than its neighbours; the veil is what stops that.
            Assert.True(
                Luminance(row) > Luminance(rail),
                $"{id}: selected rail row is not above the rail on {backdrop}");
        }
    }

    /// <summary>
    /// Transparency mode is a trade, and this is the size of it: the dim ink
    /// comes out AHEAD of where it sits on a solid rail, against the worst
    /// backdrop there is. Asserted rather than described, because the whole
    /// reason the previous attempt shipped a 36px strip was a number nobody had
    /// re-run after the inks changed.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Translucent_metadata_ink_beats_its_opaque_self(string id)
    {
        var theme = HoardThemes.ById(id);
        var opaque = theme.Tokens(translucent: false);
        var clear = theme.Tokens(translucent: true);

        var solidRail = Contrast(opaque["TextDim"], opaque["Surface"]);
        var worstRail = Contrast(clear["TextDim"], Over(clear["ChromeSurface"], White));

        Assert.True(
            worstRail >= solidRail,
            $"{id}: translucent rail {worstRail:0.00}:1 is below the solid rail's {solidRail:0.00}:1");
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
    /// The wall keeps its ground in both states, and the tile keeps its own
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

        foreach (var translucent in new[] { false, true })
        {
            var t = theme.Tokens(translucent);
            Assert.Equal(255, t["WallGround"].A);
            Assert.Equal(255, t["TileGround"].A);
            Assert.Equal(255, t["SurfaceRaised"].A);
            Assert.Equal(theme.Ground, t["WallGround"]);
        }
    }

    /// <summary>
    /// Transparency has to be visible to be worth having. The previous attempt's
    /// failure was not that it was illegible — it was that a 36px strip at 85%
    /// is not something anyone can see.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Transparency_is_actually_transparent(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(translucent: true);

        foreach (var key in new[] { "ChromeSurface", "ChromeGround", "CaptionFill" })
        {
            Assert.True(t[key].A < 250, $"{id}: {key} is effectively opaque");
            Assert.True(t[key].A >= 200, $"{id}: {key} lets too much through to read on");
        }

        Assert.Equal(0, t["ShellGround"].A);
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

        ThemeService.ApplyTo(resources, HoardThemes.Phosphor, translucent: false);

        Assert.Same(surface, resources["Surface"]);
        Assert.Equal(HoardThemes.Phosphor.Surface, surface.Color);
        Assert.Equal(HoardThemes.Phosphor.Flare, ((SolidColorBrush)resources["Flare"]!).Color);
        Assert.False(resources.ContainsKey("Volt"), "a key the dictionary lacks was added");
    }

    [Fact]
    public void An_unknown_stored_theme_reads_as_unset()
    {
        // A preference file written by a later version must not stop the app.
        Assert.Same(HoardThemes.Default, HoardThemes.ById("a-theme-from-the-future"));
        Assert.Same(HoardThemes.Default, HoardThemes.ById(null));
        Assert.Same(HoardThemes.Hoard, HoardThemes.Default);
    }

    // ── The sums ────────────────────────────────────────────────────────────
    // WCAG 2.x relative luminance and contrast ratio, and an sRGB source-over
    // composite — which is what the GPU does, so it is what the window shows.

    private static Color Over(Color ink, Color backdrop)
    {
        var a = ink.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(ink.R * a + backdrop.R * (1 - a)),
            (byte)Math.Round(ink.G * a + backdrop.G * (1 - a)),
            (byte)Math.Round(ink.B * a + backdrop.B * (1 - a)));
    }

    private static Color Backsolve(Color composite, Color ink, double alpha) => Color.FromRgb(
        (byte)Math.Clamp(Math.Round((composite.R - alpha * ink.R) / (1 - alpha)), 0, 255),
        (byte)Math.Clamp(Math.Round((composite.G - alpha * ink.G) / (1 - alpha)), 0, 255),
        (byte)Math.Clamp(Math.Round((composite.B - alpha * ink.B) / (1 - alpha)), 0, 255));

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
