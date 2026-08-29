using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.App.Services;
using Winnow.App.Themes;

namespace Winnow.App.ViewModels;

/// <summary>
/// Settings > Appearance screen: theme picker, layout, and transparency slider.
/// Raises commands and reads state via <see cref="ThemeService"/> (§5.1).
/// </summary>
public partial class AppearanceViewModel : ObservableObject
{
    /// <summary>Track and thumb widths used to position the AA mark in pixels.</summary>
    private const double TrackWidth = 340;
    private const double ThumbWidth = 16;

    private readonly ThemeService _service;

    public AppearanceViewModel(ThemeService service)
    {
        _service = service;
        Themes = [];
        RebuildThemes();

        Backdrops =
        [
            .. WinnowBackdrops.All.Select(b => new AppearanceOptionViewModel(
                b, WinnowBackdrops.Name(b), WinnowBackdrops.Reason(b))),
        ];

        Reach =
        [
            new AppearanceOptionViewModel(
                false,
                "Frame and sidebars",
                "Title bar, rail and filter panel. The library stays solid."),
            new AppearanceOptionViewModel(
                true,
                "Everything but covers",
                "All panes at one level. Covers stay solid."),
        ];

        Layouts =
        [
            .. WinnowLayouts.All.Select(l => new LayoutChoiceViewModel(l)),
        ];

        _service.Applied += (_, _) => Refresh();

        // The SET of themes changing is a different event from which one is up:
        // this one rebuilds cards, the other repaints them. See
        // ThemeService.CatalogueChanged.
        _service.CatalogueChanged += (_, _) =>
        {
            RebuildThemes();
            Refresh();
        };

        Refresh();
    }

    /// <summary>Exposed for the window's backdrop binding.</summary>
    public ThemeService Service => _service;

    /// <summary>Theme cards (built-ins then user themes). Rebuilt on catalogue change for hot reload.</summary>
    public ObservableCollection<ThemeChoiceViewModel> Themes { get; }

    /// <summary>Acrylic or Mica: which material Windows composes behind the
    /// window. Only shown once the slider has left zero — with nothing coming
    /// through, there is nothing for it to be a material of.</summary>
    public IReadOnlyList<AppearanceOptionViewModel> Backdrops { get; }

    /// <summary>How far the transparency reaches: the chrome, or the chrome and
    /// the cover wall's field.</summary>
    public IReadOnlyList<AppearanceOptionViewModel> Reach { get; }

    /// <summary>Window layout choices (§15): flush or floating panes. Drawn as miniature cards.</summary>
    public IReadOnlyList<LayoutChoiceViewModel> Layouts { get; }

    public string Title => "Appearance";

    public string IntroMessage =>
        "Theme and window appearance. Changes apply immediately and persist.";

    // ══ The transparency slider ═════════════════════════════════════════════
    // Mica is a binary window hint, but nothing anyone can SEE is: the perceived
    // translucency is entirely the alpha on our own surfaces over that backdrop,
    // so it is continuous and ours to set. A checkbox was the wrong control for
    // it, and at the alpha the checkbox turned on it was also not visibly doing
    // anything.

    /// <summary>Transparency percent (0-100), two-way bound to the slider.</summary>
    public double Transparency
    {
        get => _service.Transparency;
        set => _service.SetTransparency((int)Math.Round(value));
    }

    public string TransparencyReading =>
        $"{_service.Transparency.ToString(CultureInfo.InvariantCulture)}%";

    public bool IsSolid => _service.Transparency == 0;

    /// <summary>Contrast ratio of metadata ink on the title bar against a dark desktop.</summary>
    public string ContrastOnDarkWallpaper => Ratio(Colorimetry.DarkDesktop);

    /// <summary>Contrast ratio against a pure white backdrop (worst case).</summary>
    public string ContrastOnWhiteWallpaper => Ratio(Colorimetry.White);

    /// <summary>Label for the white-wallpaper row; notes when it drops under AA.</summary>
    public string WhiteWallpaperNote => UnderAa
        ? "on a white one - under the 4.5:1 minimum"
        : "on a white one";

    /// <summary>Label for the dark-desktop contrast row.</summary>
    public string DarkWallpaperNote => "on a dark desktop";

    /// <summary>True once the white-backdrop measurement is under AA.</summary>
    public bool UnderAa => !IsSolid
        && Colorimetry.WorstMetadataContrast(_service.Theme, _service.Transparency / 100.0, Colorimetry.White)
            < Colorimetry.AaThreshold;

    /// <summary>Highest transparency % where worst-case contrast still clears AA for the active theme.</summary>
    public int AaCeiling => Colorimetry.AaCeiling(_service.Theme);

