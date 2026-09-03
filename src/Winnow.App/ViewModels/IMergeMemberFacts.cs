namespace Winnow.App.ViewModels;

/// <summary>
/// The facts <see cref="MergeMemberLabels"/> reads to tell same-titled members
/// apart: stores, year, publisher, added one at a time until no two members
/// share a label.
/// </summary>
/// <remarks>
/// <see cref="MergeSideViewModel"/> is the only implementer. This interface
/// exists so the ladder is testable against a row's facts rather than against
/// a view.
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
