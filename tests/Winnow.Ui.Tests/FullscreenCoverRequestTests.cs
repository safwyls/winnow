using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Winnow.App.Views.Fullscreen;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenCoverRequestTests
{
    [AvaloniaFact]
    public void First_layout_requests_only_the_display_bucket_and_uses_warm_art()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(2, 3), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        var leases = new Leases(new CoverArt(bitmap, bitmap));
        var tile = TileFixture.Tile(DateTime.UtcNow, coverKey: CoverKey.Steam("42"), covers: leases);
        var cover = new FullscreenCover(tile) { Width = 400, Height = 600 };
        var window = new Window { Width = 800, Height = 800, Content = cover };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(new[] { CoverImaging.SnapWidth(400) }, leases.Widths);
            Assert.Contains(cover.GetVisualDescendants().OfType<Image>(), image => ReferenceEquals(image.Source, bitmap));
            cover.SetSelected(true);
            cover.SetSelected(false);
            Assert.Single(leases.Widths);
        }
        finally { window.Close(); }
    }

    private sealed class Leases(CoverArt art) : ICoverLeases
    {
        public List<int> Widths { get; } = [];
        public ICoverLease Acquire(CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor)
        {
            var width = CoverImaging.SnapWidth(displayWidthPixels);
            Widths.Add(width);
            return new Lease(key, width, layers, width == CoverImaging.SnapWidth(400) ? art : null);
        }
    }

    private sealed class Lease(CoverKey key, int width, CoverLayers layers, CoverArt? art) : ICoverLease
    {
        public CoverKey Key => key;
        public int Width => width;
        public CoverLayers Layers => layers;
        public bool TryGetArt(out CoverArt value) { value = art!; return art is not null; }
        public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult(art);
        public void Dispose() { }
    }
}
