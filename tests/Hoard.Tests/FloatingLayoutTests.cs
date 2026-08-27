using Avalonia.Media;
using Hoard.App.Services;
using Hoard.App.Themes;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The floating layout (design-system.md §15), held to the same standard §14's
/// translucency is: what it claims about the palette is asserted here rather
/// than looked at once in a screenshot.
///
/// <para>Four claims carry the layout, and each of them is a test below. The
/// ground is CONTINUOUS — the caption, the command bar, the cut bar and every
/// gap are one ink at one alpha, which is what makes the panes read as lying on
/// a field rather than as three tones meeting. It is the DEEPEST tone in the
/// window, so a gap is a recess and never a lit slot. It never composites
/// TWICE, which is the only reason §14.3's measured chrome alphas survive a
/// layout that puts a painted ground behind translucent panes. And it moves
/// NOTHING that was measured — the rail, the wall, the tiles and the panes are
/// bit-for-bit what the flush layout produces, so the Appearance screen's AA
/// ceiling is still the truth under both.</para>
/// </summary>
public class FloatingLayoutTests
{
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    public static TheoryData<string> ThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var theme in HoardThemes.All)
        {
            data.Add(theme.Id);
        }

        return data;
    }

    private static IEnumerable<double> Range()
    {
        for (var percent = 0; percent <= 100; percent++)
        {
            yield return percent / 100.0;
        }
    }

    /// <summary>
    /// The collision this layout had to resolve, asserted rather than argued.
    ///
    /// <para>§9's amendment made the caption take the rail's ink so the chrome
    /// would be one continuous bracket instead of two tones meeting at a corner.
    /// Floating dissolves the corner — the caption and the rail no longer touch —
    /// and moves the continuity onto the GROUND: the caption, the command bar,
    /// the cut bar and the gaps are one field, and the panes lie on it. So the
    /// test that used to say "the caption is the rail" says "the caption is the
    /// ground" here, and it is the same claim about seams pointed at the surface
    /// that now carries it.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_caption_is_the_ground(string id)
    {
        var theme = HoardThemes.ById(id);

        var solid = theme.Tokens(transparency: 0, layout: HoardLayout.Floating);

        // At SOLID the four are literally one painted field: same ink, same
        // alpha, no seam anywhere in the first inch of the window.
        Assert.Equal(theme.Well, solid["CaptionFill"]);
        Assert.Equal(theme.Well, solid["ChromeGround"]);
        Assert.Equal(theme.Well, solid["ShellGround"]);
        Assert.Equal(solid["CaptionFill"], solid["ChromeGround"]);

        // Past SOLID the caption and the bars stay level with each other. The
        // GAPS do not, and that is stated in §15 rather than asserted away here:
        // a gap carries nothing and opens the whole way, which is exactly the
        // effect the layout is for.
        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency, layout: HoardLayout.Floating);
            Assert.Equal(t["CaptionFill"], t["ChromeGround"]);
        }
    }

    /// <summary>
    /// A gap is a recess, never a lit slot. The window's ground is below the
    /// field the covers hang in, which is below the chrome panes on it — three
    /// tones ranked in one direction, so a gap reads as depth in every theme
    /// rather than as a missing pane in some of them.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_gaps_are_the_deepest_tone_in_the_window(string id)
    {
        var theme = HoardThemes.ById(id);
        var t = theme.Tokens(transparency: 0, layout: HoardLayout.Floating);

        Assert.True(
            Luminance(t["ShellGround"]) < Luminance(t["WallGround"]),
            $"{id}: the gaps are not below the art field");
        Assert.True(
            Luminance(t["WallGround"]) < Luminance(t["ChromeSurface"]),
            $"{id}: the art field is not below the chrome");
    }

    /// <summary>
    /// The one construction fact the whole layout rests on.
    ///
    /// <para>A painted ground behind translucent panes would stack: a rail at
    /// §14.3's measured 0.30 over a shell at 0.30 composites to 0.51, and every
    /// contrast figure the Appearance screen prints would be describing a window
    /// that is not on screen. <c>ShellGround</c> is a step and not a ramp for
    /// exactly this reason, and floating is the layout that turns that from a
    /// tidiness into a load-bearing property — so it is asserted at every
    /// position rather than at the ends.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_pane_never_composites_over_a_painted_ground(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency, layout: HoardLayout.Floating);

            if (transparency == 0)
            {
                Assert.Equal(255, t["ShellGround"].A);
            }
            else
            {
                Assert.Equal(0, t["ShellGround"].A);
            }
        }
    }

    /// <summary>
    /// Everything §14 measured is untouched, so the Appearance screen's numbers
    /// are still true under both layouts.
    ///
    /// <para>This is what makes the setting cheap: the AA ceiling is computed off
    /// a selected rail row, the polarity floor off the wall against a dormant
    /// capsule, and the dormancy ramp off <c>TileGround</c> — and the layout
    /// moves none of the three. It moves the ground the panes lie on, the
    /// caption, the bars and one field fill, and nothing else at all.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_layout_moves_only_the_ground_the_caption_and_the_bars(string id)
    {
        var theme = HoardThemes.ById(id);
        var moved = new[] { "ShellGround", "CaptionFill", "ChromeGround", "ChromeFieldOnGround" };

        foreach (var transparency in Range())
        {
            foreach (var wall in new[] { false, true })
            {
                var flush = theme.Tokens(transparency, wall, HoardLayout.Flush);
                var floating = theme.Tokens(transparency, wall, HoardLayout.Floating);

                Assert.Equal(flush.Count, floating.Count);
                foreach (var (key, colour) in flush)
                {
                    if (moved.Contains(key))
                    {
                        continue;
                    }

                    Assert.True(
                        colour == floating[key],
                        $"{id}: {key} moved with the layout at {transparency:0.00} (wall {wall})");
                }
            }
        }
    }

    /// <summary>
    /// The tile keeps its own ground in both layouts, which is §14.4 and is
    /// construction rather than measurement: the dormancy ramp is two layers that
    /// are only opaque together, and the floating layout adds gaps a
    /// part-decoded tile could otherwise show the desktop through.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_tile_ground_is_opaque_in_both_layouts(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var transparency in Range())
        {
            foreach (var layout in HoardLayouts.All)
            {
                var t = theme.Tokens(transparency, wallTranslucent: true, layout);
                Assert.Equal(theme.Ground, t["TileGround"]);
            }
        }
    }

    /// <summary>
    /// §14.7's forced identity, re-derived in the layout that changed what a
    /// field sits on.
    ///
    /// <para>A field admits half of what the surface around it admits, which lands
    /// it on the cover wall's own share of the desktop:
    /// <c>(1 − barAlpha)·(1 − fieldAlpha) = 1 − wallAlpha</c>. The worry the
    /// layout raises is that the command bar moved onto the window ground and the
    /// stack lost a layer — and the answer is that the command bar was never
    /// inside a pane in either layout. It is painted directly on the shell, which
    /// contributes nothing past SOLID, so the sum has the same two terms it always
    /// had. Asserted rather than reasoned, in both layouts, past the ink ramp
    /// where the factor has settled.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_still_admits_the_walls_share_when_the_bar_is_the_ground(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var layout in HoardLayouts.All)
        {
            var t = theme.Tokens(transparency: 1, layout: layout);

            var throughBar = (1 - (t["ChromeGround"].A / 255.0))
                * (1 - (t["ChromeFieldOnGround"].A / 255.0));

            // Absolute tolerance, not decimal places: both alphas are bytes, so
            // the product carries two quantisations and a rounding boundary is
            // not a failure. The same 0.006 ThemeContrastTests holds the flush
            // identity to.
            Assert.True(
                Math.Abs((1 - HoardTheme.MinWallAlpha) - throughBar) <= 0.006,
                $"{id}: a field on the {layout} bar admits {throughBar:P1}, the wall {1 - HoardTheme.MinWallAlpha:P1}");
        }
    }

    /// <summary>
    /// A field is a step from its container, and the step keeps its size when the
    /// container moves down the family.
    ///
    /// <para>Flush, the command bar is Ground and the search box is Surface — one
    /// step up. Floating, the bar is Well, so the box is Ground: one step up
    /// again, rather than the two-step jump that leaving it at Surface would have
    /// produced. A field is CUT INTO its bar, not raised out of it.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_is_one_step_above_the_bar_it_sits_on(string id)
    {
        var theme = HoardThemes.ById(id);

        var flush = theme.Tokens(transparency: 0, layout: HoardLayout.Flush);
        Assert.Equal(theme.Ground, flush["ChromeGround"]);
        Assert.Equal(theme.Surface, flush["ChromeFieldOnGround"]);

        var floating = theme.Tokens(transparency: 0, layout: HoardLayout.Floating);
        Assert.Equal(theme.Well, floating["ChromeGround"]);
        Assert.Equal(theme.Ground, floating["ChromeFieldOnGround"]);

        // And in both, up rather than down: an elevation step that inverted would
        // be a field that reads as a shadow.
        Assert.True(Luminance(flush["ChromeFieldOnGround"]) > Luminance(flush["ChromeGround"]));
        Assert.True(Luminance(floating["ChromeFieldOnGround"]) > Luminance(floating["ChromeGround"]));
    }

    /// <summary>
    /// The chrome strips get BRIGHTER ink relief, never dimmer, from the layout.
    ///
    /// <para>The caption and the command bar carry the wordmark, the caption
    /// glyphs and the command bar's labels, and floating repaints both from Well
    /// instead of Surface and Ground. Well is the darkest tone in the palette, so
    /// over the brightest backdrop a wallpaper can be, every ink on those strips
    /// lands on a deeper ground than it did — the layout cannot be the thing that
    /// takes a label under §8's floor.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_layout_never_costs_the_chrome_strips_contrast(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var transparency in Range())
        {
            var flush = theme.Tokens(transparency, layout: HoardLayout.Flush);
            var floating = theme.Tokens(transparency, layout: HoardLayout.Floating);

            foreach (var key in new[] { "CaptionFill", "ChromeGround" })
            {
                var ink = flush["TextDim"];
                var before = Contrast(ink, Over(flush[key], White));
                var after = Contrast(ink, Over(floating[key], White));

                Assert.True(
                    after >= before - 0.001,
                    $"{id}: {key} lost contrast to the layout at {transparency:0.00} ({before:0.00} to {after:0.00})");
            }
        }
    }

    [Fact]
    public void An_unknown_stored_layout_reads_as_unset()
    {
        // A preference written by a later version must not stop the app, and the
        // safe answer is the arrangement every measurement was taken against.
        Assert.Equal(HoardLayout.Flush, HoardLayouts.ById("flush"));
        Assert.Equal(HoardLayout.Floating, HoardLayouts.ById("floating"));
        Assert.Equal(HoardLayouts.Default, HoardLayouts.ById("islands"));
        Assert.Equal(HoardLayouts.Default, HoardLayouts.ById("tiles-2029"));
        Assert.Equal(HoardLayouts.Default, HoardLayouts.ById(null));
        Assert.Equal(HoardLayout.Flush, HoardLayouts.Default);
        Assert.Equal("floating", HoardLayouts.Id(HoardLayout.Floating));
        Assert.Equal("flush", HoardLayouts.Id(HoardLayout.Flush));
    }

    /// <summary>The preference makes the round trip it is stored for.</summary>
    [Fact]
    public async Task A_stored_layout_comes_back()
    {
        var settings = new StubSettings { [ThemeService.LayoutSettingKey] = "floating" };
        var service = new ThemeService(settings);

        await service.LoadAsync();

        Assert.Equal(HoardLayout.Floating, service.Layout);
        Assert.True(service.IsFloating);
        Assert.Empty(settings.Writes);
    }

    [Fact]
    public async Task Choosing_a_layout_writes_it_once()
    {
        var settings = new StubSettings();
        var service = new ThemeService(settings);
        await service.LoadAsync();

        service.SetLayout(HoardLayout.Floating);
        await service.PendingSave;
        service.SetLayout(HoardLayout.Floating);
        await service.PendingSave;

        Assert.Equal([(ThemeService.LayoutSettingKey, "floating")], settings.Writes);
    }

    /// <summary>
    /// The session seal covers the layout too.
    ///
    /// <para>The capture flags exist so every arrangement can be photographed
    /// without leaving a row behind in somebody's real library, and the seal is
    /// for the whole session rather than for the moment the override is applied —
    /// a promise that holds until the first click is not a promise. A fifth
    /// decision arriving on the Appearance screen is exactly the shape of the
    /// regression that rule was written for.</para>
    /// </summary>
    [Fact]
    public async Task An_overridden_session_never_writes_a_layout()
    {
        var settings = new StubSettings();
        var service = new ThemeService(settings);

        service.OverrideForSession(
            HoardThemes.Tungsten, 60, HoardBackdrop.Mica, wallTranslucent: true, layout: HoardLayout.Floating);

        Assert.Equal(HoardLayout.Floating, service.Layout);

        service.SetLayout(HoardLayout.Flush);
        service.SetLayout(HoardLayout.Floating);
        await service.PendingSave;

        Assert.Empty(settings.Writes);
        Assert.Equal(HoardLayout.Floating, service.Layout);
    }

    private sealed class StubSettings : Hoard.Core.Repositories.ISettingsRepository
    {
        private readonly Dictionary<string, string> _stored = [];

        public List<(string Key, string Value)> Writes { get; } = [];

        public string this[string key]
        {
            set => _stored[key] = value;
        }

        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_stored.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            Writes.Add((key, value));
            _stored[key] = value;
            return Task.CompletedTask;
        }
    }

    private static Color Over(Color ink, Color backdrop)
    {
        var a = ink.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round((ink.R * a) + (backdrop.R * (1 - a))),
            (byte)Math.Round((ink.G * a) + (backdrop.G * (1 - a))),
            (byte)Math.Round((ink.B * a) + (backdrop.B * (1 - a))));
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