    /// <summary>AA mark position in pixels from the track's left edge.</summary>
    public Thickness AaMarkMargin =>
        new(((TrackWidth - ThumbWidth) * (AaCeiling / 100.0)) + (ThumbWidth / 2) - 0.5, 0, 0, 0);

    public double SliderWidth => TrackWidth;

    public string ContrastNote => IsSolid
        ? "Measured on the title bar. Solid, so contrast is fixed."
        : UnderAa
            ? $"Measured on the title bar. Past {AaCeiling}% the white figure drops under 4.5:1."
            : "Measured on the title bar. Your desktop falls between these two values.";

    /// <summary>True when transparency was requested but the compositor refused it.</summary>
    public bool TransparencyUnavailable => _service.TransparencyRequested && !_service.BackdropAvailable;

    public string TransparencyStatus => TransparencyUnavailable
        ? "Desktop compositing is not available, so the window draws solid. Your setting is saved."
        : WallTranslucent
            ? "All panes at one level. Covers stay solid."
            : "Frame and sidebars only. The library pane stays solid.";

    // ══ Material, and reach ═════════════════════════════════════════════════
    // The screen holds four decisions now, and four rows would be a wall of
    // controls. So the transparency card is ONE quantity with two qualifiers
    // hanging off it — how much, what it is made of, how far it goes — and the
    // two qualifiers are not drawn at all while the quantity is zero, because at
    // zero neither of them does anything at all.

    /// <summary>Material and reach blocks are hidden at SOLID (meaningless there).</summary>
    public bool ShowComposition => !IsSolid;

    /// <summary>What the user asked Windows for.</summary>
    public WinnowBackdrop Backdrop => _service.Backdrop;

    /// <summary>True when the user picked Mica.</summary>
    public bool MicaPicked => _service.Backdrop == WinnowBackdrop.Mica;

    /// <summary>Measured Mica composite colour on this machine, rendered in Plex Mono (§3).</summary>
    public string MicaComposite => "#201F1E";

    public string MicaCompositeNote =>
        "is what Mica resolves to on this machine. Mica tints toward its base colour rather than showing the desktop directly.";

    /// <summary>True when the compositor substituted a different material than requested.</summary>
    public bool BackdropSubstituted => _service.BackdropSubstituted;

    public string BackdropSubstitutedNote => _service.Backdrop == WinnowBackdrop.Mica
        ? "Mica requires Windows 11. Using acrylic instead. Your preference is saved."
        : "Acrylic was refused, using Mica instead.";

    /// <summary>Whether the cover wall's field is included.</summary>
    public bool WallTranslucent => _service.WallTranslucent;

    /// <summary>Whether the content panes float (§15).</summary>
    public bool IsFloating => _service.IsFloating;

    /// <summary>Shown when floating layout and transparency are both active.</summary>
    public bool ShowGapNote => IsFloating && !IsSolid;

    public string GapNote =>
        "The gaps and title bar share one transparency level, one step more open than the panes.";

    /// <summary>Desktop admitted through the window ground (gaps and title bar), as a whole percent.</summary>
    public string GroundAdmits => Admits(WinnowTheme.MinShellAlpha);

    /// <summary>Desktop admitted through panes (rail, filter panel, library), as a whole percent.</summary>
    public string PaneAdmits => Admits(WinnowTheme.MinWallAlpha);

    public string PaneAdmitsNote =>
        "of every pane — rail, filter panel and library alike.";

    // ══ YOUR THEMES ═════════════════════════════════════════════════════════
    // A folder of JSON files at %LOCALAPPDATA%\Winnow\themes. The block below is
    // three things and no more: where the folder is, what is wrong with what is
    // in it, and what the theme that is up measures.
    //
    // Deliberately NOT a theme editor. §14's argument is that a room is a value
    // structure and a material rather than a set of hues, which is exactly the
    // thing a row of colour pickers cannot express — the file can, and the file
    // is also the thing a person can keep, diff and copy to another machine.

    /// <summary>Path to the user's theme folder, shown for easy copying.</summary>
    public string ThemeFolder => _service.UserThemeDirectory ?? string.Empty;

    public bool HasThemeFolder => !string.IsNullOrEmpty(ThemeFolder);

    /// <summary>Number of successfully parsed user themes.</summary>
    public int UserThemeCount => _service.Catalogue.Count(t => t.IsUserTheme);

    public string UserThemeSummary => UserThemeCount switch
    {
        0 => "No theme files yet. Export a built-in as a starting point, or drop a .json file in.",
        1 => "One theme file loaded. It reloads on save.",
        _ => $"{UserThemeCount.ToString(CultureInfo.InvariantCulture)} theme files loaded. They reload on save.",
    };

    /// <summary>Theme-folder diagnostics, worst first. Failed files are skipped and listed here.</summary>
    public IReadOnlyList<ThemeProblemViewModel> Problems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

