using System.Globalization;
using System.Text.Json;
using Winnow.Core.Identity;
using Winnow.Enrich.Igdb.Model;

namespace Winnow.Enrich.Igdb;

public sealed partial class IgdbClient
{
    public const string EditionCacheProvider = "igdb-editions-v1";
    public const int EditionPayloadVersion = 1;
    public static string EditionCacheKey(int sourceId, string uid)
        => sourceId.ToString(CultureInfo.InvariantCulture) + ":" + uid;

    public async Task<IReadOnlyDictionary<string, IgdbEditionMatch>> ResolveEditionsByExternalIdsAsync(
        int externalGameSourceId, IEnumerable<string> uids, TimeSpan? cacheTtl = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uids);
        var result = new Dictionary<string, IgdbEditionMatch>(StringComparer.Ordinal);
        if (externalGameSourceId <= 0) return result;
        var requested = uids.Where(Apicalypse.IsSafeStringValue).Distinct(StringComparer.Ordinal).ToArray();
        var now = _clock.GetUtcNow().UtcDateTime;
        var ttl = cacheTtl ?? _options.CacheTtl;
        var cached = await _cache.GetManyAsync(EditionCacheProvider,
            requested.Select(uid => EditionCacheKey(externalGameSourceId, uid)), ct);
        var pending = new List<string>();
        foreach (var uid in requested)
        {
            var key = EditionCacheKey(externalGameSourceId, uid);
            if (ttl > TimeSpan.Zero && cached.TryGetValue(key, out var entry)
                && entry.FetchedAt <= now && entry.FetchedAt > now - ttl
                && ReadEditionPayload(entry.PayloadJson, externalGameSourceId, uid) is { } payload)
            {
                result[uid] = EditionResult(payload, key, entry.PayloadJson!, entry.FetchedAt + ttl);
            }
            else pending.Add(uid);
        }
        if (pending.Count == 0 || await _credentials.GetAsync(ct) is null) return result;
        foreach (var batch in pending.Chunk(BatchSize))
        {
            var page = await FetchAllAsync<JsonElement>("external_games",
                (limit, offset) => Apicalypse.ExternalEditions(batch, externalGameSourceId, limit, offset), ct, requireArray: true);
            if (!page.Succeeded) continue;
            var rows = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
            var uncorrelated = false;
            foreach (var row in page.Items)
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("uid", out var id)
                    || row.EnumerateObject().Count(property => property.NameEquals("uid")) != 1
                    || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()))
                { uncorrelated = true; continue; }
                var uid = id.GetString()!;
                if (!batch.Contains(uid, StringComparer.Ordinal)) continue;
                if (!rows.TryGetValue(uid, out var matches)) rows[uid] = matches = [];
                matches.Add(row);
            }
            var fetched = _clock.GetUtcNow().UtcDateTime;
            foreach (var uid in batch)
            {
                rows.TryGetValue(uid, out var matches);
                // An unidentifiable row may belong to any requested UID, so even a
                // seemingly unique positive mapping cannot establish exclusivity.
                if (uncorrelated) continue;
                var payload = ClassifyEdition(externalGameSourceId, uid, matches ?? []);
                var json = JsonSerializer.Serialize(payload, IgdbEditionJson.Default.IgdbEditionPayload);
                var key = EditionCacheKey(externalGameSourceId, uid);
                await _cache.SetAsync(EditionCacheProvider, key, json, fetched, ct);
                result[uid] = EditionResult(payload, key, json, fetched + ttl);
            }
        }
        return result;
    }

    private static IgdbEditionMatch EditionResult(IgdbEditionPayload payload, string key, string json, DateTime expires)
        => new(payload.Uid, payload.Status, payload.GameId, payload.ParentId, payload.Title,
            CachedEvidenceSource.FromPayload(EditionCacheProvider, key, json), expires);

    private static IgdbEditionPayload? ReadEditionPayload(string? json, int sourceId, string uid)
    {
        if (json is null) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || HasDuplicateProperties(document.RootElement)) return null;
            var p = JsonSerializer.Deserialize(json, IgdbEditionJson.Default.IgdbEditionPayload);
            if (p is null || p.Version != EditionPayloadVersion || p.SourceId != sourceId || p.Uid != uid) return null;
            return p.Status switch
            {
                IgdbEditionMatchStatus.Edition when p.GameId is > 0 && p.ParentId is > 0
                    && p.GameId != p.ParentId && !string.IsNullOrWhiteSpace(p.Title) => p,
                IgdbEditionMatchStatus.NotEdition or IgdbEditionMatchStatus.Missing or IgdbEditionMatchStatus.Ambiguous
                    when p.GameId is null && p.ParentId is null && p.Title is null => p,
                _ => null,
            };
        }
        catch (JsonException) { return null; }
    }

    private static IgdbEditionPayload ClassifyEdition(int source, string uid, List<JsonElement> rows)
    {
        IgdbEditionPayload Negative(IgdbEditionMatchStatus status) => new(EditionPayloadVersion, source, uid, status, null, null, null);
        if (rows.Count == 0) return Negative(IgdbEditionMatchStatus.Missing);
        (long Game, long? Parent, string? Title)? mapping = null;
        foreach (var row in rows)
        {
            if (HasDuplicateProperties(row)
                || !PositiveNumber(row, "external_game_source", out var actualSource) || actualSource != source
                || !row.TryGetProperty("game", out var game) || game.ValueKind != JsonValueKind.Object
                || HasDuplicateProperties(game)
                || !PositiveNumber(game, "id", out var gameId)) return Negative(IgdbEditionMatchStatus.Ambiguous);
            long? parent = null;
            string? title = null;
            if (game.TryGetProperty("version_parent", out var parentValue) && parentValue.ValueKind != JsonValueKind.Null)
            {
                if (parentValue.ValueKind != JsonValueKind.Number || !parentValue.TryGetInt64(out var parentId) || parentId <= 0 || parentId == gameId)
                    return Negative(IgdbEditionMatchStatus.Ambiguous);
                parent = parentId;
            }
            if (game.TryGetProperty("version_title", out var titleValue) && titleValue.ValueKind != JsonValueKind.Null)
            {
                if (titleValue.ValueKind != JsonValueKind.String) return Negative(IgdbEditionMatchStatus.Ambiguous);
                title = titleValue.GetString()?.Trim();
                if (title?.Length == 0) title = null;
            }
            var current = (gameId, parent, title);
            if (mapping is { } previous && previous != current) return Negative(IgdbEditionMatchStatus.Ambiguous);
            mapping = current;
        }
        var match = mapping!.Value;
        if (match.Parent is null && match.Title is null) return Negative(IgdbEditionMatchStatus.NotEdition);
        if (match.Parent is null || match.Title is null) return Negative(IgdbEditionMatchStatus.Ambiguous);
        return new(EditionPayloadVersion, source, uid, IgdbEditionMatchStatus.Edition, match.Game, match.Parent, match.Title);
    }

    private static bool PositiveNumber(JsonElement element, string property, out long value)
    {
        value = 0;
        return element.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number
            && number.TryGetInt64(out value) && value > 0;
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property => !names.Add(property.Name));
    }
}
