using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Winnow.Core.Domain;
using Winnow.Data;

namespace Winnow.Enrich.SteamGridDb;

public interface ISteamGridDbClient
{
    ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default);
    /// <summary>Null means unavailable; an empty list is a confirmed absence of suitable heroes.</summary>
    Task<IReadOnlyList<GameImage>?> GetHeroesAsync(string steamAppId, CancellationToken ct = default);
}

/// <summary>Short credential-specific pause prevents an invalid key from failing for every game in a pass.</summary>
public sealed class SteamGridDbAvailability
{
    private readonly object _gate = new();
    private string? _fingerprint;
    private DateTimeOffset _until;

    public bool IsPaused(string key, DateTimeOffset now)
    {
        lock (_gate) return _fingerprint == Fingerprint(key) && now < _until;
    }

    public void Pause(string key, DateTimeOffset now, bool unauthorized)
    {
        lock (_gate)
        {
            _fingerprint = Fingerprint(key);
            _until = now + (unauthorized ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30));
        }
    }

    private static string Fingerprint(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}

public sealed partial class SteamGridDbClient(
    HttpClient http,
    ISteamGridDbKeyProvider credentials,
    ISqliteConnectionFactory factory,
    SteamGridDbOptions options,
    SteamGridDbAvailability availability,
    TimeProvider clock) : ISteamGridDbClient
{
    public const string HttpClientName = "steamgriddb";
    public const string CacheProvider = "steamgriddb";
    public const int PayloadVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string CacheKey(string steamAppId) => "heroes:steam:" + steamAppId;

    public async ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
        => !string.IsNullOrWhiteSpace(await credentials.GetApiKeyAsync(ct));

    public async Task<IReadOnlyList<GameImage>?> GetHeroesAsync(string steamAppId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(steamAppId) || steamAppId.Length > 10 || !steamAppId.All(char.IsAsciiDigit)) return null;
        CacheRow? cached;
        using (var lease = factory.Lease())
            cached = await lease.Connection.QuerySingleOrDefaultAsync<CacheRow>(new CommandDefinition(
                "SELECT payload_json AS PayloadJson,fetched_at AS FetchedAt FROM metadata_cache WHERE provider=@provider AND provider_id=@id;",
                new { provider = CacheProvider, id = CacheKey(steamAppId) }, lease.Transaction, cancellationToken: ct));
        Payload? payload = null;
        try { if (cached?.PayloadJson is { } json) payload = JsonSerializer.Deserialize<Payload>(json, JsonOptions); }
        catch (JsonException) { }
        var now = clock.GetUtcNow();
        if (payload is { Version: PayloadVersion, Images: not null } && cached!.FetchedAt >= now.UtcDateTime - options.CacheTtl)
            return payload.Images;
        var fallback = payload?.Images is { Count: > 0 } old ? old : null;
        var key = await credentials.GetApiKeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key) || availability.IsPaused(key, now)) return fallback;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.BaseAddress,
                $"heroes/steam/{steamAppId}?types=static&nsfw=false&humor=false&epilepsy=false"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.NotFound && !response.IsSuccessStatusCode)
            {
                availability.Pause(key, clock.GetUtcNow(), response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
                return fallback;
            }

            IReadOnlyList<GameImage> images;
            if (response.StatusCode == HttpStatusCode.NotFound) images = [];
            else
            {
                using var document = JsonDocument.Parse(await ReadBoundedAsync(response.Content, ct));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                    || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    availability.Pause(key, clock.GetUtcNow(), unauthorized: false);
                    return fallback;
                }
                images = data.EnumerateArray().Select(Project).OfType<GameImage>()
                    .DistinctBy(image => image.ImageId, StringComparer.Ordinal).ToArray();
            }
            using var lease = factory.Lease();
            await lease.Connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO metadata_cache(provider,provider_id,payload_json,fetched_at) VALUES(@provider,@id,@json,@now)
                ON CONFLICT(provider,provider_id) DO UPDATE SET payload_json=excluded.payload_json,fetched_at=excluded.fetched_at;
                """, new { provider = CacheProvider, id = CacheKey(steamAppId), json = JsonSerializer.Serialize(new Payload(PayloadVersion, images), JsonOptions), now = now.UtcDateTime },
                lease.Transaction, cancellationToken: ct));
            return images;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
        {
            availability.Pause(key, clock.GetUtcNow(), unauthorized: false);
            return fallback;
        }
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > options.MaxResponseBytes) throw new InvalidDataException();
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, options.MaxResponseBytes - (int)buffer.Length + 1)), ct);
            if (read == 0) return buffer.ToArray();
            if (buffer.Length + read > options.MaxResponseBytes) throw new InvalidDataException();
            buffer.Write(chunk, 0, read);
        }
    }

    private static GameImage? Project(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var assetId) || assetId <= 0
            || !row.TryGetProperty("width", out var width) || width.ValueKind != JsonValueKind.Number || !width.TryGetInt32(out var w)
            || !row.TryGetProperty("height", out var height) || height.ValueKind != JsonValueKind.Number || !height.TryGetInt32(out var h)
            || w <= h || h <= 0 || w > 8192 || h > 8192 || (long)w * h > 32 * 1024 * 1024
            || !row.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String
            || !SafeUrl().IsMatch(url.GetString()!)) return null;
        foreach (var flag in new[] { "nsfw", "humor", "epilepsy", "animated" })
            if (row.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True) return null;
        if (row.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
            && tags.EnumerateArray().Any(tag => tag.ValueKind == JsonValueKind.String
                && tag.GetString()?.ToLowerInvariant() is "nsfw" or "humor" or "epilepsy" or "animated")) return null;
        if (row.TryGetProperty("mime", out var mime) && mime.ValueKind == JsonValueKind.String
            && mime.GetString() is not ("image/png" or "image/jpeg" or "image/webp")) return null;
        return new GameImage { ImageId = assetId.ToString(CultureInfo.InvariantCulture), Url = url.GetString(), Width = w, Height = h, Animated = false, ImageType = "hero" };
    }

    [GeneratedRegex("^https://cdn2\\.steamgriddb\\.com/hero/[a-fA-F0-9]{32}\\.(png|jpg|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeUrl();
    private sealed record Payload(int Version, IReadOnlyList<GameImage>? Images);
    private sealed class CacheRow { public string? PayloadJson { get; init; } public DateTime FetchedAt { get; init; } }
}
