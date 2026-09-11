using SkiaSharp;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class CoverDecodeFailureTests
{
    [Fact]
    public async Task A_failed_floor_decode_releases_the_already_decoded_native_vivid_layer()
    {
        using var directory = new TempCoverDirectory();
        var options = directory.Options();
        var disk = new CoverDiskCache(options);
        var key = CoverKey.Steam("42");
        disk.WriteSource(key, [1]);
        disk.WriteFloor(key, [2]);
        using var vivid = new SKBitmap(160, 240);
        var calls = 0;
        using var pipeline = new CoverPipeline([], disk, options, null, (_, _) => ++calls == 1
            ? vivid : throw new InvalidOperationException("Injected floor decoder failure."));

        Assert.Null(await pipeline.GetAsync(key, 160));
        Assert.Equal(2, calls);
        Assert.Equal(IntPtr.Zero, vivid.Handle);
        Assert.False(pipeline.IsKnownMissing(key));
    }
}
