using Avalonia.Media;

namespace Winnow.App.Themes;

/// <summary>
/// The themes that ship, and the reason each one is here.
///
/// <para><b>Four, and the first one is the default and stays the standout.</b>
/// The other three are not alternative brandings of the same idea. Each is a
/// different room, and the test of the set is that a THUMBNAIL OF THE RAIL
/// ALONE identifies which one you are in, with no label.</para>
///
/// <para><b>Hue is the weakest axis, and the first set of themes spent all of
/// its budget there.</b> Four palettes that differ by rotation and by value read
/// as four settings of one design — a darker one, a lifted one, a green one.
/// So these differ on the axes that actually make a room:</para>
///
/// <list type="table">
///   <item>
///     <term>Temperature</term>
///     <description>Winnow and Nightshift are cool, Tungsten is genuinely warm,
///     Box art is neutral. A warm room is not a hue rotation of a cool one: it
///     inverts which end of the wheel is the ground and which is the signal.</description>
///   </item>
///   <item>
///     <term>Chroma strategy</term>
///     <description>How much colour the chrome is allowed at all. Winnow's
///     neutral is committed green-teal; Box art's has none, and holds saturation
///     back for the cover art and the two colours that mean STOP and UNREAD.</description>
///   </item>
///   <item>
///     <term>Value structure</term>
///     <description>Where the contrast lives. Winnow steps evenly; Nightshift is
///     almost flat and lets edges carry the layout; Box art puts a 4.8x jump
///     between the art field and the chrome; Tungsten's steps are the softest in
///     the set.</description>
///   </item>
///   <item>
///     <term>Material</term>
///     <description>What the chrome reads as — inked board, black glass, felt,
///     mount card. Carried by how surfaces step and how visible the edges are,
///     never by the accent.</description>
///   </item>
/// </list>
///
/// <para><b>Two rules hold across all four.</b> Volt is the room's own colour at
/// full voltage, so selection reads as the chrome intensified rather than as a
/// decoration on top of it (§2). And Flare is the one hue the room cannot
/// produce, spent on unread updates and the bucket that counts them and on
/// nothing else — the colour may change per theme, the job may not.</para>
///
/// <para>Deliberately <b>no light theme.</b> §9 keeps the caption at a chrome
/// tone so the first inch of the window is unlit, §5.3's tile scrim fades to
/// Ground, and §5.1's dormancy floor was calibrated against dark capsules on a
/// dark field. A light theme is not this table with the steps reversed; it is a
/// second pass over all three, and shipping a half-done one would break the ramp
/// that is the product's whole encoding.</para>
/// </summary>
public static class WinnowThemes
{
    /// <summary>
    /// The house look, unchanged (§2). One dark green-teal ink stepped six
    /// times, mint Volt, hot pink Flare.
    ///
    /// <para><b>Material: inked board.</b> The chrome is a stage — a hued
    /// neutral so Volt is the room turned up, dark enough that cover art is the
    /// only thing on screen with real light in it, and cool enough that the
    /// warm-and-dark Steam capsule reads as warm against it. The value structure
    /// is the most even in the set: 1.8x from the art field to the rail, 1.6x
    /// from the rail to a selected row, edges at 1.6:1. Nothing is doing
    /// anything clever, which is what makes it the one you can look at for an
    /// hour.</para>
    ///
    /// <para>This is the default because it is the one tuned against six hundred
    /// real capsules rather than against a mock.</para>
    /// </summary>
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

    /// <summary>
    /// Black glass, with the layout etched onto it rather than stacked out of it.
    ///
    /// <para><b>This is the theme that had to earn its slot.</b> Shipped first as
    /// "Winnow with the lights off", it was a value change and nothing else, and
    /// the verdict on it was exactly that. What makes it a room of its own is not
    /// how dark it is — it is <b>where the contrast lives</b>. Every other theme
    /// separates its surfaces by stepping them apart; this one does not step at
    /// all. The art field, the rail and the caption sit within 1.4x of each other
    /// at the bottom of the scale, effectively one black pane, and every boundary
    /// in the window is a <i>drawn line</i>: <c>Line</c> runs at 2.5:1 against
    /// the rail, the brightest edge in the set and nearly twice Winnow's. The
    /// window reads as one sheet of glass with the layout scribed on it.</para>
    ///
    /// <para><b>Material: black glass. Temperature: cold.</b> The ink is a blue
    /// black rather than Winnow's green-teal, so the two are not siblings at any
    /// brightness — and the room at full voltage is a hard cyan rather than a
    /// mint. Chroma in the neutrals is almost nil; what little there is, is in
    /// the edges, which is why the hairlines read as light on glass rather than
    /// as grey rules.</para>
    ///
    /// <para>It answers a real condition: a dark room, and a panel that does not
    /// backlight. The chrome contributes almost no light of its own, so the only
    /// lit things in the window are six hundred capsules — §1 taken literally,
    /// which the flat structure states far better than dimness did.</para>
    /// </summary>
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

