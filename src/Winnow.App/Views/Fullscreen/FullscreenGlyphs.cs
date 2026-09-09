using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Path = Avalonia.Controls.Shapes.Path;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Bundled Kenney button silhouettes, recolored with the active TV theme.</summary>
public static class FullscreenGlyphs
{
    private static readonly Dictionary<string, Geometry> Geometries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = "xbox_button_a_outline", ["B"] = "xbox_button_b_outline",
        ["X"] = "xbox_button_x_outline", ["Y"] = "xbox_button_y_outline",
        ["LB"] = "xbox_lb_outline", ["RB"] = "xbox_rb_outline",
        ["LT"] = "xbox_lt_outline", ["RT"] = "xbox_rt_outline",
        ["Menu"] = "xbox_button_menu_outline", ["View"] = "xbox_button_view_outline",
        ["LS"] = "xbox_stick_l", ["RS"] = "xbox_stick_r",
        ["Dpad"] = "xbox_dpad", ["Horizontal"] = "xbox_dpad_horizontal_outline",
        ["Vertical"] = "xbox_dpad_vertical_outline", ["Controller"] = "controller_xboxseries"
    };

    public static Control Icon(string key, double size = 32)
    {
        if (key is "← / →" or "← →") key = "Horizontal";
        if (key is "↑ / ↓" or "↑ ↓") key = "Vertical";
        if (!Files.ContainsKey(key) && !key.Equals("Winnow", StringComparison.OrdinalIgnoreCase))
            return FullscreenUi.Text(key, size * .75);
        if (!Geometries.TryGetValue(key, out var geometry))
        {
            var dragon = key.Equals("Winnow", StringComparison.OrdinalIgnoreCase);
            var asset = dragon ? "Icons/dragon.svg" : $"Controller/{Files[key]}.svg";
            using var stream = AssetLoader.Open(new Uri($"avares://Winnow/Assets/{asset}"));
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var svg = XDocument.Load(reader);
            // These audited assets contain only untransformed, filled paths. This is deliberately
            // not a general SVG renderer; the source vectors stay bundled for provenance.
            var data = string.Join(" ", svg.Descendants().Where(e => e.Name.LocalName == "path").Select(e => (string?)e.Attribute("d")));
            var parsed = PathGeometry.Parse(data);
            parsed.FillRule = dragon ? FillRule.EvenOdd : FillRule.NonZero;
            Geometries[key] = geometry = parsed;
        }
        var path = new Path { Data = geometry, Width = size, Height = size, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        path[!Path.FillProperty] = new DynamicResourceExtension("Text");
        return path;
    }

    public static Control Hints(string hints)
    {
        var result = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 28, VerticalAlignment = VerticalAlignment.Center };
        var matches = Regex.Matches(hints, @"(?:^|\s{2,})(LT\s*/\s*RT|LB\s*/\s*RB|←\s*/?\s*→|↑\s*/?\s*↓|Menu|View|Dpad|RS|LS|LB|RB|LT|RT|[ABXY])\s+");
        if (matches.Count == 0)
            result.Children.Add(FullscreenUi.Text(hints, 24, "TextDim"));
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : hints.Length;
            result.Children.Add(Hint(match.Groups[1].Value, hints[start..end].Trim()));
        }
        return result;
    }

    public static Control Hint(string key, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        var pair = Regex.Match(key, @"^(LT|LB)\s*/\s*(RT|RB)$");
        if (pair.Success)
        {
            row.Children.Add(Icon(pair.Groups[1].Value));
            row.Children.Add(Icon(pair.Groups[2].Value));
        }
        else row.Children.Add(Icon(key));
        var text = FullscreenUi.Text(label, 24, "TextDim");
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextWrapping = TextWrapping.NoWrap;
        row.Children.Add(text);
        return row;
    }
}
