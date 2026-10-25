using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.PluginSdk;

namespace Winnow.App.Api;

internal static class LibraryWorkspaceMapping
{
    internal static (LibrarySnapshot snapshot, IdentityResolution identity, FacetSnapshot facets,
        HashSet<long> pins, IReadOnlyDictionary<string, EpicLaunchKey> epic,
        IReadOnlyDictionary<string, StorefrontDetails> storefronts, IReadOnlyDictionary<long, string?> headers,
        IReadOnlyDictionary<long, PluginEntryActions> pluginActions, IReadOnlyList<ArtworkChoice> artwork)
        Map(LibraryWorkspaceResponse workspace)
    {
        var identity = IdentityResolution.FromLiveLinks(workspace.IdentityLinks);
        var groups = workspace.Buckets.GroupBy(row => row.ResolvedWorkId).ToDictionary(group => group.Key, group =>
        {
            var game = group.First().Game;
            return GameGrouping.FromSnapshot(game.ResolvedWorkId, game.Bucket, game.PlaytimeMinutes,
                game.LastPlayedAt, game.MajorUpdateAt, game.UnreadUpdateCount, game.EntryCount, game.Lifecycle);
        });
        var buckets = workspace.Buckets.Select(row => new OwnershipBucket
        {
            OwnershipId = row.OwnershipId, ReleaseId = row.ReleaseId, WorkId = row.WorkId,
            ResolvedWorkId = row.ResolvedWorkId, PlaytimeMinutes = row.PlaytimeMinutes,
            LastPlayedAt = row.LastPlayedAt, MajorUpdateAt = row.MajorUpdateAt,
            Bucket = row.Bucket, ConsolidatedDemoCount = row.ConsolidatedDemoCount,
            Lifecycle = row.Lifecycle, Game = groups[row.ResolvedWorkId]
        }).ToArray();
        var snapshot = new LibrarySnapshot(buckets, workspace.Works, workspace.Ownerships,
            workspace.Releases, workspace.ExternalIds, workspace.Lists, workspace.ListItems)
        { IdentityResolution = identity };
        var epic = new Dictionary<string, EpicLaunchKey>();
        foreach (var (id, key) in workspace.EpicLaunchKeys)
            if (EpicLaunchKey.Create(key.Namespace, key.CatalogItemId, key.ArtifactId) is { } valid) epic[id] = valid;
        var actions = workspace.PluginActions.ToDictionary(pair => pair.Key, pair =>
        {
            var action = pair.Value;
            return new PluginEntryActions(action.SourceLabel,
                action.CanPlay && action.PluginId is not null && action.SourceId is not null
                    ? GameLink.ForPlugin("Play", pair.Key, action.PluginId, action.SourceId, PluginGameActionKind.Play) : null,
                action.CanOpenStore && action.PluginId is not null && action.SourceId is not null
                    ? GameLink.ForPlugin("Store page", pair.Key, action.PluginId, action.SourceId, PluginGameActionKind.OpenStore) : null);
        });
        return (snapshot, identity, new FacetSnapshot { Facets = workspace.Facets, Releases = workspace.ReleaseFacets },
            workspace.PinnedWorkIds.ToHashSet(), epic, workspace.Storefronts, workspace.PreferredHeaderStores,
            actions, workspace.ArtworkChoices);
    }
}
