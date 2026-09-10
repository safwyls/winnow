using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Covers.Igdb;

namespace Winnow.App.Views.Fullscreen;

/// <summary>Landscape art with independent cache lifetime and a quiet cover fallback.</summary>
public sealed class FullscreenBackdrop : Panel
{
    private ICoverLease? _held;
    private ICoverLease? _outgoingLease;
    private readonly Image _outgoing = new() { Stretch = Stretch.UniformToFill };
    private readonly DispatcherTimer _fade = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _fadeTime = new();
    private ICoverLease? _pending;
    private readonly FullscreenContext _context;
    private GameTileViewModel _tile;
    private readonly Image _image;
    private readonly ContentControl _fallback = new();
    private CoverKey? _key;
    private int _requestedWidth;
    private bool _attached;
    private int _generation;
    public FullscreenBackdrop(FullscreenContext context, GameTileViewModel tile, bool cinematic = false)
    {
        IsHitTestVisible = false; ClipToBounds = true;
        _context = context;
        _tile = tile;
        _image = new Image { Stretch = Stretch.UniformToFill };
        Children.Add(_fallback);
        Children.Add(new Panel { Opacity = cinematic ? 1 : .8, Children = { _outgoing, _image } });
        _fade.Tick += (_, _) =>
        {
            var progress = _context.ReducedMotion ? 1 : Math.Clamp(_fadeTime.Elapsed.TotalMilliseconds / 180, 0, 1);
            _image.Opacity = progress;
            if (progress >= 1) FinishFade();
        };
        if (!cinematic) OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.Transparent, .3), new GradientStop(Colors.White, .85)]
        };
        var ground = context.Themes.FirstOrDefault(t => t.Id == context.ThemeId)?.Ground ?? Color.Parse("#0F1C1E");
        var clearGround = Color.FromArgb(0, ground.R, ground.G, ground.B);
        if (cinematic)
        {
            // The title reads against a solid left edge while landscape detail survives on the right.
            Children.Add(new Border { Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
                GradientStops = [new GradientStop(ground, 0), new GradientStop(Color.FromArgb(220, ground.R, ground.G, ground.B), .58),
                    new GradientStop(Color.FromArgb(55, ground.R, ground.G, ground.B), .85), new GradientStop(clearGround, 1)]
            } });
            Children.Add(new Border { Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(.5, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(ground, 0), new GradientStop(Color.FromArgb(220, ground.R, ground.G, ground.B), .1),
                    new GradientStop(clearGround, .2)]
            } });
        }
        Children.Add(new Border { Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(.5, 1, RelativeUnit.Relative),
            GradientStops = cinematic
                ? [new GradientStop(clearGround, 0), new GradientStop(Color.FromArgb(35, ground.R, ground.G, ground.B), .25),
                    new GradientStop(ground, .55), new GradientStop(ground, 1)]
                : [new GradientStop(clearGround, 0), new GradientStop(ground, .85)]
        } });
        void RefreshTint(object? sender, EventArgs e)
        {
            var current = context.Shared.Appearance.Service.Theme.Ground;
            foreach (var veil in Children.OfType<Border>())
                if (veil.Background is LinearGradientBrush gradient)
                    foreach (var stop in gradient.GradientStops)
                        stop.Color = Color.FromArgb(stop.Color.A, current.R, current.G, current.B);
        }
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            context.Shared.Appearance.Service.Applied += RefreshTint;
            RefreshTint(this, EventArgs.Empty);
            BeginSelection();
        };
        // A Viewbox can change pixel scale without changing this reference canvas size.
        LayoutUpdated += (_, _) => RequestDisplaySize();
        DetachedFromVisualTree += (_, _) =>
        {
            context.Shared.Appearance.Service.Applied -= RefreshTint;
            _attached = false;
            FinishFade();
            _generation++;
            _pending?.Dispose(); _pending = null;
            _image.Source = null;
            _fallback.Content = null;
            _held?.Dispose(); _held = null;
        };
    }

    /// <summary>Keep the current art until this selection has a resolved replacement.</summary>
    public void Select(GameTileViewModel tile)
    {
        if (ReferenceEquals(_tile, tile)) return;
        _tile = tile;
        if (_attached) BeginSelection();
    }

    private void BeginSelection()
    {
        var generation = ++_generation;
        _pending?.Dispose(); _pending = null;
        _key = null;
        _requestedWidth = 0;
        _ = ResolveAsync(_tile, generation);
    }

    private async Task ResolveAsync(GameTileViewModel tile, int generation)
    {
        CoverKey? key = null;
        try
        {
            if (_context.Services?.GetService<IWorkRepository>() is { } works)
            {
                var work = await works.GetAsync(tile.Primary.WorkId);
                if (UserArtRef.Token(work?.BackgroundUrl) is { } token) key = CoverKey.User(token);
                else if (IgdbImageUrl.ImageId(work?.BackgroundUrl) is { } id) key = CoverKey.IgdbBackdrop(id);
            }
            if (key is null && _context.Services?.GetService<IWorkImageRepository>() is { } images)
            {
                var rows = await images.GetForWorkAsync(tile.Primary.WorkId);
                var id = rows.Where(row => row.Source == ImageSources.Igdb && row.Kind == ImageKinds.Screenshot)
                    .SelectMany(row => row.Ids).FirstOrDefault();
                if (id is not null) key = CoverKey.IgdbBackdrop(id);
            }
        }
        catch (Exception) { /* Missing metadata uses the selected game's cover. */ }
        if (!_attached || generation != _generation) return;
        _key = key;
        if (key is null || _context.Services?.GetService<ICoverLeases>() is null) ShowFallback();
        else RequestDisplaySize();
    }

    private void ShowFallback()
    {
        FinishFade();
        _image.Source = null;
        _held?.Dispose(); _held = null;
        _fallback.Content = new FullscreenCover(_tile, background: true);
    }

    private async Task LoadAsync(ICoverLease lease, int generation)
    {
        CoverArt? art = null;
        try { art = await lease.GetAsync().ConfigureAwait(false); }
        catch (Exception) { /* Failed downloads use the selected game's cover. */ }
        Dispatcher.UIThread.Post(() => Settle(lease, art, generation));
    }

    private void Settle(ICoverLease lease, CoverArt? art, int generation)
    {
        if (!_attached || generation != _generation || !ReferenceEquals(_pending, lease)) return;
        _pending = null;
        if (art?.Vivid is null)
        {
            var key = lease.Key;
            lease.Dispose();
            // A failed resolution upgrade should not discard usable artwork.
            if (_held?.Key != key) ShowFallback();
            return;
        }
        FinishFade();
        var previous = _held;
        var previousImage = _image.Source;
        _held = lease;
        _image.Source = art.Vivid;
        _fallback.Content = null;
        if (previousImage is not null && previous?.Key != lease.Key && !_context.ReducedMotion)
        {
            _outgoingLease = previous;
            _outgoing.Source = previousImage;
            _image.Opacity = 0;
            _fadeTime.Restart();
            _fade.Start();
        }
        else previous?.Dispose();
    }

    private void FinishFade()
    {
        _fade.Stop();
        _fadeTime.Reset();
        _image.Opacity = 1;
        _outgoing.Source = null;
        _outgoingLease?.Dispose(); _outgoingLease = null;
    }

    private void RequestDisplaySize()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || Bounds.Width <= 0) return;
        var scale = Math.Abs(this.TransformToVisual(top)?.M11 ?? 1) * top.RenderScaling;
        if (_key is not { } key || _context.Services?.GetService<ICoverLeases>() is not { } leases) return;
        var width = CoverImaging.SnapWidth(Math.Max(Bounds.Width, Bounds.Height * 16 / 9) * scale);
        if (width <= _requestedWidth) return;
        _requestedWidth = width;
        _pending?.Dispose();
        var lease = leases.Acquire(key, width, CoverLayers.Vivid);
        _pending = lease;
        if (lease.TryGetArt(out var hit)) Settle(lease, hit, _generation);
        else _ = LoadAsync(lease, _generation);
    }
}
