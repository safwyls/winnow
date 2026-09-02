namespace Winnow.App.ViewModels;

/// <summary>Which of the screen's three surfaces raised the outcome report, so the
/// report renders only in that surface's header. Without this, one shared
/// <c>HasReport</c> put a same-game outcome into the EXPANSIONS header, with a
/// Retract button for an act the expansion surface never performed.</summary>
public enum MergeReportSurface
{
    /// <summary>No report standing.</summary>
    None,

    /// <summary>The review surface raised this report.</summary>
    Review,

    /// <summary>The expansions surface raised this report.</summary>
    Expansions,

    /// <summary>The history surface raised this report.</summary>
    History,
}
