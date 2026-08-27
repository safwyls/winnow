using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.App.Services;
using Hoard.App.Themes;

namespace Hoard.App.ViewModels;

/// <summary>
/// The rail's <c>SETTINGS › APPEARANCE</c> screen: which theme is up, and
/// whether the window lets the desktop through.
///
/// <para><b>Why a rail screen and not the command bar's Display popover.</b>
/// Three reasons, and the third is the one that decides it. A theme picker is
/// four rows of colour swatches with a sentence each, and the popover is capped
/// at 360px because it hangs off a control in a row that must not reflow.
/// Display's two toggles are preferences about <i>this view</i> — how covers are
/// drawn, which rows the query returns — and they sit beside the density slider
/// and the sort menu they belong with; a theme is application-wide chrome and
/// belongs nowhere near a control that only exists while the library is on
/// screen. And the rail's SETTINGS section was written to grow downward for
/// exactly this: it says so in its own comment, and until now it had one row
/// under a heading that named a category.</para>
///
/// <para>§5.1 holds: this raises commands and reads state. The service owns the
/// resource dictionary and the settings table.</para>
/// </summary>
public partial class AppearanceViewModel : ObservableObject
{
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

    /// <summary>
    /// The toggle. Bound two-way, so the checkbox writes straight through.
    /// </summary>
    public bool Transparency
    {
        get => _service.TransparencyRequested;
        set => _service.SetTransparency(value);
    }

    /// <summary>
    /// What the machine actually did with the request. Windows 10, a
    /// remote-desktop session and a compositor that refuses all end here, and
    /// the screen has to say so — a toggle that is on and doing nothing is worse
    /// than a toggle that is off.
    /// </summary>
    public bool TransparencyUnavailable => _service.TransparencyRequested && !_service.BackdropAvailable;

    public string TransparencyStatus => TransparencyUnavailable
        ? "This machine is not compositing the desktop behind the window, so Hoard is drawing solid. The setting stays on and takes effect where it can."
        : "Off, every surface is solid. On, the rail, the filter panel, the command bar and the title bar let the desktop through. The cover wall stays solid either way — the art needs its own ground.";

    [RelayCommand]
    private void SelectTheme(ThemeChoiceViewModel? choice)
    {
        if (choice is not null)
        {
            _service.SelectTheme(choice.Theme);
        }
    }

    private void Refresh()
    {
        foreach (var choice in Themes)
        {
            choice.IsSelected = ReferenceEquals(choice.Theme, _service.Theme);
        }

        OnPropertyChanged(nameof(Transparency));
        OnPropertyChanged(nameof(TransparencyUnavailable));
        OnPropertyChanged(nameof(TransparencyStatus));
    }
}

/// <summary>
/// One theme on the picker.
///
/// <para><b>The swatch row is a miniature of the window, not a strip of
/// dots.</b> A row of colours tells you the hues and nothing about what they do,
/// and this system's whole claim is that a colour means a job. So each card
/// draws the actual layout at 1/8 scale out of the theme's own palette — the
/// caption lip, the rail with one lit row, the wall of capsules — and every
/// miniature carries exactly one Flare dot, in the corner where a real tile
/// carries it. Four cards side by side then state the invariant without a
/// sentence: the colour of the dot changes, the fact that there is one of it
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

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
