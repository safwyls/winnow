using Avalonia.Media;
using Hoard.App.Services;
using SkiaSharp;
using Xunit;

namespace Hoard.Covers.Tests;

public class CoverImagingTests
{
    /// <summary>
    /// The dormancy floor has to be one number, not two implementations of it.
    /// Procedural placeholder art and a real cover sit in the same grid and fade
    /// to the same endpoint, so the SkiaSharp colour matrix and
    /// PlaceholderArt.ToFloor must agree per channel.
    /// </summary>
    [Fact]
    public void Floor_variant_matches_PlaceholderArt_ToFloor()
    {
        Color[] samples =
        [
            Color.FromRgb(0xFF, 0x00, 0x00),
            Color.FromRgb(0x00, 0xFF, 0x00),
            Color.FromRgb(0x00, 0x00, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x00, 0x00, 0x00),
            Color.FromRgb(0x4D, 0xE8, 0xC2), // Volt
            Color.FromRgb(0xFF, 0x5C, 0x8A), // Flare
            Color.FromRgb(0x8A, 0x63, 0x2B),
            Color.FromRgb(0x12, 0x34, 0x56),
        ];

        using var source = new SKBitmap(
            new SKImageInfo(samples.Length, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        for (var x = 0; x < samples.Length; x++)
        {
            source.SetPixel(x, 0, new SKColor(samples[x].R, samples[x].G, samples[x].B, 255));
        }

        // Read the floor from the shared constants rather than literals: this
        // test's job is to prove the two implementations agree, so retuning the
        // floor must move both sides together or fail loudly — not silently
        // compare the new maths against a stale hard-coded endpoint.
        using var floor = CoverImaging.ApplyFloor(
            source, (float)Dormancy.SatFloor, (float)Dormancy.BrightFloor);

        for (var x = 0; x < samples.Length; x++)
        {
            var expected = PlaceholderArt.ToFloor(samples[x]);
            var actual = floor.GetPixel(x, 0);

            Assert.InRange(actual.Red, expected.R - 2, expected.R + 2);
            Assert.InRange(actual.Green, expected.G - 2, expected.G + 2);
            Assert.InRange(actual.Blue, expected.B - 2, expected.B + 2);
        }
    }

    /// <summary>
    /// The clamp is the point of §5.1: a floored cover must still be
    /// identifiable, never flat grey. Distinct hues must stay distinct.
    /// </summary>
    [Fact]
    public void Floor_variant_keeps_covers_identifiable()
    {
        using var source = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Opaque));
        source.SetPixel(0, 0, new SKColor(0xE0, 0x3A, 0x2C));
        source.SetPixel(1, 0, new SKColor(0x18, 0x2C, 0xA0));

        // Read the floor from the shared constants rather than literals: this
        // test's job is to prove the two implementations agree, so retuning the
        // floor must move both sides together or fail loudly — not silently
        // compare the new maths against a stale hard-coded endpoint.
        using var floor = CoverImaging.ApplyFloor(
            source, (float)Dormancy.SatFloor, (float)Dormancy.BrightFloor);
        var a = floor.GetPixel(0, 0);
        var b = floor.GetPixel(1, 0);

        Assert.NotEqual(a, b);
        // Still carrying chroma, not collapsed to luma.
        Assert.True(Math.Abs(a.Red - a.Blue) > 8);
    }

    [Fact]
    public void Decode_targets_display_resolution_not_source_resolution()
    {
        var capsule = TestArt.Capsule(1200, 1800);

        using var decoded = CoverImaging.DecodeToWidth(capsule, 320);

        Assert.NotNull(decoded);
        Assert.Equal(320, decoded.Width);
        Assert.Equal(480, decoded.Height);
        // 1200x1800 BGRA would be 8.2 MB; a grid of these is §5.4's failure mode.
        Assert.True(decoded.ByteCount < 700_000);
    }

    [Fact]
    public void Decode_never_upscales_past_the_source()
    {
        var capsule = TestArt.Capsule(600, 900);

        using var decoded = CoverImaging.DecodeToWidth(capsule, 640);

        Assert.NotNull(decoded);
        Assert.Equal(600, decoded.Width);
    }

    [Theory]
    [InlineData(148, 160)]   // 148 DIP tile at 1x
    [InlineData(160, 160)]
    [InlineData(222, 240)]   // 1.5x
    [InlineData(296, 320)]   // 2x
    [InlineData(592, 640)]   // 4x
    [InlineData(2000, 640)]  // clamped: no display needs more than the capsule holds
    public void Display_widths_snap_to_a_finite_set_of_buckets(double requested, int expected)
        => Assert.Equal(expected, CoverImaging.SnapWidth(requested));

    [Fact]
    public void Cache_stem_sanitizes_provider_ids()
    {
        Assert.Equal("steam_220", CoverKey.Steam("220").CacheStem);
        // Ids come from external data: nothing may escape the cache directory.
        var hostile = new CoverKey("steam", @"../../etc").CacheStem;
        Assert.Equal("steam_______etc", hostile);
        Assert.DoesNotContain('.', hostile);
        Assert.DoesNotContain('/', hostile);
    }
}
