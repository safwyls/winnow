using System.Buffers.Binary;
using System.Text.Json;
using Winnow.PluginSdk;

namespace Winnow.Plugin.Psn;

internal sealed class PsnArtworkClient(IPluginContext context, TimeProvider clock)
{
    internal async Task<IReadOnlyList<PluginArtwork>?> GetAsync(string? url, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (url is null) return [];
        if (!Allowed(url)) return null;
        var key = "image-dimensions:v1:" + PsnAccountClient.Hash(url);
        Dimensions? prior = null;
        try
        {
            var cached = await context.Cache.GetAsync(key, ct);
            if (cached is { Payload.Length: <= 1024 })
            {
                try { prior = JsonSerializer.Deserialize<Dimensions>(cached.Payload); }
                catch (JsonException) { }
                if (prior is not null && !Valid(prior)) prior = null;
                if (prior is not null && cached.ExpiresAt > clock.GetUtcNow()) return Artwork(url, prior);
            }
            // The icon endpoints omit dimensions. Probe the actual bytes rather than inventing a cover size.
            var response = await context.Http.SendAsync(new(url)
            { Headers = new Dictionary<string, string> { ["Range"] = "bytes=0-65535", ["Accept"] = "image/png,image/jpeg,image/webp" } }, ct);
            if (response.StatusCode is not (200 or 206) || ReadDimensions(response.Body) is not { } dimensions) return prior is null ? null : Artwork(url, prior);
            await context.Cache.SetAsync(key, new(JsonSerializer.SerializeToUtf8Bytes(dimensions), clock.GetUtcNow().AddDays(30)), ct);
            return Artwork(url, dimensions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (PsnAccountClient.SoftFailure(ex) || ex is OperationCanceledException)
        { return prior is null ? null : Artwork(url, prior); }
    }

    internal static bool Allowed(string url) => url.Length <= 4096 && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && uri.IdnHost is "image.api.playstation.com" or "psnobj.prod.dl.playstation.net";

    internal static Dimensions? ReadDimensions(ReadOnlySpan<byte> bytes)
    {
        Dimensions? dimensions = null;
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) && bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            dimensions = new(BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4)), BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4)), "image/png");
        else if (bytes.Length >= 30 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            if (bytes.Slice(12, 4).SequenceEqual("VP8X"u8) && (bytes[20] & 2) == 0)
                dimensions = new(1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16), 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16), "image/webp");
            else if (bytes.Slice(12, 4).SequenceEqual("VP8 "u8) && bytes.Slice(23, 3).SequenceEqual(new byte[] { 157, 1, 42 }))
                dimensions = new(BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26, 2)) & 0x3fff, BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28, 2)) & 0x3fff, "image/webp");
        }
        else if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216)
        {
            var offset = 2;
            while (offset + 4 <= bytes.Length)
            {
                if (bytes[offset++] != 255) break;
                while (offset < bytes.Length && bytes[offset] == 255) offset++;
                if (offset + 3 > bytes.Length) break;
                var marker = bytes[offset++];
                if (marker is 217 or 218) break;
                if (marker is 1 or >= 208 and <= 215) continue;
                var size = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
                if (size < 2 || offset + size > bytes.Length) break;
                if (marker is 192 or 193 or 194 && size >= 8)
                {
                    dimensions = new(BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2)), "image/jpeg");
                    break;
                }
                offset += size;
            }
        }
        return dimensions is not null && Valid(dimensions) ? dimensions : null;
    }

    private static bool Valid(Dimensions value) => value.Width is >= 16 and <= 16384 && value.Height is >= 16 and <= 16384
        && value.Mime is "image/png" or "image/jpeg" or "image/webp";
    private static PluginArtwork[] Artwork(string url, Dimensions size) =>
        [new("icon:" + PsnAccountClient.Hash(url), url, size.Width, size.Height) { Kind = PluginArtworkKind.Cover, ImageType = size.Mime }];
    internal sealed record Dimensions(int Width, int Height, string Mime);
}
