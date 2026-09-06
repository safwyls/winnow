using System.Net;
using SkiaSharp;
using Winnow.Covers.Igdb;
using Xunit;

namespace Winnow.Covers.Tests;

public sealed class CoverHardeningTests
{
    [Fact]
    public async Task Same_pipeline_reopens_a_miss_when_IGDB_becomes_configured()
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var cdn = new FakeCoverCdn();
        var igdb = new FakeIgdbClient { Configured = false };
        igdb.AddCover("42", "co42");
        cdn.AddIgdbCover("co42", TestArt.Capsule(160, 240));
        using var pipeline = dir.Pipeline(options, new IgdbCoverSource(igdb, cdn, options,
            new IgdbCoverOptions { PrewarmFromNegativeMarkers = false, BatchLinger = TimeSpan.Zero }));
        var key = CoverKey.Steam("42");

        Assert.Null(await pipeline.GetAsync(key, 160));
        Assert.True(pipeline.IsKnownMissing(key));
        igdb.Configured = true;
        using var art = await pipeline.GetAsync(key, 160);
        Assert.NotNull(art);
        Assert.False(pipeline.IsKnownMissing(key));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_source_identity_reopens_memory_and_disk_misses(bool diskFirst)
    {
        using var dir = new TempCoverDirectory();
        var options = dir.Options();
        var source = new ChangingSource();
        var key = CoverKey.Steam("42");
        if (diskFirst)
        {
            new CoverDiskCache(options).MarkMissing(key, source.SourceSetId);
        }

        using var pipeline = dir.Pipeline(options, source);
        Assert.Null(await pipeline.GetAsync(key, 160));
        Assert.True(pipeline.IsKnownMissing(key));
        source.Available = true;
        Assert.False(pipeline.IsKnownMissing(key));
        using var art = await pipeline.GetAsync(key, 160);
        Assert.NotNull(art);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HTTP_sources_reject_oversize_without_buffering_the_body(bool igdb, bool declared)
    {
        using var dir = new TempCoverDirectory();
        using var cdn = new OversizeCdn(declared);
        ICoverSource source = igdb
            ? new IgdbCoverSource(new FakeIgdbClient(), cdn, dir.Options(), new IgdbCoverOptions())
            : new SteamCapsuleSource(cdn, dir.Options());
        var key = igdb ? new CoverKey(CoverProviders.Igdb, "co42") : CoverKey.Steam("42");
        await Assert.ThrowsAsync<InvalidDataException>(() => source.TryFetchAsync(key));
        Assert.Equal(declared ? 0 : CoverDownload.MaxBytes + 1, cdn.Body.ReadBytes);
        Assert.True(cdn.Body.Disposed);
    }

    [Theory]
    [InlineData(8193, 10)]
    [InlineData(10, 8193)]
    [InlineData(8192, 8192)]
    public void Image_header_dimensions_are_rejected_before_decode(int width, int height)
    {
        var bytes = TestArt.Capsule(16, 16);
        var frame = Enumerable.Range(0, bytes.Length - 9)
            .First(i => bytes[i] == 0xff && bytes[i + 1] == 0xc0);
        bytes[frame + 5] = (byte)(height >> 8);
        bytes[frame + 6] = (byte)height;
        bytes[frame + 7] = (byte)(width >> 8);
        bytes[frame + 8] = (byte)width;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
        Assert.Null(CoverImaging.DecodeToWidth(bytes, 160));
    }

    private sealed class ChangingSource : ICoverSource
    {
        public bool Available { get; set; }
        public string Name => "changing";
        public string SourceSetId => Available ? "available" : "unavailable";
        public bool CanHandle(CoverKey key) => true;
        public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
            => Task.FromResult(Available ? TestArt.Capsule(160, 240) : null);
    }

    private sealed class OversizeCdn(bool declared) : FakeCoverCdn
    {
        public CountingStream Body { get; } = new();
        protected override Task<HttpResponseMessage> OnSendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var content = new StreamContent(Body);
            if (declared)
            {
                content.Headers.ContentLength = CoverDownload.MaxBytes + 100L;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class CountingStream : Stream
    {
        public int ReadBytes { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            ReadBytes += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            buffer.Span.Clear();
            ReadBytes += buffer.Length;
            return ValueTask.FromResult(buffer.Length);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
