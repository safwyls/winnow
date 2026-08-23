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

    /// <summary>
    /// The dormancy floor variant of a colour: saturation 0.22, a −6° cool
    /// shift, then brightness 0.68 — the same composition, in the same order,
    /// as Hoard.Covers' CoverImaging.FloorMatrix. A placeholder tile and a real
    /// cover must reach an identical endpoint, or a tile would visibly jump the
    /// moment its cover finishes downloading.
    /// </summary>
    public static Color ToFloor(Color c)
    {
        var (r, g, b) = ((double)c.R, (double)c.G, (double)c.B);

        // 1. Rec.709 luma desaturation toward the floor.
        var luma = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        var sr = luma + ((r - luma) * Dormancy.SatFloor);
        var sg = luma + ((g - luma) * Dormancy.SatFloor);
        var sb = luma + ((b - luma) * Dormancy.SatFloor);

        // 2. Cool shift (§1: dormant reads faded *and cool*, not merely grey).
        var radians = HueDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var hr = (sr * (0.2126 + (cos * 0.7874) - (sin * 0.2126)))
               + (sg * (0.7152 - (cos * 0.7152) - (sin * 0.7152)))
               + (sb * (0.0722 - (cos * 0.0722) + (sin * 0.9278)));
        var hg = (sr * (0.2126 - (cos * 0.2126) + (sin * 0.143)))
               + (sg * (0.7152 + (cos * 0.2848) + (sin * 0.140)))
               + (sb * (0.0722 - (cos * 0.0722) - (sin * 0.283)));
        var hb = (sr * (0.2126 - (cos * 0.2126) - (sin * 0.7874)))
               + (sg * (0.7152 - (cos * 0.7152) + (sin * 0.7152)))
               + (sb * (0.0722 + (cos * 0.9278) + (sin * 0.0722)));

        // 3. Brightness scale.
        byte Clamp(double v) => (byte)Math.Clamp(v * Dormancy.BrightFloor, 0, 255);
        return Color.FromArgb(c.A, Clamp(hr), Clamp(hg), Clamp(hb));
    }

    /// <summary>The §5.1 cool shift. Mirrors CoverImaging.DefaultHueDegrees.</summary>
    private const double HueDegrees = -6.0;

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
