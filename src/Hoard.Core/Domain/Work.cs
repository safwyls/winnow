namespace Hoard.Core.Domain;

/// <summary>
/// Canonical identity layer 1 of 4: the game as a concept ("Skyrim").
/// Never collapse <see cref="Release"/> into this — Skyrim SE is not Skyrim.
/// </summary>
public sealed record Work
{
    public long Id { get; init; }
    public long? IgdbId { get; init; }
    public required string Name { get; init; }
    public string? SortName { get; init; }
    public int? FirstReleaseYear { get; init; }
    public string? Summary { get; init; }
    public string? CoverUrl { get; init; }
}
