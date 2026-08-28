using Winnow.Core.Queries;

namespace Winnow.Core.Domain;

/// <summary>
/// A user-authored list. Maps to the <c>lists</c> table; named GameList to avoid
/// colliding with BCL List.
///
/// <para><b>Two kinds, and the names are decided.</b> A fixed set of games the
/// user put there by hand is a <b>list</b>. A set defined by a rule, recomputed
/// every time it is read, is a <b>live list</b>. Not "smart", not "dynamic
/// collection" — those name the mechanism, and the user does not care about the
/// mechanism; "live" names what they will actually notice, which is that the
/// thing keeps up with them.</para>
///
/// <para><b>The column is still called <c>is_smart</c>.</b> It was named in
/// migration 0001 and migrations are append-only, so the schema keeps the older
/// word and this type translates: <see cref="IsLive"/> is the name the rest of
/// the application uses. Renaming the column would mean a table rebuild — SQLite
/// can rename a column, but §6's schema is quoted verbatim in the design document
/// and the mismatch costs one property, which is cheaper than the divergence.
/// Documented here rather than migrated, deliberately.</para>
/// </summary>
public sealed record GameList
{
    public long Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// The stored column. <see cref="IsLive"/> is the name to read and write in
    /// application code; this one exists because migration 0001 named it.
    /// </summary>
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
    /// The rule this live list is defined by, or
    /// <see cref="LibraryFilter.Empty"/> for a manual one.
    ///
    /// <para>Total, because <see cref="LibraryFilter.FromJson"/> is: a live list
    /// whose stored filter no longer parses shows the whole library rather than
    /// disappearing from the sidebar. That is a visible, recoverable wrong
    /// answer, which is the best available outcome for a value that came off
    /// disk.</para>
    /// </summary>
    public LibraryFilter Filter => IsSmart ? LibraryFilter.FromJson(FilterJson) : LibraryFilter.Empty;

    /// <summary>A list the user fills by hand. Ordered by <see cref="ListItem.Position"/>.</summary>
    public static GameList Manual(string name, string? description = null)
        => new() { Name = name, Description = description, IsSmart = false, FilterJson = null };

    /// <summary>
    /// A list defined by a rule — "saving a filtered set as a new list".
    ///
    /// <para>Never has <c>list_items</c>. Writing its current members into that
    /// table would freeze it: the games it names today would be the games it
    /// names forever, and the one thing the user asked for — that it keep up — is
    /// exactly the thing that would stop happening.</para>
    /// </summary>
    public static GameList Live(string name, LibraryFilter filter, string? description = null)
        => new() { Name = name, Description = description, IsSmart = true, FilterJson = filter.ToJson() };
}
