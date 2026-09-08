using Xunit;

namespace Winnow.Covers.Tests;

/// <summary>
/// A request states which layers of the dormancy cross-fade it needs
/// (<see cref="CoverLayers"/>). Only the surfaces that stack two images — wall
/// tile, list row, feed card, merge row — need the floor variant; the detail
/// modal, the screenshot strip, the lightbox, the IGDB candidate rows and the
/// metadata previews all draw the art at full saturation, and before
/// TASK-152.2 every one of them decoded and cached a second bitmap nothing
/// would ever paint.
/// </summary>
public class CoverLayerRequestTests
{
    private static readonly CoverKey Key = CoverKey.Steam("220");

    [Fact]
    public async Task A_vivid_only_request_decodes_one_layer()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        cdn.AddCapsule("220", TestArt.Capsule());

        using var pipeline = dir.Pipeline(cdn);
        using var art = await pipeline.GetAsync(Key, 320, CoverLayers.Vivid);

        Assert.NotNull(art);
        Assert.NotNull(art!.Vivid);
        Assert.Null(art.Floor);
    }

    /// <summary>
    /// The stored floor variant is a colour-matrix pass and a second file. A
    /// key that is only ever drawn vivid — an IGDB screenshot, most obviously —
    /// pays for neither until something asks to draw it dimmed.
    /// </summary>
    [Fact]
    public async Task A_vivid_only_fetch_writes_no_floor_variant()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        cdn.AddCapsule("220", TestArt.Capsule());
        var disk = new CoverDiskCache(dir.Options());

        using (var pipeline = dir.Pipeline(cdn))
        {
            using var art = await pipeline.GetAsync(Key, 320, CoverLayers.Vivid);
            Assert.NotNull(art);
        }

        Assert.True(File.Exists(disk.SourcePath(Key)));
        Assert.False(disk.HasFloor(Key));

        // The first two-layer request is what generates it, from the source
        // already on disk and without a second fetch.
        using (var pipeline = dir.Pipeline(cdn))
        {
            using var art = await pipeline.GetAsync(Key, 320, CoverLayers.VividAndFloor);
            Assert.NotNull(art);
            Assert.NotNull(art!.Floor);
        }

        Assert.True(disk.HasFloor(Key));
        Assert.Equal(1, cdn.RequestCount);
    }

    [Fact]
    public async Task A_two_layer_request_decodes_both_at_the_display_width()
    {
        using var dir = new TempCoverDirectory();
        var cdn = new FakeCoverCdn();
        cdn.AddCapsule("220", TestArt.Capsule());

        using var pipeline = dir.Pipeline(cdn);
        using var art = await pipeline.GetAsync(Key, 240, CoverLayers.VividAndFloor);

        Assert.NotNull(art);
        Assert.NotNull(art!.Floor);
        Assert.Equal(240, art.Vivid.Width);
        Assert.Equal(240, art.Floor!.Width);
    }
}
