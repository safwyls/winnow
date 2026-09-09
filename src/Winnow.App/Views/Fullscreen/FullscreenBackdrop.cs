using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    private LeasedCover? _lease;
    private int _generation;
    public FullscreenBackdrop(FullscreenContext context, GameTileViewModel tile, bool cinematic = false)
    {
        IsHitTestVisible = false; ClipToBounds = true;
        var fallback = new FullscreenCover(tile, background: true);
        var image = new Image { Stretch = Stretch.UniformToFill, Opacity = cinematic ? 1 : .8 };
        Children.Add(fallback); Children.Add(image);
        if (!cinematic) OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.White, .6)]
        };
        var ground = context.Themes.FirstOrDefault(t => t.Id == context.ThemeId)?.Ground ?? Color.Parse("#0F1C1E");
        var clearGround = Color.FromArgb(0, ground.R, ground.G, ground.B);
        if (cinematic)
        {
            // The title reads against a solid left edge while landscape detail survives on the right.
            Children.Add(new Border { Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
                GradientStops = [new GradientStop(ground, 0), new GradientStop(Color.FromArgb(220, ground.R, ground.G, ground.B), .48),
                    new GradientStop(Color.FromArgb(55, ground.R, ground.G, ground.B), .75), new GradientStop(clearGround, 1)]
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
        AttachedToVisualTree += async (_, _) =>
        {
            context.Shared.Appearance.Service.Applied += RefreshTint;
            RefreshTint(this, EventArgs.Empty);
            var generation = ++_generation;
            try
            {
                CoverKey? key = null;
                if (context.Services?.GetService<IWorkRepository>() is { } works)
                {
                    var work = await works.GetAsync(tile.Primary.WorkId);
                    if (UserArtRef.Token(work?.BackgroundUrl) is { } token) key = CoverKey.User(token);
                    else if (IgdbImageUrl.ImageId(work?.BackgroundUrl) is { } id) key = CoverKey.IgdbBackdrop(id);
                }
                if (key is null && context.Services?.GetService<IWorkImageRepository>() is { } images)
                {
                    var rows = await images.GetForWorkAsync(tile.Primary.WorkId);
                    var id = rows.Where(row => row.Source == ImageSources.Igdb && row.Kind == ImageKinds.Screenshot).SelectMany(row => row.Ids).FirstOrDefault();
                    if (id is not null) key = CoverKey.IgdbBackdrop(id);
                }
                if (generation != _generation || key is null) return;
                _lease = new LeasedCover(context.Services?.GetService<ICoverLeases>(), key, CoverLayers.Vivid,
                    art => { image.Source = art?.Vivid; fallback.IsVisible = art?.Vivid is null; });
                RequestDisplaySize();
            }
            catch (Exception) { /* Unavailable artwork retains the ordinary cover fallback. */ }
        };
        // A Viewbox can change pixel scale without changing this reference canvas size.
        LayoutUpdated += (_, _) => RequestDisplaySize();
        DetachedFromVisualTree += (_, _) =>
        {
            context.Shared.Appearance.Service.Applied -= RefreshTint;
            _generation++; _lease?.Dispose(); _lease = null; image.Source = null;
        };
    }

    private void RequestDisplaySize()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || Bounds.Width <= 0) return;
        var scale = Math.Abs(this.TransformToVisual(top)?.M11 ?? 1) * top.RenderScaling;
        _lease?.Request(Math.Max(Bounds.Width, Bounds.Height * 16 / 9) * scale);
    }
}
