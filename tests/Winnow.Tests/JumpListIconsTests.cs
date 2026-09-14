using SkiaSharp;
using Winnow.App.Services;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

public sealed class JumpListIconsTests
{
    [Fact]
    public void Icon_contains_six_square_png_frames_from_the_center_of_the_cover()
    {
        using var source = new SKBitmap(80, 160);
        using (var canvas = new SKCanvas(source))
        {
            canvas.Clear(SKColors.Red);
            using var paint = new SKPaint { Color = SKColors.Lime };
            canvas.DrawRect(SKRect.Create(0, 40, 80, 80), paint);
        }
        var bytes = JumpListIcons.Encode(source);
        using var reader = new BinaryReader(new MemoryStream(bytes));
        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        Assert.Equal(6, reader.ReadUInt16());
        foreach (var size in new[] { 16, 24, 32, 48, 64, 128 })
        {
            Assert.Equal(size, reader.ReadByte());
            Assert.Equal(size, reader.ReadByte());
            reader.ReadUInt16();
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            var length = reader.ReadInt32();
            var offset = reader.ReadInt32();
            using var frame = SKBitmap.Decode(bytes.AsSpan(offset, length).ToArray());
            Assert.Equal(size, frame.Width);
            Assert.Equal(size, frame.Height);
            Assert.Equal(SKColors.Lime, frame.GetPixel(size / 2, size / 2));
        }
    }

    [Fact]
    public async Task Icons_are_cached_in_the_selected_directory_and_missing_art_falls_back()
    {
        var root = Path.Combine(Path.GetTempPath(), "winnow-icon-test-" + Guid.NewGuid().ToString("N"));
        var options = new CoverCacheOptions { CacheDirectory = Path.Combine(root, "covers") };
        using var pipeline = new CoverPipeline([new Source()], new CoverDiskCache(options), options);
        var icons = new JumpListIcons(root, pipeline);
        try
        {
            var path = await icons.GetAsync(new CoverKey("test", "game"), default);
            Assert.NotNull(path);
            Assert.Equal(Path.Combine(root, "jump-list-icons"), Path.GetDirectoryName(path));
            Assert.Equal(path, await icons.GetAsync(new CoverKey("test", "game"), default));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "jump-list-icons")));
            Assert.Null(await icons.GetAsync(null, default));
            Assert.Null(await icons.GetAsync(new CoverKey("missing", "game"), default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Source : ICoverSource
    {
        public string Name => "test";
        public bool CanHandle(CoverKey key) => key.Provider == "test";
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        {
            using var bitmap = new SKBitmap(64, 96);
            bitmap.Erase(SKColors.Blue);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return Task.FromResult<byte[]?>(data.ToArray());
        }
    }
}
