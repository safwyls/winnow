using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GamePreviewBubbleTests
{
    [AvaloniaFact]
    public void Artwork_continues_through_pointer_without_border_seam_or_rectangular_spill()
    {
        using var source = SolidBitmap();
        var bubble = new GamePreviewBubble
        {
            Background = Brushes.Black,
            BorderBrush = Brushes.White,
            Source = source,
            ArrowOffset = 40
        };
        Layout(bubble);
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 100));
        bitmap.Render(bubble);
        var pixels = Pixels(bitmap);
        Assert.Equal(0, Pixel(pixels, 2, 10)[3]);
        Assert.Equal(Pixel(pixels, 30, 40), Pixel(pixels, 10, 40));
        Assert.Equal(Pixel(pixels, 30, 40), Pixel(pixels, 5, 40));
        Assert.InRange(Pixel(pixels, 30, 40)[2], (byte)44, (byte)48);
        Assert.Equal(255, Pixel(pixels, 30, 40)[3]);
    }

    [AvaloniaFact]
    public void Flipped_pointer_moves_artwork_and_content_gutter_together()
    {
        var child = new Border();
        var bubble = new GamePreviewBubble { Background = Brushes.Black, Child = child };
        Layout(bubble);
        Assert.Equal(26, child.Bounds.X);
        var contentWidth = child.Bounds.Width;
        bubble.ArrowOnRight = true;
        Layout(bubble);
        Assert.Equal(16, child.Bounds.X);
        Assert.Equal(contentWidth, child.Bounds.Width);
        using var bitmap = new RenderTargetBitmap(new PixelSize(200, 100));
        bitmap.Render(bubble);
        var pixels = Pixels(bitmap);
        Assert.Equal(0, Pixel(pixels, 197, 10)[3]);
        Assert.Equal(255, Pixel(pixels, 195, 40)[3]);
        Assert.Equal(255, Pixel(pixels, 2, 40)[3]);
    }

    private static void Layout(Control control)
    {
        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));
    }

    private static byte[] Pixel(byte[] pixels, int x, int y)
    {
        var start = (y * 200 + x) * 4;
        return pixels[start..(start + 4)];
    }

    private static Bitmap SolidBitmap()
    {
        byte[] pixels = [0, 0, 255, 255];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(),
                new PixelSize(1, 1), new Vector(96, 96), 4);
        }
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