    public string ProblemsHeading
    {
        get
        {
            // Distinct FILES, not diagnostics. One file can produce four errors
            // — a bad colour, a missing seed, an override in the wrong block —
            // and a heading that counted those would tell an author with one
            // broken file that they have four.
            var errors = Problems
                .Where(p => p.IsError)
                .Select(p => p.File)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return errors switch
            {
                0 => "WORTH A LOOK",
                1 => "1 FILE DID NOT LOAD",
                _ => $"{errors.ToString(CultureInfo.InvariantCulture)} FILES DID NOT LOAD",
            };
        }
    }

    /// <summary>Status text from the last export or reload action.</summary>
    [ObservableProperty]
    public partial string ThemeActionStatus { get; set; } = string.Empty;

    public bool HasThemeActionStatus => !string.IsNullOrEmpty(ThemeActionStatus);

    // ── The contrast report ─────────────────────────────────────────────────
    // Drawn for a USER theme only. The built-ins carry the same numbers on the
    // slider's own AA mark and in §14's tables, and repeating them on a card
    // would be a third place for one of them to drift.

    public bool ShowThemeReport => _service.Theme.IsUserTheme;

    private ThemeReport Report => ThemeAudit.Report(_service.Theme);

    /// <summary>Max transparency % before chrome drops under AA on white wallpaper.</summary>
    public string ReportAaCeiling => Percent(Report.AaCeiling);

    public string ReportAaNote =>
        "of the slider before contrast drops under 4.5:1 on a white wallpaper.";

    /// <summary>Transparency % where cover dimming inverts. Should be past the AA mark (§14.6).</summary>
    public string ReportWallCeiling => Percent(Report.WallCeiling);

    public string ReportWallNote => Report.WallCeiling >= Report.AaCeiling
        ? "before the cover dimming inverts. Past the contrast mark."
        : "before the cover dimming inverts. Before the contrast mark — on this theme the covers fail first.";

    public string ReportMetadata => Ratio(Report.MetadataOnChrome);

    public string ReportMetadataNote =>
        "text contrast at solid. Minimum is 4.5:1.";

    public string ReportEdge => Ratio(Report.Edge);

    public string ReportEdgeNote =>
        "border contrast. Built-ins range from 1.38 to 2.46.";

    /// <summary>Cover-to-chrome luminance depth (§14.1.1), as a multiple.</summary>
    public string ReportField =>
        Report.FieldToChrome.ToString("0.0", CultureInfo.InvariantCulture) + "x";

    public string ReportFieldNote =>
        "depth between cover area and frame. 1.4x is flat; 4.8x is strongly recessed.";

    /// <summary>Exports the active theme's seeds and proportions as a JSON template.</summary>
    [RelayCommand]
    private void ExportTheme()
    {
        var (file, problem) = _service.ExportTheme(_service.Theme);
        ThemeActionStatus = file is not null
            ? $"Wrote {file}. Change its id and its name, then edit - it is already in the list above."
            : problem ?? "There is no themes folder on this machine.";

        OnPropertyChanged(nameof(HasThemeActionStatus));
    }

    /// <summary>Manually re-reads the themes folder (fallback when the file watcher is unavailable).</summary>
    [RelayCommand]
    private void ReloadThemes()
    {
        _service.ReloadUserThemes();
        ThemeActionStatus = UserThemeCount == 1
            ? "Re-read the folder: 1 theme."
            : $"Re-read the folder: {UserThemeCount.ToString(CultureInfo.InvariantCulture)} themes.";
        OnPropertyChanged(nameof(HasThemeActionStatus));
    }

    private void RebuildThemes()
    {
        Themes.Clear();
        foreach (var theme in _service.Catalogue)
        {
            Themes.Add(new ThemeChoiceViewModel(theme));
        }

        Problems =
        [
            .. _service.Diagnostics
                .OrderByDescending(d => d.IsError)
                .Select(d => new ThemeProblemViewModel(d)),
        ];
    }

    private static string Percent(int value)
        => value.ToString(CultureInfo.InvariantCulture) + "%";

