using System.Text.Json.Nodes;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ThemeTypographyTests
{
    private static readonly ThemeTypography Custom = new()
    {
        HeadingFont = "Georgia", InterfaceFont = "Segoe UI", DataFont = "Consolas", SizePercent = 115,
    };

    [Fact]
    public void Typography_round_trips_in_exported_theme()
    {
        var (theme, diagnostics) = ThemeJson.Parse("copy.json",
            ThemeJson.Export(WinnowThemes.Default with { Typography = Custom }));
        Assert.NotNull(theme);
        Assert.Equal(Custom, theme.Typography);
        Assert.DoesNotContain(diagnostics, d => d.IsError);
    }

    [Fact]
    public void Old_themes_and_partial_typography_keep_bundled_defaults()
    {
        var document = JsonNode.Parse(ThemeJson.Export(WinnowThemes.Default))!.AsObject();
        document.Remove("typography");
        Assert.Equal(ThemeTypography.Default, ThemeJson.Parse("old.json", document.ToJsonString()).Theme!.Typography);
        document["typography"] = new JsonObject { ["sizePercent"] = 110 };
        Assert.Equal(ThemeTypography.Default with { SizePercent = 110 },
            ThemeJson.Parse("partial.json", document.ToJsonString()).Theme!.Typography);
    }

    [Theory]
    [InlineData("headingFont", "")]
    [InlineData("interfaceFont", "https://example.test/font.ttf")]
    [InlineData("dataFont", "C:\\font.ttf")]
    [InlineData("headingFont", "Arial, Georgia")]
    [InlineData("dataFont", "font#Family")]
    [InlineData("interfaceFont", "Arial\n")]
    public void Invalid_font_names_report_their_field(string field, string value)
    {
        var document = JsonNode.Parse(ThemeJson.Export(WinnowThemes.Default))!;
        document["typography"]![field] = value;
        var (theme, diagnostics) = ThemeJson.Parse("bad.json", document.ToJsonString());
        Assert.Null(theme);
        Assert.Contains(diagnostics, d => d.IsError && d.Field == "typography." + field);
    }

    [Theory]
    [InlineData("{\"sizePercent\":79}")]
    [InlineData("{\"sizePercent\":121}")]
    [InlineData("{\"sizePercent\":100.5}")]
    [InlineData("{\"headingFont\":null}")]
    [InlineData("{\"unknownFont\":\"Arial\"}")]
    public void Malformed_typography_is_rejected(string typography)
    {
        var document = JsonNode.Parse(ThemeJson.Export(WinnowThemes.Default))!;
        document["typography"] = JsonNode.Parse(typography);
        Assert.Null(ThemeJson.Parse("bad.json", document.ToJsonString()).Theme);
    }

    [Fact]
    public async Task Preferences_follow_theme_switches_and_restart_and_reset_to_authored_defaults()
    {
        var settings = new MemorySettings();
        var service = new ThemeService(settings);
        var first = service.Theme;
        var second = WinnowThemes.All.First(t => t.Id != first.Id);
        service.SetTypography(Custom);
        service.SelectTheme(second);
        Assert.Equal(second.Typography, service.Typography);
        service.SetTypography(Custom with { SizePercent = 90 });
        service.SelectTheme(first);
        Assert.Equal(Custom, service.Typography);
        await service.PendingSave;

        var restarted = new ThemeService(settings);
        await restarted.LoadAsync();
        Assert.Equal(Custom, restarted.Typography);
        restarted.SelectTheme(second);
        Assert.Equal(90, restarted.Typography.SizePercent);
        restarted.ResetTypography();
        Assert.Equal(second.Typography, restarted.Typography);
        await restarted.PendingSave;
        var reset = new ThemeService(settings);
        await reset.LoadAsync();
        Assert.Equal(second.Typography, reset.Typography);
        reset.SelectTheme(first);
        Assert.Equal(Custom, reset.Typography);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task Malformed_preferences_use_authored_defaults(string stored)
    {
        var settings = new MemorySettings();
        await settings.SetAsync(ThemeService.TypographySettingKey, stored);
        var service = new ThemeService(settings);
        await service.LoadAsync();
        Assert.Equal(service.Theme.Typography, service.Typography);
    }

    [Fact]
    public async Task Invalid_preference_entry_does_not_discard_other_themes()
    {
        var first = WinnowThemes.Default;
        var second = WinnowThemes.All.First(t => t.Id != first.Id);
        var settings = new MemorySettings();
        await settings.SetAsync(ThemeService.TypographySettingKey,
            $$$"""{"{{{first.Id}}}":{"sizePercent":"bad"},"{{{second.Id}}}":{"sizePercent":110}}""");
        var service = new ThemeService(settings);
        await service.LoadAsync();
        Assert.Equal(first.Typography, service.Typography);
        service.SelectTheme(second);
        Assert.Equal(110, service.Typography.SizePercent);
    }

    [Fact]
    public void Reset_restores_authored_fonts_and_size()
    {
        var service = new ThemeService();
        service.SelectTheme(WinnowThemes.Default with { Typography = Custom });
        service.SetTypography(ThemeTypography.Default);
        service.ResetTypography();
        Assert.Equal(Custom, service.Typography);
    }

    [Fact]
    public void Export_uses_effective_typography_without_changing_authored_theme()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winnow-typography-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ThemeService(userThemes: new UserThemeStore(directory));
            var original = service.Theme;
            service.SetTypography(Custom);
            var (file, problem) = service.ExportTheme(original);
            Assert.Null(problem);
            Assert.NotNull(file);
            var (exported, _) = ThemeJson.Parse(file, File.ReadAllText(Path.Combine(directory, file)));
            Assert.NotNull(exported);
            Assert.Equal(Custom, exported.Typography);
            Assert.Equal(ThemeTypography.Default, original.Typography);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class MemorySettings : ISettingsRepository
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
