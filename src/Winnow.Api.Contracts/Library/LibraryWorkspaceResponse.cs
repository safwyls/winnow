using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Api.Contracts.Library;

/// <summary>One coherent browse workspace. Collections are facts, not remotely callable repositories.</summary>
public sealed record LibraryWorkspaceResponse(
    LibraryPreferences Preferences,
    IReadOnlyList<WorkspaceOwnershipBucket> Buckets,
    IReadOnlyList<Work> Works,
    IReadOnlyList<Ownership> Ownerships,
    IReadOnlyList<Release> Releases,
    IReadOnlyList<ExternalId> ExternalIds,
    IReadOnlyList<GameList> Lists,
    IReadOnlyList<ListItem> ListItems,
    IReadOnlyList<GameListResponse> ListVersions,
    IReadOnlyList<IdentityLink> IdentityLinks,
    IReadOnlyList<Facet> Facets,
    IReadOnlyList<ReleaseFacets> ReleaseFacets,
    IReadOnlyList<long> PinnedWorkIds,
    IReadOnlyDictionary<string, EpicLaunchKeyResponse> EpicLaunchKeys,
    IReadOnlyDictionary<string, StorefrontDetails> Storefronts,
    IReadOnlyDictionary<long, string?> PreferredHeaderStores,
    IReadOnlyList<ArtworkChoice> ArtworkChoices,
    IReadOnlyDictionary<long, PluginEntryActionResponse> PluginActions);

public sealed record WorkspaceOwnershipBucket(long OwnershipId, long ReleaseId, long WorkId, long ResolvedWorkId,
    long PlaytimeMinutes, DateTime? LastPlayedAt, DateTime? MajorUpdateAt, string Bucket,
    int ConsolidatedDemoCount, GameLifecycle? Lifecycle, WorkspaceGameGrouping Game);
public sealed record WorkspaceGameGrouping(long ResolvedWorkId, string Bucket, long PlaytimeMinutes,
    DateTime? LastPlayedAt, DateTime? MajorUpdateAt, int UnreadUpdateCount, int EntryCount, GameLifecycle? Lifecycle);
public sealed record EpicLaunchKeyResponse(string Namespace, string CatalogItemId, string ArtifactId);
public sealed record PluginEntryActionResponse(string? SourceLabel, string? PluginId, string? SourceId, bool CanPlay, bool CanOpenStore);
public sealed record CreateLiveListRequest(string Name, LibraryFilter Filter, string? Description = null);
public sealed record SetListFilterRequest(LibraryFilter Filter, string ExpectedRevision);
