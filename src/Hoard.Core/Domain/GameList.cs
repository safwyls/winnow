namespace Hoard.Core.Domain;

/// <summary>
/// A user-authored (or smart/filter-backed) list. Maps to the
/// <c>lists</c> table; named GameList to avoid colliding with BCL List.
/// </summary>
public sealed record GameList
{
    public long Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public bool IsSmart { get; init; }
    public string? FilterJson { get; init; }
}
