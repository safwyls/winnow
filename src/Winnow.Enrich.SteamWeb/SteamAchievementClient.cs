using System.Globalization;
using System.Text.Json;
using Winnow.Core.Domain;
using Winnow.Enrich.SteamWeb.Credentials;

namespace Winnow.Enrich.SteamWeb;

public interface ISteamAchievementClient
{
    Task<AchievementFetch> FetchAsync(SteamId account, uint appId, SteamApiKey key, CancellationToken ct = default);
}

/// <summary>Official user-key endpoints; a session token is not substituted for their API-key contract.</summary>
public sealed class SteamAchievementClient(HttpClient http, TimeProvider clock) : ISteamAchievementClient
{
    public async Task<AchievementFetch> FetchAsync(SteamId account, uint appId, SteamApiKey key, CancellationToken ct = default)
    {
        if (appId == 0) throw new ArgumentOutOfRangeException(nameof(appId));
        var result = new AchievementFetch { AttemptedAt = clock.GetUtcNow().UtcDateTime };
        var credential = SteamCredential.FromApiKey(key)!;
        var app = appId.ToString(CultureInfo.InvariantCulture);
        var schemaBody = await ReadAsync(credential.AppendTo($"ISteamUserStats/GetSchemaForGame/v2/?appid={app}&l=english"), ct);
        var schema = ParseSchema(schemaBody);
        if (schema is null) return result;
        result = result with { Schema = schema };
        if (schema.Count == 0) return result;
        var progressBody = await ReadAsync(credential.AppendTo(
            $"ISteamUserStats/GetPlayerAchievements/v1/?appid={app}&steamid={account}&l=english"), ct);
        var globalBody = await ReadAsync($"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={app}", ct);
        return result with
        {
            Unlocks = ParseUnlocks(progressBody, account, schema, result.AttemptedAt),
            GlobalPercentages = ParseGlobals(globalBody, schema),
        };
    }

    private async Task<string?> ReadAsync(string uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(ct) : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { return null; }
    }

    private static T? Parse<T>(string? body, Func<JsonElement, T?> read) where T : class
    {
        if (body is null) return null;
        try { using var doc = JsonDocument.Parse(body); return read(doc.RootElement); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException or KeyNotFoundException)
        { return null; }
    }

    private static IReadOnlyList<AchievementDefinition>? ParseSchema(string? body)
        => Parse<IReadOnlyList<AchievementDefinition>>(body, root =>
        {
            if (!root.TryGetProperty("game", out var game) || game.ValueKind != JsonValueKind.Object
                || !game.TryGetProperty("gameName", out var title) || string.IsNullOrWhiteSpace(title.GetString())) return null;
            // Only an explicit empty achievement list confirms absence; omitted fields remain unanswered.
            if (!game.TryGetProperty("availableGameStats", out var stats)) return null;
            if (!stats.TryGetProperty("achievements", out var list)) return null;
            var rows = new List<AchievementDefinition>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name) || !keys.Add(name)) return null;
                var display = item.TryGetProperty("displayName", out var value) ? value.GetString() : name;
                var description = item.TryGetProperty("description", out value) ? value.GetString() : null;
                rows.Add(new(name, string.IsNullOrWhiteSpace(display) ? name : display, description,
                    item.TryGetProperty("hidden", out value) && value.GetInt32() == 1));
            }
            return rows;
        });

    private static IReadOnlyDictionary<string, DateTime?>? ParseUnlocks(string? body, SteamId account,
        IReadOnlyList<AchievementDefinition> schema, DateTime observedAt)
        => Parse<IReadOnlyDictionary<string, DateTime?>>(body, root =>
        {
            if (!root.TryGetProperty("playerstats", out var stats)
                || !stats.TryGetProperty("success", out var success) || !success.GetBoolean()
                || !stats.TryGetProperty("steamID", out var identity) || identity.GetString() != account.ToString()
                || !stats.TryGetProperty("achievements", out var list)) return null;
            var expected = schema.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unlocks = new Dictionary<string, DateTime?>(StringComparer.Ordinal);
            foreach (var item in list.EnumerateArray())
            {
                var name = item.GetProperty("apiname").GetString();
                var achieved = item.GetProperty("achieved").GetInt32();
                if (name is null || !expected.Contains(name) || !seen.Add(name) || achieved is < 0 or > 1) return null;
                if (achieved == 0) continue;
                var unix = item.TryGetProperty("unlocktime", out var time) ? time.GetInt64() : 0;
                var at = unix > 0 ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : (DateTime?)null;
                if (unix < 0 || at > observedAt) return null;
                unlocks.Add(name, at);
            }
            return seen.SetEquals(expected) ? unlocks : null;
        });

    private static IReadOnlyDictionary<string, double>? ParseGlobals(string? body, IReadOnlyList<AchievementDefinition> schema)
        => Parse<IReadOnlyDictionary<string, double>>(body, root =>
        {
            if (!root.TryGetProperty("achievementpercentages", out var stats)
                || !stats.TryGetProperty("achievements", out var list)) return null;
            var expected = schema.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            var percentages = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var item in list.EnumerateArray())
            {
                var name = item.GetProperty("name").GetString();
                var percent = item.GetProperty("percent").GetDouble();
                if (name is null || !double.IsFinite(percent) || percent is < 0 or > 100) return null;
                if (expected.Contains(name) && !percentages.TryAdd(name, percent)) return null;
            }
            return percentages.Count == expected.Count ? percentages : null;
        });
}
