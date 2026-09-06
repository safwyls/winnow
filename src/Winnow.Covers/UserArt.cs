using System.Security.Cryptography;

namespace Winnow.Covers;

/// <summary>
/// The <c>winnow://user-art/&lt;token&gt;</c> shape a stored art field holds.
/// Strict parsing for the same reason <c>IgdbImageUrl.ImageId</c> is strict:
/// a guessed token becomes a miss, and a miss becomes placeholder art.
/// </summary>
public static class UserArtRef
{
    public const string Prefix = "winnow://user-art/";

    public static string Format(string token) => Prefix + token;

    public static string? Token(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var token = trimmed[Prefix.Length..];
        if (token.Length is 0 or > 64)
        {
            return null;
        }

        foreach (var c in token)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                return null;
            }
        }

        return token;
    }
}

public enum UserArtImportFailure
{
    None,

    FileNotFound,

    Unreadable,

    TooLarge,

    NotAnImage,

    BadUrl,

    DownloadFailed,
}

public readonly record struct UserArtImport(string? Reference, UserArtImportFailure Failure)
{
    public bool Ok => Reference is not null;

    public static UserArtImport Imported(string reference) => new(reference, UserArtImportFailure.None);

    public static UserArtImport Failed(UserArtImportFailure failure) => new(null, failure);
}

/// <summary>
/// Imports a local file or a URL into
/// <c>&lt;CoverCacheOptions.CacheDirectory&gt;/user/&lt;token&gt;.img</c>.
/// <see cref="CoverCacheOptions.CacheDirectory"/> is <c>&lt;data-dir&gt;/covers</c>,
/// so <c>--data-dir</c> is honoured with no new path to keep in step.
///
/// <para>The token is the first 32 hex characters of the SHA-256 of the
/// bytes, so importing the same picture twice writes one file and reuses it.
/// Bytes are sniffed for JPEG/PNG/GIF/BMP/WEBP magic and capped at 16 MB —
/// a store page saved as HTML is a refusal, not a corrupt tile.</para>
///
/// <para>The file the user chose is opened read-only and never written —
/// the same rule AGENTS.md states for store files. Writes are
/// temp-file-plus-move, matching <see cref="CoverDiskCache"/>, so a kill
/// mid-write never leaves a truncated image under a name that says it is
/// complete. Every failure is a returned <see cref="UserArtImportFailure"/>,
/// never a throw.</para>
/// </summary>
public sealed class UserArtStore
{
    public const string HttpClientName = "winnow.user-art";

    public const long MaxBytes = 16L * 1024 * 1024;

    private readonly CoverCacheOptions _options;
    private readonly Func<HttpClient>? _httpClient;

    public UserArtStore(CoverCacheOptions options, Func<HttpClient>? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient;
    }

    public string Root => Path.Combine(_options.CacheDirectory, "user");

    public string PathFor(string token) => Path.Combine(Root, token + ".img");

    public bool TryRead(string token, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var path = PathFor(token);
            if (!File.Exists(path))
            {
                return false;
            }

            bytes = File.ReadAllBytes(path);
            return bytes.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task<UserArtImport> ImportFileAsync(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return UserArtImport.Failed(UserArtImportFailure.FileNotFound);
        }

        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return UserArtImport.Failed(UserArtImportFailure.FileNotFound);
            }

            if (info.Length > MaxBytes)
            {
                return UserArtImport.Failed(UserArtImportFailure.TooLarge);
            }

            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }
        catch (IOException)
        {
            return UserArtImport.Failed(UserArtImportFailure.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return UserArtImport.Failed(UserArtImportFailure.Unreadable);
        }
        catch (ArgumentException)
        {
            return UserArtImport.Failed(UserArtImportFailure.FileNotFound);
        }
        catch (NotSupportedException)
        {
            return UserArtImport.Failed(UserArtImportFailure.FileNotFound);
        }

        return Store(bytes);
    }

    public async Task<UserArtImport> ImportUrlAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return UserArtImport.Failed(UserArtImportFailure.BadUrl);
        }

        if (_httpClient is null)
        {
            return UserArtImport.Failed(UserArtImportFailure.DownloadFailed);
        }

        byte[] bytes;
        try
        {
            var client = _httpClient();
            using var response = await client
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UserArtImport.Failed(UserArtImportFailure.DownloadFailed);
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return UserArtImport.Failed(UserArtImportFailure.TooLarge);
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();

            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxBytes)
                {
                    return UserArtImport.Failed(UserArtImportFailure.TooLarge);
                }

                buffer.Write(chunk, 0, read);
            }

            bytes = buffer.ToArray();
        }
        catch (HttpRequestException)
        {
            return UserArtImport.Failed(UserArtImportFailure.DownloadFailed);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return UserArtImport.Failed(UserArtImportFailure.DownloadFailed);
        }

        return Store(bytes);
    }

    private UserArtImport Store(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return UserArtImport.Failed(UserArtImportFailure.Unreadable);
        }

        if (!LooksLikeImage(bytes))
        {
            return UserArtImport.Failed(UserArtImportFailure.NotAnImage);
        }

        var token = Convert.ToHexStringLower(SHA256.HashData(bytes))[..32];
        var path = PathFor(token);

        try
        {
            Directory.CreateDirectory(Root);
            if (!File.Exists(path))
            {
                var temp = path + "." + Guid.NewGuid().ToString("n") + ".tmp";
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
        }
        catch (IOException)
        {
            return UserArtImport.Failed(UserArtImportFailure.Unreadable);
        }
        catch (UnauthorizedAccessException)
        {
            return UserArtImport.Failed(UserArtImportFailure.Unreadable);
        }

        return UserArtImport.Imported(UserArtRef.Format(token));
    }

    internal static bool LooksLikeImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        // JPEG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }

        // PNG
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return true;
        }

        // GIF87a / GIF89a
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            return true;
        }

        // BMP
        if (bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return true;
        }

        // RIFF....WEBP
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// An ordinary <see cref="ICoverSource"/> over the <c>user</c> provider, so
/// user art reaches a tile through the same pipeline, disk cache and leases
/// as a Steam capsule. Handles only <c>user:</c> keys, so registering it
/// does not change the <see cref="CoverSourceSet"/> identity for any steam
/// or igdb key and invalidates no existing negative marker.
/// </summary>
public sealed class UserArtCoverSource : ICoverSource
{
    private readonly UserArtStore _store;

    public UserArtCoverSource(UserArtStore store) => _store = store;

    public string Name => "user-art";

    public bool CanHandle(CoverKey key)
        => string.Equals(key.Provider, CoverProviders.User, StringComparison.Ordinal);

    public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
        => Task.FromResult(_store.TryRead(key.Id, out var bytes) ? bytes : null);
}
