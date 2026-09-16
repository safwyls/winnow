using System.Text.Json.Nodes;
using Winnow.App.Themes;
using Xunit;

namespace Winnow.Tests;

public sealed class DawnThemeContrastTests
{
    [Theory]
    [InlineData("rose-pine-dawn")]
    [InlineData("silkcircuit-dawn")]
    public void Light_controls_have_readable_labels_and_visible_boundaries(string id)
    {
        var theme = WinnowThemes.ById(id);
        Assert.True(theme.IsLight);
        var t = theme.Tokens(0);
        foreach (var surface in new[] { "Well", "Ground", "Surface", "SurfaceRaised", "SurfaceHigh" })
        {
            foreach (var role in new[] { "VoltForeground", "VoltHoverForeground", "AmberForeground", "AzureForeground", "DangerForeground", "Text", "TextDim" })
                Assert.True(Colorimetry.Contrast(t[role], t[surface]) >= 4.5, $"{id} {role}/{surface}");
            Assert.True(Colorimetry.Contrast(t["Line"], t[surface]) >= 3, $"{id} Line/{surface}");
        }
        foreach (var state in new[] { "", "Hover", "Press" })
            foreach (var role in new[] { "Volt", "Danger" })
                Assert.True(Colorimetry.Contrast(t[role + "Ink"], t[role + state]) >= 4.5,
                    $"{id} {role}Ink/{role}{state}");
        Assert.Equal(theme.Volt, t["Volt"]);
    }

    [Theory]
    [InlineData("rose-pine-dawn", true)]
    [InlineData("silkcircuit-dawn", true)]
    [InlineData("winnow", false)]
    public void Legacy_files_infer_variant_and_explicit_variant_round_trips(string id, bool light)
    {
        var json = JsonNode.Parse(ThemeJson.Export(WinnowThemes.ById(id)))!.AsObject();
        json.Remove("variant");
        var (legacy, _) = ThemeJson.Parse("legacy.json", json.ToJsonString());
        Assert.NotNull(legacy);
        Assert.Equal(light, legacy.IsLight);
        json["variant"] = light ? "dark" : "light";
        var (explicitTheme, _) = ThemeJson.Parse("explicit.json", json.ToJsonString());
        Assert.NotNull(explicitTheme);
        Assert.Equal(!light, explicitTheme.IsLight);
        var (roundTrip, _) = ThemeJson.Parse("roundtrip.json", ThemeJson.Export(explicitTheme));
        Assert.Equal(explicitTheme.IsLight, roundTrip!.IsLight);
        json["variant"] = "dakr";
        var (invalid, diagnostics) = ThemeJson.Parse("typo.json", json.ToJsonString());
        Assert.Null(invalid);
        Assert.Contains(diagnostics, d => d.IsError && d.Field == "variant");
    }

    [Fact]
    public void Dark_theme_accents_keep_their_authored_colors()
    {
        foreach (var theme in WinnowThemes.All.Where(theme => !theme.IsLight))
        {
            var t = theme.Tokens(0);
            foreach (var role in new[] { "Volt", "Amber", "Azure", "Danger" })
                Assert.Equal(t[role], t[role + "Foreground"]);
            Assert.Equal(t["VoltHover"], t["VoltHoverForeground"]);
        }
    }
}
