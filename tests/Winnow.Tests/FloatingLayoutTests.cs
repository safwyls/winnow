using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.Themes;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Floating layout (design-system.md §15) palette assertions.
/// </summary>
public class FloatingLayoutTests
{
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    private static readonly Color DarkDesktop = Color.FromRgb(0x20, 0x1F, 0x1E);

    public static TheoryData<string> ThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var theme in WinnowThemes.All)
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
    /// The caption is the ground in the floating layout, not the rail.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_caption_is_the_ground(string id)
    {
        var theme = WinnowThemes.ById(id);

        var solid = theme.Tokens(transparency: 0, layout: WinnowLayout.Floating);

        // At SOLID the caption and the ground are literally one painted field:
        // same ink, same alpha, no seam anywhere in the first inch of the window.
        Assert.Equal(theme.Well, solid["CaptionFill"]);
        Assert.Equal(theme.Well, solid["ShellGround"]);
        Assert.Equal(solid["CaptionFill"], solid["ShellGround"]);

        // PAST SOLID THE CAPTION PAINTS NOTHING AT ALL, and the ground behind
        // it is what is seen. That is stronger than the claim this test used to
        // make. It used to assert that the caption carried the ground's ink at
        // the CHROME's alpha and that the gaps, carrying no wordmark, opened
        // further — one field at SOLID and a field with brighter slots cut in it
        // everywhere else, which §15.7 recorded as an honest cost. There are no
        // slots: the caption and every gap are not two surfaces that agree, they
        // are one surface.
        foreach (var transparency in Range())
        {
            var t = theme.Tokens(transparency, layout: WinnowLayout.Floating);
            var caption = t["CaptionFill"];

            if (transparency <= 0)
            {
                Assert.Equal(t["ShellGround"], caption);
                continue;
            }

            Assert.Equal(0, caption.A);

            // Which is to say: over ANY backdrop the caption composites to
            // exactly the gap beside it. Asserted rather than argued, because
            // "same ink and same alpha" is a claim about pixels.
            foreach (var backdrop in new[] { White, DarkDesktop, Black })
            {
                Assert.Equal(
                    Over(t["ShellGround"], backdrop),
                    Over(caption, Over(t["ShellGround"], backdrop)));
            }
        }

        // And it is still not the brightest thing in the window at SOLID, which
        // is what §9's rule actually asks. Past SOLID over a bright wallpaper it
        // IS — the ground is the most open surface there is, so the caption and
        // the gaps are the brightest band in the window together. §15.7 conceded
        // that for the gaps; the caption joins them, and what it costs is
        // measured on the slider's own AA mark rather than hidden.
        Assert.True(
            Luminance(solid["CaptionFill"]) < Luminance(solid["ChromeSurface"]),
            $"{id}: the caption is not below the chrome at SOLID");

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
        var theme = WinnowThemes.ById(id);
        var t = theme.Tokens(transparency: 0, layout: WinnowLayout.Floating);

        Assert.True(
            Luminance(t["ShellGround"]) < Luminance(t["WallGround"]),
            $"{id}: the gaps are not below the art field");
        Assert.True(
            Luminance(t["WallGround"]) < Luminance(t["ChromeSurface"]),
            $"{id}: the art field is not below the chrome");
    }

    /// <summary>
    /// A pane composites over the window's ground exactly once.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_pane_composites_over_the_ground_exactly_once(string id)
    {
        var theme = WinnowThemes.ById(id);

        foreach (var transparency in Range())
        {
            foreach (var layout in WinnowLayouts.All)
            {
                var t = theme.Tokens(transparency, wallTranslucent: true, layout);
                var ground = 1 - (t["ShellGround"].A / 255.0);

                if (transparency == 0)
                {
                    Assert.Equal(255, t["ShellGround"].A);
                    Assert.Equal(0, ground);
                    continue;
                }

                foreach (var pane in new[] { "ChromeSurface", "WallGround", "PaneGround" })
                {
                    var admitted = ground * (1 - (t[pane].A / 255.0));

                    // One coat: the product of exactly two layers, and never
                    // more than the ground itself lets in.
                    Assert.True(
                        admitted <= ground + 0.001,
                        $"{id}: {pane} admits more than the ground under {layout} at {transparency:P0}");

                    // Past the ink ramp it is the wall's own linear share, which
                    // is what the early ramp buys. Bytes, so both quantisations
                    // ride on the product.
                    if (transparency >= 0.25)
                    {
                        Assert.True(
                            Math.Abs(admitted - ((1 - WinnowTheme.MinWallAlpha) * transparency)) <= 0.006,
                            $"{id}: {pane} admits {admitted:P1} under {layout} at {transparency:P0}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The layout moves only the ground and the caption; everything else is
    /// identical across both layouts.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_layout_moves_only_the_ground_and_the_caption(string id)
    {
        var theme = WinnowThemes.ById(id);

        // TWO tokens, down from four. The command bar's fill is gone entirely and
        // its field's ink stopped being layout-dependent, because a field steps
        // from its container and the container is the library pane in both
        // layouts now. The special case was not deleted — it stopped existing.
        var moved = new[] { "ShellGround", "CaptionFill" };

        foreach (var transparency in Range())
        {
            foreach (var wall in new[] { false, true })
            {
                var flush = theme.Tokens(transparency, wall, WinnowLayout.Flush);
                var floating = theme.Tokens(transparency, wall, WinnowLayout.Floating);

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
        var theme = WinnowThemes.ById(id);

        foreach (var transparency in Range())
        {
            foreach (var layout in WinnowLayouts.All)
            {
                var t = theme.Tokens(transparency, wallTranslucent: true, layout);
                Assert.Equal(theme.Ground, t["TileGround"]);
            }
        }
    }

    /// <summary>
    /// A field admits the wall's share in either layout.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_admits_the_walls_share_in_either_layout(string id)
    {
        var theme = WinnowThemes.ById(id);

        foreach (var layout in WinnowLayouts.All)
        {
            var t = theme.Tokens(transparency: 1, wallTranslucent: true, layout);

            // THREE terms now, not two, and the third is the window's ground.
            // A pane is painted on it and a pane's own alpha is derived assuming
            // so, which is the identity reaching one level further out than it
            // used to: ground, pane, field.
            var throughPaneField = (1 - (t["ShellGround"].A / 255.0))
                * (1 - (t["PaneGround"].A / 255.0))
                * (1 - (t["ChromeFieldOnGround"].A / 255.0));

            // Absolute tolerance, not decimal places: both alphas are bytes, so
            // the product carries two quantisations and a rounding boundary is
            // not a failure. The same 0.006 ThemeContrastTests holds the identity
            // to.
            Assert.True(
                Math.Abs((1 - WinnowTheme.MinWallAlpha) - throughPaneField) <= 0.006,
                $"{id}: the search box admits {throughPaneField:P1} under {layout}, the wall {1 - WinnowTheme.MinWallAlpha:P1}");
        }
    }

    /// <summary>
    /// A field is one step above the pane in either layout.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void A_field_is_one_step_above_the_pane_in_either_layout(string id)
    {
        var theme = WinnowThemes.ById(id);

        foreach (var layout in WinnowLayouts.All)
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
    /// The AA mark holds in whichever layout is up, taken across both.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void The_aa_mark_holds_in_whichever_layout_is_up(string id)
    {
        var theme = WinnowThemes.ById(id);
        var ceiling = Colorimetry.AaCeiling(theme);

        for (var percent = 0; percent <= ceiling; percent++)
        {
            var transparency = percent / 100.0;

            foreach (var layout in WinnowLayouts.All)
            {
                var t = theme.Tokens(transparency, layout: layout);
                var shell = Over(t["ShellGround"], White);
                var caption = Over(t["CaptionFill"], shell);
                var rail = Over(t["ChromeSurface"], shell);

                Assert.True(
                    Contrast(t["TextDim"], caption) >= 4.5,
                    $"{id}: the caption is under AA under {layout} at {percent}%, inside the mark at {ceiling}%");
                Assert.True(
                    Contrast(t["TextDim"], rail) >= 4.5,
                    $"{id}: the rail is under AA under {layout} at {percent}%, inside the mark at {ceiling}%");
                Assert.True(
                    Contrast(t["TextDim"], Over(t["ChromeRaised"], rail)) >= 4.5,
                    $"{id}: a selected row is under AA under {layout} at {percent}%");
            }
        }

        // And the floating caption is the surface that sets it — the flush one
        // holds far past here, so the mark is floating's number in every theme.
        var flushCeiling = 0;
        for (var percent = 0; percent <= 100; percent++)
        {
            var t = theme.Tokens(percent / 100.0, layout: WinnowLayout.Flush);
            var shell = Over(t["ShellGround"], White);
            if (Contrast(t["TextDim"], Over(t["CaptionFill"], shell)) < 4.5)
            {
                break;
            }

            flushCeiling = percent;
        }

        Assert.True(
            flushCeiling > ceiling,
            $"{id}: the flush caption fails at {flushCeiling}%, not past the mark at {ceiling}%");
    }

    [Fact]
    public void An_unknown_stored_layout_reads_as_unset()
    {
        // A preference written by a later version must not stop the app, and the
        // safe answer is the arrangement every measurement was taken against.
        Assert.Equal(WinnowLayout.Flush, WinnowLayouts.ById("flush"));
        Assert.Equal(WinnowLayout.Floating, WinnowLayouts.ById("floating"));
        Assert.Equal(WinnowLayouts.Default, WinnowLayouts.ById("islands"));
        Assert.Equal(WinnowLayouts.Default, WinnowLayouts.ById("tiles-2029"));
        Assert.Equal(WinnowLayouts.Default, WinnowLayouts.ById(null));
        Assert.Equal(WinnowLayout.Flush, WinnowLayouts.Default);
        Assert.Equal("floating", WinnowLayouts.Id(WinnowLayout.Floating));
        Assert.Equal("flush", WinnowLayouts.Id(WinnowLayout.Flush));
    }

    /// <summary>The preference makes the round trip it is stored for.</summary>
    [Fact]
    public async Task A_stored_layout_comes_back()
    {
        var settings = new StubSettings { [ThemeService.LayoutSettingKey] = "floating" };
        var service = new ThemeService(settings);

        await service.LoadAsync();

        Assert.Equal(WinnowLayout.Floating, service.Layout);
        Assert.True(service.IsFloating);
        Assert.Empty(settings.Writes);
    }

    [Fact]
    public async Task Choosing_a_layout_writes_it_once()
    {
        var settings = new StubSettings();
        var service = new ThemeService(settings);
        await service.LoadAsync();

        service.SetLayout(WinnowLayout.Floating);
        await service.PendingSave;
        service.SetLayout(WinnowLayout.Floating);
        await service.PendingSave;

        Assert.Equal([(ThemeService.LayoutSettingKey, "floating")], settings.Writes);
    }

    /// <summary>
    /// The session seal covers the layout too.
    /// </summary>
    [Fact]
    public async Task An_overridden_session_never_writes_a_layout()
    {
        var settings = new StubSettings();
        var service = new ThemeService(settings);

        service.OverrideForSession(
            WinnowThemes.Tungsten, 60, WinnowBackdrop.Mica, wallTranslucent: true, layout: WinnowLayout.Floating);

        Assert.Equal(WinnowLayout.Floating, service.Layout);

        service.SetLayout(WinnowLayout.Flush);
        service.SetLayout(WinnowLayout.Floating);
        await service.PendingSave;

        Assert.Empty(settings.Writes);
        Assert.Equal(WinnowLayout.Floating, service.Layout);
    }

    private sealed class StubSettings : Winnow.Core.Repositories.ISettingsRepository
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
