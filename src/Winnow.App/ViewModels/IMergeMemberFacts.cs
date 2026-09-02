namespace Winnow.App.ViewModels;

/// <summary>
/// The facts <see cref="MergeMemberLabels"/> reads to tell same-titled members
/// apart: stores, year, publisher, added one at a time until no two members
/// share a label.
/// </summary>
/// <remarks>
/// The ladder was written against <see cref="MergeSideViewModel"/>, which carries
/// cover art and a decode request the history log does not need. A history row
/// supplies <see cref="MergeMemberFacts"/>; a card supplies its own side.
/// </remarks>
internal interface IMergeMemberFacts
{
    /// <summary>The title the library shows for this member.</summary>
    string Title { get; }

    /// <summary>False when no ownership row names a store, in which case the store rung is skipped.</summary>
    bool HasStores { get; }

    /// <summary>The stores spelled out as display names, comma-joined.</summary>
    string StoreNames { get; }

    /// <summary>First release year, or null when no source has supplied one.</summary>
    int? Year { get; }

    /// <summary>The same year as the row draws it, or an em dash.</summary>
    string YearText { get; }

    /// <summary>The publisher, or null when no source has supplied one.</summary>
    string? Publisher { get; }
}

/// <summary>
/// One member's facts with no view attached, for the history log. Stores are
/// fetched only when two titles inside one act collide, so the common row
/// costs one work read.
/// </summary>
/// <param name="Title">The title the library shows for this member.</param>
/// <param name="StoreNames">Store display names, comma-joined. Empty when no ownership row names a store.</param>
/// <param name="Year">First release year, or null when no source has supplied one.</param>
/// <param name="Publisher">The publisher, or null when no source has supplied one.</param>
internal sealed record MergeMemberFacts(
    string Title,
    string StoreNames,
    int? Year,
    string? Publisher) : IMergeMemberFacts
{
    /// <inheritdoc/>
    public bool HasStores => StoreNames.Length > 0;

    /// <inheritdoc/>
    public string YearText => Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
}
