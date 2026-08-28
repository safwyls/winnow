namespace Winnow.Core.Domain;

/// <summary>
/// Layer 2 of 4: a specific edition of a <see cref="Work"/>
/// (Skyrim / Special Edition / Anniversary). Genuinely different games with
/// different achievement sets and mod ecosystems; merging them is a bug.
/// </summary>
public sealed record Release
{
    public long Id { get; init; }
    public required long WorkId { get; init; }
    public long? IgdbVersionId { get; init; }
    public required string Name { get; init; }
    public string? Platform { get; init; }
    public string? EditionNote { get; init; }
}