    private static string Ratio(double value)
        => value.ToString("0.00", CultureInfo.InvariantCulture) + ":1";

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
        if (choice?.Value is WinnowBackdrop backdrop)
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
        var ratio = Colorimetry.WorstMetadataContrast(
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
            choice.IsSelected = choice.Value is WinnowBackdrop b && b == _service.Backdrop;
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
        OnPropertyChanged(nameof(GroundAdmits));
        OnPropertyChanged(nameof(PaneAdmits));

        OnPropertyChanged(nameof(IsFloating));
        OnPropertyChanged(nameof(ShowGapNote));

        OnPropertyChanged(nameof(ThemeFolder));
        OnPropertyChanged(nameof(HasThemeFolder));
        OnPropertyChanged(nameof(UserThemeCount));
        OnPropertyChanged(nameof(UserThemeSummary));
        OnPropertyChanged(nameof(Problems));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsHeading));

        OnPropertyChanged(nameof(ShowThemeReport));
        OnPropertyChanged(nameof(ReportAaCeiling));
        OnPropertyChanged(nameof(ReportWallCeiling));
        OnPropertyChanged(nameof(ReportWallNote));
        OnPropertyChanged(nameof(ReportMetadata));
        OnPropertyChanged(nameof(ReportEdge));
        OnPropertyChanged(nameof(ReportField));
    }
}

/// <summary>
/// A text-only option card (backdrop material or transparency reach).
/// <see cref="Value"/> carries the typed payload the command reads back.
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
/// One theme on the picker, drawn as a 1/8-scale miniature of the window using
/// the theme's own palette. Each card includes one Flare dot to show how the
/// unread marker looks in that theme.
/// </summary>
public partial class ThemeChoiceViewModel(WinnowTheme theme) : ObservableObject
{
    public WinnowTheme Theme { get; } = theme;

    public string Name { get; } = theme.Name;

    public string Reason { get; } = theme.Reason;

    /// <summary>True for a theme that came out of the themes folder. The card
    /// draws two extra lines for one: the file it came from, and what it
    /// measures.</summary>
    public bool IsUserTheme { get; } = theme.IsUserTheme;

    public string SourceFile { get; } = theme.SourceFile ?? string.Empty;

    /// <summary>AA contrast ceiling for user themes; empty for built-ins (tested at build time).</summary>
    public string ContrastHeadline { get; } =
        theme.IsUserTheme ? ThemeAudit.Report(theme).Headline : string.Empty;

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
/// One layout option (flush or floating), drawn as a miniature in the active
/// theme's palette. Repaints on theme change via <see cref="Repaint"/>.
/// </summary>
public partial class LayoutChoiceViewModel : ObservableObject
{
    /// <summary>The gap in the miniature, at the miniature's scale. The real one
    /// is 8px on a ~800px window; this is 3px on a 96px card, which is the same
    /// proportion to within a rounding.</summary>
    private const double MiniGap = 3;

    /// <summary>And the radius, on the same scale as the gap.</summary>
    private const double MiniRadius = 3;

    public LayoutChoiceViewModel(WinnowLayout layout)
    {
        Layout = layout;
        Name = WinnowLayouts.Name(layout);
        Reason = WinnowLayouts.Reason(layout);

        var floating = layout == WinnowLayout.Floating;

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

        Repaint(WinnowThemes.Default);
    }

    public WinnowLayout Layout { get; }

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

    /// <summary>The cover wall's background. Same in both layouts (§5.1 polarity).</summary>
    [ObservableProperty]
    public partial IBrush WallFill { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial IBrush LineFill { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial IBrush FlareFill { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Rebuilds brushes from the given theme (new objects, not the live tokens).</summary>
    public void Repaint(WinnowTheme theme)
    {
        var floating = Layout == WinnowLayout.Floating;

        GroundFill = new ImmutableSolidColorBrush(floating ? theme.Well : theme.Ground);
        CaptionFill = new ImmutableSolidColorBrush(floating ? theme.Well : theme.Surface);
        RailFill = new ImmutableSolidColorBrush(theme.Surface);
        WallFill = new ImmutableSolidColorBrush(theme.Ground);
        LineFill = new ImmutableSolidColorBrush(theme.Line);
        FlareFill = new ImmutableSolidColorBrush(theme.Flare);
    }
}

/// <summary>One theme-folder diagnostic. Exposes <see cref="IsError"/> for brush binding.</summary>
public sealed class ThemeProblemViewModel(ThemeDiagnostic diagnostic)
{
    public bool IsError { get; } = diagnostic.IsError;

    /// <summary>The file and the field, as one label: <c>midnight.json ›
    /// seeds.flare</c>. Bricolage, so it reads as a heading for the sentence
    /// under it rather than as more prose.</summary>
    /// <summary>Which file, on its own — the heading counts files, and one file
    /// can raise four diagnostics.</summary>
    public string File { get; } = diagnostic.File;

    public string Where { get; } = string.IsNullOrEmpty(diagnostic.Field)
        ? diagnostic.File
        : $"{diagnostic.File} › {diagnostic.Field}";

    public string Message { get; } = diagnostic.Message;

    /// <summary>Textual verdict alongside the colour indicator (§8: hue alone is insufficient).</summary>
    public string Verdict { get; } = diagnostic.IsError ? "DID NOT LOAD" : "LOADED ANYWAY";
}
