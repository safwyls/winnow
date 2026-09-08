namespace Winnow.App.Themes;

/// <summary>
/// How the window is put together: flush (one surface divided by rules) or
/// floating (content panes as rounded cards on the window's ground).
/// Orthogonal to theme and transparency; every layout works with both.
/// </summary>
public enum WinnowLayout
{
    /// <summary>Panes meet edge to edge, divided by a 1px rule. The layout every
    /// original measurement in design-system.md §14 was taken against.</summary>
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
    /// <summary>What an unset preference reads as.</summary>
    public const WinnowLayout Default = WinnowLayout.Floating;

    public static IReadOnlyList<WinnowLayout> All { get; } =
        [WinnowLayout.Floating, WinnowLayout.Flush];

    /// <summary>Stable id. Persisted; never localised.</summary>
    public static string Id(WinnowLayout layout) => layout switch
    {
        WinnowLayout.Floating => "floating",
        _ => "flush",
    };

    public static WinnowLayout ById(string? id) => id switch
    {
        "flush" => WinnowLayout.Flush,
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
            "Rounded cards with a gap around each.",
        _ =>
            "Panes meet edge to edge, divided by a rule.",
    };
}
