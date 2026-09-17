using System.Globalization;
using System.Text.Json;
using Winnow.PluginSdk;
using static Winnow.Plugin.Xbox.XboxProtocol;

namespace Winnow.Plugin.Xbox;

internal sealed class XboxHistoryClient(IPluginContext context, XboxAccountClient account, TimeProvider clock)
{
    internal async Task<XboxHistory?> GetAsync(CancellationToken ct)
    {
        var scope = await account.CacheScopeAsync(ct);
        if (scope is null) return null;
        var key = "history:v1:" + scope;
        var cached = await context.Cache.GetAsync(key, ct);
        var prior = Read(cached, scope);
        if (prior is not null && cached!.ExpiresAt > clock.GetUtcNow()) return prior;
        var session = await account.SessionAsync(ct);
        if (session is null) return prior;
        try
        {
            var response = await context.Http.SendAsync(new($"https://titlehub.xboxlive.com/users/xuid({session.Xuid})/titles/titlehistory/decoration/scid?maxItems=5000") { Headers = session.Headers }, ct);
            if (response.StatusCode != 200) return prior;
            using var json = Parse(response.Body);
            var root = json.RootElement;
            var rows = Field(root, "titles");
            if (Text(root, "xuid") != session.Xuid || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() > 5000) return prior;
            var games = new List<XboxHistoryGame>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                if (Text(row, "type") != "Game") continue;
                var id = Text(row, "titleId");
                var name = Clean(Text(row, "name"));
                if (!TitleId(id) || name is null || !ids.Add(id!)) return prior;
                var pfn = Text(row, "pfn");
                if (pfn is not null && !Pfn(pfn)) pfn = null;
                var devices = Rows(Field(row, "devices")).Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
                var last = Date(Field(row, "titleHistory"), "lastTimePlayed");
                if (last > clock.GetUtcNow().AddMinutes(5)) last = null;
                var scid = Guid.TryParse(Text(row, "serviceConfigId"), out var parsedScid) && parsedScid != Guid.Empty ? parsedScid.ToString("D") : null;
                games.Add(new(id!, pfn, name, devices.Contains("PC"), devices.Any(x => x is "XboxOne" or "XboxSeries" or "Xbox360"), last, null, scid));
            }
            var previousMinutes = prior?.Games.Where(x => x.Minutes is not null).Select(x => x.TitleId).ToHashSet() ?? [];
            var minutes = await MinutesAsync(session, games.OrderBy(x => previousMinutes.Contains(x.TitleId)).ToArray(), ct);
            games = games.Select(x => x with { Minutes = minutes.GetValueOrDefault(x.TitleId) ?? prior?.Games.FirstOrDefault(p => p.TitleId == x.TitleId)?.Minutes }).ToList();
            if (!await account.IsCurrentAsync(session, ct) || scope != await account.CacheScopeAsync(ct)) return null;
            var history = new XboxHistory(1, scope, session.Xuid, games);
            var payload = JsonSerializer.SerializeToUtf8Bytes(history, Json);
            if (payload.Length <= MaxBytes) await context.Cache.SetAsync(key, new(payload, clock.GetUtcNow().AddHours(6)), ct);
            return history;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { return prior; }
    }

    private async Task<Dictionary<string, long?>> MinutesAsync(XboxSession session, XboxHistoryGame[] games, CancellationToken ct)
    {
        var result = new Dictionary<string, long?>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        var identities = games.Where(x => x.ServiceConfigId is not null).GroupBy(x => x.ServiceConfigId!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().TitleId, StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in identities.Chunk(100))
        {
            try
            {
                var response = await context.Http.SendAsync(Post("https://userstats.xboxlive.com/batch", new
                {
                    arrangebyfield = "xuid", stats = chunk.Select(id => new { name = "MinutesPlayed", scid = id.Key }), xuids = new[] { session.Xuid }
                }, session.Headers), budget.Token);
                if (response.StatusCode != 200) break;
                using var json = Parse(response.Body);
                var groups = Rows(Field(json.RootElement, "statlistscollection")).ToArray();
                foreach (var group in groups)
                {
                    if (Text(group, "arrangebyfield") != "xuid" || Text(group, "arrangebyfieldid") != session.Xuid) continue;
                    foreach (var stat in Rows(Field(group, "stats")))
                    {
                        var scid = Text(stat, "scid");
                        var id = scid is not null ? chunk.FirstOrDefault(x => string.Equals(x.Key, scid, StringComparison.OrdinalIgnoreCase)).Value : null;
                        var accountId = Text(stat, "xuid");
                        if (id is null || Text(stat, "name") != "MinutesPlayed" || accountId is not null && accountId != session.Xuid) continue;
                        if (!long.TryParse(Text(stat, "value"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0) continue;
                        // Conflicting duplicate observations carry no authority.
                        if (result.TryGetValue(id, out var previous) && previous != value) result[id] = null;
                        else if (!result.ContainsKey(id)) result[id] = value;
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (SoftFailure(ex) || ex is OperationCanceledException) { break; }
        }
        return result;
    }
    private static XboxHistory? Read(PluginCacheEntry? entry, string scope)
    {
        if (entry is null || entry.Payload.Length > MaxBytes) return null;
        try
        {
            var history = JsonSerializer.Deserialize<XboxHistory>(entry.Payload, Json);
            return history is { Version: 1, Games: not null } && history.Scope == scope && Xuid(history.Xuid) && history.Games.Count <= 5000
                && history.Games.All(x => x is not null && TitleId(x.TitleId) && Clean(x.Title) is not null && (x.Pfn is null || Pfn(x.Pfn)) && x.Minutes is not < 0)
                && history.Games.Select(x => x.TitleId).Distinct().Count() == history.Games.Count ? history : null;
        }
        catch (JsonException) { return null; }
    }
}

internal sealed record XboxHistory(int Version, string Scope, string Xuid, IReadOnlyList<XboxHistoryGame> Games);
internal sealed record XboxHistoryGame(string TitleId, string? Pfn, string Title, bool Pc, bool Console, DateTimeOffset? LastPlayed, long? Minutes, string? ServiceConfigId = null);
