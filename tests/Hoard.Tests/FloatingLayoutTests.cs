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
/// ground is CONTINUOUS — the caption and every gap are one ink at one alpha,
/// which is what makes the panes read as lying on a field rather than as three
/// tones meeting. It is the DEEPEST tone in the window, so a gap is a recess and
/// never a lit slot. It never composites TWICE, which is the only reason §14.3's
/// measured chrome alphas survive a layout that puts a painted ground behind
/// translucent panes. And it moves NOTHING that was measured — the rail, the
/// wall, the tiles and the panes are bit-for-bit what the flush layout produces,
/// so the Appearance screen's AA ceiling is still the truth under both.</para>
///
/// <para><b>The command bar and the cut bar used to be a fifth thing on that
/// ground and are not any more.</b> They are inside the library pane in both
/// layouts (§15.1, revised), which is why this file no longer asserts anything
/// about them: the layout does not reach them, and the two tests that used to
/// prove it does now prove it does not.</para>
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
    /// and moves the continuity onto the GROUND: the caption and the gaps are one
    /// field, and the panes lie on it. So the test that used to say "the caption
    /// is the rail" says "the caption is the ground" here, and it is the same
    /// claim about seams pointed at the surface that now carries it.</para>
    ///
    /// <para><b>The caption is the ONLY strip on that field now.</b> The command
    /// bar and the cut bar took this same ink at this same alpha, and caption
    /// plus command bar flush together read as one tall undifferentiated block of
    /// chrome in the first inch of the window — which is what sent those controls
    /// inside the library pane. The claim the ink makes is unchanged and there is
    /// simply less of it: a lip, which is all §9 ever asked the caption to
    /// be.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_caption_is_the_ground(string id)
    {
        var theme = HoardThemes.ById(id);

        var solid = theme.Tokens(transparency: 0, layout: HoardLayout.Floating);

        // At SOLID the caption and the ground are literally one painted field:
        // same ink, same alpha, no seam anywhere in the first inch of the window.
        Assert.Equal(theme.Well, solid["CaptionFill"]);
        Assert.Equal(theme.Well, solid["ShellGround"]);
        Assert.Equal(solid["CaptionFill"], solid["ShellGround"]);

        // Past SOLID the caption keeps the ground's INK and the chrome's alpha —
        // it carries a wordmark and three glyphs, so it pays for them. The GAPS
        // open all the way, and that is stated in §15 rather than asserted away
        // here: a gap carries nothing, which is exactly the effect the layout is
        // for.
        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency, layout: HoardLayout.Floating);
            var caption = t["CaptionFill"];
            var chrome = t["ChromeSurface"];

            Assert.Equal(chrome.A, caption.A);
            Assert.True(
                Luminance(caption) <= Luminance(chrome),
                $"{id}: the caption rose above the chrome at {transparency:P0}");
        }

        // And nothing else is on that field. The library pane's own bars carry no
        // fill at all in either layout, so there is no second strip for the
        // caption's ink to have to match.
        Assert.False(solid.ContainsKey("ChromeGround"));
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
    /// moves none of the three. It moves the ground the panes lie on and the
    /// caption, and nothing else at all.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_layout_moves_only_the_ground_and_the_caption(string id)
    {
        var theme = HoardThemes.ById(id);

        // TWO tokens, down from four. The command bar's fill is gone entirely and
        // its field's ink stopped being layout-dependent, because a field steps
        // from its container and the container is the library pane in both
        // layouts now. The special case was not deleted — it stopped existing.
        var moved = new[] { "ShellGround", "CaptionFill" };

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
    /// §14.7's forced identity, re-derived where a field actually sits — and the
    /// answer is that the layout has nothing to do with it.
    ///
    /// <para>The identity is
    /// <c>(1 − containerAlpha)·(1 − fieldAlpha) = 1 − wallAlpha</c>, and the
    /// container is whatever surface the field is painted on. When the command
    /// bar was a strip on the window ground that container was the bar, the sum
    /// had two terms, and the previous version of this test asserted them in both
    /// layouts. The bar is inside the library pane now, so the container is the
    /// PANE — which is already at the wall's share — and the field's own term
    /// solves to 1. Same identity, one fewer layer, and it comes out
    /// layout-independent because a pane is <c>Ground</c> in both.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_admits_the_walls_share_in_either_layout(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var layout in HoardLayouts.All)
        {
            var t = theme.Tokens(transparency: 1, wallTranslucent: true, layout);

            var throughPaneField = (1 - (t["PaneGround"].A / 255.0))
                * (1 - (t["ChromeFieldOnGround"].A / 255.0));

            // Absolute tolerance, not decimal places: both alphas are bytes, so
            // the product carries two quantisations and a rounding boundary is
            // not a failure. The same 0.006 ThemeContrastTests holds the identity
            // to.
            Assert.True(
                Math.Abs((1 - HoardTheme.MinWallAlpha) - throughPaneField) <= 0.006,
                $"{id}: the search box admits {throughPaneField:P1} under {layout}, the wall {1 - HoardTheme.MinWallAlpha:P1}");
        }
    }

    /// <summary>
    /// A field is a step CUT INTO the surface it sits on, and the layout does not
    /// move that surface any more.
    ///
    /// <para>Floating used to step the search box down to <c>Ground</c>, because
    /// its container had gone from the <c>Ground</c>-inked command bar to the
    /// <c>Well</c>-inked window ground and a two-step jump would have read as
    /// raised rather than cut. Its container is the library pane in both layouts
    /// now, and a pane is <c>Ground</c> in both, so the field is <c>Surface</c> in
    /// both — one step up, and the same one.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_is_one_step_above_the_pane_in_either_layout(string id)
    {
        var theme = HoardThemes.ById(id);

        foreach (var layout in HoardLayouts.All)
        {
            var t = theme.Tokens(transparency: 0, layout: layout);

            Assert.Equal(theme.Ground, t["PaneGround"]);
            Assert.Equal(theme.Surface, t["ChromeFieldOnGround"]);

            // Up rather than down: an elevation step that inverted would be a
            // field that reads as a shadow.
            Assert.True(
                Luminance(t["ChromeFieldOnGround"]) > Luminance(t["PaneGround"]),
                $"{id}: the field sank below the pane under {layout}");
        }
    }

    /// <summary>
    /// The chrome strips get BRIGHTER ink relief, never dimmer, from the layout.
    ///
    /// <para>The caption carries the wordmark and the caption glyphs, and
    /// floating repaints it from Well instead of Surface. Well is the darkest tone
    /// in the palette, so over the brightest backdrop a wallpaper can be, every
    /// ink on that strip lands on a deeper ground than it did — the layout cannot
    /// be the thing that takes a label under §8's floor. It is the only strip left
    /// to check: the command bar's labels are on the library pane in both
    /// layouts, so the layout does not reach them at all.</para>
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

            foreach (var key in new[] { "CaptionFill" })
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
