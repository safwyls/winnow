using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenCoverPaddingTests
{
    [AvaloniaFact]
    public void Padding_uses_each_artwork_edge_and_clears_when_released()
    {
        // Red top edge and blue bottom edge make black padding or a single average unmistakable.
        using var source = Bitmap(new byte[]
        {
            0, 0, 240, 255, 0, 0, 240, 255,
            220, 0, 0, 255, 220, 0, 0, 255
        });
        var padding = new FullscreenCoverPadding { Source = source };
        padding.Measure(new Size(20, 60));
        padding.Arrange(new Rect(0, 0, 20, 60));
        using var rendered = new RenderTargetBitmap(new PixelSize(20, 60));
        rendered.Render(padding);
        var pixels = Pixels(rendered);
        Assert.Equal(new byte[] { 0, 0, 240, 255 }, pixels[(10 * 4)..(11 * 4)]);
        var bottom = (59 * 20 + 10) * 4;
        Assert.Equal(new byte[] { 220, 0, 0, 255 }, pixels[bottom..(bottom + 4)]);
        Assert.Equal(0, pixels[(30 * 20 + 10) * 4 + 3]);
        padding.Source = null;
        using var cleared = new RenderTargetBitmap(new PixelSize(20, 60));
        cleared.Render(padding);
        Assert.All(Pixels(cleared), pixel => Assert.Equal(0, pixel));
    }

    [AvaloniaFact]
    public void Tall_artwork_fills_side_gaps_without_painting_over_the_image()
    {
        using var source = Bitmap(new byte[]
        {
            0, 180, 0, 255, 0, 0, 240, 255,
            0, 180, 0, 255, 0, 0, 240, 255
        });
        var padding = new FullscreenCoverPadding { Source = source };
        padding.Measure(new Size(60, 20));
        padding.Arrange(new Rect(0, 0, 60, 20));
        using var rendered = new RenderTargetBitmap(new PixelSize(60, 20));
        rendered.Render(padding);
        var pixels = Pixels(rendered);
        var left = 10 * 60 * 4;
        var right = (10 * 60 + 59) * 4;
        Assert.Equal(new byte[] { 0, 180, 0, 255 }, pixels[left..(left + 4)]);
        Assert.Equal(new byte[] { 0, 0, 240, 255 }, pixels[right..(right + 4)]);
        Assert.Equal(0, pixels[(10 * 60 + 30) * 4 + 3]);
    }

    private static Bitmap Bitmap(byte[] pixels)
    {
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(), new PixelSize(2, 2), new Vector(96, 96), 8); }
        finally { pin.Free(); }
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4); }
        finally { pin.Free(); }
        return pixels;
    }
}
