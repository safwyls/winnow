using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Queries;

namespace Winnow.Application.Library;

public sealed partial class LibraryApplication
{
    public async Task<LibraryWorkspaceResponse> GetWorkspaceAsync(LibraryPreferences? preferences = null, CancellationToken ct = default)
    {
        // The auxiliary identity, artwork and list reads must describe the same library version.
        using var transaction = transactions.Begin();
        preferences ??= await GetPreferencesAsync(ct);
        var snapshot = await library.GetSnapshotAsync(BucketThresholds.Default with
        {
            ShowNonGameEntries = preferences.ShowNonGameEntries,
            ShowExplicitContent = preferences.ShowExplicitContent,
            MaturityCap = BucketThresholds.ParseMaturityCap(preferences.MaturityCap),
        }, ct);
        var facetSnapshot = await facets.GetSnapshotAsync(ct);
        var links = (await identity.GetHistoryAsync(ct: ct)).Where(x => x.IsLive).ToArray();
        var pinnedIds = await pins.GetLivePinnedWorkIdsAsync(ct);
        var epic = await epicKeys.GetAllAsync(ct);
        var storeDetails = await storefronts.ReadAllAsync(ct);
        var preferredHeaders = await headers.GetAllAsync(ct);
        var choices = await artwork.GetAllAsync(ct);
        var plugin = pluginActions is null ? null : await pluginActions.ReadAsync(snapshot, ct);
        var groups = snapshot.Buckets.Select(x => x.Game).Distinct().ToDictionary(x => x.ResolvedWorkId,
            x => new WorkspaceGameGrouping(x.ResolvedWorkId, x.Bucket, x.PlaytimeMinutes, x.LastPlayedAt,
                x.MajorUpdateAt, x.UnreadUpdateCount, x.EntryCount, x.Lifecycle));
        var listItems = snapshot.ListItems.ToLookup(x => x.ListId);
        var response = new LibraryWorkspaceResponse(preferences,
            snapshot.Buckets.Select(x => new WorkspaceOwnershipBucket(x.OwnershipId, x.ReleaseId, x.WorkId,
                x.ResolvedWorkId, x.PlaytimeMinutes, x.LastPlayedAt, x.MajorUpdateAt, x.Bucket,
                x.ConsolidatedDemoCount, x.Lifecycle, groups[x.ResolvedWorkId])).ToArray(),
            snapshot.Works, snapshot.Ownerships, snapshot.Releases, snapshot.ExternalIds, snapshot.Lists, snapshot.ListItems,
            snapshot.Lists.Select(x => ListResponse(x, listItems[x.Id].OrderBy(i => i.Position).Select(i => i.ReleaseId).ToArray())).ToArray(),
            links, facetSnapshot.Facets, facetSnapshot.Releases, pinnedIds.ToArray(),
            epic.ToDictionary(x => x.Key, x => new EpicLaunchKeyResponse(x.Value.Namespace, x.Value.CatalogItemId, x.Value.ArtifactId)),
            storeDetails, preferredHeaders, choices,
            plugin?.ToDictionary(x => x.Key, x => new PluginEntryActionResponse(x.Value.SourceLabel,
                (x.Value.Play ?? x.Value.Store)?.PluginId, (x.Value.Play ?? x.Value.Store)?.PluginSourceId,
                x.Value.Play is not null, x.Value.Store is not null)) ?? []);
        transaction.Commit();
        return response;
    }

    public async Task<IReadOnlyList<ManualGameResponse>> GetManualGamesAsync(CancellationToken ct = default)
        => (await manual.GetAllAsync(ct)).Select(ManualResponse).ToArray();

    public async Task<ManualGameResponse?> GetManualGameAsync(long ownershipId, CancellationToken ct = default)
        => await manual.GetAsync(ownershipId, ct) is { } entry ? ManualResponse(entry) : null;

    public async Task<GameListResponse> CreateLiveListAsync(CreateLiveListRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.Filter);
        using var transaction = transactions.Begin();
        var id = await lists.InsertAsync(GameList.Live(request.Name.Trim(), request.Filter, request.Description), ct);
        var response = await ReadListAsync(id, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"lists/{id}");
        return response;
    }

    public async Task<GameListResponse> SetListFilterAsync(long listId, SetListFilterRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.Filter);
        using var transaction = transactions.Begin();
        var current = await CheckListAsync(listId, request.ExpectedRevision, ct);
        if (!current.IsLive) throw new ArgumentException("Only live lists have filter rules.");
        await lists.SetFilterAsync(listId, request.Filter, ct);
        var response = await ReadListAsync(listId, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"lists/{listId}");
        return response;
    }
}
