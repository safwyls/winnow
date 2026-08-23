using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Hoard.App.Services;

/// <summary>
/// Procedural cover stand-ins until IGDB art lands (M1): a deterministic-hue
/// gradient on a Surface-toned field, per mock-library.html's <c>.art</c>
/// treatment. Also computes the desaturated/darkened "floor" endpoint colours
/// so the tile can run the real two-layer dormancy cross-fade now — when a
/// real bitmap arrives, <see cref="ToFloor"/>'s colour math is exactly the
/// per-pixel matrix the cover cache will apply (Rec.709 luma desaturation to
/// 0.22, brightness scale to 0.60).
/// </summary>
public static class PlaceholderArt
{
    /// <summary>Deterministic vivid gradient endpoints for a title (FNV-1a over the name).</summary>
    public static (Color Start, Color End) VividColors(string name)
    {
        var hash = Fnv1a(name);
        var hue = hash % 360u;
        // Dark, saturated field — mock uses deep two-stop gradients so the
        // white Bricolage title always reads.
        var start = FromHsv(hue, 0.62, 0.58);
        var end = FromHsv((hue + 28u) % 360u, 0.70, 0.30);
        return (start, end);
    }

    /// <summary>The dormancy floor variant of a colour: saturation 0.22, brightness 0.60.</summary>
    public static Color ToFloor(Color c)
    {
        // Rec.709 luma, matching the SkiaSharp colour-matrix pass the cover
        // cache pipeline will use for real bitmaps (spike doc, code sketch).
        var luma = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        byte Mix(byte channel) =>
            (byte)Math.Clamp((luma + (channel - luma) * Dormancy.SatFloor) * Dormancy.BrightFloor, 0, 255);
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    /// <summary>A ~155° two-stop gradient brush (mock's <c>linear-gradient(155deg, …)</c>).</summary>
    public static IBrush Gradient(Color start, Color end) =>
        new ImmutableLinearGradientBrush(
            [
                new Avalonia.Media.Immutable.ImmutableGradientStop(0, start),
                new Avalonia.Media.Immutable.ImmutableGradientStop(1, end),
            ],
            startPoint: new RelativePoint(0.30, 0.0, RelativeUnit.Relative),
            endPoint: new RelativePoint(0.70, 1.0, RelativeUnit.Relative));

    private static uint Fnv1a(string text)
    {
        // Stable across runs/processes (string.GetHashCode is randomized).
        var hash = 2166136261u;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
