namespace Winnow.App.Themes;

/// <summary>
/// What a theme asks the rest of the Appearance screen to be set to when it is
/// picked — how much desktop, made of what, reaching how far, arranged how.
///
/// <para><b>Why a theme gets an opinion about the slider at all.</b> §14 is
/// careful that the slider is a statement about how much desktop the USER wants
/// and should not mean a different thing after a theme change, and that argument
/// still holds for the FLOOR the slider walks to — <c>MinChromeAlpha</c> is a
/// constant shared by every theme for exactly that reason. This is a different
/// claim: a theme is a look, and "40% acrylic with the field open" can be part
/// of a look in a way "the chrome bottoms out at 0.30" cannot. A theme built
/// against an open field and then seen solid is not that theme.</para>
///
/// <para><b>They are defaults, and defaults lose.</b> Nothing here locks a
/// control: every value is applied once, at the moment the theme is picked, and
/// the slider beside it still moves. And on the way back in, a value the user
/// has actually stored beats the theme's suggestion — which is what makes this a
/// starting position rather than a setting the theme keeps taking back. The four
/// built-ins declare none of this, so they behave exactly as they always
/// have.</para>
///
/// <para>Every field is optional. A theme that only wants to ask for the field
/// to be open says so and leaves the other three alone.</para>
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
