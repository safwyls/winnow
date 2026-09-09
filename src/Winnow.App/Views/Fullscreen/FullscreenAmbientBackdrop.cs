using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Bundled, decorative SVG paths use the active theme rather than baked palette values.</summary>
internal sealed class FullscreenAmbientBackdrop : Viewbox
{
    public FullscreenAmbientBackdrop(string asset)
    {
        IsHitTestVisible = false;
        Stretch = Stretch.UniformToFill;
        Opacity = .38;
        Child = FullscreenVectorArt.Load(asset);
    }

    public static Grid Behind(Control content, string asset)
    {
        var layers = new Grid { ClipToBounds = true };
        layers.Children.Add(new FullscreenAmbientBackdrop(asset));
        layers.Children.Add(content);
        return layers;
    }
}

internal static class FullscreenVectorArt
{
    // These authored assets contain only paths. This deliberately is not a general SVG renderer.
    public static Canvas Load(string asset)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://Winnow/Assets/Fullscreen/{asset}.svg"));
        var root = XDocument.Load(stream).Root!;
        var view = root.Attribute("viewBox")!.Value.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        var canvas = new Canvas { Width = view[2], Height = view[3], IsHitTestVisible = false };
        foreach (var element in root.Elements().Where(e => e.Name.LocalName == "path"))
        {
            var path = new ShapePath { Data = Geometry.Parse(element.Attribute("d")!.Value) };
            if (element.Attribute("data-fill") is { } fill) path[!ShapePath.FillProperty] = new DynamicResourceExtension(fill.Value);
            if (element.Attribute("data-stroke") is { } stroke) path[!ShapePath.StrokeProperty] = new DynamicResourceExtension(stroke.Value);
            path.StrokeThickness = double.Parse(element.Attribute("stroke-width")?.Value ?? "1", CultureInfo.InvariantCulture);
            path.Opacity = double.Parse(element.Attribute("opacity")?.Value ?? "1", CultureInfo.InvariantCulture);
            canvas.Children.Add(path);
        }
        return canvas;
    }
}
