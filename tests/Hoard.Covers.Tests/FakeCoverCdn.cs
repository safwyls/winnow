using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Hoard.Covers.Tests;

/// <summary>
/// A stand-in Steam CDN. No test in this project touches the network: the whole
/// point of a cover cache is what it does on the second launch, and that is only
/// observable if requests are countable.
/// </summary>
internal class FakeCoverCdn : HttpMessageHandler, IHttpClientFactory
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private int _requests;

    /// <summary>Every request that reached the "network", in order.</summary>
    public List<string> Requests { get; } = [];

    public int RequestCount => Volatile.Read(ref _requests);

    /// <summary>Serves <paramref name="jpeg"/> at the 2x capsule path for an appid.</summary>
    public void AddCapsule(string appId, byte[] jpeg)
        => _files[$"/steam/apps/{appId}/library_600x900_2x.jpg"] = jpeg;

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false)
    {
        BaseAddress = null,
        Timeout = TimeSpan.FromSeconds(5),
    };

    protected sealed override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => OnSendAsync(request, ct);

    protected virtual Task<HttpResponseMessage> OnSendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _requests);
        var path = request.RequestUri!.AbsolutePath;
        lock (Requests)
        {
            Requests.Add(path);
        }

        if (_files.TryGetValue(path, out var bytes))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            });
        }

        // Exactly what Steam does for a tool or a redistributable.
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal static class TestArt
{
    /// <summary>A synthetic 2x portrait capsule: real JPEG bytes, no network.</summary>
    public static byte[] Capsule(int width = 1200, int height = 1800)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(width, height),
                [new SKColor(0xE0, 0x3A, 0x2C), new SKColor(0x18, 0x2C, 0xA0)],
                null,
                SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(SKRect.Create(width, height), paint);
        }

        return CoverImaging.EncodeJpeg(bitmap, 92);
    }
}

internal sealed class TempCoverDirectory : IDisposable
{
    public TempCoverDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hoard-covers-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public CoverCacheOptions Options() => new()
    {
        CacheDirectory = Path,
        SteamCdnBaseUrl = "https://cdn.test.invalid/steam/apps",
        MaxConcurrentFetches = 2,
    };

    public CoverPipeline Pipeline(FakeCoverCdn cdn, CoverCacheOptions? options = null)
    {
        options ??= Options();
        var source = new SteamCapsuleSource(cdn, options, NullLogger<SteamCapsuleSource>.Instance);
        return new CoverPipeline([source], new CoverDiskCache(options), options, NullLogger<CoverPipeline>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
