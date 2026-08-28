namespace Winnow.Core.Domain;

/// <summary>
/// Membership of a <see cref="Release"/> in a <see cref="GameList"/>,
/// with a user-controlled position.
/// </summary>
public sealed record ListItem
{
    public required long ListId { get; init; }
    public required long ReleaseId { get; init; }
    public required int Position { get; init; }
}
