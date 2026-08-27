using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.Services;
using Hoard.App.Themes;

namespace Hoard.App.ViewModels;

/// <summary>
/// The rail's <c>SETTINGS › APPEARANCE</c> screen: which theme is up, and how
/// much of the desktop the window lets through.
///
/// <para><b>Why a rail screen and not the command bar's Display popover.</b>
/// Three reasons, and the third is the one that decides it. A theme picker is
/// four cards with a sentence each, and the popover is capped at 360px because
/// it hangs off a control in a row that must not reflow. Display's two toggles
/// are preferences about <i>this view</i> — how covers are drawn, which rows the
/// query returns — and they sit beside the density slider and the sort menu they
/// belong with; a theme is application-wide chrome and belongs nowhere near a
/// control that only exists while the library is on screen. And the rail's
/// SETTINGS section was written to grow downward for exactly this.</para>
///
/// <para>§5.1 holds: this raises commands and reads state. The service owns the
/// resource dictionary and the settings table.</para>
/// </summary>
public partial class AppearanceViewModel : ObservableObject
{
    /// <summary>
    /// The slider's drawn width, and the thumb's. The AA mark is positioned in
    /// pixels off these, because a mark that does not sit exactly over the value
    /// it names is worse than no mark: it would be a claim about a threshold,
    /// drawn in the wrong place.
    /// </summary>
    private const double TrackWidth = 340;
    private const double ThumbWidth = 16;

    private readonly ThemeService _service;

