using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// The four themes that ship. Each differs on temperature, chroma strategy,
/// value structure and material so a rail thumbnail alone identifies the theme.
/// Volt is always the room at full voltage; Flare is always the unreachable hue.
/// </summary>
public static class WinnowThemes
{
    /// <summary>The house look (§2). Green-teal inked board, mint Volt, hot
    /// pink Flare. Default because it was tuned against real capsules.</summary>
    public static readonly WinnowTheme Winnow = new()
    {
        Id = "winnow",
        Name = "Winnow",
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

        TranslucentSurface = C("#071214"),
        TranslucentChromeGround = C("#040C0D"),
        TranslucentTextDim = C("#A8BDB7"),
        TranslucentTextFaint = C("#7A9CA0"),
    };

    /// <summary>Black glass. Surfaces barely step; every boundary is a drawn
    /// line (2.5:1 edge). Blue-black ink, hard cyan Volt.</summary>
    public static readonly WinnowTheme Nightshift = new()
    {
        Id = "nightshift",
        Name = "Nightshift",
        Reason = "Black glass. The surfaces stop stepping apart and every boundary becomes a drawn line, so the window is one dark pane with the layout scribed on it.",

        Well = C("#04060A"),
        Ground = C("#070A10"),
        // 1.44x above Ground — the flattest step in the set, and the point.
        Surface = C("#0A0E15"),
        SurfaceRaised = C("#121823"),
        SurfaceHigh = C("#1A2231"),
        // 2.46:1 against the rail. This is the theme's structure; do not dim it
        // toward the other themes' edges or the layout stops being legible.
        Line = C("#3E5275"),
        Text = C("#E8EDF5"),
        TextDim = C("#8D9AB4"),
        TextFaint = C("#5C6B87"),
        Flare = C("#FF3D8C"),
        Volt = C("#2FE0FF"),
        VoltInk = C("#032430"),
        VoltHover = C("#6FEAFF"),
        VoltPress = C("#14C2E2"),
        Amber = C("#FFC24A"),
        // Pale periwinkle, 35° off Volt — the same separation Winnow puts between
        // its own mint and azure, so a link and a selection edge stay distinct.
        Azure = C("#8AA9FF"),
        Danger = C("#E8483F"),
        DangerHover = C("#F2645A"),
        DangerPress = C("#B8342C"),
        DangerInk = C("#FFF2EF"),

        TranslucentSurface = C("#03060B"),
        TranslucentChromeGround = C("#020407"),
        TranslucentTextDim = C("#A8B5CC"),
        TranslucentTextFaint = C("#7C8AA6"),
    };

    /// <summary>A warm room lit by one lamp. Tobacco-brown felt, brass Volt,
    /// softest edges in the set (1.4:1). Warm covers settle into the field.</summary>
    public static readonly WinnowTheme Tungsten = new()
    {
        Id = "tungsten",
        Name = "Tungsten",
        Reason = "A warm room lit by one lamp. Edges nearly disappear and warm cover art settles into the field instead of standing off it.",

        Well = C("#0C0704"),
        Ground = C("#17100A"),
        Surface = C("#221810"),
        SurfaceRaised = C("#2D2116"),
        SurfaceHigh = C("#38291C"),
        // 1.38:1 against the rail — deliberately the faintest edge in the set.
        Line = C("#40301F"),
        Text = C("#F5EBDC"),
        TextDim = C("#B09A7E"),
        TextFaint = C("#7C6950"),
        // The one hue a tobacco room cannot reach.
        Flare = C("#FF3DBE"),
        // Brass: the room at full voltage.
        Volt = C("#F7C544"),
        VoltInk = C("#2A1B06"),
        VoltHover = C("#FFD670"),
        VoltPress = C("#DCA92C"),
        // Ember, and hotter than the brass rather than merely darker — "you have
        // been here a lot" has to read as heat in a room already made of it.
        Amber = C("#FF6B33"),
        // The one cool thing in the chrome, which is what an informational colour
        // should be here: a link is not part of the room.
        Azure = C("#7FAECF"),
        // Crimson rather than the default's orange-red, so it is not read as a
        // hotter Amber. 31° clear of Flare, which is the §2 separation.
        Danger = C("#D93B52"),
        DangerHover = C("#E85668"),
        DangerPress = C("#AE2C40"),
        DangerInk = C("#FFF0F2"),

        TranslucentSurface = C("#0E0904"),
        TranslucentChromeGround = C("#0A0603"),
        TranslucentTextDim = C("#C7B394"),
        TranslucentTextFaint = C("#94805F"),
    };

    /// <summary>A neutral mount. True graphite with no chroma; the covers and
    /// Flare are the only hues in the window. Starkest value structure in the
    /// set (4.8x field-to-chrome). Cold white Volt.</summary>
    public static readonly WinnowTheme BoxArt = new()
    {
        Id = "box-art",
        Name = "Box art",
        Reason = "A neutral mount. The chrome gives up colour entirely, so the covers - and the unread dot - are the only hues in the window.",

        Well = C("#060708"),
        // Near-black, and 4.8x below the chrome: the art hangs in a recess.
        Ground = C("#0B0C0D"),
        Surface = C("#202429"),
        SurfaceRaised = C("#2C3137"),
        SurfaceHigh = C("#383E45"),
        Line = C("#474E56"),
        Text = C("#ECEEF0"),
        TextDim = C("#9BA3AA"),
        TextFaint = C("#6B747C"),
        // The only saturated hue in the chrome, and the point of the theme.
        Flare = C("#FF4D9E"),
        // Cold white light. A room with no hue has no hue at full voltage
        // either; what it has is more of it.
        Volt = C("#E8F4F4"),
        VoltInk = C("#12161A"),
        VoltHover = C("#F6FCFC"),
        VoltPress = C("#C2D8D8"),
        // Sand, not amber: attention without chroma.
        Amber = C("#DEC08C"),
        Azure = C("#8FB4D6"),
        // Danger keeps its saturation. A close button that has been talked down
        // to a grey is a close button that no longer says what it does (§8).
        Danger = C("#E05252"),
        DangerHover = C("#EA6C6C"),
        DangerPress = C("#B33F3F"),
        DangerInk = C("#FFF2F2"),

        TranslucentSurface = C("#0E1114"),
        TranslucentChromeGround = C("#08090B"),
        TranslucentTextDim = C("#B2BAC2"),
        TranslucentTextFaint = C("#838C95"),
    };

    /// <summary>In the order the settings screen draws them. The default leads,
    /// then the two cool rooms, then the warm one and the neutral one — so no two
    /// adjacent cards are the same argument.</summary>
    public static readonly IReadOnlyList<WinnowTheme> All = [Winnow, Nightshift, Tungsten, BoxArt];

    public static WinnowTheme Default => Winnow;

    /// <summary>
    /// The id the house theme shipped under before the rename. Tried only
    /// after a real lookup misses, so a user theme claiming "hoard" still wins.
    /// </summary>
    public const string LegacyDefaultId = "hoard";

    /// <summary>
    /// The theme stored under <paramref name="id"/>, or the default. An id this
    /// build does not know is treated as unset rather than as an error: a
    /// preference file written by a later version must not stop the app, and a
    /// preference written by an EARLIER one may name a theme that has since been
    /// retired ("cold-storage", "phosphor"), which lands here for the same reason
    /// and with the same answer.
    /// </summary>
    public static WinnowTheme ById(string? id)
        => All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal))
            ?? (string.Equals(id, LegacyDefaultId, StringComparison.Ordinal) ? Winnow : Default);

    private static Color C(string hex) => Color.Parse(hex);
}
