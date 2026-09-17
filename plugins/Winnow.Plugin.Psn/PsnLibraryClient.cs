using System.Globalization;
using System.Text.Json;
using System.Xml;
using Winnow.PluginSdk;

namespace Winnow.Plugin.Psn;

internal sealed record PsnLibrarySnapshot(string Scope, IReadOnlyList<PsnTitle> Games);
internal sealed record PsnTitle(PluginLibraryGame Library, PluginMetadata Metadata, string? IconUrl);

internal sealed class PsnLibraryClient(IPluginContext context, TimeProvider clock)
{
    private const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxTitles = 10_000;
    private const int PurchasePageSize = 24;
    private const int HistoryPageSize = 200;
    private const string PurchaseHash = "827a423f6a8ddca4107ac01395af2ec0eafd8396fc7fa204aaf9b7ed2eefa168";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };

    internal async Task<PsnLibrarySnapshot?> GetAsync(PsnSession session, bool includePlayed, bool includeLegacy, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var purchases = await EndpointAsync(session, "purchases", PurchasesAsync, ct);
        var played = includePlayed ? await EndpointAsync(session, "played", PlayedAsync, ct) : null;
        var legacy = includeLegacy ? await EndpointAsync(session, "legacy", LegacyAsync, ct) : null;
        if (purchases is null && played is null && legacy is null) return null;

        var titles = (purchases ?? []).ToDictionary(x => x.Library.SourceId, StringComparer.Ordinal);
        foreach (var game in played ?? [])
        {
            if (titles.TryGetValue(game.Library.SourceId, out var purchase))
            {
                titles[game.Library.SourceId] = purchase with
                {
                    Library = purchase.Library with
                    {
                        PlaytimeMinutes = game.Library.PlaytimeMinutes,
                        LastPlayedAt = game.Library.LastPlayedAt,
                        LibrarySourceLabel = "PlayStation purchase library and played history · access may depend on a subscription"
                    },
                    Metadata = game.Metadata,
                    IconUrl = purchase.IconUrl ?? game.IconUrl
                };
            }
            else titles.Add(game.Library.SourceId, game);
        }
        foreach (var game in legacy ?? []) titles.TryAdd(game.Library.SourceId, game);
        ct.ThrowIfCancellationRequested();
        return new(session.Scope, titles.Values.ToArray());
    }

    private async Task<IReadOnlyList<PsnTitle>?> EndpointAsync(PsnSession session, string endpoint,
        Func<PsnSession, CancellationToken, Task<IReadOnlyList<PsnTitle>?>> fetch, CancellationToken ct)
    {
        var key = $"library:v1:{session.Scope}:{endpoint}";
        PluginCacheEntry? cached = null;
        IReadOnlyList<PsnTitle>? prior = null;
        try
        {
            cached = await context.Cache.GetAsync(key, ct);
            if (cached is not null && cached.Payload.Length <= MaxBytes)
            {
                var entry = JsonSerializer.Deserialize<EndpointCache>(cached.Payload, Json);
                if (entry is { Version: 1 } && entry.Scope == session.Scope && entry.AccountId == session.AccountId &&
                    entry.Endpoint == endpoint && ValidCache(entry.Games, session.AccountId)) prior = entry.Games;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex)) { }
        if (prior is not null && cached!.ExpiresAt > clock.GetUtcNow() && cached.ExpiresAt <= clock.GetUtcNow().AddHours(6)) return prior;
        if (string.IsNullOrEmpty(session.AccessToken)) return prior;

        try
        {
            var games = await fetch(session, ct);
            ct.ThrowIfCancellationRequested();
            if (games is null) return prior;
            var payload = JsonSerializer.SerializeToUtf8Bytes(new EndpointCache(1, session.Scope, session.AccountId, endpoint, games), Json);
            if (payload.Length <= MaxBytes)
            {
                try { await context.Cache.SetAsync(key, new(payload, clock.GetUtcNow().AddHours(6)), ct); }
                catch (Exception ex) when (SoftFailure(ex)) { }
            }
            return games;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { return prior; }
    }

    private async Task<IReadOnlyList<PsnTitle>?> PurchasesAsync(PsnSession session, CancellationToken ct)
    {
        var games = new Dictionary<string, PsnTitle>(StringComparer.Ordinal);
        var entitlements = new HashSet<string>(StringComparer.Ordinal);
        for (var start = 0; start <= MaxTitles; start += PurchasePageSize)
        {
            var variables = JsonSerializer.Serialize(new
            {
                isActive = true, platform = new[] { "ps4", "ps5" }, size = PurchasePageSize, start,
                sortBy = "ACTIVE_DATE", sortDirection = "desc"
            });
            var extensions = JsonSerializer.Serialize(new { persistedQuery = new { version = 1, sha256Hash = PurchaseHash } });
            var url = "https://web.np.playstation.com/api/graphql/v1/op?operationName=getPurchasedGameList&variables=" +
                      Uri.EscapeDataString(variables) + "&extensions=" + Uri.EscapeDataString(extensions);
            using var response = await RequestAsync(url, session, ct);
            if (response is null || HasError(response.RootElement)) return null;
            var rows = Field(Field(Field(response.RootElement, "data"), "purchasedTitlesRetrieve"), "games");
            if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > PurchasePageSize || start + rows.GetArrayLength() > MaxTitles) return null;
            foreach (var row in rows.EnumerateArray())
            {
                var platform = Text(row, "platform");
                if (string.IsNullOrWhiteSpace(platform)) return null;
                if (platform is not ("PS4" or "PS5")) continue;
                var id = Text(row, "titleId");
                var name = Clean(Text(row, "name"));
                if (!TitleId(id) || name is null) return null;
                var entitlement = Clean(Text(row, "entitlementId"), 256) ?? Clean(Text(row, "productId"), 256) ?? id;
                // Multiple entitlements can identify one title, but a repeated entitlement means pagination did not advance.
                if (!entitlements.Add(id + ":" + entitlement)) return null;
                var game = Title(session, "title:" + id, name, platform, "PlayStation purchase library · access may depend on a subscription",
                    Icon(Text(Field(row, "image"), "url")));
                games.TryAdd(game.Library.SourceId, game);
            }
            if (rows.GetArrayLength() < PurchasePageSize) return games.Values.ToArray();
        }
        return null;
    }

    private Task<IReadOnlyList<PsnTitle>?> PlayedAsync(PsnSession session, CancellationToken ct)
        => HistoryAsync(session, false, ct);

    private Task<IReadOnlyList<PsnTitle>?> LegacyAsync(PsnSession session, CancellationToken ct)
        => HistoryAsync(session, true, ct);

    private async Task<IReadOnlyList<PsnTitle>?> HistoryAsync(PsnSession session, bool legacy, CancellationToken ct)
    {
        var games = new List<PsnTitle>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var offset = 0;
        int? expectedTotal = null;
        for (var page = 0; page <= MaxTitles; page++)
        {
            var path = legacy ? "trophy/v1/users/" + session.AccountId + "/trophyTitles" : "gamelist/v2/users/" + session.AccountId + "/titles";
            var url = $"https://m.np.playstation.com/api/{path}?limit={HistoryPageSize}&offset={offset}" +
                      (legacy ? "" : "&categories=ps4_game%2Cps5_native_game");
            using var response = await RequestAsync(url, session, ct);
            if (response is null || HasError(response.RootElement)) return null;
            var root = response.RootElement;
            var returnedAccount = Text(root, "accountId");
            if (returnedAccount is not null && returnedAccount != session.AccountId) return null;
            var rows = Field(root, legacy ? "trophyTitles" : "titles");
            var total = Integer(root, "totalItemCount");
            if (rows.ValueKind != JsonValueKind.Array || total is null or < 0 or > MaxTitles ||
                rows.GetArrayLength() > HistoryPageSize || offset + rows.GetArrayLength() > total ||
                expectedTotal is not null && expectedTotal != total) return null;
            expectedTotal = total;
            foreach (var row in rows.EnumerateArray())
            {
                if (legacy && (string.IsNullOrWhiteSpace(Text(row, "npServiceName")) || !TitleId(Text(row, "npCommunicationId")) ||
                               string.IsNullOrWhiteSpace(Text(row, "trophyTitlePlatform"))) ||
                    !legacy && (string.IsNullOrWhiteSpace(Text(row, "titleId")) || string.IsNullOrWhiteSpace(Text(row, "category")))) return null;
                var identity = legacy ? Text(row, "npServiceName") + ":" + Text(row, "npCommunicationId") : Text(row, "titleId");
                if (string.IsNullOrWhiteSpace(identity) || !identities.Add(identity)) return null;
                var title = legacy ? ReadLegacy(session, row) : ReadPlayed(session, row);
                if (title is not null) games.Add(title);
                else if (IsSupported(row, legacy)) return null;
            }
            var end = offset + rows.GetArrayLength();
            var nextField = Field(root, "nextOffset");
            var next = Integer(root, "nextOffset");
            if (end == total)
            {
                if (nextField.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null)) return null;
                return games;
            }
            if (rows.GetArrayLength() == 0 || next != end || next <= offset) return null;
            offset = next.Value;
        }
        return null;
    }

    private PsnTitle? ReadPlayed(PsnSession session, JsonElement row)
    {
        if (!IsSupported(row, false)) return null;
        var id = Text(row, "titleId");
        var name = Clean(Text(row, "localizedName")) ?? Clean(Text(row, "name"));
        if (!TitleId(id) || name is null) return null;
        var platform = Text(row, "category") == "ps4_game" ? "PS4" : "PS5";
        var title = Title(session, "title:" + id, name, platform, "PlayStation played history · ownership unverified",
            Icon(Text(row, "localizedImageUrl")) ?? Icon(Text(row, "imageUrl")));
        return title with
        {
            Library = title.Library with { PlaytimeMinutes = Minutes(Text(row, "playDuration")), LastPlayedAt = Date(Text(row, "lastPlayedDateTime")) },
            Metadata = title.Metadata with { Genres = Genres(Field(Field(row, "concept"), "genres")) }
        };
    }

    private static PsnTitle? ReadLegacy(PsnSession session, JsonElement row)
    {
        if (!IsSupported(row, true)) return null;
        var id = Text(row, "npCommunicationId");
        var service = Text(row, "npServiceName");
        var name = Clean(Text(row, "trophyTitleName"));
        if (service != "trophy" || id is null || !TitleId(id) || !id.StartsWith("NPWR", StringComparison.Ordinal) || name is null) return null;
        var platforms = LegacyPlatforms(row);
        var title = Title(session, "trophy:" + service + ":" + id, name, platforms[0],
            "PlayStation trophy history · ownership and playtime unverified", Icon(Text(row, "trophyTitleIconUrl")));
        return title with { Metadata = title.Metadata with { Summary = Clean(Text(row, "trophyTitleDetail"), 8000), Tags = platforms } };
    }

    private static PsnTitle Title(PsnSession session, string id, string name, string platform, string label, string? icon)
        => new(new(id, name)
        {
            AccountRef = session.AccountId,
            ExternalIds = new Dictionary<string, string> { ["plugin:psn"] = id },
            LibrarySourceLabel = label
        }, new() { Tags = [platform] }, icon);

    private async Task<JsonDocument?> RequestAsync(string url, PsnSession session, CancellationToken ct)
    {
        var response = await context.Http.SendAsync(new(url)
        {
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + session.AccessToken, ["Accept"] = "application/json" }
        }, ct);
        ct.ThrowIfCancellationRequested();
        if (response.StatusCode != 200 || response.Body.Length > MaxBytes) return null;
        return JsonDocument.Parse(response.Body, new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static bool IsSupported(JsonElement row, bool legacy)
        => legacy ? LegacyPlatforms(row).Length > 0 : Text(row, "category") is "ps4_game" or "ps5_native_game";

    private static string[] LegacyPlatforms(JsonElement row)
    {
        var raw = Text(row, "trophyTitlePlatform");
        if (raw is null) return [];
        var platforms = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x is "PSVITA" or "Vita" or "PS Vita" ? "PS Vita" : x).Distinct(StringComparer.Ordinal).ToArray();
        return platforms.Length > 0 && platforms.All(x => x is "PS3" or "PS Vita") ? platforms : [];
    }

    private static string[] Genres(JsonElement value)
    {
        var genres = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()) :
            value.ValueKind == JsonValueKind.String ? [value.GetString()] : Enumerable.Empty<string?>();
        return genres.Select(x => Clean(x, 100)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray();
    }

    private DateTimeOffset? Date(string? text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) &&
           value.Year >= 1994 && value <= clock.GetUtcNow().AddMinutes(5) ? value : null;

    private static long? Minutes(string? duration)
    {
        if (duration is not { Length: > 0 and < 100 } || !duration.StartsWith('P') || duration.Contains('Y') ||
            duration.Split('T')[0].Contains('M')) return null;
        try
        {
            var value = XmlConvert.ToTimeSpan(duration);
            return value < TimeSpan.Zero ? null : value.Ticks / TimeSpan.TicksPerMinute;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException) { return null; }
    }

    private static string? Icon(string? text)
        => text is { Length: > 0 and <= 4096 } && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
           uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment) &&
           uri.Host is "image.api.playstation.com" or "psnobj.prod.dl.playstation.net" ? uri.AbsoluteUri : null;

    private static bool ValidCache(IReadOnlyList<PsnTitle>? games, string account)
        => games is { Count: <= MaxTitles } && games.All(x => x is not null && x.Library is not null && x.Metadata is not null &&
            x.Library.AccountRef == account && Clean(x.Library.Title) is not null && ValidSourceId(x.Library.SourceId) &&
            x.Library.Installed is null && x.Library.InstallPath is null && x.Library.AcquiredAt is null &&
            x.Library.PlaytimeMinutes is not < 0 && x.Library.Actions is { Count: 0 } &&
            x.Metadata.Genres is not null && x.Metadata.Tags is not null &&
            x.Library.ExternalIds is not null && x.Library.ExternalIds.Count == 1 &&
            x.Library.ExternalIds.TryGetValue("plugin:psn", out var id) && id == x.Library.SourceId &&
            (x.IconUrl is null || Icon(x.IconUrl) is not null)) &&
           games.Select(x => x.Library.SourceId).Distinct(StringComparer.Ordinal).Count() == games.Count;

    private static bool ValidSourceId(string? source)
        => source is not null && (source.StartsWith("title:", StringComparison.Ordinal) && TitleId(source[6..]) ||
            source.StartsWith("trophy:trophy:NPWR", StringComparison.Ordinal) && TitleId(source[14..]));

    private static bool TitleId(string? value)
        => value is { Length: 12 } && value.AsSpan(0, 4).ToArray().All(char.IsAsciiLetterUpper) &&
           value.AsSpan(4, 5).ToArray().All(char.IsAsciiDigit) && value[9] == '_' && value.AsSpan(10).ToArray().All(char.IsAsciiDigit);

    private static string? Clean(string? value, int limit = 500)
        => value is { Length: > 0 } && value.Length <= limit && !string.IsNullOrWhiteSpace(value) &&
           !value.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t') ? value.Trim() : null;

    private static JsonElement Field(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var field) ? field : default;
    private static string? Text(JsonElement value, string name)
        => Field(value, name) is { ValueKind: JsonValueKind.String } field ? field.GetString() : null;
    private static int? Integer(JsonElement value, string name)
        => Field(value, name) is { ValueKind: JsonValueKind.Number } field && field.TryGetInt32(out var number) ? number : null;
    private static bool HasError(JsonElement root)
        => Field(root, "error").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) ||
           Field(root, "errors") is var errors && errors.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
           (errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() != 0);
    private static bool SoftFailure(Exception ex)
        => ex is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or NotSupportedException or ArgumentException;

    private sealed record EndpointCache(int Version, string Scope, string AccountId, string Endpoint, IReadOnlyList<PsnTitle> Games);
}
