using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// The eight colours that ARE a theme, in the sense that nothing else about it
/// can be guessed from them.
///
/// <para><b>Why eight, and why these eight.</b> A <see cref="WinnowTheme"/> has
/// twenty-four colours in it, and a format that demanded all twenty-four would
/// be unauthorable — worse, it would let someone ship an incoherent theme by
/// getting the eighteenth one wrong, because nothing would be holding the ramps
/// to each other. The test applied to every field was: <i>can this be derived
/// from something else without losing a decision?</i></para>
///
/// <list type="bullet">
///   <item><description><b>Sixteen could.</b> <c>SurfaceRaised</c> is
///   <c>Surface</c> lifted, <c>VoltPress</c> is <c>Volt</c> pressed,
///   <c>TranslucentTextDim</c> is <c>TextDim</c> paying for the alpha the chrome
///   spent. Every one of them is a CONSEQUENCE of a decision made
///   elsewhere.</description></item>
///   <item><description><b>These eight could not.</b> Two of them build the room
///   — <see cref="Ground"/> is the field the art hangs in and
///   <see cref="Surface"/> is the chrome, and the jump between them is §14.1.1's
///   value-structure axis (1.4x in Nightshift, 4.8x in Box art). One is the ink
///   the room is read with. And five are the ROLES §2 assigns jobs to, which a
///   theme may recolour and may never conflate.</description></item>
/// </list>
///
/// <para><b><see cref="Flare"/> is a seed for a reason that is not about
/// derivation.</b> It is the one hue the room cannot produce — that is what an
/// unread marker has to be — so there is nothing in the room to derive it from,
/// by construction. Everything else here has a family; Flare is the one colour
/// with no family, which is exactly its job.</para>
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
