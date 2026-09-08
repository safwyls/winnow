using Avalonia.Media;
using Winnow.App.Services;
using Winnow.App.Themes;
using Xunit;

namespace Winnow.Tests;

public class BundledThemeTests
{
    [Theory]
    [InlineData("bottle-green", "Bottle green", "#0A140E", 40)]
    [InlineData("silkcircuit", "SilkCircuit", "#0A0A0F", null)]
    [InlineData("silkcircuit-dawn", "SilkCircuit Dawn", "#F4F0FA", 0)]
    [InlineData("rose-pine", "Rosé Pine", "#191724", null)]
    [InlineData("rose-pine-dawn", "Rosé Pine Dawn", "#FAF4ED", 0)]
    public void Authored_themes_ship_without_a_local_theme_directory(
        string id, string name, string ground, int? transparency)
    {
        var service = new ThemeService();
        var theme = Assert.Single(service.Catalogue, t => t.Id == id);
        Assert.Equal(name, theme.Name);
        Assert.Equal(Color.Parse(ground), theme.Ground);
        Assert.Equal(transparency, theme.Defaults?.Transparency);
        Assert.False(theme.IsUserTheme);
        Assert.Same(theme, WinnowThemes.ById(id));
        Assert.DoesNotContain(service.Diagnostics, d => d.IsError);
    }

    [Theory]
    [InlineData("silkcircuit-dawn")]
    [InlineData("rose-pine-dawn")]
    public void Selecting_dawn_applies_its_solid_default_and_retains_audit_findings(string id)
    {
        var service = new ThemeService();
        service.SetTransparency(70);
        service.SelectTheme(WinnowThemes.ById(id));
        Assert.Equal(0, service.Transparency);
        Assert.Contains(service.Diagnostics, d => d.File == id + ".json" && !d.IsError);
    }
}
