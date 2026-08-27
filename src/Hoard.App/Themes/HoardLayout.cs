namespace Hoard.App.Themes;

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
/// <para><b>Which of Hoard's regions are content.</b> The rail, the cover wall
/// (or the list view, or whichever pane has replaced it) and the filter panel.
/// The caption, the command bar and the cut bar are chrome and actions, so they
/// stay flush on the ground with the gaps — see the notes in
/// <see cref="HoardTheme.Tokens"/> for what that does to the palette.</para>
/// </summary>
public enum HoardLayout
{
    /// <summary>Panes meet edge to edge, divided by a 1px rule. The layout every
    /// measurement in design-system.md §14 was taken against, and the
    /// default.</summary>
    Flush,

    /// <summary>Content panes are rounded cards with a uniform gap around them,
    /// on a window ground that runs unbroken behind the caption, the command bar
    /// and every gap.</summary>
    Floating,
}

/// <summary>
/// The layout list, and the copy the Appearance screen shows for each one.
///
/// <para>Written here beside the enum rather than in the view model for the same
/// reason <see cref="HoardBackdrops"/> is: the name and the sentence are part of
/// what the value MEANS, and a settings screen that had to carry them would be
/// the second place they could drift.</para>
/// </summary>
public static class HoardLayouts
{
    /// <summary>What an unset preference reads as. Flush, because it is what the
    /// app has always looked like and it is the arrangement every contrast
    /// measurement in §14 was taken against.</summary>
    public const HoardLayout Default = HoardLayout.Flush;

    public static IReadOnlyList<HoardLayout> All { get; } =
        [HoardLayout.Flush, HoardLayout.Floating];

    /// <summary>Stable id. Persisted; never localised.</summary>
    public static string Id(HoardLayout layout) => layout switch
    {
        HoardLayout.Floating => "floating",
        _ => "flush",
    };

    public static HoardLayout ById(string? id) => id switch
    {
        "floating" => HoardLayout.Floating,
        _ => Default,
    };

    /// <summary>What the settings screen calls it. Named for the shape it
    /// produces rather than for the framework that popularised it — "islands" is
    /// what JetBrains users call this and it is not a word anyone would arrive at
    /// from looking at the window.</summary>
    public static string Name(HoardLayout layout) => layout switch
    {
        HoardLayout.Floating => "Floating",
        _ => "Flush",
    };

    /// <summary>One sentence, written for the person choosing (§7): what the
    /// arrangement does, not how it feels.</summary>
    public static string Reason(HoardLayout layout) => layout switch
    {
        HoardLayout.Floating =>
            "The rail, the library and the filter panel become rounded cards with a gap around each. The window's own ground runs behind all of them - across the title bar, under the command bar and through every gap - and with transparency up it is the desktop that shows in the gaps.",
        _ =>
            "The rail, the library and the filter panel meet edge to edge, divided by a 1px rule. How Hoard has looked until now, and what every contrast figure on this screen was measured against.",
    };
}
