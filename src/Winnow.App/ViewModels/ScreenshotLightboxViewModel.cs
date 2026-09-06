using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// The screenshot lightbox. A thumbnail in ABOUT (design-system.md §10.1) opens
/// it; it dims everything behind it and gives the shot the whole window.
/// <para>It is an overlay, never a <c>Popup</c> or a <c>Flyout</c>: the view is
/// declared as a sibling of <c>GameDetailsView</c> in the window's own
/// <c>Grid</c>, so it is in the window's visual tree and §10.7's drawn focus
/// rings work exactly as they do in the modal.</para>
/// <para>One instance lives for the life of the library view model and is handed
/// down to whichever <see cref="GameScreenshotsViewModel"/> is current, so the
/// window can bind <c>Library.Lightbox.IsOpen</c> without a null path.</para>
/// <para>Navigation wraps in both directions, the same answer §10.3's action
/// menu gives for Up and Down.</para>
/// </summary>
public sealed partial class ScreenshotLightboxViewModel : ObservableObject
{
    /// <summary>
    /// Drawn width cap in device-independent pixels. 1280x720 is the native
    /// size of IGDB's <c>t_screenshot_huge</c>, the rendition
    /// <c>CoverKey.IgdbScreenshot</c> resolves to; past it every pixel is
    /// upscale. The frame shrinks below this to fit a smaller window and never
    /// grows above it.
    /// </summary>
    public const double ImageWidth = 1280;

    /// <summary>
    /// Drawn height cap. 16:9 at <see cref="ImageWidth"/>, and the native
    /// height of <c>t_screenshot_huge</c>.
    /// </summary>
    public const double ImageHeight = 720;

    private ICoverCache? _covers;

    private IReadOnlyList<GameScreenshotViewModel> _shots = [];

    private double _scaling = 1.0;

    private int _index;

    // Guards a load that lands after the user has already navigated on.
    private int _generation;

    /// <summary>True while the overlay is up. The window binds the view's
    /// <c>IsVisible</c> to this.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    /// <summary>The shot on screen, at the widest rendition the cover cache
    /// decodes. The thumbnail stands in until it arrives, so navigation never
    /// draws an empty frame.</summary>
    [ObservableProperty]
    public partial Bitmap? Image { get; private set; }

    /// <summary>
    /// The overlay's accessible name. It names the surface as a dialog and
    /// states the position and the count, because Avalonia 11.3.20 has neither
    /// <c>IsDialog</c> nor a working <c>PositionInSet</c> (§8).
    /// </summary>
    [ObservableProperty]
    public partial string AutomationName { get; private set; } = string.Empty;

    /// <summary>
    /// The line under the image. It is a bound <c>TextBlock</c> marked as a
    /// live region, the route §8 leaves for a value that changes while its
    /// surface is on screen — changing the overlay's <c>Name</c> raises no UIA
    /// event.
    /// </summary>
    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    /// <summary>False for a game with a single screenshot, which draws no
    /// navigation controls rather than two inert ones.</summary>
    [ObservableProperty]
    public partial bool CanNavigate { get; private set; }

    /// <summary>Accessible name for the close control.</summary>
    public string CloseAutomationName => ScreenshotLightboxCopy.CloseAutomationName;

    /// <summary>Tooltip on the close control.</summary>
    public string CloseTooltip => ScreenshotLightboxCopy.CloseTooltip;

    /// <summary>Accessible name for the previous-shot control.</summary>
    public string PreviousAutomationName => ScreenshotLightboxCopy.PreviousAutomationName;

    /// <summary>Tooltip on the previous-shot control.</summary>
    public string PreviousTooltip => ScreenshotLightboxCopy.PreviousTooltip;

    /// <summary>Accessible name for the next-shot control.</summary>
    public string NextAutomationName => ScreenshotLightboxCopy.NextAutomationName;

    /// <summary>Tooltip on the next-shot control.</summary>
    public string NextTooltip => ScreenshotLightboxCopy.NextTooltip;

    /// <summary>
    /// Opens the overlay on one shot of a strip. The scaling is the window's
    /// render scaling, so the decode targets the size the image is drawn at.
    /// </summary>
    public void Open(
        IReadOnlyList<GameScreenshotViewModel> shots,
        GameScreenshotViewModel shot,
        ICoverCache? covers,
        double scaling)
    {
        ArgumentNullException.ThrowIfNull(shots);
        ArgumentNullException.ThrowIfNull(shot);

        var index = -1;
        for (var i = 0; i < shots.Count; i++)
        {
            if (ReferenceEquals(shots[i], shot))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        _shots = shots;
        _covers = covers;
        _scaling = scaling > 0 ? scaling : 1.0;
        CanNavigate = shots.Count > 1;

        Show(index);
        IsOpen = true;
    }

    /// <summary>
    /// Closes the overlay and drops the bitmap, the largest object this view
    /// model holds. The shots keep their selection mark, so the strip still
    /// shows where the user got to.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Image = null;
        _generation++;
    }

    /// <summary>Steps back one shot, wrapping from the first to the last.
    /// The left arrow key is the keyboard equivalent.</summary>
    [RelayCommand]
    public void Previous() => Step(-1);

    /// <summary>Steps forward one shot, wrapping from the last to the first.
    /// The right arrow key is the keyboard equivalent.</summary>
    [RelayCommand]
    public void Next() => Step(1);

    private void Step(int delta)
    {
        if (!IsOpen || _shots.Count < 2)
        {
            return;
        }

        var count = _shots.Count;
        Show(((_index + delta) % count + count) % count);
    }

    private void Show(int index)
    {
        _index = index;

        var shot = _shots[index];
        foreach (var candidate in _shots)
        {
            candidate.IsSelected = ReferenceEquals(candidate, shot);
        }

        AutomationName = ScreenshotLightboxCopy.DialogAutomationName(index + 1, _shots.Count);
        Caption = ScreenshotLightboxCopy.Caption(index + 1, _shots.Count);

        var token = ++_generation;

        if (_covers is null)
        {
            Image = shot.Image;
            return;
        }

        if (_covers.TryGet(shot.Key, ImageWidth * _scaling, out var cached))
        {
            Image = cached.Vivid;
            return;
        }

        // The thumbnail stands in while the wide rendition decodes, so
        // navigation never draws an empty frame.
        Image = shot.Image;

        _ = LoadAsync(shot, ImageWidth * _scaling, art =>
        {
            if (token == _generation)
            {
                Image = art;
            }
        });
    }

    private async Task LoadAsync(GameScreenshotViewModel shot, double width, Action<Bitmap> apply)
    {
        var art = await _covers!.GetAsync(shot.Key, width).ConfigureAwait(false);
        if (art is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => apply(art.Vivid));
    }
}
