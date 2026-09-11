using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Winnow.PluginSdk;

namespace Winnow.Plugin.SteamGridDb;

/// <summary>An artwork provider using only the public SDK and host-managed services.</summary>
public sealed partial class SteamGridDbPlugin : IArtworkProviderPlugin
{
    public const int PayloadVersion = 1;
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock;
    private readonly object _availabilityGate = new();
    private string? _pausedFingerprint;
    private DateTimeOffset _pausedUntil;
    private IPluginContext? _context;

    public SteamGridDbPlugin() : this(TimeProvider.System) { }

    public SteamGridDbPlugin(TimeProvider clock) => _clock = clock;

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        return ValueTask.CompletedTask;
    }

    public async Task<IReadOnlyList<PluginArtwork>?> GetArtworkAsync(PluginGame game, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = _context ?? throw new InvalidOperationException("The plugin has not been initialized.");
        if (!game.ExternalIds.TryGetValue("steam", out var appId) || string.IsNullOrEmpty(appId)
            || appId.Length > 10 || !appId.All(char.IsAsciiDigit)
            || !uint.TryParse(appId, CultureInfo.InvariantCulture, out var numericId) || numericId == 0) return null;

        var cacheKey = "heroes:steam:" + appId;
        var cached = await context.Cache.GetAsync(cacheKey, cancellationToken);
        var payload = ReadCache(cached);
        var now = _clock.GetUtcNow();
        if (payload is { Version: PayloadVersion, Images: not null } && cached!.ExpiresAt > now)
            return payload.Images.Select(ToArtwork).ToArray();
        var fallback = payload?.Images is { Count: > 0 } previous ? previous.Select(ToArtwork).ToArray() : null;
        var key = (await context.Secrets.GetAsync("apikey", cancellationToken))?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || IsPaused(key, now)) return fallback;

        try
        {
            var request = new PluginHttpRequest(
                $"https://www.steamgriddb.com/api/v2/heroes/steam/{appId}?types=static&nsfw=false&humor=false&epilepsy=false")
            {
                Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + key },
            };
            var response = await context.Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != 404 && response.StatusCode is < 200 or >= 300)
            {
                Pause(key, response.StatusCode is 401 or 403);
                return fallback;
            }

            IReadOnlyList<CachedImage> images;
            if (response.StatusCode == 404) images = [];
            else
            {
                if (response.Body.Length > MaxResponseBytes) throw new InvalidDataException();
                using var document = JsonDocument.Parse(response.Body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                    || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    Pause(key, unauthorized: false);
                    return fallback;
                }
                images = data.EnumerateArray().Select(Project).OfType<CachedImage>()
                    .DistinctBy(image => image.ImageId, StringComparer.Ordinal).ToArray();
            }

            // This envelope also reads the original built-in provider's cache during migration.
            await context.Cache.SetAsync(cacheKey, new PluginCacheEntry(
                JsonSerializer.SerializeToUtf8Bytes(new Payload(PayloadVersion, images), JsonOptions), now + CacheTtl), cancellationToken);
            return images.Select(ToArtwork).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
        {
            Pause(key, unauthorized: false);
            return fallback;
        }
    }

    private static Payload? ReadCache(PluginCacheEntry? entry)
    {
        if (entry is null || entry.Payload.Length > MaxResponseBytes) return null;
        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(entry.Payload, JsonOptions);
            return payload?.Images is { } images && images.All(image => image is not null && IsSuitable(image)) ? payload : null;
        }
        catch (JsonException) { return null; }
    }

    private static PluginArtwork ToArtwork(CachedImage image)
        => new(image.ImageId, image.Url, image.Width, image.Height) { ImageType = "hero" };

    private static CachedImage? Project(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var assetId) || assetId <= 0
            || !row.TryGetProperty("width", out var width) || width.ValueKind != JsonValueKind.Number || !width.TryGetInt32(out var w)
            || !row.TryGetProperty("height", out var height) || height.ValueKind != JsonValueKind.Number || !height.TryGetInt32(out var h)
            || !row.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) return null;
        var image = new CachedImage(assetId.ToString(CultureInfo.InvariantCulture), url.GetString()!, w, h);
        if (!IsSuitable(image)) return null;
        foreach (var flag in new[] { "nsfw", "humor", "epilepsy", "animated" })
            if (row.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True) return null;
        if (row.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
            && tags.EnumerateArray().Any(tag => tag.ValueKind == JsonValueKind.String
                && tag.GetString()?.ToLowerInvariant() is "nsfw" or "humor" or "epilepsy" or "animated")) return null;
        if (row.TryGetProperty("mime", out var mime) && mime.ValueKind == JsonValueKind.String
            && mime.GetString() is not ("image/png" or "image/jpeg" or "image/webp")) return null;
        return image;
    }

    private static bool IsSuitable(CachedImage image)
        => !string.IsNullOrEmpty(image.ImageId) && long.TryParse(image.ImageId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            && image.Width > image.Height && image.Height > 0 && image.Width <= 8192 && image.Height <= 8192
            && (long)image.Width * image.Height <= 32 * 1024 * 1024 && !image.Animated
            && image.Url is not null && SafeUrl().IsMatch(image.Url);

    private bool IsPaused(string key, DateTimeOffset now)
    {
        lock (_availabilityGate) return _pausedFingerprint == Fingerprint(key) && now < _pausedUntil;
    }

    private void Pause(string key, bool unauthorized)
    {
        lock (_availabilityGate)
        {
            _pausedFingerprint = Fingerprint(key);
            _pausedUntil = _clock.GetUtcNow() + (unauthorized ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30));
        }
    }

    private static string Fingerprint(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    [GeneratedRegex("^https://cdn2\\.steamgriddb\\.com/hero/[a-fA-F0-9]{32}\\.(png|jpg|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeUrl();

    private sealed record Payload(int Version, IReadOnlyList<CachedImage>? Images);
    private sealed record CachedImage(string ImageId, string Url, int Width, int Height)
    {
        public bool Animated { get; init; }
        public string ImageType { get; init; } = "hero";
    }
}
