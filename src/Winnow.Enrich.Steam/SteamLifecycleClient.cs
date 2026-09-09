using System.Globalization;
using System.Text.Json;
using Winnow.Core.Lifecycle;
using Winnow.Enrich.Steam.Storage;

namespace Winnow.Enrich.Steam;

public sealed record SteamLifecycleSnapshot(string Source, DateTime ObservedAt, LifecycleSignals Signals, string RawJson);

public interface ISteamLifecycleClient
{
    Task<IReadOnlyList<SteamLifecycleSnapshot>> GetAsync(string appId, CancellationToken ct = default);
}

public sealed class SteamLifecycleClient(HttpClient http, IStoreMetadataCache cache,
    ISteamStoreClient store, TimeProvider clock) : ISteamLifecycleClient
{
    public const string CacheProvider = "steam-lifecycle-v1";
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);

    public async Task<IReadOnlyList<SteamLifecycleSnapshot>> GetAsync(string appId, CancellationToken ct = default)
    {
        if (!uint.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId) || numericId == 0)
            return [];
        var results = new List<SteamLifecycleSnapshot>();
        await FetchAsync("steam-players", "players:" + appId,
            "https://api.steampowered.com/ISteamUserStats/GetNumberOfCurrentPlayers/v1/?appid=" + appId,
            ParsePlayers, results, ct);
        await FetchAsync("steam-reviews", "reviews:" + appId,
            "https://store.steampowered.com/appreviews/" + appId + "?json=1&filter=recent&language=all&purchase_type=all&review_type=all&num_per_page=100&cursor=*",
            ParseReviews, results, ct);
        var newsUrl = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=" + appId + "&count=1&maxlength=1&feeds=steam_community_announcements";
        await FetchAsync("steam-announcements", "announcements:" + appId, newsUrl,
            (root, at) => ParseNews(root, at, numericId, false), results, ct, cacheForbidden: true);
        await FetchAsync("steam-patchnotes", "patchnotes:" + appId, newsUrl + "&tags=patchnotes",
            (root, at) => ParseNews(root, at, numericId, true), results, ct, cacheForbidden: true);
        try
        {
            var items = await store.GetItemsAsync([appId], ct: ct);
            var entry = await cache.GetAsync(SteamStoreClient.CacheProvider, SteamStoreClient.AppCacheKey(appId), ct);
            // Presence in a regional store is positive evidence only. A miss says nothing about delisting.
            if (items.ContainsKey(appId) && entry is { PayloadJson: not null } present)
                results.Add(new("steam-store", present.FetchedAt, new LifecycleSignals { StoreListed = true },
                    JsonSerializer.Serialize(new { appid = numericId, listed = true, fetched_at = present.FetchedAt })));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
        return results;
    }

    private async Task FetchAsync(string source, string key, string url,
        Func<JsonElement, DateTime, LifecycleSignals?> parse, List<SteamLifecycleSnapshot> results, CancellationToken ct,
        bool cacheForbidden = false)
    {
        try
        {
            var cached = await cache.GetAsync(CacheProvider, key, ct);
            var now = clock.GetUtcNow().UtcDateTime;
            if (cached is { PayloadJson: null } missing && missing.FetchedAt >= now - CacheTtl) return;
            if (cached is { PayloadJson: not null } hit && hit.FetchedAt >= now - CacheTtl)
            {
                using var json = JsonDocument.Parse(hit.PayloadJson);
                var signals = parse(json.RootElement, hit.FetchedAt);
                if (signals is not null) results.Add(new(source, hit.FetchedAt, signals, EvidenceJson(source, json.RootElement, hit.FetchedAt, hit.PayloadJson)));
                return;
            }
            using var response = await http.GetAsync(url, ct);
            if (cacheForbidden && response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                await cache.SetAsync(CacheProvider, key, null, clock.GetUtcNow().UtcDateTime, ct);
                return;
            }
            if (!response.IsSuccessStatusCode) return;
            var raw = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(raw);
            var observed = clock.GetUtcNow().UtcDateTime;
            var result = parse(document.RootElement, observed);
            if (result is null) return;
            await cache.SetAsync(CacheProvider, key, raw, observed, ct);
            results.Add(new(source, observed, result, EvidenceJson(source, document.RootElement, observed, raw)));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or FormatException || ex is OperationCanceledException && !ct.IsCancellationRequested) { }
    }

    private static LifecycleSignals? ParsePlayers(JsonElement root, DateTime observed)
    {
        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("result", out var result) || !result.TryGetInt32(out var success) || success != 1 ||
            !response.TryGetProperty("player_count", out var count) || !count.TryGetInt32(out var players) || players < 0) return null;
        return new LifecycleSignals { CurrentPlayers = players };
    }

    private static string EvidenceJson(string source, JsonElement root, DateTime observed, string raw)
    {
        if (source != "steam-reviews") return raw;
        var timestamps = root.GetProperty("reviews").EnumerateArray()
            .Select(r => r.GetProperty("timestamp_created").GetInt64()).ToArray();
        var cutoff = new DateTimeOffset(observed.AddDays(-30)).ToUnixTimeSeconds();
        return JsonSerializer.Serialize(new
        {
            success = 1,
            filter = "recent",
            language = "all",
            purchase_type = "all",
            review_type = "all",
            window_days = 30,
            observed_at = observed,
            complete = timestamps.Length < 100 || timestamps.Any(t => t < cutoff),
            timestamp_created = timestamps,
        });
    }

    private static LifecycleSignals? ParseReviews(JsonElement root, DateTime observed)
    {
        if (!root.TryGetProperty("success", out var success) || !success.TryGetInt32(out var code) || code != 1 ||
            !root.TryGetProperty("reviews", out var reviews) || reviews.ValueKind != JsonValueKind.Array) return null;
        var cutoff = new DateTimeOffset(observed.AddDays(-30)).ToUnixTimeSeconds();
        var latest = new DateTimeOffset(observed).ToUnixTimeSeconds();
        var count = 0;
        var reachedOlder = false;
        foreach (var review in reviews.EnumerateArray())
        {
            if (!review.TryGetProperty("timestamp_created", out var stamp) || !stamp.TryGetInt64(out var created) || created < 0 || created > latest) return null;
            if (created >= cutoff) count++;
            else reachedOlder = true;
        }
        // The summary is lifetime reviews. A full page of recent entries is a lower bound,
        // so preserve the body but never misrepresent it as an exact 30-day count.
        return new LifecycleSignals { RecentReviewCount = reachedOlder || reviews.GetArrayLength() < 100 ? count : null };
    }

    private static LifecycleSignals? ParseNews(JsonElement root, DateTime observed, uint appId, bool patchnotes)
    {
        if (!root.TryGetProperty("appnews", out var news) ||
            !news.TryGetProperty("appid", out var id) || !id.TryGetUInt32(out var responseId) || responseId != appId ||
            !news.TryGetProperty("newsitems", out var items) || items.ValueKind != JsonValueKind.Array) return null;
        if (items.GetArrayLength() == 0) return new LifecycleSignals();
        var item = items[0];
        if (!item.TryGetProperty("feedname", out var feed) || feed.GetString() != "steam_community_announcements" ||
            !item.TryGetProperty("feed_type", out var feedType) || !feedType.TryGetInt32(out var type) || type != 1 ||
            !item.TryGetProperty("appid", out var itemId) || !itemId.TryGetUInt32(out var itemAppId) || itemAppId != appId ||
            !item.TryGetProperty("date", out var date) || !date.TryGetInt64(out var unix) || unix <= 0 ||
            unix > new DateTimeOffset(observed).ToUnixTimeSeconds()) return null;
        var at = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
        return patchnotes ? new LifecycleSignals { LastDevelopmentAt = at } : new LifecycleSignals { LastCommunicationAt = at };
    }
}
