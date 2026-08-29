using Avalonia.Media;
using Winnow.App.Themes;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// JSON theme format: built-in round-trip, derivation fidelity, and safe
/// handling of malformed input.
/// </summary>
public class ThemeJsonTests
{
    public static TheoryData<string> BuiltInIds()
    {
        var data = new TheoryData<string>();
        foreach (var theme in WinnowThemes.All)
        {
            data.Add(theme.Id);
        }

        return data;
    }

    // ══ The forcing function ════════════════════════════════════════════════

    /// <summary>
    /// Export a built-in, read it back, and every one of the twenty-four
    /// colours has to be the same byte.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void Every_builtin_round_trips_through_the_format(string id)
    {
        var original = WinnowThemes.ById(id);

        var json = ThemeJson.Export(original);
        var (loaded, diagnostics) = ThemeJson.Parse($"{id}.json", json);

        Assert.NotNull(loaded);
        Assert.DoesNotContain(diagnostics, d => d.IsError);

        foreach (var (role, colour) in ColoursOf(original))
        {
            Assert.True(
                colour == ColoursOf(loaded)[role],
                $"{id}.{role}: C# has {ThemeJson.Hex(colour)}, JSON produced {ThemeJson.Hex(ColoursOf(loaded)[role])}");
        }
    }

