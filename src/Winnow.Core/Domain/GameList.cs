using Winnow.Core.Queries;

namespace Winnow.Core.Domain;

/// <summary>
/// A user-authored list. Maps to the <c>lists</c> table; named GameList to avoid
/// colliding with BCL List. Either manual (fixed set) or live (rule-defined,
/// recomputed at read time). The DB column is <c>is_smart</c> (migration 0001);
/// application code uses <see cref="IsLive"/>.
/// </summary>
public sealed record GameList
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The stored column (<c>is_smart</c>). Use <see cref="IsLive"/> in application code.</summary>
    public bool IsSmart { get; init; }

    /// <summary>
    /// A serialised <see cref="LibraryFilter"/> for a live list; null for a
    /// manual one.
    /// </summary>
    public string? FilterJson { get; init; }

    /// <summary>
    /// Whether membership is computed from <see cref="Filter"/> at read time
    /// rather than stored in <c>list_items</c>.
    /// </summary>
    public bool IsLive => IsSmart;

    /// <summary>
    /// The rule this live list is defined by, or <see cref="LibraryFilter.Empty"/> for a manual one.
    /// Falls back to the whole library if the stored filter no longer parses.
    /// </summary>
    public LibraryFilter Filter => IsSmart ? LibraryFilter.FromJson(FilterJson) : LibraryFilter.Empty;

    /// <summary>A list the user fills by hand. Ordered by <see cref="ListItem.Position"/>.</summary>
    public static GameList Manual(string name, string? description = null)
        => new() { Name = name, Description = description, IsSmart = false, FilterJson = null };

    /// <summary>Creates a live list defined by a rule. Never has <c>list_items</c>.</summary>
    public static GameList Live(string name, LibraryFilter filter, string? description = null)
        => new() { Name = name, Description = description, IsSmart = true, FilterJson = filter.ToJson() };
}
