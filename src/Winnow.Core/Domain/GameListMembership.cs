namespace Winnow.Core.Domain;

/// <summary>
/// One list that contains a game, and the release row that put it there.
/// Membership is stored per release and resolved per read: a list contains a
/// game when any release of any work in that game's live <c>same_game</c>
/// group is a member.
/// </summary>
public sealed record GameListMembership
{
    /// <summary>The list's id.</summary>
    public required long ListId { get; init; }

    /// <summary>The list's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The release id whose <c>list_items</c> row confers membership.</summary>
    public required long ReleaseId { get; init; }
}
