namespace Winnow.App.Themes;

/// <summary>
/// How the window is put together: flush (one surface divided by rules) or
/// floating (content panes as rounded cards on the window's ground).
/// Orthogonal to theme and transparency; every layout works with both.
/// </summary>
public enum WinnowLayout
{
    /// <summary>Panes meet edge to edge, divided by a 1px rule. The layout every
    /// measurement in design-system.md §14 was taken against, and the
    /// default.</summary>
    Flush,

    /// <summary>Content panes are rounded cards with a uniform gap around them,
    /// on a window ground that runs unbroken behind the caption and every
    /// gap.</summary>
    Floating,
}

/// <summary>The layout list and the copy the Appearance screen shows for
/// each one.</summary>
public static class WinnowLayouts
{
    /// <summary>What an unset preference reads as. Flush, because it is what the
    /// app has always looked like and it is the arrangement every contrast
    /// measurement in §14 was taken against.</summary>
    public const WinnowLayout Default = WinnowLayout.Flush;

    public static IReadOnlyList<WinnowLayout> All { get; } =
        [WinnowLayout.Flush, WinnowLayout.Floating];

    /// <summary>Stable id. Persisted; never localised.</summary>
    public static string Id(WinnowLayout layout) => layout switch
    {
        WinnowLayout.Floating => "floating",
        _ => "flush",
    };

    public static WinnowLayout ById(string? id) => id switch
    {
        "floating" => WinnowLayout.Floating,
        _ => Default,
    };

    /// <summary>What the settings screen calls it. Named for the shape it
    /// produces rather than for the framework that popularised it — "islands" is
    /// what JetBrains users call this and it is not a word anyone would arrive at
    /// from looking at the window.</summary>
    public static string Name(WinnowLayout layout) => layout switch
    {
        WinnowLayout.Floating => "Floating",
        _ => "Flush",
    };

    /// <summary>One sentence, written for the person choosing (§7): what the
    /// arrangement does, not how it feels.</summary>
    public static string Reason(WinnowLayout layout) => layout switch
    {
        WinnowLayout.Floating =>
            "The rail, the library and the filter panel become rounded cards with a gap around each, all three starting under the title bar. The window's own ground runs behind them and through every gap, and with transparency up it is the desktop that shows there.",
        _ =>
            "The rail, the library and the filter panel meet edge to edge, divided by a 1px rule. How Winnow has looked until now, and what every contrast figure on this screen was measured against.",
    };
}
