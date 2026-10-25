namespace Winnow.Api.Contracts.Library;

public sealed record LibraryResponse(IReadOnlyList<LibraryGame> Games, IReadOnlyList<GameListResponse> Lists);
public sealed record LibraryGame(long WorkId, string Title, int? FirstReleaseYear, string? Summary,
    string? Publisher, string? CoverUrl, string? BackgroundUrl, string Bucket, long PlaytimeMinutes,
    DateTime? LastPlayedAt, IReadOnlyList<GameEntry> Entries);
public sealed record GameEntry(long OwnershipId, long ReleaseId, long WorkId, string Title,
    string Store, string? Platform, bool Installed, long PlaytimeMinutes, DateTime? LastPlayedAt);
public sealed record GameListResponse(long Id, string Name, string? Description, bool IsLive,
    IReadOnlyList<long> ReleaseIds, string Revision)
{
    public Winnow.Core.Queries.LibraryFilter? Filter { get; init; }
}
public sealed record CreateListRequest(string Name, IReadOnlyList<long> ReleaseIds);
public sealed record EditListRequest(string Name, string? Description, string ExpectedRevision);
public sealed record ListMembersRequest(IReadOnlyList<long> ReleaseIds, string ExpectedRevision);
public sealed record DeleteListRequest(string ExpectedRevision);
public sealed record SetHiddenRequest(IReadOnlyList<long> WorkIds, bool Hidden);
public sealed record HiddenGameResponse(long WorkId, string Title, DateTime HiddenAt, int StoreEntryCount);
public sealed record ManualGameRequest(string Title, int? FirstReleaseYear = null,
    string? PlatformLabel = null, string? ExecutablePath = null, string? InstallPath = null,
    long? IgdbId = null, string? SteamAppId = null, long? ExpectedIgdbMappingRevision = null,
    string? ExpectedRevision = null);
public sealed record ManualGameResponse(long OwnershipId, long ReleaseId, long WorkId, string Title,
    long? IgdbId, string? SteamAppId, long IgdbMappingRevision, int? FirstReleaseYear,
    string? PlatformLabel, string? ExecutablePath, string? InstallPath, DateTime AddedAt, DateTime UpdatedAt)
{
    public string Revision { get; init; } = "";
}
public sealed record LinkGamesRequest(long ParentWorkId, IReadOnlyList<long> ChildWorkIds,
    IReadOnlyDictionary<long, long> ExpectedSameGameRoots, string Kind = "same_game", string? RelationLabel = null);
public sealed record IdentityActResponse(long ActId);
public sealed record SeparateGameRequest(long ExpectedLinkId);
public sealed record LibraryPreferences(bool ShowNonGameEntries, bool ShowExplicitContent, string MaturityCap);
