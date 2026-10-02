using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.Application.Library;

public sealed partial class LibraryApplication(
    ILibraryQueryRepository library, IGameListRepository lists, IHiddenGameRepository hidden,
    IManualEntryRepository manual, IIdentityLinkRepository identity, ISettingsRepository settings,
    IWorkRepository works, IUnitOfWorkFactory transactions, IApplicationChangePublisher changes,
    IFacetRepository facets, IWorkIgdbPinRepository pins, IGroupHeaderPreferenceRepository headers,
    IArtworkChoiceRepository artwork, IStorefrontRepository storefronts,
    Winnow.App.Services.IEpicLaunchKeys epicKeys,
    Winnow.App.Services.PluginGameActionService? pluginActions = null) : ILibraryApplication
{
    public async Task<LibraryResponse> GetLibraryAsync(CancellationToken ct = default)
    {
        var preferences = await GetPreferencesAsync(ct);
        var snapshot = await library.GetSnapshotAsync(BucketThresholds.Default with
        {
            ShowNonGameEntries = preferences.ShowNonGameEntries,
            ShowExplicitContent = preferences.ShowExplicitContent,
            MaturityCap = BucketThresholds.ParseMaturityCap(preferences.MaturityCap),
        }, ct);
        var workMap = snapshot.Works.ToDictionary(x => x.Id);
        var releaseMap = snapshot.Releases.ToDictionary(x => x.Id);
        var ownershipMap = snapshot.Ownerships.ToDictionary(x => x.Id);
        var games = snapshot.Buckets.GroupBy(x => x.ResolvedWorkId).Select(group =>
        {
            var work = workMap[group.Key];
            var game = group.First().Game;
            var entries = group.Select(bucket =>
            {
                var release = releaseMap[bucket.ReleaseId];
                var ownership = ownershipMap[bucket.OwnershipId];
                return new GameEntry(ownership.Id, release.Id, release.WorkId, release.Name,
                    ownership.Store, release.Platform, ownership.Installed,
                    bucket.PlaytimeMinutes, bucket.LastPlayedAt);
            }).ToArray();
            return new LibraryGame(work.Id, work.Name, work.FirstReleaseYear, work.Summary, work.Publisher,
                work.CoverUrl, work.BackgroundUrl, game.Bucket, game.PlaytimeMinutes, game.LastPlayedAt, entries);
        }).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.WorkId).ToArray();
        var items = snapshot.ListItems.ToLookup(x => x.ListId);
        return new LibraryResponse(games, snapshot.Lists.Select(list => ListResponse(list,
            items[list.Id].OrderBy(x => x.Position).Select(x => x.ReleaseId).ToArray())).ToArray());
    }

    public async Task<LibraryGame?> GetGameAsync(long workId, CancellationToken ct = default)
        => (await GetLibraryAsync(ct)).Games.FirstOrDefault(x => x.WorkId == workId || x.Entries.Any(e => e.WorkId == workId));

    public async Task<IReadOnlyList<HiddenGameResponse>> GetHiddenGamesAsync(CancellationToken ct = default)
        => (await hidden.GetHiddenGamesAsync(ct)).Select(x => new HiddenGameResponse(x.WorkId, x.Title, x.HiddenAt, x.StoreEntryCount)).ToArray();

    public async Task SetHiddenAsync(SetHiddenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.WorkIds);
        using var transaction = transactions.Begin();
        foreach (var workId in request.WorkIds.Distinct())
        {
            if (await works.GetAsync(workId, ct) is null) throw Missing("Game");
            if (request.Hidden) await hidden.HideAsync(workId, ct);
            else await hidden.UnhideAsync(workId, ct);
        }
        transaction.Commit();
        changes.Publish("library.changed");
    }

    public async Task<GameListResponse> CreateListAsync(CreateListRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentNullException.ThrowIfNull(request.ReleaseIds);
        using var transaction = transactions.Begin();
        var id = await lists.CreateManualAsync(request.Name.Trim(), request.ReleaseIds, ct);
        var result = await ReadListAsync(id, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"lists/{id}");
        return result;
    }

    public async Task<GameListResponse> EditListAsync(long listId, EditListRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        using var transaction = transactions.Begin();
        await CheckListAsync(listId, request.ExpectedRevision, ct);
        await lists.RenameAsync(listId, request.Name.Trim(), request.Description, ct);
        var result = await ReadListAsync(listId, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"lists/{listId}");
        return result;
    }

    public Task<GameListResponse> AddListMembersAsync(long listId, ListMembersRequest request, CancellationToken ct = default)
        => ChangeMembersAsync(listId, request, lists.AppendItemsAsync, ct);
    public Task<GameListResponse> RemoveListMembersAsync(long listId, ListMembersRequest request, CancellationToken ct = default)
        => ChangeMembersAsync(listId, request, lists.RemoveItemsAsync, ct);
    public Task<GameListResponse> ReorderListAsync(long listId, ListMembersRequest request, CancellationToken ct = default)
        => ChangeMembersAsync(listId, request, lists.ReorderAsync, ct);

    private async Task<GameListResponse> ChangeMembersAsync(long id, ListMembersRequest request,
        Func<long, IReadOnlyList<long>, CancellationToken, Task<IReadOnlyList<long>>> write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request.ReleaseIds);
        using var transaction = transactions.Begin();
        var current = await CheckListAsync(id, request.ExpectedRevision, ct);
        if (current.IsLive) throw new ArgumentException("Live lists have computed membership.");
        await write(id, request.ReleaseIds, ct);
        var result = await ReadListAsync(id, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"lists/{id}");
        return result;
    }

    public async Task DeleteListAsync(long listId, string expectedRevision, CancellationToken ct = default)
    {
        using var transaction = transactions.Begin();
        await CheckListAsync(listId, expectedRevision, ct);
        await lists.DeleteAsync(listId, ct);
        transaction.Commit();
        changes.Publish("library.changed", $"lists/{listId}");
    }

    public async Task<ManualGameResponse> CreateManualGameAsync(ManualGameRequest request, CancellationToken ct = default)
    {
        var entry = await manual.CreateAsync(Draft(request), ct);
        changes.Publish("library.changed", $"games/{entry.WorkId}");
        return ManualResponse(entry);
    }

    public async Task<ManualGameResponse> UpdateManualGameAsync(long ownershipId, ManualGameRequest request, CancellationToken ct = default)
    {
        if (request.ExpectedIgdbMappingRevision is null)
            throw new ArgumentException("The loaded mapping revision is required when editing a manual game.");
        using var transaction = transactions.Begin();
        var current = await manual.GetAsync(ownershipId, ct) ?? throw Missing("Manual game");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedRevision);
        if (ManualResponse(current).Revision != request.ExpectedRevision)
            throw new ApplicationConflictException("The manual game changed. Reload it before editing.");
        if (!await manual.UpdateAsync(ownershipId, Draft(request), ct)) throw Missing("Manual game");
        var result = ManualResponse((await manual.GetAsync(ownershipId, ct))!);
        transaction.Commit();
        changes.Publish("library.changed", $"games/{result.WorkId}");
        return result;
    }

    public async Task DeleteManualGameAsync(long ownershipId, CancellationToken ct = default)
    {
        if (!await manual.DeleteAsync(ownershipId, ct)) throw Missing("Manual game");
        changes.Publish("library.changed");
    }

    public async Task<IdentityActResponse> LinkGamesAsync(LinkGamesRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.ChildWorkIds);
        ArgumentNullException.ThrowIfNull(request.ExpectedSameGameRoots);
        if (request.ChildWorkIds.Prepend(request.ParentWorkId).Any(id => !request.ExpectedSameGameRoots.ContainsKey(id)))
            throw new ArgumentException("The observed identity root of every selected game is required.");
        var id = await identity.LinkAsync(new IdentityLinkRequest
        {
            ParentWorkId = request.ParentWorkId, ChildWorkIds = request.ChildWorkIds,
            Kind = request.Kind, RelationLabel = request.RelationLabel,
            ExpectedSameGameRoots = request.ExpectedSameGameRoots,
        }, ct);
        changes.Publish("library.changed", "identity");
        return new IdentityActResponse(id);
    }

    public async Task SeparateGameAsync(long childWorkId, SeparateGameRequest request, CancellationToken ct = default)
    {
        if (request.ExpectedLinkId <= 0) throw new ArgumentException("The observed identity link is required.");
        using var transaction = transactions.Begin();
        var live = (await identity.GetHistoryAsync(childWorkId, ct)).SingleOrDefault(x => x.IsLive && x.ChildWorkId == childWorkId);
        if (live?.Id != request.ExpectedLinkId)
            throw new ApplicationConflictException("The game relationship changed. Reload it before separating.");
        var changed = await identity.RetractLinkAsync(childWorkId, ct: ct);
        transaction.Commit();
        if (changed) changes.Publish("library.changed", "identity");
    }

    public async Task UndoIdentityActAsync(long actId, CancellationToken ct = default)
    {
        if (await identity.RetractActAsync(actId, ct: ct)) changes.Publish("library.changed", "identity");
    }

    public async Task<LibraryPreferences> GetPreferencesAsync(CancellationToken ct = default)
        => new(BucketThresholds.ParseShowNonGameEntries(await settings.GetAsync(BucketThresholds.ShowNonGameEntriesSettingKey, ct)),
            BucketThresholds.ParseShowExplicitContent(await settings.GetAsync(BucketThresholds.ShowExplicitContentSettingKey, ct)),
            BucketThresholds.FormatMaturityCap(BucketThresholds.ParseMaturityCap(await settings.GetAsync(BucketThresholds.MaturityCapSettingKey, ct))));

    public async Task SetPreferencesAsync(LibraryPreferences preferences, CancellationToken ct = default)
    {
        var cap = BucketThresholds.ParseMaturityCap(preferences.MaturityCap);
        if (BucketThresholds.FormatMaturityCap(cap) != preferences.MaturityCap) throw new ArgumentException("Unknown maturity cap.");
        using var transaction = transactions.Begin();
        await settings.SetAsync(BucketThresholds.ShowNonGameEntriesSettingKey, BucketThresholds.FormatShowNonGameEntries(preferences.ShowNonGameEntries), ct);
        await settings.SetAsync(BucketThresholds.ShowExplicitContentSettingKey, BucketThresholds.FormatShowExplicitContent(preferences.ShowExplicitContent), ct);
        await settings.SetAsync(BucketThresholds.MaturityCapSettingKey, preferences.MaturityCap, ct);
        transaction.Commit();
        changes.Publish("library.changed", "preferences/library");
    }

    private async Task<GameListResponse> ReadListAsync(long id, CancellationToken ct)
        => ListResponse(await lists.GetAsync(id, ct) ?? throw Missing("List"),
            (await lists.GetItemsAsync(id, ct)).Select(x => x.ReleaseId).ToArray());

    private async Task<GameListResponse> CheckListAsync(long id, string expected, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);
        var current = await ReadListAsync(id, ct);
        if (current.Revision != expected) throw new ApplicationConflictException("The list changed. Reload it before editing.");
        return current;
    }

    private static GameListResponse ListResponse(GameList list, IReadOnlyList<long> members)
    {
        var fingerprint = new ListFingerprint(list.Id, list.Name, list.Description, list.IsLive, list.FilterJson, members.ToArray());
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(fingerprint, ApplicationJsonContext.Default.ListFingerprint))));
        return new(list.Id, list.Name, list.Description, list.IsLive, members, revision)
            { Filter = list.IsLive ? list.Filter : null };
    }

    private static ManualGameDraft Draft(ManualGameRequest request) => new()
    {
        Title = request.Title, FirstReleaseYear = request.FirstReleaseYear, PlatformLabel = request.PlatformLabel,
        ExecutablePath = request.ExecutablePath, InstallPath = request.InstallPath, IgdbId = request.IgdbId,
        SteamAppId = request.SteamAppId, ExpectedIgdbMappingRevision = request.ExpectedIgdbMappingRevision,
    };
    private static ManualGameResponse ManualResponse(ManualEntry entry)
    {
        var response = new ManualGameResponse(entry.OwnershipId, entry.ReleaseId,
            entry.WorkId, entry.Title, entry.IgdbId, entry.SteamAppId, entry.IgdbMappingRevision,
            entry.FirstReleaseYear, entry.PlatformLabel, entry.ExecutablePath, entry.InstallPath, entry.AddedAt, entry.UpdatedAt);
        return response with { Revision = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(response, ApplicationJsonContext.Default.ManualGameResponse))) };
    }
    private static ApplicationNotFoundException Missing(string entity) => new($"{entity} was not found.");
}

internal sealed record ListFingerprint(long Id, string Name, string? Description, bool IsLive, string? Filter, long[] Members);
[JsonSerializable(typeof(ListFingerprint))]
[JsonSerializable(typeof(ManualGameResponse))]
internal sealed partial class ApplicationJsonContext : JsonSerializerContext;
