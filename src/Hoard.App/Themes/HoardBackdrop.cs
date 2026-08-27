namespace Hoard.App.Themes;

/// <summary>
/// Which material Windows composes behind the window — and, as the value the
/// platform reports back, which one it actually agreed to.
///
/// <para><b>The two are genuinely different pictures, not two spellings of
/// "translucent".</b> Acrylic is a blur-behind at a high radius: it samples
/// whatever is under the window and the desktop is unmistakably present in the
/// chrome. Mica samples the wallpaper and then tints toward its own near-black
/// base so hard that the wallpaper contributes almost nothing — measured on this
/// machine the composite behind our chrome back-solves to the same near-black
/// tone whether the wallpaper under the window is orange rock or blue sky. That
/// makes Mica a TONE rather than a VIEW, which is a legitimate thing to prefer
/// and a dishonest thing to sell as transparency.</para>
///
/// <para><see cref="None"/> is not a choice anyone can pick. It is what the
/// platform reports when it refused both, and it exists so the answer to "what
/// did we get" is a value rather than a bool that cannot tell a refusal from a
/// substitution.</para>
/// </summary>
public enum HoardBackdrop
{
    /// <summary>Nothing composited. Windows 10, a remote session, a compositor
    /// that declined — the state the opaque token set exists to catch.</summary>
    None,

    /// <summary>Blur-behind. The default, and the one the slider can be seen
    /// through.</summary>
    Acrylic,

    /// <summary>Windows 11's wallpaper tint. Quieter, more native, and closer to
    /// a tone than to a view.</summary>
    Mica,
}

/// <summary>
/// The two backdrops the Appearance screen offers, their stored ids, and what
/// each one does said in a few words (§7).
/// </summary>
public static class HoardBackdrops
{
    /// <summary>
    /// Acrylic, and it stays the default.
    ///
    /// <para>§14.3 records why: the previous build led with Mica and the verdict
    /// was that transparency "doesn't come across as transparency at all". The
    /// limit was the material, not the alpha. A default that cannot show what the
    /// slider does is a default that makes the slider look broken.</para>
    /// </summary>
    public const HoardBackdrop Default = HoardBackdrop.Acrylic;

    /// <summary>In the order the screen draws them: the default first.</summary>
    public static readonly IReadOnlyList<HoardBackdrop> All =
        [HoardBackdrop.Acrylic, HoardBackdrop.Mica];

    public static string Id(HoardBackdrop backdrop) => backdrop switch
    {
        HoardBackdrop.Mica => "mica",
        HoardBackdrop.Acrylic => "acrylic",
        _ => "none",
    };

    public static string Name(HoardBackdrop backdrop) => backdrop switch
    {
        HoardBackdrop.Mica => "Mica",
        HoardBackdrop.Acrylic => "Acrylic",
        _ => "None",
    };

    /// <summary>
    /// What picking it does, in a sentence, written for the person choosing.
    /// Mica's is the one that matters: it is not sold as a lesser acrylic, and
    /// it is not sold as a view of the desktop either, because it is neither.
    /// </summary>
    public static string Reason(HoardBackdrop backdrop) => backdrop switch
    {
        HoardBackdrop.Mica =>
            "Windows 11 only. Tints toward its own near-black base so hard that the wallpaper barely reaches the window - a quieter, more native tone rather than a view of the desktop.",
        HoardBackdrop.Acrylic =>
            "Blurs whatever is behind the window, so the desktop is genuinely visible through the chrome. This is the one the slider can be seen through.",
        _ =>
            "Nothing composited behind the window.",
    };

    /// <summary>
    /// The backdrop stored under <paramref name="id"/>, or the default.
    ///
    /// <para>An id this build does not know reads as unset rather than as an
    /// error, for the same reason <c>HoardThemes.ById</c> does it: a preference
    /// written by a later version must not stop the app, and <c>none</c> — which
    /// is a report, never a choice — lands here too and gets the same answer.</para>
    /// </summary>
    public static HoardBackdrop ById(string? id) => id switch
    {
        "mica" => HoardBackdrop.Mica,
        "acrylic" => HoardBackdrop.Acrylic,
        _ => Default,
    };
}
