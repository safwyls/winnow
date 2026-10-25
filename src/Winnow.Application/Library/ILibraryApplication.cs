using Winnow.Api.Contracts.Library;

namespace Winnow.Application.Library;

public interface ILibraryApplication
{
    Task<LibraryWorkspaceResponse> GetWorkspaceAsync(LibraryPreferences? preferences = null, CancellationToken ct = default);
    Task<IReadOnlyList<ManualGameResponse>> GetManualGamesAsync(CancellationToken ct = default);
    Task<ManualGameResponse?> GetManualGameAsync(long ownershipId, CancellationToken ct = default);
    Task<GameListResponse> CreateLiveListAsync(CreateLiveListRequest request, CancellationToken ct = default);
    Task<GameListResponse> SetListFilterAsync(long listId, SetListFilterRequest request, CancellationToken ct = default);
    Task<LibraryResponse> GetLibraryAsync(CancellationToken ct = default);
    Task<LibraryGame?> GetGameAsync(long workId, CancellationToken ct = default);
    Task<IReadOnlyList<HiddenGameResponse>> GetHiddenGamesAsync(CancellationToken ct = default);
    Task SetHiddenAsync(SetHiddenRequest request, CancellationToken ct = default);
    Task<GameListResponse> CreateListAsync(CreateListRequest request, CancellationToken ct = default);
    Task<GameListResponse> EditListAsync(long listId, EditListRequest request, CancellationToken ct = default);
    Task<GameListResponse> AddListMembersAsync(long listId, ListMembersRequest request, CancellationToken ct = default);
    Task<GameListResponse> RemoveListMembersAsync(long listId, ListMembersRequest request, CancellationToken ct = default);
    Task<GameListResponse> ReorderListAsync(long listId, ListMembersRequest request, CancellationToken ct = default);
    Task DeleteListAsync(long listId, string expectedRevision, CancellationToken ct = default);
    Task<ManualGameResponse> CreateManualGameAsync(ManualGameRequest request, CancellationToken ct = default);
    Task<ManualGameResponse> UpdateManualGameAsync(long ownershipId, ManualGameRequest request, CancellationToken ct = default);
    Task DeleteManualGameAsync(long ownershipId, CancellationToken ct = default);
    Task<IdentityActResponse> LinkGamesAsync(LinkGamesRequest request, CancellationToken ct = default);
    Task SeparateGameAsync(long childWorkId, SeparateGameRequest request, CancellationToken ct = default);
    Task UndoIdentityActAsync(long actId, CancellationToken ct = default);
    Task<LibraryPreferences> GetPreferencesAsync(CancellationToken ct = default);
    Task SetPreferencesAsync(LibraryPreferences preferences, CancellationToken ct = default);
}