    /// <summary>
    /// A warm room, lit by one lamp. The only theme in the set that is not cool.
    ///
    /// <para><b>Temperature is the axis, and it is the one none of the first four
    /// took.</b> Every hue rotation between blue, teal and green is still a cold
    /// room; a tobacco-brown ground is not that room at a different angle. It
    /// changes which end of the wheel is ground and which is signal, and it
    /// changes what happens to the art: warm-and-dark Steam capsules stop being
    /// pushed cool by simultaneous contrast and settle into the field instead of
    /// standing off it. Covers read softer here than anywhere else in the table.
    /// That is a preference, not a defect, and it is the whole reason to pick
    /// it.</para>
    ///
    /// <para><b>Material: felt. Value structure: the softest in the set.</b>
    /// <c>Line</c> runs at 1.4:1 against the rail — the quietest edge of the
    /// four — so the surfaces are told apart by tone rather than by rule, and
    /// nothing in the chrome has a hard boundary. Beside Nightshift, which is the
    /// same idea inverted, the two are unmistakable at thumbnail size: one is all
    /// edge and no step, the other all step and no edge.</para>
    ///
    /// <para><b>What it costs, stated rather than hidden.</b> A warm room spends
    /// the warm end of the wheel on the ground, so Volt (brass) and Amber (ember)
    /// sit 27° apart — closer than any other pair in the set. They are told apart
    /// by lightness and by where each appears, and the room's one unreachable
    /// hue, magenta, is reserved for Flare as always.</para>
    /// </summary>
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

    /// <summary>
    /// A neutral mount, and the art is the only colour in the window.
    ///
    /// <para><b>Chroma strategy is the axis here, not hue — there is no hue.</b>
    /// §1 says the art is the interface; this is that claim taken to the end of
    /// its argument. The whole neutral family is a true graphite with no lean at
    /// all, <c>Volt</c> is cold white light rather than a colour (a neutral room
    /// at full voltage is not a hue, it is brightness), <c>Amber</c> is a sand
    /// and <c>Azure</c> a steel. Only the two colours that mean STOP and UNREAD
    /// keep their saturation, which makes this the theme where §2's rule is
    /// literally visible: <c>Flare</c> is not merely the one hue the room cannot
    /// produce, it is the only hue in the window that did not come out of a cover.</para>
    ///
    /// <para><b>Value structure: the starkest in the set, and inverted from what
    /// the others do.</b> The art field drops to near-black and the chrome jumps
    /// 4.8x above it — against Winnow's 1.8x and Nightshift's 1.4x. So the covers
    /// sit in a mount, the way a print does, and the chrome is a board around
    /// them rather than a slightly different shade of the same room. At thumbnail
    /// size it is the only rail in the set that is visibly lighter than the wall
    /// beside it.</para>
    ///
    /// <para><b>Material: mount card.</b> Matte, neutral, and it never argues
    /// with what is hung on it. Someone whose library is mostly art they chose
    /// for the art picks this one.</para>
    ///
    /// <para>Volt and Azure sit 29° apart on the wheel and are told apart by
    /// lightness rather than by hue — Volt is a near-white at 17:1 against the
    /// art field, Azure a mid steel. That is deliberate and it is the cost of the
    /// no-chroma rule: this room has no second saturated colour to spend.</para>
    /// </summary>
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
    /// The id the house theme shipped under before the rename to Winnow, and
    /// the value sitting in <c>appearance.theme</c> in every database that
    /// predates it.
    ///
    /// <para><b>Renaming the id without this would orphan a real preference.</b>
    /// It happens to orphan it onto the same theme today, because this theme is
    /// also the default — so the window would look identical and the bug would
    /// be invisible until the day the default changed, at which point a
    /// preference the user set on purpose would silently become someone else's
    /// choice. Aliasing is a line of code; discovering that later is not.</para>
    ///
    /// <para><b>It is tried only after a real lookup misses</b>, so a
    /// user-authored theme that claims the id <c>hoard</c> still wins it — the
    /// alias is a bridge for old settings, not a reservation.</para>
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
