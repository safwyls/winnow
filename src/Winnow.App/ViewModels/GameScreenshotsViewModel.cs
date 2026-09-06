using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;

namespace Winnow.App.ViewModels;

/// <summary>
/// One thumbnail in the screenshot strip. Rides the existing cover cache under
/// <see cref="CoverKey.IgdbScreenshot"/>, which resolves to <c>t_screenshot_huge</c>
/// — an IGDB cover is 3:4 and a screenshot is 16:9, so the provider is what picks
/// the size token, and there is no second image path.
/// </summary>
public sealed partial class GameScreenshotViewModel : ObservableObject
{
    public GameScreenshotViewModel(string imageId, int position, int total)
    {
        Key = CoverKey.IgdbScreenshot(imageId);
        AutomationName = GameScreenshotsCopy.ThumbnailAutomationName(position, total);
        Tooltip = GameScreenshotsCopy.ThumbnailTooltip(position, total);
    }

    /// <summary>Cover key at the screenshot rendition, not the cover one.</summary>
    public CoverKey Key { get; }

    /// <summary>Accessible name stating the shot's position in the strip.</summary>
    public string AutomationName { get; }

    /// <summary>Tooltip stating the shot's position in the strip.</summary>
    public string Tooltip { get; }

    /// <summary>The thumbnail bitmap. Null until it arrives from the cache.</summary>
    [ObservableProperty]
    public partial Bitmap? Image { get; set; }

    /// <summary>True when this shot is the one the user picked for the hero view.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

/// <summary>
/// The screenshot strip inside ABOUT (design-system.md §10.1). Thumbnails are
/// requested once, when the modal asks for its cover.
/// <para>Picking a thumbnail opens the lightbox, the full-window overlay that
/// gives the shot the whole frame. Artwork rows are a different
/// <c>ImageKinds</c> value and are not screenshots. No ids means no view model,
/// which is what makes "nothing rather than an empty frame" a property of the
/// data.</para>
/// </summary>
public sealed partial class GameScreenshotsViewModel : ObservableObject
{
    /// <summary>Thumbnail width in device-independent pixels.</summary>
    public const double ThumbnailWidth = 120;

    /// <summary>Thumbnail height, 16:9 at <see cref="ThumbnailWidth"/>.</summary>
    public const double ThumbnailHeight = 68;

    private readonly ICoverCache? _covers;

    private readonly ScreenshotLightboxViewModel? _lightbox;

    private double _scaling = 1.0;

    private bool _requested;

    private GameScreenshotsViewModel(
        IReadOnlyList<GameScreenshotViewModel> shots,
        ICoverCache? covers,
        ScreenshotLightboxViewModel? lightbox)
    {
        Shots = shots;
        _covers = covers;
        _lightbox = lightbox;
        Caption = GameScreenshotsCopy.Caption(shots.Count);
    }

    /// <summary>The thumbnails in the strip, in the publisher's order.</summary>
    public IReadOnlyList<GameScreenshotViewModel> Shots { get; }

    /// <summary>True when there is at least one screenshot to draw.</summary>
    public bool HasShots => Shots.Count > 0;

    /// <summary>Caption stating the count, e.g. "3 screenshots".</summary>
    public string Caption { get; }

    /// <summary>Accessible name for the thumbnail strip.</summary>
    public string ListAutomationName => GameScreenshotsCopy.ListAutomationName;

    /// <summary>
    /// The overlay a thumbnail opens. Null only in tests that build a strip
    /// without one; the application always hands one down from the library view
    /// model, which is what lets the window bind <c>Library.Lightbox.IsOpen</c>
    /// without a null path.
    /// </summary>
    public ScreenshotLightboxViewModel? Lightbox => _lightbox;

    /// <summary>
    /// Builds the strip from the stored image rows. Returns null when no
    /// screenshot ids exist, which is what makes "nothing rather than an empty
    /// frame" a property of the data.
    /// </summary>
    public static GameScreenshotsViewModel? From(
        IReadOnlyList<WorkImages>? images,
        ICoverCache? covers,
        ScreenshotLightboxViewModel? lightbox = null)
    {
        var ids = images
            ?.Where(row => row.Source == ImageSources.Igdb && row.Kind == ImageKinds.Screenshot)
            .SelectMany(row => row.Ids)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

        if (ids.Length == 0)
        {
            return null;
        }

        var shots = ids
            .Select((id, index) => new GameScreenshotViewModel(id, index + 1, ids.Length))
            .ToArray();

        return new GameScreenshotsViewModel(shots, covers, lightbox);
    }

    /// <summary>
    /// Requests thumbnails once, at the view's render scaling. Called when the
    /// modal asks for its cover, so both loads share one trip through the cache.
    /// </summary>
    public void RequestThumbnails(double scaling)
    {
        _scaling = scaling > 0 ? scaling : 1.0;

        if (_covers is null || _requested)
        {
            return;
        }

        _requested = true;

        foreach (var shot in Shots)
        {
            if (_covers.TryGet(shot.Key, ThumbnailWidth * _scaling, out var cached))
            {
                shot.Image = cached.Vivid;
                continue;
            }

            _ = LoadAsync(shot, ThumbnailWidth * _scaling, art => shot.Image = art);
        }
    }

    /// <summary>
    /// Opens the lightbox on the pressed thumbnail. The overlay owns the
    /// selection mark from here on, so the strip keeps showing which shot is up
    /// as the user navigates.
    /// </summary>
    [RelayCommand]
    private void Select(GameScreenshotViewModel? shot)
    {
        if (shot is null)
        {
            return;
        }

        if (_lightbox is null)
        {
            foreach (var candidate in Shots)
            {
                candidate.IsSelected = ReferenceEquals(candidate, shot);
            }

            return;
        }

        _lightbox.Open(Shots, shot, _covers, _scaling);
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
