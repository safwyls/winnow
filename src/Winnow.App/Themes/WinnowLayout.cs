namespace Winnow.App.Themes;

/// <summary>
/// How the window is put together: one continuous surface divided by rules, or
/// content panes floating as rounded cards on the window's own ground.
///
/// <para><b>This is structure, not material.</b> A theme says what the room is
/// made of and the transparency slider says how much of the desktop it admits;
/// this says how the pieces are ARRANGED, and it is orthogonal to both — every
/// layout works in every theme at every position on the slider. That is why it
/// is its own section on the Appearance screen rather than a fifth theme or a
/// third qualifier hanging off the slider.</para>
///
/// <para><b>Where the pattern comes from.</b> VS Code shipped it in Aug 2026 and
/// JetBrains ships the same thing, where its users call it <i>islands</i>. Both
/// draw the same line in the same place, and it is not "everything becomes a
/// card": <b>chrome stays flush to the window and content regions detach.</b>
/// The title bar spans the full width and does not float; neither does the
/// activity strip or the status bar. What floats is the sidebar, the editor and
/// the secondary panel — and the window's own background shows through the gaps
/// between them, which is the whole of what makes them read as floating.</para>
///
/// <para><b>Which of Winnow's regions are content.</b> The rail, the library pane
/// (the cover wall, the list view, or whichever screen has replaced it) and the
/// filter panel. Only the caption stays flush on the ground with the gaps — see
/// the notes in <see cref="WinnowTheme.Tokens"/> for what that does to the
/// palette.</para>
///
/// <para><b>The command bar and the cut bar are not a fourth region.</b> They
/// were, and it did not work: flush under the caption and painted from the
/// caption's own ink, they made the first inch of the window one tall
/// undifferentiated block of chrome — and they are not window chrome. Search,
/// layout, density, display and sort all act on the library. They are its
/// header, so they are inside its pane, in BOTH layouts. Which pane a control
/// belongs to is a fact about what the control does, and that is not a function
/// of whether the panes are inset.</para>
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

/// <summary>
/// The layout list, and the copy the Appearance screen shows for each one.
///
/// <para>Written here beside the enum rather than in the view model for the same
/// reason <see cref="WinnowBackdrops"/> is: the name and the sentence are part of
/// what the value MEANS, and a settings screen that had to carry them would be
/// the second place they could drift.</para>
/// </summary>
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
