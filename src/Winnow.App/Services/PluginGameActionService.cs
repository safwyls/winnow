using System.Globalization;
using System.Text.Json;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;

namespace Winnow.App.Services;

public sealed record PluginEntryActions(string? SourceLabel, GameLink? Play, GameLink? Store);

/// <summary>Persists advertised actions, but rechecks ownership and the active provider at dispatch.</summary>
public sealed class PluginGameActionService(PluginCatalog catalog, IMetadataCache cache,
    IOwnershipRepository ownerships, IReleaseRepository releases)
{
    internal sealed record Observation(string? SourceLabel, PluginGameActionKind[] Actions);
    // Separate from the plugin's writable cache namespace: providers cannot forge a host observation.
    private const string CacheProvider = "plugin-library-actions";
    private static string Key(long releaseId, string pluginId) => $"{pluginId}:{releaseId.ToString(CultureInfo.InvariantCulture)}";

    internal static Task SaveAsync(IMetadataCache cache, long releaseId, string pluginId,
        PluginLibraryGame game, CancellationToken ct)
    {
        var label = game.LibrarySourceLabel is { Length: > 0 and <= 250 } value && !value.Any(char.IsControl) ? value : null;
        var actions = (game.Actions ?? []).Where(Enum.IsDefined).Distinct().Take(2).ToArray();
        return cache.SetAsync(CacheProvider, Key(releaseId, pluginId),
            JsonSerializer.Serialize(new Observation(label, actions)), DateTime.UtcNow, ct);
    }

    public async Task<IReadOnlyDictionary<long, PluginEntryActions>> ReadAsync(LibrarySnapshot snapshot, CancellationToken ct)
    {
        var entries = snapshot.Ownerships.Where(o => o.Store.StartsWith("plugin:", StringComparison.Ordinal)).ToArray();
        var cached = await cache.GetManyAsync(CacheProvider, entries.Select(o => Key(o.ReleaseId, o.Store[7..])), ct);
        var active = catalog.GetActive<IPluginGameActions>().Select(p => p.Manifest.Id).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<long, PluginEntryActions>();
        foreach (var entry in entries)
        {
            if (!cached.TryGetValue(Key(entry.ReleaseId, entry.Store[7..]), out var row) || Parse(row.PayloadJson) is not { } data) continue;
            var sourceId = snapshot.ExternalIds.FirstOrDefault(id => id.ReleaseId == entry.ReleaseId && id.Provider == entry.Store)?.ProviderId;
            var canAct = sourceId is not null && active.Contains(entry.Store[7..]);
            result[entry.Id] = new(data.SourceLabel,
                canAct && entry.Installed && data.Actions.Contains(PluginGameActionKind.Play)
                    ? GameLink.ForPlugin("Play", entry.Id, entry.Store[7..], sourceId!, PluginGameActionKind.Play) : null,
                canAct && data.Actions.Contains(PluginGameActionKind.OpenStore)
                    ? GameLink.ForPlugin("Store page", entry.Id, entry.Store[7..], sourceId!, PluginGameActionKind.OpenStore) : null);
        }
        return result;
    }

    public async Task<bool> ExecuteAsync(long ownershipId, GameLink action, CancellationToken ct = default)
    {
        if (action.PluginOwnershipId != ownershipId || action.PluginId is not { } pluginId || action.PluginSourceId is not { } sourceId || action.PluginAction is not { } kind) return false;
        var plugin = catalog.GetActive<IPluginGameActions>().SingleOrDefault(p => p.Manifest.Id == pluginId);
        if (plugin is null) return false;
        var ownership = await ownerships.GetAsync(ownershipId, ct);
        if (ownership is null || ownership.Store != "plugin:" + pluginId || kind == PluginGameActionKind.Play && !ownership.Installed) return false;
        var release = await releases.FindByExternalIdAsync(ownership.Store, sourceId, ct);
        if (release?.Id != ownership.ReleaseId) return false;
        var cached = await cache.GetAsync(CacheProvider, Key(ownership.ReleaseId, pluginId), ct);
        if (Parse(cached?.PayloadJson)?.Actions.Contains(kind) != true) return false;
        var result = await catalog.InvokeAsync<PluginGameActionResult>(plugin,
            async (instance, token) => await ((IPluginGameActions)instance).ExecuteGameActionAsync(sourceId, kind, token), ct);
        return result?.HandedOff == true;
    }

    private static Observation? Parse(string? json)
    {
        if (json is null || json.Length > 4096) return null;
        try
        {
            var value = JsonSerializer.Deserialize<Observation>(json);
            return value is { Actions.Length: <= 2 } && value.Actions.All(Enum.IsDefined)
                && (value.SourceLabel is null || value.SourceLabel.Length <= 250 && !value.SourceLabel.Any(char.IsControl)) ? value : null;
        }
        catch (JsonException) { return null; }
    }
}
