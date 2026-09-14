using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

/// <summary>Local metadata and leased artwork for a game's transient hover preview.</summary>
public partial class GamePreviewViewModel(GameTileViewModel tile) : ObservableObject, IDisposable
{
    private bool _disposed;
    private LeasedBackdrop? _backdrop;
    private CancellationTokenSource? _backdropLoading;
    private CancellationTokenSource? _ratingsLoading;
    private IReadOnlyList<WorkImages>? _backdropImages;
    private double _backdropWidth;
    private double _backdropHeight;

    public GameTileViewModel Tile { get; } = tile;

    [ObservableProperty]
    public partial Bitmap? Backdrop { get; set; }

    [ObservableProperty]
    public partial GameReceptionViewModel? Reception { get; set; }

    public void RequestRatings()
    {
        if (_disposed || _ratingsLoading is not null || Tile.LoadRatings is not { } load) return;
        _ratingsLoading = new CancellationTokenSource();
        _ = LoadRatingsAsync(load, _ratingsLoading);
    }

    private async Task LoadRatingsAsync(Func<CancellationToken, Task<IReadOnlyList<WorkRating>>> load,
        CancellationTokenSource request)
    {
        IReadOnlyList<WorkRating> ratings;
        try { ratings = await load(request.Token).ConfigureAwait(false); }
        catch (Exception) { ratings = []; }
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(_ratingsLoading, request)) return;
            _ratingsLoading = null;
            request.Dispose();
            Reception = GameReceptionViewModel.From(ratings);
        });
    }

    public void RequestBackdrop(double widthPixels, double heightPixels)
    {
        if (_disposed || !double.IsFinite(widthPixels) || !double.IsFinite(heightPixels)
            || widthPixels <= 0 || heightPixels <= 0) return;
        _backdropWidth = widthPixels;
        _backdropHeight = heightPixels;
        if (_backdrop is null)
        {
            _backdrop = new LeasedBackdrop(Tile.Leases, art => Backdrop = art?.Vivid);
            if (Tile.BackdropPreferences is { } preferences) preferences.Changed += BackdropPreferencesChanged;
            if (_backdropImages is null && Tile.LoadBackdropImages is { } load)
            {
                _backdropLoading = new CancellationTokenSource();
                _ = LoadBackdropAsync(load, _backdropLoading);
            }
        }
        UpdateBackdrop();
    }

    private async Task LoadBackdropAsync(Func<CancellationToken, Task<IReadOnlyList<WorkImages>>> load,
        CancellationTokenSource request)
    {
        IReadOnlyList<WorkImages> images;
        try { images = await load(request.Token).ConfigureAwait(false); }
        catch (Exception) { images = []; }
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(_backdropLoading, request)) return;
            _backdropLoading = null;
            request.Dispose();
            _backdropImages = images;
            UpdateBackdrop();
        });
    }

    private void BackdropPreferencesChanged() => Dispatcher.UIThread.Post(UpdateBackdrop);

    private void UpdateBackdrop()
    {
        if (_disposed || _backdrop is null) return;
        var keys = BackdropSelection.Candidates(Tile.BackgroundUrl, _backdropImages,
            _backdropWidth / _backdropHeight, Tile.SteamBackdropAppIds, Tile.BackdropPreferences?.SourceOrder);
        _backdrop.Request(keys, key => BackdropSelection.DecodeWidth(key, _backdropImages,
            _backdropWidth, _backdropHeight));
    }

    public void ReleaseBackdrop()
    {
        _ratingsLoading?.Cancel();
        _ratingsLoading?.Dispose();
        _ratingsLoading = null;
        Reception = null;
        if (Tile.BackdropPreferences is { } preferences) preferences.Changed -= BackdropPreferencesChanged;
        _backdropLoading?.Cancel();
        _backdropLoading?.Dispose();
        _backdropLoading = null;
        _backdrop?.Dispose();
        _backdrop = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBackdrop();
    }
}