    public AppearanceViewModel(ThemeService service)
    {
        _service = service;
        Themes = [.. HoardThemes.All.Select(t => new ThemeChoiceViewModel(t))];

        Backdrops =
        [
            .. HoardBackdrops.All.Select(b => new AppearanceOptionViewModel(
                b, HoardBackdrops.Name(b), HoardBackdrops.Reason(b))),
        ];

        Reach =
        [
            new AppearanceOptionViewModel(
                false,
                "Chrome only",
                "The rail, the title bar and the filter panel. The library pane stays solid - its command bar and cut bar with it, since they are part of it - and so do the settings screens and the list view. How Hoard has looked until now."),
            new AppearanceOptionViewModel(
                true,
                "Chrome and the wall",
                "The library pane opens up too, at half the amount - its field of art, its command bar and its cut bar together - and so does everything that shares its place: settings, the review queue, the list view. The covers themselves stay solid, so the desktop shows in the gutters between them."),
        ];

        Layouts =
        [
            .. HoardLayouts.All.Select(l => new LayoutChoiceViewModel(l)),
        ];

        _service.Applied += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>
    /// The service the window needs for its backdrop. Exposed here rather than
    /// resolved from the container by the view, so the shell has one source of
    /// this state and a hand-built view model still produces a working window.
    /// </summary>
    public ThemeService Service => _service;

    public IReadOnlyList<ThemeChoiceViewModel> Themes { get; }

    /// <summary>Acrylic or Mica: which material Windows composes behind the
    /// window. Only shown once the slider has left zero — with nothing coming
    /// through, there is nothing for it to be a material of.</summary>
    public IReadOnlyList<AppearanceOptionViewModel> Backdrops { get; }

    /// <summary>How far the transparency reaches: the chrome, or the chrome and
    /// the cover wall's field.</summary>
    public IReadOnlyList<AppearanceOptionViewModel> Reach { get; }

    /// <summary>
    /// How the window is put together (§15): panes meeting edge to edge, or
    /// content panes floating as rounded cards on the window's ground.
    ///
    /// <para><b>Its own section, above TRANSPARENCY and below THEME, and not a
    /// third qualifier on the slider.</b> The two qualifiers that already hang
    /// off the slider are qualifiers because they are meaningless without it —
    /// at SOLID there is no material to choose and no reach to set. Layout is not
    /// like that: it applies in every theme at every position, including SOLID,
    /// so hanging it off a quantity it does not depend on would be a lie about
    /// what it is. It goes above transparency because structure is read before
    /// material — you see how a window is put together before you notice what it
    /// is made of.</para>
    ///
    /// <para><b>And it is drawn the way THEME is drawn, not the way the
    /// qualifiers are.</b> The qualifiers are consequences and the honest way to
    /// show a consequence is to say it, which is why they are a name and a
    /// sentence. A layout is a SHAPE — it is the one thing on this screen that a
    /// picture states better than a paragraph — so it takes the theme cards'
    /// miniature, which this screen already established, at the size the two of
    /// them need rather than the four.</para>
    /// </summary>
    public IReadOnlyList<LayoutChoiceViewModel> Layouts { get; }

    public string Title => "Appearance";

    public string IntroMessage =>
        "How the window looks. Everything here applies everywhere and survives a restart.";

    // ══ The transparency slider ═════════════════════════════════════════════
    // Mica is a binary window hint, but nothing anyone can SEE is: the perceived
    // translucency is entirely the alpha on our own surfaces over that backdrop,
    // so it is continuous and ours to set. A checkbox was the wrong control for
    // it, and at the alpha the checkbox turned on it was also not visibly doing
    // anything.

    /// <summary>
    /// Whole percent, 0 to 100. Bound two-way to the slider.
    ///
    /// <para><b>Zero is a real position, not an off state dressed as one.</b> It
    /// is the default, it is bit-for-bit the opaque palette, and it is the answer
    /// for anyone who wants §8's floor with no argument — which is why the label
    /// under that end of the track is a word and not an absence.</para>
    /// </summary>
    public double Transparency
    {
        get => _service.Transparency;
        set => _service.SetTransparency((int)Math.Round(value));
    }

    public string TransparencyReading =>
        $"{_service.Transparency.ToString(CultureInfo.InvariantCulture)}%";

    public bool IsSolid => _service.Transparency == 0;

    /// <summary>
    /// What the metadata ink measures right now on the chrome surface that does
    /// worst — usually a selected rail row, which is the rail with a veil over it
    /// — against a dark desktop.
    ///
    /// <para>It goes UP as the slider travels, because a dark desktop is darker
    /// than our own rail and admitting more of it deepens the ground the labels
    /// sit on. Reporting only this would be flattering and useless, which is what
    /// the second number is for.</para>
    /// </summary>
    public string ContrastOnDarkWallpaper => Ratio(Colorimetry.DarkDesktop);

    /// <summary>
    /// The same measurement against a pure white backdrop — the brightest thing
    /// any wallpaper can be, so a number that holds here holds everywhere.
    ///
    /// <para>This is the one that falls, and it is stated rather than buried. The
    /// point is not to talk anyone out of the setting: it is that a user
    /// accepting a contrast cost should be able to see the size of it.</para>
    /// </summary>
    public string ContrastOnWhiteWallpaper => Ratio(Colorimetry.White);

    /// <summary>
    /// The label beside the worst-case number, which names the line it crossed
    /// rather than leaving the reader to compare two figures. Said once, in the
    /// row it belongs to; the note underneath does not repeat it.
    /// </summary>
    public string WhiteWallpaperNote => UnderAa
        ? "on a white one - under the 4.5:1 minimum"
        : "on a white one";

    /// <summary>The label beside the friendlier number. Named for the condition a
    /// reader can check, not for the compositor that produced it.</summary>
    public string DarkWallpaperNote => "on a dark desktop";

    /// <summary>True once the white-backdrop measurement is under AA.</summary>
    public bool UnderAa => !IsSolid
        && Colorimetry.ChromeMetadataContrast(_service.Theme, _service.Transparency / 100.0, Colorimetry.White)
            < Colorimetry.AaThreshold;

    /// <summary>The highest setting at which the worst case still clears AA, for
    /// the theme that is up. It moves with the theme, which is why it is drawn
    /// rather than written into the copy.</summary>
    public int AaCeiling => Colorimetry.AaCeiling(_service.Theme);

    /// <summary>Where to draw the mark on the track, in pixels from its left
    /// edge. The thumb's centre travels from half a thumb in to half a thumb from
    /// the end, so the mark follows the same geometry or it lies.</summary>
    public Thickness AaMarkMargin =>
        new(((TrackWidth - ThumbWidth) * (AaCeiling / 100.0)) + (ThumbWidth / 2) - 0.5, 0, 0, 0);

    public double SliderWidth => TrackWidth;

    public string ContrastNote => IsSolid
        ? "Measured on the chrome surface that does worst, which is a selected rail row. Solid, so no desktop reaches it and the number cannot move."
        : UnderAa
            ? $"Measured on the chrome surface that does worst, which is a selected rail row. Past {AaCeiling}% the white figure drops under 4.5:1. Your desktop sits somewhere between the two, and a dark wallpaper sits at the first."
            : "Measured on the chrome surface that does worst, which is a selected rail row. Your desktop sits somewhere between the two, and a dark wallpaper sits at the first.";

    /// <summary>
    /// What the machine actually did with the request. Windows 10, a
    /// remote-desktop session and a compositor that refuses all end here, and
    /// the screen has to say so — a slider that is up and doing nothing is worse
    /// than a slider at zero.
    /// </summary>
    public bool TransparencyUnavailable => _service.TransparencyRequested && !_service.BackdropAvailable;

    public string TransparencyStatus => TransparencyUnavailable
        ? "This machine is not compositing the desktop behind the window, so Hoard is drawing solid. The setting stays where you left it and takes effect where it can."
        : WallTranslucent
            ? "The rail, the title bar, the filter panel and their fields all admit the desktop, and so does the library pane - its field of art, its two bars and the search box on them - along with the panes that share its place. The covers themselves never do, at any setting - the dormancy ramp is two layers that are only opaque together, and it needs its own ground under it."
            : "The rail, the title bar, the filter panel and the fields in it admit the desktop. The library pane stays solid, and so do its command bar, its cut bar, its search box and the panes that share its place.";

    // ══ Material, and reach ═════════════════════════════════════════════════
    // The screen holds four decisions now, and four rows would be a wall of
    // controls. So the transparency card is ONE quantity with two qualifiers
    // hanging off it — how much, what it is made of, how far it goes — and the
    // two qualifiers are not drawn at all while the quantity is zero, because at
    // zero neither of them does anything at all.

    /// <summary>Whether the material and reach blocks are drawn. They are
    /// meaningless at SOLID, and hiding them keeps the common case to one
    /// slider.</summary>
    public bool ShowComposition => !IsSolid;

    /// <summary>What the user asked Windows for.</summary>
    public HoardBackdrop Backdrop => _service.Backdrop;

    /// <summary>True when Mica is the request — a positive test, and the reason
    /// the measured note below it is drawn.</summary>
    public bool MicaPicked => _service.Backdrop == HoardBackdrop.Mica;

    /// <summary>The measured composite behind our chrome under Mica on this
    /// machine, whatever the wallpaper under the window happens to be. Rendered
    /// in Plex Mono like every other number on this screen (§3).</summary>
    public string MicaComposite => "#201F1E";

    public string MicaCompositeNote =>
        "is what the backdrop resolves to under Mica here, whichever wallpaper is behind the window. That is the material tinting toward its own base, not a fault - but it is why Mica reads as a tone and not as a view.";

    /// <summary>
    /// True when the machine composited a DIFFERENT material from the one that
    /// was asked for.
    ///
    /// <para>Falling through is right — a machine that refuses Mica is better off
    /// with acrylic than with a solid window — and doing it silently is not.
    /// Someone who picked Mica and got acrylic would otherwise conclude the
    /// choice does nothing, which is the same complaint the slider was rebuilt
    /// to answer.</para>
    /// </summary>
    public bool BackdropSubstituted => _service.BackdropSubstituted;

    public string BackdropSubstitutedNote => _service.Backdrop == HoardBackdrop.Mica
        ? "Mica was refused here - it needs Windows 11 - so the window is running acrylic instead. The preference stays where you left it and comes back on a machine that can do it."
        : "Acrylic was refused here, so the window is running Mica instead. It will read as a tone rather than as a view of the desktop.";

    /// <summary>Whether the cover wall's field is included.</summary>
    public bool WallTranslucent => _service.WallTranslucent;

    /// <summary>Whether the content panes float (§15).</summary>
    public bool IsFloating => _service.IsFloating;

    /// <summary>
    /// What the gaps between the panes admit, said only when there are gaps and
    /// only when something can come through them.
    ///
    /// <para>It is a separate sentence rather than a clause on
    /// <see cref="TransparencyStatus"/> because it is the one surface in the
    /// window that does NOT take the chrome's alpha: a gap carries no wordmark,
    /// no label and no art, so it opens the whole way, and a screen that reports
    /// every other surface's share owes this one its own line.</para>
    /// </summary>
    public bool ShowGapNote => IsFloating && !IsSolid;

    public string GapNote =>
        "The gaps between the panes take no fill of ours at all, so they admit the whole desktop rather than the chrome's share of it. That is what makes the panes read as floating rather than as drawn with a seam - and at SOLID it is the window's own ground in them, one step below the field the covers hang in.";

    /// <summary>How much of the chrome is desktop at this position, as a whole
    /// percent. The number the slider is really setting.</summary>
    public string ChromeAdmits => Admits(HoardTheme.MinChromeAlpha);

    /// <summary>And the wall's, which is exactly half of it.</summary>
    public string WallAdmits => Admits(HoardTheme.MinWallAlpha);

    public string WallAdmitsNote =>
        "of the wall is - half. Measured over a real wallpaper, the wall at the chrome's own amount comes out level with the rail and lighter than a dormant cover, which turns dimmed art into a hole and the recess the covers hang in into a flat pane. At half it stays under both.";

    [RelayCommand]
    private void SelectTheme(ThemeChoiceViewModel? choice)
    {
        if (choice is not null)
        {
            _service.SelectTheme(choice.Theme);
        }
    }

    [RelayCommand]
    private void SelectBackdrop(AppearanceOptionViewModel? choice)
    {
        if (choice?.Value is HoardBackdrop backdrop)
        {
            _service.SelectBackdrop(backdrop);
        }
    }

    [RelayCommand]
    private void SelectLayout(LayoutChoiceViewModel? choice)
    {
        if (choice is not null)
        {
            _service.SetLayout(choice.Layout);
        }
    }

    [RelayCommand]
    private void SelectReach(AppearanceOptionViewModel? choice)
    {
        if (choice?.Value is bool translucent)
        {
            _service.SetWallTranslucent(translucent);
        }
    }

    /// <summary>How much desktop a surface with this floor lets through at the
    /// position the slider is holding, as a whole percent.</summary>
    private string Admits(double floorAlpha)
    {
        var t = _service.Transparency / 100.0;
        var alpha = 1 - (t * (1 - floorAlpha));
        return Math.Round((1 - alpha) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private string Ratio(Color backdrop)
    {
        var ratio = Colorimetry.ChromeMetadataContrast(
            _service.Theme, _service.Transparency / 100.0, backdrop);
        return ratio.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
    }

    private void Refresh()
    {
        foreach (var choice in Themes)
        {
            choice.IsSelected = ReferenceEquals(choice.Theme, _service.Theme);
        }

        foreach (var choice in Backdrops)
        {
            choice.IsSelected = choice.Value is HoardBackdrop b && b == _service.Backdrop;
        }

        foreach (var choice in Reach)
        {
            choice.IsSelected = choice.Value is bool w && w == _service.WallTranslucent;
        }

        // The miniatures are repainted rather than rebuilt, for the reason the
        // theme cards are not: a theme card draws its OWN palette and never
        // changes, while a layout card draws whichever theme is up — the
        // question it asks is "what would this arrangement look like in the room
        // you are in", and a card still showing the previous room would answer a
        // question nobody asked.
        foreach (var choice in Layouts)
        {
            choice.IsSelected = choice.Layout == _service.Layout;
            choice.Repaint(_service.Theme);
        }

        OnPropertyChanged(nameof(Transparency));
        OnPropertyChanged(nameof(TransparencyReading));
        OnPropertyChanged(nameof(IsSolid));
        OnPropertyChanged(nameof(ContrastOnDarkWallpaper));
        OnPropertyChanged(nameof(ContrastOnWhiteWallpaper));
        OnPropertyChanged(nameof(UnderAa));
        OnPropertyChanged(nameof(WhiteWallpaperNote));
        OnPropertyChanged(nameof(DarkWallpaperNote));
        OnPropertyChanged(nameof(AaCeiling));
        OnPropertyChanged(nameof(AaMarkMargin));
        OnPropertyChanged(nameof(ContrastNote));
        OnPropertyChanged(nameof(TransparencyUnavailable));
        OnPropertyChanged(nameof(TransparencyStatus));

        OnPropertyChanged(nameof(ShowComposition));
        OnPropertyChanged(nameof(Backdrop));
        OnPropertyChanged(nameof(MicaPicked));
        OnPropertyChanged(nameof(BackdropSubstituted));
        OnPropertyChanged(nameof(BackdropSubstitutedNote));
        OnPropertyChanged(nameof(WallTranslucent));
        OnPropertyChanged(nameof(ChromeAdmits));
        OnPropertyChanged(nameof(WallAdmits));

        OnPropertyChanged(nameof(IsFloating));
        OnPropertyChanged(nameof(ShowGapNote));
    }
}

/// <summary>
/// One text-only choice on the Appearance screen — a backdrop material, or how
/// far the transparency reaches.
///
/// <para><b>Not a theme card, and deliberately not built like one.</b> A theme
/// is a picture and the card draws it; these two are not pictures, they are
/// consequences, and the honest way to show a consequence is to say it. So each
/// one is a name and a sentence, in the same Button grammar the theme cards use
/// (one Tab stop, fires on Space, a Volt edge at a thickness that never changes)
/// at a smaller size.</para>
///
/// <para><see cref="Value"/> is the payload the command reads back —
/// a <see cref="Hoard.App.Themes.HoardBackdrop"/> for the material, a
/// <c>bool</c> for the reach. One class for both because the card is identical
/// and two would be two copies of the same markup.</para>
/// </summary>
public partial class AppearanceOptionViewModel(object value, string name, string reason)
    : ObservableObject
{
    public object Value { get; } = value;

    public string Name { get; } = name;

    public string Reason { get; } = reason;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// One theme on the picker.
///
/// <para><b>The card is a miniature of the window, not a strip of dots.</b> A row
/// of colours tells you the hues and nothing about what they do, and this
/// system's whole claim is that a colour means a job — and, since the themes
/// stopped differing by hue, that a room is a value structure and a material
/// rather than a tint. So each card draws the actual layout at 1/8 scale out of
/// the theme's own palette: the caption and rail as one continuous chrome
/// bracket, the seam between chrome and art at the theme's own <c>Line</c>
/// weight, the wall of capsules with two of them dimmed. Which is what makes
/// Nightshift's flat surfaces and bright seam, Box art's 4.8x drop into the wall,
/// and Tungsten's near-invisible edges legible side by side.</para>
///
/// <para>Every miniature carries exactly one Flare dot, in the corner where a
/// real tile carries it. Four cards side by side then state the invariant without
/// a sentence: the colour of the dot changes, the fact that there is one of it
/// does not.</para>
/// </summary>
public partial class ThemeChoiceViewModel(HoardTheme theme) : ObservableObject
{
    public HoardTheme Theme { get; } = theme;

    public string Name { get; } = theme.Name;

    public string Reason { get; } = theme.Reason;

    // The miniature's palette. Brushes rather than colours so the markup can
    // bind them straight to a Background, and NEW brushes rather than the app's
    // own tokens because these have to keep showing their own theme while the
    // window is wearing another one.
    public IBrush Well { get; } = new SolidColorBrush(theme.Well);
    public IBrush Ground { get; } = new SolidColorBrush(theme.Ground);
    public IBrush Surface { get; } = new SolidColorBrush(theme.Surface);
    public IBrush SurfaceRaised { get; } = new SolidColorBrush(theme.SurfaceRaised);
    public IBrush Line { get; } = new SolidColorBrush(theme.Line);
    public IBrush Text { get; } = new SolidColorBrush(theme.Text);
    public IBrush TextDim { get; } = new SolidColorBrush(theme.TextDim);
    public IBrush Volt { get; } = new SolidColorBrush(theme.Volt);
    public IBrush Amber { get; } = new SolidColorBrush(theme.Amber);
    public IBrush Azure { get; } = new SolidColorBrush(theme.Azure);

    /// <summary>The unread marker's own colour. Legal on this screen for the
    /// same reason it is legal on a tile: it is naming the thing it marks, not
    /// decorating a settings row.</summary>
    public IBrush Flare { get; } = new SolidColorBrush(theme.Flare);

    // ── The art in the miniature is the SAME art on all four cards ──────────
    // Not drawn out of the theme, and that is the entire point of the card. Real
    // cover art does not change when the room does, so four miniatures that
    // differ only in their chrome are the claim this palette system makes, made
    // visually: the same seven capsules hung in four different rooms. Cards whose
    // "art" was the theme's own Azure and Amber said the opposite — that a theme
    // recolours the library — and they read as chip rows rather than as walls.
    //
    // Two of the seven carry §5.1's dormancy floor (saturation 0.22, hue -6°,
    // brightness 0.68) already applied, so the card also shows the one encoding
    // the product is built on.
    public static IBrush Art1 { get; } = Fixed("#2B4C74");
    public static IBrush Art2 { get; } = Fixed("#7A3B2A");
    public static IBrush Art3 { get; } = Fixed("#1E3A32");
    public static IBrush Art4 { get; } = Fixed("#4A3352");
    public static IBrush ArtDormant1 { get; } = Fixed("#5E5954");
    public static IBrush Art5 { get; } = Fixed("#5E2F38");
    public static IBrush ArtDormant2 { get; } = Fixed("#252729");

    private static IBrush Fixed(string hex)
        => new ImmutableSolidColorBrush(Color.Parse(hex));

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// One layout on the picker, drawn as a miniature of the window.
///
/// <para><b>The same device the theme cards use, and for a sharper reason.</b> A
/// theme card draws a miniature because a chip row says what the hues are and
/// nothing about what they do. A layout card draws one because a layout is
/// <i>only</i> shape — there is no colour in it at all — and the miniature is
/// the whole of what the setting produces, at 1/8 scale, rather than an
/// illustration of it.</para>
///
/// <para><b>Its palette is the theme that is up, not a fixed one.</b> This is
/// the one place the two card kinds deliberately differ. Four theme cards side
/// by side answer "which room"; two layout cards answer "what would this
/// arrangement look like in the room I am already in", and a card frozen in the
/// default palette would answer a question nobody asked. So the brushes are
/// rebuilt on every <see cref="ThemeService.Applied"/> — see
/// <see cref="Repaint"/>.</para>
///
/// <para><b>Three values are the entire difference between the two cards</b>, and
/// that is the point of drawing them from one template: the ground the panes lie
/// on, the margin around each pane, and its corner radius. Everything else — the
/// rail, the wall of capsules, the seam, the one Flare dot — is identical,
/// because everything else about the window is.</para>
/// </summary>
public partial class LayoutChoiceViewModel : ObservableObject
{
    /// <summary>The gap in the miniature, at the miniature's scale. The real one
    /// is 8px on a ~800px window; this is 3px on a 96px card, which is the same
    /// proportion to within a rounding.</summary>
    private const double MiniGap = 3;

    /// <summary>And the radius, on the same scale as the gap.</summary>
    private const double MiniRadius = 3;

    public LayoutChoiceViewModel(HoardLayout layout)
    {
        Layout = layout;
        Name = HoardLayouts.Name(layout);
        Reason = HoardLayouts.Reason(layout);

        var floating = layout == HoardLayout.Floating;

        // One pane owns each gap, which is what the real shell does: the rail
        // gives up its right margin and the wall carries both of its own. Halves
        // from each neighbour were tried first and produced a four-pixel gutter
        // in the one state where the second pane is missing (§15.3), and a
        // miniature that did not match the window would be worse than no
        // miniature.
        RailMargin = floating ? new Thickness(MiniGap, MiniGap, 0, MiniGap) : default;
        WallMargin = floating ? new Thickness(MiniGap) : default;
        PaneRadius = floating ? new CornerRadius(MiniRadius) : default;

        // The seam between rail and wall is a drawn rule flush and a gap
        // floating, so the two cards carry the theme's Line in two different
        // places: down the rail's right edge on one, and all the way round both
        // panes on the other. Drawing both would say the floating layout keeps a
        // divider it does not have.
        RailEdge = floating ? new Thickness(1) : new Thickness(0, 0, 1, 0);
        WallEdge = floating ? new Thickness(1) : default;

        Repaint(HoardThemes.Default);
    }

    public HoardLayout Layout { get; }

    public string Name { get; }

    public string Reason { get; }

    /// <summary>Around the rail in the miniature: nothing, or the gap on the
    /// three edges it owns.</summary>
    public Thickness RailMargin { get; }

    /// <summary>And around the wall, mirrored.</summary>
    public Thickness WallMargin { get; }

    public CornerRadius PaneRadius { get; }

    /// <summary>The rail's own edge: the chrome/art seam flush (§11.1), a card
    /// border floating.</summary>
    public Thickness RailEdge { get; }

    /// <summary>The wall's. Nothing flush — the rail's seam is the only rule
    /// between them — and a card border floating.</summary>
    public Thickness WallEdge { get; }

    /// <summary>The field the panes lie on: the theme's <c>Ground</c> where the
    /// panes meet edge to edge and it is never seen, and its <c>Well</c> where
    /// the gaps expose it.</summary>
    [ObservableProperty]
    public partial IBrush GroundFill { get; set; } = Brushes.Transparent;

    /// <summary>The caption strip, which is the rail's ink flush and the
    /// ground's floating — §9, and §15's amendment of it.</summary>
    [ObservableProperty]
    public partial IBrush CaptionFill { get; set; } = Brushes.Transparent;

    /// <summary>The rail and the filter panel — the same in both layouts, which
    /// is half of what the pair of cards is showing.</summary>
    [ObservableProperty]
    public partial IBrush RailFill { get; set; } = Brushes.Transparent;

    /// <summary>The field the covers hang in. Unchanged by the layout, and
    /// unchanged by it on purpose: §5.1's polarity is measured against this tone
    /// and nothing here is allowed to move it.</summary>
    [ObservableProperty]
    public partial IBrush WallFill { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial IBrush LineFill { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial IBrush FlareFill { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// Rebuilds the miniature out of whichever theme is up.
    ///
    /// <para>New brush objects rather than the application's own tokens: these
    /// have to show what the layout does to the caption and the ground
    /// <i>specifically</i>, which are exactly the two tokens the live dictionary
    /// has already resolved to the layout that is currently on. A card drawn from
    /// the live tokens would show both cards wearing the setting in force, which
    /// is the one thing a picker may not do.</para>
    /// </summary>
    public void Repaint(HoardTheme theme)
    {
        var floating = Layout == HoardLayout.Floating;

        GroundFill = new ImmutableSolidColorBrush(floating ? theme.Well : theme.Ground);
        CaptionFill = new ImmutableSolidColorBrush(floating ? theme.Well : theme.Surface);
        RailFill = new ImmutableSolidColorBrush(theme.Surface);
        WallFill = new ImmutableSolidColorBrush(theme.Ground);
        LineFill = new ImmutableSolidColorBrush(theme.Line);
        FlareFill = new ImmutableSolidColorBrush(theme.Flare);
    }
}
