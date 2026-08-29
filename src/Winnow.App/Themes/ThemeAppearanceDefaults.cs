namespace Winnow.App.Themes;

/// <summary>
/// What a theme asks the rest of the Appearance screen to be set to when it
/// is picked. Applied once at selection time; stored user values win. All
/// fields optional; the built-ins declare none.
/// </summary>
public sealed record ThemeAppearanceDefaults
{
    /// <summary>Whole percent, 0 to 100. 0 is a real position — fully opaque —
    /// so a theme that wants to be seen solid says <c>0</c> rather than
    /// omitting the field.</summary>
    public int? Transparency { get; init; }

    /// <summary>Acrylic or Mica. Never <see cref="WinnowBackdrop.None"/>: that is
    /// a report from the platform, not a thing anyone can ask for.</summary>
    public WinnowBackdrop? Backdrop { get; init; }

    /// <summary>Whether the cover wall's field opens up along with the chrome.
    /// The tiles never do, at any setting, in any theme.</summary>
    public bool? WallTranslucent { get; init; }

    /// <summary>Flush or floating (§15).</summary>
    public WinnowLayout? Layout { get; init; }

    /// <summary>True when the theme asks for nothing, which is the built-ins'
    /// case and the common one. Lets the service skip the whole path rather
    /// than applying four no-ops.</summary>
    public bool IsEmpty
        => Transparency is null && Backdrop is null && WallTranslucent is null && Layout is null;
}
