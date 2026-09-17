using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Winnow.App.Views;

/// <summary>Decorative action symbols shared by popup and fullscreen menus.</summary>
internal static class ActionIcons
{
    private const string RefreshPath = "M20,10 A8,8 0 1 0 20,16 M20,4 L20,10 L14,10";
    public static Geometry RefreshGeometry { get; } = Geometry.Parse(RefreshPath);

    public static Control Create(string label, double size = 20)
    {
        var text = label.ToLowerInvariant();
        var data = text switch
        {
            "cancel" or "close" => "M6,6 L18,18 M18,6 L6,18",
            "resume" or "open game" => "M8,4 L20,12 L8,20 Z",
            "not now" => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,12 L16,14",
            "not interested" => "M12,3 A9,9 0 1 1 11.99,3 M7,12 L17,12",
            "search" => "M10,3 A7,7 0 1 1 9.99,3 M15,15 L21,21",
            "exit fullscreen" => "M3,9 L9,9 L9,3 M15,3 L15,9 L21,9 M3,15 L9,15 L9,21 M15,21 L15,15 L21,15",
            "quit winnow" => "M12,2 L12,12 M7,5 A9,9 0 1 0 17,5",
            _ when text.Contains("artwork") || text.Contains("cover") => "M3,3 L21,3 L21,21 L3,21 Z M3,17 L9,11 L14,16 L17,13 L21,17 M16,6 A2,2 0 1 1 15.99,6",
            _ when text.Contains("version") => "M3,7 L17,7 L17,21 L3,21 Z M7,3 L21,3 L21,17",
            _ when text.Contains("session") || text.Contains("history") => "M12,3 A9,9 0 1 1 11.99,3 M12,7 L12,12 L16,14",
            _ when text.Contains("folder") => "M3,6 L9,6 L11,8 L21,8 L21,20 L3,20 Z",
            _ when text.Contains("refresh") || text.Contains("refetch") || text.Contains("reset") => RefreshPath,
            _ when text.Contains("undo") || text.Contains("restore") => "M9,4 L3,10 L9,16 M3,10 L14,10 Q21,10 21,20",
            _ when text.Contains("edit") || text.Contains("rename") => "M4,16 L16,4 L20,8 L8,20 L3,21 Z M13,7 L17,11",
            _ when text.Contains("delete") => "M3,6 L21,6 M9,6 L9,3 L15,3 L15,6 M6,6 L7,21 L17,21 L18,6 M10,10 L10,17 M14,10 L14,17",
            _ when text.Contains("hide") || text.Contains("hidden") => "M2,12 Q12,0 22,12 Q12,24 2,12 M3,3 L21,21",
            _ when text.Contains("earlier") => "M12,21 L12,3 M5,10 L12,3 L19,10",
            _ when text.Contains("later") => "M12,3 L12,21 M5,14 L12,21 L19,14",
            _ when text.Contains("remove") || text.Contains("separate") || text.Contains("ungroup") => "M3,5 L15,5 M3,11 L15,11 M3,17 L10,17 M14,17 L22,17",
            _ when text.Contains("add") || text.Contains("new list") => "M3,5 L15,5 M3,11 L15,11 M3,17 L10,17 M14,17 L22,17 M18,13 L18,21",
            _ when text.Contains("save") => "M3,3 L18,3 L21,6 L21,21 L3,21 Z M7,3 L7,9 L17,9 L17,3 M7,21 L7,14 L17,14 L17,21",
            _ when text.Contains("filter") || text.Contains("sort") || text.Contains("order") => "M3,5 L21,5 M6,12 L18,12 M9,19 L15,19",
            _ when text.Contains("settings") || text.Contains("tools") || text.Contains("installation") || text.Contains("uninstall") => "M3,6 L21,6 M3,18 L21,18 M8,2 L8,10 M16,14 L16,22",
            _ when text.Contains("told the feed") => "M3,3 L21,3 L21,17 L9,17 L3,22 Z M7,7 L17,7 M7,12 L14,12",
            _ when text.Contains("rating") || text.Contains(" / 5") => "M12,2 L15,8 L22,9 L17,14 L18,21 L12,18 L6,21 L7,14 L2,9 L9,8 Z",
            _ when text.Contains("wrong") || text.Contains("match") => "M8,7 Q8,2 13,3 Q20,5 14,10 Q12,11 12,15 M12,18 L12,21",
            _ when text.Contains("open") || text.Contains("store") || text.Contains("website") => "M14,3 L21,3 L21,10 M21,3 L10,14 M10,5 L3,5 L3,21 L19,21 L19,14",
            _ => "M9,5 L16,12 L9,19"
        };
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        path[!Path.StrokeProperty] = new DynamicResourceExtension("Text");
        var canvas = new Canvas { Width = 24, Height = 24, Children = { path } };
        return new Viewbox { Width = size, Height = size, Child = canvas,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    }
}
