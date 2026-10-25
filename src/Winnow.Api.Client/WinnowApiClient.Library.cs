using Winnow.Api.Contracts.Library;

namespace Winnow.Api.Client;

public sealed partial class WinnowApiClient
{
    public Task<LibraryWorkspaceResponse> GetWorkspaceAsync(LibraryPreferences? preferences = null, CancellationToken ct = default) =>
        preferences is null ? GetAsync<LibraryWorkspaceResponse>("library/workspace", ct)
        : SendAsync<LibraryPreferences, LibraryWorkspaceResponse>(HttpMethod.Post, "library/workspace", preferences, ct: ct);
    public Task<IReadOnlyList<ManualGameResponse>> GetManualGamesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<ManualGameResponse>>("manual-games", ct);
    public Task<ManualGameResponse> GetManualGameAsync(long ownershipId, CancellationToken ct = default) => GetAsync<ManualGameResponse>($"manual-games/{ownershipId}", ct);
    public Task<GameListResponse> CreateLiveListAsync(CreateLiveListRequest request, CancellationToken ct = default) =>
        SendAsync<CreateLiveListRequest, GameListResponse>(HttpMethod.Post, "lists/live", request, ct: ct);
    public Task<GameListResponse> SetListFilterAsync(long listId, SetListFilterRequest request, CancellationToken ct = default) =>
        SendAsync<SetListFilterRequest, GameListResponse>(HttpMethod.Put, $"lists/{listId}/filter", request, ct: ct);
    public Task<LibraryResponse> GetLibraryAsync(CancellationToken ct = default) => GetAsync<LibraryResponse>("library", ct);
    public Task<LibraryGame> GetGameAsync(long workId, CancellationToken ct = default) => GetAsync<LibraryGame>($"games/{workId}", ct);
    public Task<IReadOnlyList<HiddenGameResponse>> GetHiddenGamesAsync(CancellationToken ct = default) => GetAsync<IReadOnlyList<HiddenGameResponse>>("hidden-games", ct);
    public Task SetHiddenAsync(SetHiddenRequest request, CancellationToken ct = default) => SendAsync(HttpMethod.Put, "hidden-games", request, ct);
    public Task<GameListResponse> CreateListAsync(CreateListRequest request, CancellationToken ct = default) =>
        SendAsync<CreateListRequest, GameListResponse>(HttpMethod.Post, "lists", request, ct: ct);
    public Task<GameListResponse> EditListAsync(long listId, EditListRequest request, CancellationToken ct = default) =>
        SendAsync<EditListRequest, GameListResponse>(HttpMethod.Put, $"lists/{listId}", request, ct: ct);
    public Task<GameListResponse> AddListMembersAsync(long listId, ListMembersRequest request, CancellationToken ct = default) =>
        SendAsync<ListMembersRequest, GameListResponse>(HttpMethod.Post, $"lists/{listId}/members", request, ct: ct);
    public Task<GameListResponse> RemoveListMembersAsync(long listId, ListMembersRequest request, CancellationToken ct = default) =>
        SendAsync<ListMembersRequest, GameListResponse>(HttpMethod.Delete, $"lists/{listId}/members", request, ct: ct);
    public Task<GameListResponse> ReorderListAsync(long listId, ListMembersRequest request, CancellationToken ct = default) =>
        SendAsync<ListMembersRequest, GameListResponse>(HttpMethod.Put, $"lists/{listId}/order", request, ct: ct);
    public Task DeleteListAsync(long listId, string expectedRevision, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"lists/{listId}", new DeleteListRequest(expectedRevision), ct);
    public Task<ManualGameResponse> CreateManualGameAsync(ManualGameRequest request, CancellationToken ct = default) =>
        SendAsync<ManualGameRequest, ManualGameResponse>(HttpMethod.Post, "manual-games", request, ct: ct);
    public Task<ManualGameResponse> UpdateManualGameAsync(long ownershipId, ManualGameRequest request, CancellationToken ct = default) =>
        SendAsync<ManualGameRequest, ManualGameResponse>(HttpMethod.Put, $"manual-games/{ownershipId}", request, ct: ct);
    public Task DeleteManualGameAsync(long ownershipId, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Delete, $"manual-games/{ownershipId}", null, ct);
    public Task<IdentityActResponse> LinkGamesAsync(LinkGamesRequest request, CancellationToken ct = default) =>
        SendAsync<LinkGamesRequest, IdentityActResponse>(HttpMethod.Post, "identity/links", request, ct: ct);
    public Task SeparateGameAsync(long childWorkId, SeparateGameRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"identity/links/{childWorkId}", request, ct);
    public Task UndoIdentityActAsync(long actId, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Post, $"identity/acts/{actId}/undo", null, ct);
    public Task<LibraryPreferences> GetPreferencesAsync(CancellationToken ct = default) => GetAsync<LibraryPreferences>("preferences/library", ct);
    public Task SetPreferencesAsync(LibraryPreferences preferences, CancellationToken ct = default) => SendAsync(HttpMethod.Put, "preferences/library", preferences, ct);
}
