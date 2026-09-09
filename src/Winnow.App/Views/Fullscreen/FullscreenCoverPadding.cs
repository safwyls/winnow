using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Extends the cover's edge colors into the space left by an uncropped image.</summary>
internal sealed class FullscreenCoverPadding : Control
{
    private static readonly ConditionalWeakTable<Bitmap, EdgeColors> Colors = new();
    private Bitmap? _source;
    private EdgeColors? _colors;

    public Bitmap? Source
    {
        get => _source;
        set
        {
            if (ReferenceEquals(_source, value)) return;
            _source = value;
            // Sample only on artwork changes, and share the tiny palette without retaining cache pixels.
            _colors = value is null ? null : Colors.GetValue(value, Sample);
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_source is null || _colors is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var size = _source.Size;
        var scale = Math.Min(Bounds.Width / size.Width, Bounds.Height / size.Height);
        var horizontal = Math.Max(0, (Bounds.Width - size.Width * scale) / 2);
        var vertical = Math.Max(0, (Bounds.Height - size.Height * scale) / 2);
        if (vertical > 0)
        {
            context.FillRectangle(_colors.Top, new Rect(0, 0, Bounds.Width, vertical));
            context.FillRectangle(_colors.Bottom, new Rect(0, Bounds.Height - vertical, Bounds.Width, vertical));
        }
        if (horizontal > 0)
        {
            context.FillRectangle(_colors.Left, new Rect(0, 0, horizontal, Bounds.Height));
            context.FillRectangle(_colors.Right, new Rect(Bounds.Width - horizontal, 0, horizontal, Bounds.Height));
        }
    }

    private static EdgeColors Sample(Bitmap bitmap)
    {
        if (bitmap.Format != PixelFormat.Bgra8888 && bitmap.Format != PixelFormat.Rgba8888)
            return EdgeColors.Empty;
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0) return EdgeColors.Empty;
        try
        {
            return new EdgeColors(
                Average(bitmap, new PixelRect(0, 0, width, 1)),
                Average(bitmap, new PixelRect(0, height - 1, width, 1)),
                Average(bitmap, new PixelRect(0, 0, 1, height)),
                Average(bitmap, new PixelRect(width - 1, 0, 1, height)));
        }
        catch (NotSupportedException)
        {
            // Some non-raster test/platform images cannot expose pixels; the theme remains the fallback.
            return EdgeColors.Empty;
        }
    }

    private static IBrush Average(Bitmap bitmap, PixelRect area)
    {
        var pixels = new byte[area.Width * area.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(area, handle.AddrOfPinnedObject(), pixels.Length, area.Width * 4); }
        finally { handle.Free(); }
        double red = 0, green = 0, blue = 0, weight = 0;
        var rgba = bitmap.Format == PixelFormat.Rgba8888;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = bitmap.AlphaFormat == AlphaFormat.Opaque ? 1 : pixels[i + 3] / 255d;
            var multiplier = bitmap.AlphaFormat == AlphaFormat.Premul ? 1 : alpha;
            red += pixels[i + (rgba ? 0 : 2)] * multiplier;
            green += pixels[i + 1] * multiplier;
            blue += pixels[i + (rgba ? 2 : 0)] * multiplier;
            weight += alpha;
        }
        return weight <= 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(
            (byte)Math.Clamp(Math.Round(red / weight), 0, 255),
            (byte)Math.Clamp(Math.Round(green / weight), 0, 255),
            (byte)Math.Clamp(Math.Round(blue / weight), 0, 255)));
    }

    private sealed record EdgeColors(IBrush Top, IBrush Bottom, IBrush Left, IBrush Right)
    {
        public static readonly EdgeColors Empty = new(Brushes.Transparent, Brushes.Transparent, Brushes.Transparent, Brushes.Transparent);
    }
}
