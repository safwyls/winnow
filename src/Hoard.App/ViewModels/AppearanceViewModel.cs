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

    public string Title => "Appearance";

    public string IntroMessage =>
        "How the window looks. Both settings apply everywhere and survive a restart.";

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
        : "The rail, the title bar, the filter panel and the command bar admit the desktop. The cover wall never does, at any setting - the art needs its own ground.";

    [RelayCommand]
    private void SelectTheme(ThemeChoiceViewModel? choice)
    {
        if (choice is not null)
        {
            _service.SelectTheme(choice.Theme);
        }
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
    }
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
