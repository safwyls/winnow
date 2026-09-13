using System.Net;
using SkiaSharp;

namespace Winnow.Covers;

internal static class SteamPublishedAssets
{
    internal static async Task<byte[]?> TryFetchAsync(HttpClient client, ISteamLibraryAssetLookup? lookup,
        CoverCacheOptions options, CoverKey key, CancellationToken ct)
    {
        if (lookup is null) return null;
        var paths = await lookup.GetPathsAsync(key, ct).ConfigureAwait(false)
            ?? throw new HttpRequestException("Steam library asset metadata is temporarily unavailable.");
        var malformed = false;
        foreach (var path in paths.Distinct(StringComparer.Ordinal).Take(8))
        {
            if (!IsSafePath(path)) { malformed = true; continue; }
            var url = $"{options.SteamAssetCdnBaseUrl.TrimEnd('/')}/{key.Id}/{path}";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) continue;
            response.EnsureSuccessStatusCode();
            var bytes = await CoverDownload.ReadAsync(response.Content, ct).ConfigureAwait(false);
            // Inspect only headers here. Pixel decoding belongs to the pipeline's
            // bounded decode stage, not to each concurrent network fetch.
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is not null && codec.Info.Width is > 0 and <= CoverImaging.MaxDimension
                && codec.Info.Height is > 0 and <= CoverImaging.MaxDimension
                && (long)codec.Info.Width * codec.Info.Height <= CoverImaging.MaxPixels) return bytes;
            throw new InvalidDataException("Steam library asset response was not a valid image.");
        }
        // Malformed metadata is not evidence that artwork does not exist.
        if (malformed) throw new InvalidDataException("Steam library asset metadata contained an invalid relative path.");
        return null;
    }

    private static bool IsSafePath(string? path)
        => path is { Length: > 0 and <= 512 }
           && path.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-' or '.')
           && path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..")
           && (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
}
