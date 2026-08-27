using Avalonia.Media;

namespace Hoard.App.Themes;

/// <summary>
/// The themes that ship, and the reason each one is here.
///
/// <para><b>Four, and the first one is the default and stays the standout.</b>
/// The other three are not alternative brandings of the same idea — each answers
/// a condition the default cannot: a bright room, a dark one, and a person who
/// wants the chrome to have a voice. None of them is a hue rotation of the ramp
/// beside it; each carries its own neutral family, its own ink temperature and
/// its own value curve, because a palette that differs only in hue reads as a
/// filter over one design rather than as a second design.</para>
///
/// <para><b>Two rules hold across all four.</b> Volt is the room's own colour at
/// full voltage, so selection reads as the chrome intensified rather than as a
/// decoration on top of it (§2). And Flare is the one hue the room cannot
/// produce, spent on unread updates and the bucket that counts them and on
/// nothing else — the colour may change per theme, the job may not.</para>
///
/// <para>Deliberately <b>no light theme.</b> §9 inverts the platform's caption
/// order so the first inch of the window is an unlit lip, §5.3's tile scrim
/// fades to Ground, and §5.1's dormancy floor was calibrated against dark
/// capsules on a dark field. A light theme is not this table with the steps
/// reversed; it is a second pass over all three, and shipping a half-done one
/// would break the ramp that is the product's whole encoding.</para>
/// </summary>
public static class HoardThemes
{
    /// <summary>
    /// The house look, unchanged (§2). One dark green-teal ink stepped six
    /// times, mint Volt, hot pink Flare.
    ///
    /// <para>The chrome is a stage: a hued neutral so Volt is the room turned
    /// up, dark enough that cover art is the only thing on screen with real
    /// light in it, and cool enough that the warm-and-dark Steam capsule reads
    /// as warm against it. This is the default because it is the one tuned
    /// against six hundred real capsules rather than against a mock.</para>
    /// </summary>
    public static readonly HoardTheme Hoard = new()
    {
        Id = "hoard",
        Name = "Hoard",
        Reason = "The house look. A green-teal room dark enough that the cover art is the only lit thing in the window.",

        Well = C("#050D0E"),
        Ground = C("#0F1C1E"),
        Surface = C("#16282A"),
        SurfaceRaised = C("#1D3437"),
        SurfaceHigh = C("#254042"),
        Line = C("#2B4A4C"),
        Text = C("#F0EDE7"),
        TextDim = C("#8FA5A0"),
        TextFaint = C("#5A8286"),
        Flare = C("#FF4D93"),
        Volt = C("#4DE8C2"),
        VoltInk = C("#0C2A24"),
        VoltHover = C("#6FEDCE"),
        VoltPress = C("#3BD1AC"),
        Amber = C("#FFB63D"),
        Azure = C("#57A8F0"),
        Danger = C("#E04B45"),
        DangerHover = C("#EF645E"),
        DangerPress = C("#B33A35"),
        DangerInk = C("#FFF2EF"),

        ChromeAlpha = 0.86,
        CaptionAlpha = 0.88,
        TranslucentWell = C("#03090A"),
        TranslucentSurface = C("#071214"),
        TranslucentChromeGround = C("#0C1B1D"),
        TranslucentTextDim = C("#A8BDB7"),
        TranslucentTextFaint = C("#7A9CA0"),
    };

    /// <summary>
    /// For a bright room. The reason is viewing conditions, not taste.
    ///
    /// <para>A <c>#0F1C1E</c> window in daylight is a mirror: the darker the
    /// field, the more of it is the reflection of the room you are sitting in.
    /// So the whole neutral family lifts about two steps and cools to a
    /// blue-steel, which puts the chrome nearer the ambient light and stops
    /// reflections competing with the grid. What it costs is the contrast
    /// between chrome and art — covers pop less against a lighter field — which
    /// is exactly the trade someone in a sunlit room wants to make.</para>
    ///
    /// <para>Volt is the room at full voltage, so it becomes an icy cyan; Azure
    /// has to move out of its way and goes cornflower, far enough round that a
    /// link and a selection edge can never be confused. Flare lightens with the
    /// ground it now sits on.</para>
    /// </summary>
    public static readonly HoardTheme ColdStorage = new()
    {
        Id = "cold-storage",
        Name = "Cold storage",
        Reason = "For a bright room. The whole family lifts two steps and cools, so daylight reflections stop competing with the grid.",

        Well = C("#10161B"),
        Ground = C("#1A232A"),
        Surface = C("#222D36"),
        SurfaceRaised = C("#2C3945"),
        SurfaceHigh = C("#374654"),
        Line = C("#455566"),
        Text = C("#EFF4F8"),
        TextDim = C("#9CB0C1"),
        TextFaint = C("#74899B"),
        Flare = C("#FF6BA6"),
        Volt = C("#5FE2F0"),
        VoltInk = C("#07272C"),
        VoltHover = C("#88ECF6"),
        VoltPress = C("#40C7D6"),
        Amber = C("#FFC24D"),
        Azure = C("#88ADF5"),
        Danger = C("#DC5349"),
        DangerHover = C("#EA6B60"),
        DangerPress = C("#B23F37"),
        DangerInk = C("#FFF3F1"),

        // The one theme that gives something up to translucency: its ink drops
        // back toward Well, so part of the lift it exists for is spent buying
        // the desktop in. Stated rather than hidden — see the report.
        ChromeAlpha = 0.88,
        CaptionAlpha = 0.90,
        TranslucentWell = C("#0A0F13"),
        TranslucentSurface = C("#131A20"),
        TranslucentChromeGround = C("#1C252D"),
        TranslucentTextDim = C("#B4C6D4"),
        TranslucentTextFaint = C("#8497A8"),
    };

