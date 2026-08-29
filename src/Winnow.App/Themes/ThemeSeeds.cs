using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// The eight colours that are a theme: two that build the room (Ground,
/// Surface), one ink (Text), and five roles. The other sixteen colours in
/// a <see cref="WinnowTheme"/> are derived from these.
/// </summary>
public sealed record ThemeSeeds
{
    /// <summary>The field the covers hang in, and the darkest of the two that
    /// build the room.</summary>
    public required Color Ground { get; init; }

    /// <summary>The chrome: rail, filter panel, caption. The jump from
    /// <see cref="Ground"/> to here is the theme's value structure.</summary>
    public required Color Surface { get; init; }

    /// <summary>Primary ink. The dim and faint inks are walked down from the
    /// room rather than down from this, so this one only has to be the
    /// brightest thing the chrome is read with.</summary>
    public required Color Text { get; init; }

    /// <summary>Unread updates and the bucket counting them. Nothing else,
    /// ever — <c>ThemeAudit</c> warns when a theme spends it twice.</summary>
    public required Color Flare { get; init; }

    /// <summary>Selection and recency: §2 asks this to be the room at full
    /// voltage rather than a decoration sitting on it.</summary>
    public required Color Volt { get; init; }

    /// <summary>"You have been here a lot" — high playtime, played out.</summary>
    public required Color Amber { get; init; }

    /// <summary>The informational one, which does the boring work.</summary>
    public required Color Azure { get; init; }

    /// <summary>The one destructive affordance. Its hover, press and ink are
    /// derived; its distance from <see cref="Flare"/> is audited.</summary>
    public required Color Danger { get; init; }
}
