using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Winnow.App.Themes;

namespace Winnow.App.Services;

/// <summary>Live type resources preserve the authored hierarchy without enlarging controls or artwork.</summary>
public static class ThemeTypographyResources
{
    private static IReadOnlyList<string>? _availableFontNames;
    private static readonly string[] BundledNames = ["Bricolage Grotesque", "Plus Jakarta Sans", "IBM Plex Mono"];
    private static readonly (string Key, double Size)[] Roles =
    [
        ("DisplayLSize", 22), ("DisplaySSize", 12), ("BodyLSize", 15), ("BodySize", 13),
        ("LabelSize", 11), ("DataSize", 12), ("DataSSize", 10)
    ];

    public static IReadOnlyList<string> AvailableFontNames
    {
        get
        {
            if (_availableFontNames is { } cached) return cached;
            // View-model and theme-file tests can run before Avalonia has installed a font manager.
            IEnumerable<string> installed;
            try { installed = FontManager.Current.SystemFonts.Select(f => f.Name).ToArray(); }
            catch (InvalidOperationException) { return BundledNames; }
            return _availableFontNames = BundledNames.Concat(installed)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
    }

    public static bool IsFontAvailable(string name) => AvailableFontNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static FontFamily ResolveFont(string name, string fallback)
    {
        var bundled = BundledNames.FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (bundled is not null) return Bundled(bundled);
        var installed = AvailableFontNames.FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        return installed is not null ? new FontFamily(installed) : Bundled(fallback);
    }

    private static FontFamily Bundled(string name) => new($"avares://Winnow/Assets/Fonts#{name}");

    public static void Apply(IResourceDictionary resources, ThemeTypography typography)
    {
        resources["DisplayFont"] = ResolveFont(typography.HeadingFont, BundledNames[0]);
        resources["BodyFont"] = ResolveFont(typography.InterfaceFont, BundledNames[1]);
        resources["DataFont"] = ResolveFont(typography.DataFont, BundledNames[2]);
        var scale = Math.Clamp(typography.SizePercent, 80, 120) / 100.0;
        resources["ThemeTextScale"] = scale;
        ApplySizes(resources, scale);
    }

    /// <summary>Fullscreen scopes apply scaling from stable control baselines, including shared styles.</summary>
    internal static void UseUnscaledSizes(IResourceDictionary resources) => ApplySizes(resources, 1);

    private static void ApplySizes(IResourceDictionary resources, double scale)
    {
        foreach (var (key, size) in Roles) resources[key] = size * scale;
        foreach (var size in FontSizes) resources[FontSizeKey(size)] = size * scale;
        foreach (var height in LineHeights) resources[LineHeightKey(height)] = height * scale;
    }

    // The numeric keys also serve deliberately smaller text inside a semantic role (a compact label,
    // for example). They keep each authored size stable at 100% and responsive when the theme changes.
    public static readonly double[] FontSizes = [8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 23, 24, 26, 28, 30, 36, 60];
    public static readonly double[] LineHeights = [15, 16, 17, 18, 19, 20, 22, 30, 34, 42];
    public static string FontSizeKey(double size) => "FontSize" + size.ToString(CultureInfo.InvariantCulture);
    public static string LineHeightKey(double height) => "TextLineHeight" + height.ToString(CultureInfo.InvariantCulture);

    public static void BindSize(TextBlock text, double size) =>
        text[!TextBlock.FontSizeProperty] = new DynamicResourceExtension(FontSizeKey(size));

    public static void BindSize(TemplatedControl control, double size) =>
        control[!TemplatedControl.FontSizeProperty] = new DynamicResourceExtension(FontSizeKey(size));

    public static void BindSize(TextElement text, double size) =>
        text[!TextElement.FontSizeProperty] = new DynamicResourceExtension(FontSizeKey(size));
}