    /// <summary>
    /// For a dark room, and for a panel that does not backlight.
    ///
    /// <para>§1 says the art is the interface. This is that claim taken
    /// literally: the neutral family drops to near-black and most of its chroma
    /// is drained out, so the chrome contributes almost no light of its own and
    /// the only lit things in the window are six hundred capsules. The accents
    /// go the other way and gain intensity, because on a field this dark a
    /// muted Volt disappears.</para>
    ///
    /// <para>It reads as a sibling of the default rather than a rival, which is
    /// the point: it is the same room with the lights off.</para>
    /// </summary>
    public static readonly HoardTheme Nightshift = new()
    {
        Id = "nightshift",
        Name = "Nightshift",
        Reason = "For a dark room. The chrome drops to near-black and gives off no light of its own, so the covers are the only lit thing.",

        Well = C("#020506"),
        Ground = C("#070C0D"),
        Surface = C("#0C1213"),
        SurfaceRaised = C("#131B1C"),
        SurfaceHigh = C("#1A2425"),
        Line = C("#263335"),
        Text = C("#F2F0EC"),
        TextDim = C("#93A09E"),
        TextFaint = C("#5C6968"),
        Flare = C("#FF3D8E"),
        Volt = C("#3CF2C4"),
        VoltInk = C("#03211B"),
        VoltHover = C("#69F7D4"),
        VoltPress = C("#22D8AA"),
        Amber = C("#FFB020"),
        Azure = C("#4FA6F5"),
        Danger = C("#E8433C"),
        DangerHover = C("#F26057"),
        DangerPress = C("#B93129"),
        DangerInk = C("#FFF2EF"),

        // Higher alphas than the rest of the table, and for a stated reason:
        // §9's "unlit lip" asks the caption to stay at or below Ground, and a
        // Ground this close to black has almost nothing left to give a backdrop.
        ChromeAlpha = 0.91,
        CaptionAlpha = 0.94,
        TranslucentWell = C("#010304"),
        TranslucentSurface = C("#050A0B"),
        TranslucentChromeGround = C("#0A1213"),
        TranslucentTextDim = C("#ABB8B6"),
        TranslucentTextFaint = C("#7A8886"),
    };

    /// <summary>
    /// The arcade register: the dark glass of a green-phosphor monitor.
    ///
    /// <para>The other three keep the chrome quiet. This one lets it speak — an
    /// olive-green room with real chroma in it, whose full voltage is a
    /// chartreuse that belongs to the room rather than sitting on top of it.
    /// Which makes it the theme that takes a position: it pushes warm capsules
    /// warmer instead of cooling them, so cover art reads hotter than it does
    /// anywhere else in this table. That is a preference, not a defect, and it
    /// is the whole reason someone would pick it.</para>
    ///
    /// <para>Flare is the one hue an olive room cannot reach, so the unread
    /// marker is a hot magenta — 50° clear of Danger, which is the separation §2
    /// asks for so a red the size of a caption button is never mistaken for a
    /// 10px dot. Amber runs hotter than the default's to clear Volt, and Azure
    /// runs cyan-ward to sit as far from Volt as the wheel allows.</para>
    /// </summary>
    public static readonly HoardTheme Phosphor = new()
    {
        Id = "phosphor",
        Name = "Phosphor",
        Reason = "The arcade register — the dark glass of a green monitor. The one theme where the chrome has a voice, and cover art reads hotter for it.",

        Well = C("#080B05"),
        Ground = C("#141A0F"),
        Surface = C("#1C2416"),
        SurfaceRaised = C("#26301D"),
        SurfaceHigh = C("#2F3B24"),
        Line = C("#42522F"),
        Text = C("#F2EFE2"),
        TextDim = C("#A3B08C"),
        TextFaint = C("#6C7A57"),
        Flare = C("#FF4DD2"),
        Volt = C("#B8F24D"),
        VoltInk = C("#182605"),
        VoltHover = C("#CCF77B"),
        VoltPress = C("#A0DA33"),
        Amber = C("#FFA63A"),
        Azure = C("#57C4F0"),
        Danger = C("#E0453F"),
        DangerHover = C("#EE6058"),
        DangerPress = C("#B33530"),
        DangerInk = C("#FFF2EF"),

        ChromeAlpha = 0.86,
        CaptionAlpha = 0.88,
        TranslucentWell = C("#050703"),
        TranslucentSurface = C("#0C110A"),
        TranslucentChromeGround = C("#141A0F"),
        TranslucentTextDim = C("#BAC6A6"),
        TranslucentTextFaint = C("#8B996F"),
    };

    /// <summary>In the order the settings screen draws them. The default leads.</summary>
    public static readonly IReadOnlyList<HoardTheme> All = [Hoard, ColdStorage, Nightshift, Phosphor];

    public static HoardTheme Default => Hoard;

    /// <summary>
    /// The theme stored under <paramref name="id"/>, or the default. An id this
    /// build does not know is treated as unset rather than as an error: a
    /// preference file written by a later version must not stop the app.
    /// </summary>
    public static HoardTheme ById(string? id)
        => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal)) ?? Default;

    private static Color C(string hex) => Color.Parse(hex);
}
