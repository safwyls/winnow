using System.Buffers.Binary;
using System.Text.Json;
using Winnow.Plugin.Psn;
using Winnow.PluginSdk;
using Xunit;

namespace Winnow.Plugin.Psn.Tests;

public sealed class PsnArtworkClientTests
{
    private const string Url = "https://image.api.playstation.com/vulcan/example.png";

    [Fact]
    public async Task Cover_uses_observed_dimensions_and_caches_them_without_credentials()
    {
        var host = new Host();
        var client = new PsnArtworkClient(host, TimeProvider.System);
        var result = Assert.Single((await client.GetAsync(Url, default))!);
        Assert.Equal((640, 640, PluginArtworkKind.Cover), (result.Width, result.Height, result.Kind));
        Assert.Equal("image/png", result.ImageType);
        Assert.Single((await client.GetAsync(Url, default))!);
        Assert.Single(host.Requests);
        Assert.False(host.Requests[0].Headers.ContainsKey("Authorization"));
        Assert.Equal("bytes=0-65535", host.Requests[0].Headers["Range"]);
    }

    [Theory]
    [InlineData("https://image.api.playstation.com.attacker.example/x.png")]
    [InlineData("http://image.api.playstation.com/x.png")]
    [InlineData("https://name@image.api.playstation.com/x.png")]
    [InlineData("https://image.api.playstation.com:444/x.png")]
    [InlineData("https://image.api.playstation.com/x.png#fragment")]
    public async Task Untrusted_images_never_make_network_requests(string url)
    {
        var host = new Host();
        Assert.Null(await new PsnArtworkClient(host, TimeProvider.System).GetAsync(url, default));
        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task Unavailable_or_malformed_images_preserve_previous_artwork()
    {
        var host = new Host { Body = "not an image"u8.ToArray() };
        Assert.Null(await new PsnArtworkClient(host, TimeProvider.System).GetAsync(Url, default));
        host.Cached = new(JsonSerializer.SerializeToUtf8Bytes(new PsnArtworkClient.Dimensions(100, 200, "image/jpeg")), DateTimeOffset.MinValue);
        var result = Assert.Single((await new PsnArtworkClient(host, TimeProvider.System).GetAsync(Url, default))!);
        Assert.Equal((100, 200), (result.Width, result.Height));
    }

    [Fact]
    public void Malformed_truncated_or_oversized_headers_are_rejected()
    {
        Assert.Null(PsnArtworkClient.ReadDimensions([]));
        Assert.Null(PsnArtworkClient.ReadDimensions(Png(0, 1)));
        Assert.Null(PsnArtworkClient.ReadDimensions(Png(int.MaxValue, 512)));
        Assert.Null(PsnArtworkClient.ReadDimensions(new byte[] { 255, 216, 255, 192, 0, 8, 8 }));
        Assert.Null(PsnArtworkClient.ReadDimensions(new byte[] { 255, 216, 255, 255 }));
    }

    [Fact]
    public void Jpeg_and_static_webp_dimensions_are_read_but_animated_webp_is_not_offered()
    {
        var jpg = new byte[] { 255, 216, 255, 192, 0, 8, 8, 1, 0, 2, 0, 1 };
        Assert.Equal(new(512, 256, "image/jpeg"), PsnArtworkClient.ReadDimensions(jpg));
        var webp = new byte[30];
        "RIFF"u8.CopyTo(webp); "WEBP"u8.CopyTo(webp.AsSpan(8)); "VP8X"u8.CopyTo(webp.AsSpan(12));
        webp[24] = 255; webp[25] = 1; webp[27] = 255;
        Assert.Equal(new(512, 256, "image/webp"), PsnArtworkClient.ReadDimensions(webp));
        webp[20] = 2;
        Assert.Null(PsnArtworkClient.ReadDimensions(webp));
    }

    [Fact]
    public async Task Cancellation_stays_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PsnArtworkClient(new Host(), TimeProvider.System).GetAsync(Url, cancellation.Token));
    }

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private sealed class Host : IPluginContext, IPluginHttp, IPluginCache
    {
        public string PluginId => "psn";
        public IPluginSettings Settings => throw new InvalidOperationException();
        public IPluginSecrets Secrets => throw new InvalidOperationException();
        public IPluginCache Cache => this;
        public IPluginHttp Http => this;
        public byte[] Body { get; set; } = Png(640, 640);
        public List<PluginHttpRequest> Requests { get; } = [];
        public PluginCacheEntry? Cached { get; set; }
        public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken cancellationToken)
        { Requests.Add(request); return Task.FromResult(new PluginHttpResponse(206, Body, new Dictionary<string, string>())); }
        public ValueTask<PluginCacheEntry?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(Cached);
        public ValueTask SetAsync(string key, PluginCacheEntry entry, CancellationToken cancellationToken) { Cached = entry; return ValueTask.CompletedTask; }
    }
}