    /// <summary>
    /// Every token matches at every slider position, both layouts, wall open and
    /// shut.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void Every_token_matches_at_every_slider_position(string id)
    {
        var original = WinnowThemes.ById(id);
        var (loaded, _) = ThemeJson.Parse($"{id}.json", ThemeJson.Export(original));
        Assert.NotNull(loaded);

        foreach (var wall in new[] { false, true })
        {
            foreach (var layout in WinnowLayouts.All)
            {
                for (var percent = 0; percent <= 100; percent += 5)
                {
                    var t = percent / 100.0;
                    var expected = original.Tokens(t, wall, layout);
                    var actual = loaded.Tokens(t, wall, layout);

                    Assert.Equal(expected.Count, actual.Count);
                    foreach (var (key, colour) in expected)
                    {
                        Assert.True(
                            actual[key] == colour,
                            $"{id} token {key} at {percent}% (wall {wall}, {layout}): expected {colour}, got {actual[key]}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// The export carries structural proportions (edge weight, seeds), not just
    /// hex colours.
    /// </summary>
    [Fact]
    public void Export_states_the_themes_own_structure()
    {
        Assert.Contains("\"edge\": 2.46", ThemeJson.Export(WinnowThemes.Nightshift));
        Assert.Contains("\"edge\": 1.38", ThemeJson.Export(WinnowThemes.Tungsten));

        // And the seeds are there under their own names, so the eight colours
        // that ARE the theme are the first thing an author sees.
        var winnow = ThemeJson.Export(WinnowThemes.Winnow);
        Assert.Contains("\"ground\": \"#0F1C1E\"", winnow);
        Assert.Contains("\"flare\": \"#FF4D93\"", winnow);
    }

    /// <summary>
    /// The derivation lands within 12 units of every hand-tuned built-in colour.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void The_derivation_lands_beside_every_builtin(string id)
    {
        var theme = WinnowThemes.ById(id);
        var shape = ThemeDerivation.Fit(theme);
        var derived = ThemeDerivation.Derive(ThemeDerivation.SeedsOf(theme), shape);
        var actual = ThemeDerivation.ActualDerivedFields(theme);

        foreach (var field in ThemeDerivation.DerivedFields)
        {
            var delta = Math.Max(
                Math.Abs(derived[field].R - actual[field].R),
                Math.Max(
                    Math.Abs(derived[field].G - actual[field].G),
                    Math.Abs(derived[field].B - actual[field].B)));

            Assert.True(
                delta <= 12,
                $"{id}.{field}: derived {ThemeJson.Hex(derived[field])} is {delta} away from the hand-tuned {ThemeJson.Hex(actual[field])}");
        }
    }

    /// <summary>
    /// The derivation reproduces structural ordering (neutral ramp, edge weight,
    /// ink levels).
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void The_derivation_reproduces_every_builtins_structure(string id)
    {
        var theme = WinnowThemes.ById(id);
        var shape = ThemeDerivation.Fit(theme);
        var derived = ThemeDerivation.Derive(ThemeDerivation.SeedsOf(theme), shape);

        var ramp = new[]
        {
            derived["Well"], theme.Ground, theme.Surface, derived["SurfaceRaised"], derived["SurfaceHigh"],
        };

        for (var i = 1; i < ramp.Length; i++)
        {
            Assert.True(
                Colorimetry.Luminance(ramp[i]) > Colorimetry.Luminance(ramp[i - 1]),
                $"{id}: derived neutral step {i} did not rise");
        }

        // The edge is stated as a ratio, so it comes back as one to within the
        // rounding the exported file carries.
        Assert.Equal(
            Colorimetry.Contrast(theme.Line, theme.Surface),
            Colorimetry.Contrast(derived["Line"], theme.Surface),
            tolerance: 0.02);

        // And the derived inks still clear §8's floor on the theme's own chrome,
        // which is the whole point of deriving them from it.
        Assert.True(Colorimetry.Contrast(derived["TextDim"], theme.Surface) >= Colorimetry.AaThreshold);
        Assert.True(Colorimetry.Contrast(derived["VoltInk"], theme.Volt) >= 7.0);
    }

    /// <summary>
    /// A theme that supplies nothing but its eight seeds has to come out
    /// coherent — that is the promise the seed/derived split is making.
    /// </summary>
    [Fact]
    public void Eight_seeds_alone_produce_a_theme_that_clears_the_floor()
    {
        var (theme, diagnostics) = ThemeJson.Parse("minimal.json", """
            {
              "schemaVersion": 1,
              "id": "minimal",
              "name": "Minimal",
              "reason": "Eight colours and nothing else.",
              "seeds": {
                "ground":  "#131018",
                "surface": "#1D1926",
                "text":    "#EFEAF5",
                "flare":   "#FF4D93",
                "volt":    "#A98CFF",
                "amber":   "#FFB63D",
                "azure":   "#57A8F0",
                "danger":  "#E04B45"
              }
            }
            """);

        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);

        var t = theme.Tokens(transparency: 0);
        Assert.True(Colorimetry.Contrast(t["TextDim"], t["Surface"]) >= Colorimetry.AaThreshold);
        Assert.True(Colorimetry.Contrast(t["TextDim"], t["Ground"]) >= Colorimetry.AaThreshold);
        Assert.True(Colorimetry.Contrast(t["Text"], t["Surface"]) >= 7.0);
        Assert.True(Colorimetry.Contrast(t["VoltInk"], t["Volt"]) >= 7.0);

        // The neutral ramp has to be a ramp: five distinct steps in order.
        var order = new[] { theme.Well, theme.Ground, theme.Surface, theme.SurfaceRaised, theme.SurfaceHigh };
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(
                Colorimetry.Luminance(order[i]) > Colorimetry.Luminance(order[i - 1]),
                $"neutral step {i} did not rise");
        }

        // And the chrome still admits the desktop for a usable part of the
        // slider, which is the number the Appearance screen reports.
        Assert.True(Colorimetry.AaCeiling(theme) >= 20);
    }

    // ══ schemaVersion, honoured from day one ════════════════════════════════

    [Fact]
    public void A_missing_schema_version_is_refused()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", """
            { "id": "x", "name": "X", "reason": "r", "seeds": {} }
            """);

        Assert.Null(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "schemaVersion");
        Assert.Equal(ThemeSeverity.Error, d.Severity);
        Assert.Contains("missing", d.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file from a build that does not exist yet is REFUSED, not read as best
    /// it can be — ROADMAP §6's <c>payload_version</c> trap, answered before it
    /// is one. Best-effort parsing of a version whose fields may have moved
    /// produces a theme the author did not write, in colours close enough to
    /// look deliberate.
    /// </summary>
    [Fact]
    public void A_future_schema_version_is_refused_and_says_why()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", """
            { "schemaVersion": 2, "id": "x", "name": "X", "reason": "r" }
            """);

        Assert.Null(theme);
        var d = Assert.Single(diagnostics);
        Assert.Equal("schemaVersion", d.Field);
        Assert.Contains("version 2", d.Message, StringComparison.Ordinal);
        Assert.Contains("Update Winnow", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_version_below_the_first_one_is_refused()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", """
            { "schemaVersion": 0, "id": "x", "name": "X", "reason": "r" }
            """);

        Assert.Null(theme);
        Assert.Contains(diagnostics, d => d.Field == "schemaVersion" && d.IsError);
    }

    // ══ Malformed input never reaches the UI as an exception ════════════════

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"just a string\"")]
    [InlineData("null")]
    [InlineData("{ \"schemaVersion\": \"one\" }")]
    [InlineData("{ \"schemaVersion\": 1, \"seeds\": 4 }")]
    [InlineData("{ \"schemaVersion\": 1, \"seeds\": { \"ground\": 12 } }")]
    [InlineData("{ \"schemaVersion\": 1, \"structure\": { \"edge\": \"wide\" } }")]
    public void Nothing_malformed_throws(string text)
    {
        var (theme, diagnostics) = ThemeJson.Parse("broken.json", text);

        Assert.Null(theme);
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal("broken.json", d.File));
        Assert.All(diagnostics, d => Assert.NotEqual(string.Empty, d.Message));
    }

    /// <summary>Within a version, a field this build does not know is a typo —
    /// and the alternative to saying so is a whole block silently doing nothing
    /// while the author waits to see its effect.</summary>
    [Fact]
    public void An_unknown_top_level_field_is_named()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", """
            { "schemaVersion": 1, "id": "x", "name": "X", "reason": "r", "strucutre": {} }
            """);

        Assert.Null(theme);
        var d = Assert.Single(diagnostics);
        Assert.Equal("strucutre", d.Field);
        Assert.Contains("schemaVersion, id, name", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_seed_names_the_seed()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", """
            {
              "schemaVersion": 1, "id": "x", "name": "X", "reason": "r",
              "seeds": {
                "ground": "#101010", "surface": "#202020", "text": "#EEEEEE",
                "flare": "#FF4D93", "volt": "#4DE8C2", "amber": "#FFB63D",
                "azure": "#57A8F0"
              }
            }
            """);

        Assert.Null(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "seeds.danger");
        Assert.Equal(ThemeSeverity.Error, d.Severity);
    }

    [Fact]
    public void A_colour_with_an_alpha_is_refused_with_the_reason()
    {
        var (_, diagnostics) = ThemeJson.Parse("x.json", Seeded("""
            "ground": "#CC0F1C1E",
            """));

        var d = Assert.Single(diagnostics, x => x.Field == "seeds.ground");
        Assert.Contains("alpha", d.Message, StringComparison.Ordinal);
        Assert.Contains("transparency slider", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_colour_name_is_refused_rather_than_guessed_at()
    {
        var (_, diagnostics) = ThemeJson.Parse("x.json", Seeded("""
            "ground": "papayawhip",
            """));

        var d = Assert.Single(diagnostics, x => x.Field == "seeds.ground");
        Assert.Contains("six hex digits", d.Message, StringComparison.Ordinal);
    }

    /// <summary>The one confusable mistake worth an error rather than a warning:
    /// it looks like it worked, and the theme would be built on a colour other
    /// than the one the author is reading in their own file.</summary>
    [Fact]
    public void Overriding_a_seed_says_where_to_put_it_instead()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(overrides: """
            "Ground": "#000000"
            """));

        Assert.Null(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "overrides.Ground");
        Assert.Equal(ThemeSeverity.Error, d.Severity);
        Assert.Contains("\"seeds\" as \"ground\"", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_override_warns_and_the_theme_still_loads()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(overrides: """
            "Sparkle": "#FF0000"
            """));

        Assert.NotNull(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "overrides.Sparkle");
        Assert.Equal(ThemeSeverity.Warning, d.Severity);
    }

    [Fact]
    public void A_misspelled_override_suggests_the_spelling()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(overrides: """
            "surfaceraised": "#333333"
            """));

        Assert.NotNull(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "overrides.surfaceraised");
        Assert.Contains("SurfaceRaised", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_scalar_out_of_range_is_clamped_and_reported()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(structure: """
            "edge": 400
            """));

        Assert.NotNull(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "structure.edge");
        Assert.Equal(ThemeSeverity.Warning, d.Severity);
        Assert.Contains("clamped", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_builtin_id_is_refused_because_it_could_never_be_picked()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(id: "nightshift"));

        Assert.Null(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "id");
        Assert.Contains("built-in", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_id_with_spaces_in_it_is_refused()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(id: "My Theme"));

        Assert.Null(theme);
        Assert.Contains(diagnostics, d => d.Field == "id" && d.IsError);
    }

    /// <summary>Comments are legal on read, because the file seeded into an
    /// empty themes folder explains itself in place and that file has to parse
    /// through the same reader everything else does.</summary>
    [Fact]
    public void Comments_and_trailing_commas_are_read()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", """
            // The room.
            {
              "schemaVersion": 1,
              "id": "commented",
              "name": "Commented",
              "reason": "A theme that explains itself.",
              "seeds": {
                "ground":  "#0F1C1E",  // the field the covers hang in
                "surface": "#16282A",
                "text":    "#F0EDE7",
                "flare":   "#FF4D93",
                "volt":    "#4DE8C2",
                "amber":   "#FFB63D",
                "azure":   "#57A8F0",
                "danger":  "#E04B45",
              },
            }
            """);

        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
        Assert.Equal("commented", theme.Id);
    }

    // ══ The Flare invariant: warn, never refuse ═════════════════════════════

    /// <summary>
    /// §2's rule is the product's own claim about what a colour means, and the
    /// built-ins are held to it by a failing test. A user theme is theirs — so
    /// this warns, and the warning has to say what breaks rather than that it
    /// is wrong.
    /// </summary>
    [Fact]
    public void Flare_spent_twice_warns_specifically_and_still_loads()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded("""
            "amber": "#FF4D93",
            """));

        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);

        var d = Assert.Single(diagnostics, x => x.Field == "seeds.amber");
        Assert.Contains("patched since you played", d.Message, StringComparison.Ordinal);
        Assert.Contains("unread count", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flare_too_near_danger_warns_with_the_measurement()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded("""
            "flare": "#F0504A",
            """));

        Assert.NotNull(theme);
        var d = Assert.Single(diagnostics, x => x.Field == "seeds.flare" && x.Message.Contains("Danger", StringComparison.Ordinal));
        Assert.Equal(ThemeSeverity.Warning, d.Severity);
        Assert.Contains("close button", d.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// §14 declines to SHIP a light theme because it is a second pass over the
    /// tile scrim, the caption order and the dormancy floor — not because a
    /// bright palette is forbidden. A user theme may go bright, and what it owes
    /// them is the list of what stops working.
    /// </summary>
    [Fact]
    public void A_bright_field_warns_about_the_dormancy_ramp_and_still_loads()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded("""
            "ground": "#F4F1EC",
            "surface": "#FFFFFF",
            "text": "#101010",
            """));

        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);

        var d = Assert.Single(diagnostics, x => x.Field == "seeds.ground");
        Assert.Contains("dormant cover", d.Message, StringComparison.Ordinal);
        Assert.Contains("hole punched", d.Message, StringComparison.Ordinal);
    }

    // ══ The contrast report ═════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void The_report_agrees_with_the_sliders_own_mark(string id)
    {
        var theme = WinnowThemes.ById(id);
        var report = ThemeAudit.Report(theme);

        Assert.Equal(Colorimetry.AaCeiling(theme), report.AaCeiling);
        Assert.Contains(report.AaCeiling.ToString(System.Globalization.CultureInfo.InvariantCulture), report.Headline);

        // §14.6: the wall must not be the thing that fails first.
        Assert.True(
            report.WallCeiling >= report.AaCeiling,
            $"{id}: wall inverts at {report.WallCeiling}%, before the chrome fails AA at {report.AaCeiling}%");
    }

    // ══ Per-theme defaults ══════════════════════════════════════════════════

    [Fact]
    public void A_theme_can_carry_its_own_opening_position()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(defaults: """
            "transparency": 40, "backdrop": "acrylic",
            "reach": "chrome-and-wall", "layout": "floating"
            """));

        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
        Assert.NotNull(theme.Defaults);
        Assert.Equal(40, theme.Defaults.Transparency);
        Assert.Equal(WinnowBackdrop.Acrylic, theme.Defaults.Backdrop);
        Assert.True(theme.Defaults.WallTranslucent);
        Assert.Equal(WinnowLayout.Floating, theme.Defaults.Layout);

        // And they survive the round trip, which is what makes them part of the
        // theme rather than a note beside it.
        var (again, _) = ThemeJson.Parse("x.json", ThemeJson.Export(theme));
        Assert.NotNull(again);
        Assert.Equal(theme.Defaults, again.Defaults);
    }

    [Fact]
    public void An_unknown_default_is_ignored_with_a_warning_rather_than_refused()
    {
        var (theme, diagnostics) = ThemeJson.Parse("x.json", Seeded(defaults: """
            "backdrop": "velvet", "reach": "everything", "transparency": 400
            """));

        Assert.NotNull(theme);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
        Assert.Contains(diagnostics, d => d.Field == "defaults.backdrop");
        Assert.Contains(diagnostics, d => d.Field == "defaults.reach");
        Assert.Contains(diagnostics, d => d.Field == "defaults.transparency");
        Assert.Equal(100, theme.Defaults?.Transparency);
    }

    /// <summary>The built-ins declare no opening position, which is what keeps
    /// them behaving exactly as they did before this existed.</summary>
    [Theory]
    [MemberData(nameof(BuiltInIds))]
    public void No_builtin_carries_defaults(string id)
    {
        Assert.Null(WinnowThemes.ById(id).Defaults);
        Assert.False(WinnowThemes.ById(id).IsUserTheme);
    }

    // ══ Helpers ═════════════════════════════════════════════════════════════

    /// <summary>A complete, valid theme with the given fragments spliced in.
    /// Seeds later in the object win, which is how one line replaces one
    /// colour.</summary>
    private static string Seeded(
        string extraSeeds = "",
        string? id = null,
        string structure = "",
        string overrides = "",
        string defaults = "")
        => $$"""
            {
              "schemaVersion": 1,
              "id": "{{id ?? "probe"}}",
              "name": "Probe",
              "reason": "A theme built for a test.",
              "seeds": {
                "ground":  "#0F1C1E",
                "surface": "#16282A",
                "text":    "#F0EDE7",
                "flare":   "#FF4D93",
                "volt":    "#4DE8C2",
                "amber":   "#FFB63D",
                "azure":   "#57A8F0",
                "danger":  "#E04B45",
                {{extraSeeds}}
              },
              "structure": { {{structure}} },
              "overrides": { {{overrides}} },
              "defaults": { {{defaults}} }
            }
            """;

    /// <summary>Every colour the record holds, by name — the comparison the
    /// round-trip test is actually making.</summary>
    private static Dictionary<string, Color> ColoursOf(WinnowTheme t) => new(StringComparer.Ordinal)
    {
        ["Well"] = t.Well,
        ["Ground"] = t.Ground,
        ["Surface"] = t.Surface,
        ["SurfaceRaised"] = t.SurfaceRaised,
        ["SurfaceHigh"] = t.SurfaceHigh,
        ["Line"] = t.Line,
        ["Text"] = t.Text,
        ["TextDim"] = t.TextDim,
        ["TextFaint"] = t.TextFaint,
        ["Flare"] = t.Flare,
        ["Volt"] = t.Volt,
        ["VoltInk"] = t.VoltInk,
        ["VoltHover"] = t.VoltHover,
        ["VoltPress"] = t.VoltPress,
        ["Amber"] = t.Amber,
        ["Azure"] = t.Azure,
        ["Danger"] = t.Danger,
        ["DangerHover"] = t.DangerHover,
        ["DangerPress"] = t.DangerPress,
        ["DangerInk"] = t.DangerInk,
        ["TranslucentSurface"] = t.TranslucentSurface,
        ["TranslucentChromeGround"] = t.TranslucentChromeGround,
        ["TranslucentTextDim"] = t.TranslucentTextDim,
        ["TranslucentTextFaint"] = t.TranslucentTextFaint,
    };
}
