using System.Globalization;
using System.Text;
using System.Text.Json;
using Winnow.Core.Lifecycle;
using Winnow.Enrich.Igdb.Credentials;
using Winnow.Enrich.Igdb.Storage;

namespace Winnow.Enrich.Igdb;

public sealed record IgdbLifecycleSnapshot(DateTime ObservedAt, LifecycleSignals Signals, string RawJson);

public interface IIgdbLifecycleClient
{
    Task<IgdbLifecycleSnapshot?> GetAsync(long gameId, CancellationToken ct = default);
}

/// <summary>A separate cache namespace keeps lifecycle changes independent of catalog payload versions.</summary>
public sealed class IgdbLifecycleClient(HttpClient http, IMetadataCache cache,
    IIgdbCredentialProvider credentials, TimeProvider clock) : IIgdbLifecycleClient
{
    public const string CacheProvider = "igdb-lifecycle-v1";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public async Task<IgdbLifecycleSnapshot?> GetAsync(long gameId, CancellationToken ct = default)
    {
        if (gameId <= 0) return null;
        var key = gameId.ToString(CultureInfo.InvariantCulture);
        var cached = await cache.GetAsync(CacheProvider, key, ct);
        if (cached is { PayloadJson: not null } hit && hit.FetchedAt >= clock.GetUtcNow().UtcDateTime - CacheTtl)
            return Parse(hit.PayloadJson, hit.FetchedAt, gameId);
        if (await credentials.GetAsync(ct) is null) return null;
        try
        {
            using var body = new StringContent($"fields game_status.status,game_modes.name; where id = {key}; limit 1;", Encoding.UTF8, "text/plain");
            using var response = await http.PostAsync("games", body, ct);
            if (!response.IsSuccessStatusCode) return null;
            var raw = await response.Content.ReadAsStringAsync(ct);
            var observed = clock.GetUtcNow().UtcDateTime;
            var result = Parse(raw, observed, gameId);
            if (result is not null) await cache.SetAsync(CacheProvider, key, raw, observed, ct);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static IgdbLifecycleSnapshot? Parse(string raw, DateTime observed, long gameId)
    {
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var game in json.RootElement.EnumerateArray())
            {
                if (!game.TryGetProperty("id", out var id) || !id.TryGetInt64(out var value) || value != gameId) continue;
                string? status = null;
                if (game.TryGetProperty("game_status", out var state) && state.ValueKind == JsonValueKind.Object &&
                    state.TryGetProperty("status", out var name) && name.ValueKind == JsonValueKind.String)
                    status = name.GetString()?.Trim().ToLowerInvariant().Replace(' ', '_');
                bool? multiplayer = null;
                bool? singlePlayer = null;
                if (game.TryGetProperty("game_modes", out var modes) && modes.ValueKind == JsonValueKind.Array)
                {
                    var names = modes.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                        .Select(m => m.GetProperty("name").GetString()).ToArray();
                    if (names.Length > 0 && names.Length == modes.GetArrayLength() && names.All(n => !string.IsNullOrWhiteSpace(n)))
                    {
                        multiplayer = names.Any(n => n is not null && (n.Contains("Multiplayer", StringComparison.OrdinalIgnoreCase) || n.Contains("Co-operative", StringComparison.OrdinalIgnoreCase)));
                        singlePlayer = names.Any(n => string.Equals(n, "Single player", StringComparison.OrdinalIgnoreCase));
                    }
                }
                return new(observed, new LifecycleSignals { IgdbStatus = status, IsMultiplayer = multiplayer, HasSinglePlayer = singlePlayer,
                    IsUnfinished = status is null ? null : status is "early_access" or "alpha" or "beta" }, raw);
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return null; }
    }
}
