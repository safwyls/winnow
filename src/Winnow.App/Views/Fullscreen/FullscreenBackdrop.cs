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

/// <summary>Real landscape art for the home hero, with independent cache lifetime and cover fallback.</summary>
public sealed class FullscreenBackdrop : Panel
{
    private LeasedCover? _lease;
    private int _generation;
    public FullscreenBackdrop(FullscreenContext context, GameTileViewModel tile)
    {
        IsHitTestVisible = false; ClipToBounds = true;
        var fallback = new FullscreenCover(tile, background: true);
        var image = new Image { Stretch = Stretch.UniformToFill, Opacity = .8 };
        Children.Add(fallback); Children.Add(image);
        OpacityMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.White, .6)]
        };
        var ground = context.Themes.FirstOrDefault(t => t.Id == context.ThemeId)?.Ground ?? Color.Parse("#0F1C1E");
        Children.Add(new Border { Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(.5, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(.5, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.FromArgb(0, ground.R, ground.G, ground.B), 0), new GradientStop(ground, .85)]
        } });
        AttachedToVisualTree += async (_, _) =>
        {
            var generation = ++_generation;
            try
            {
                CoverKey? key = null;
                if (context.Services?.GetService<IWorkRepository>() is { } works)
                {
                    var work = await works.GetAsync(tile.Primary.WorkId);
                    if (UserArtRef.Token(work?.BackgroundUrl) is { } token) key = CoverKey.User(token);
                    else if (IgdbImageUrl.ImageId(work?.BackgroundUrl) is { } id) key = CoverKey.IgdbScreenshot(id);
                }
                if (key is null && context.Services?.GetService<IWorkImageRepository>() is { } images)
                {
                    var rows = await images.GetForWorkAsync(tile.Primary.WorkId);
                    var id = rows.Where(row => row.Source == ImageSources.Igdb && row.Kind == ImageKinds.Screenshot).SelectMany(row => row.Ids).FirstOrDefault();
                    if (id is not null) key = CoverKey.IgdbScreenshot(id);
                }
                if (generation != _generation || key is null) return;
                _lease = new LeasedCover(context.Services?.GetService<ICoverLeases>(), key, CoverLayers.Vivid,
                    art => { image.Source = art?.Vivid; fallback.IsVisible = art?.Vivid is null; });
                var top = TopLevel.GetTopLevel(this);
                var scale = top is null ? 1 : Math.Abs(this.TransformToVisual(top)?.M11 ?? 1) * top.RenderScaling;
                _lease.Request(Math.Max(1100, Bounds.Width) * scale);
            }
            catch (Exception) { /* Unavailable artwork retains the ordinary cover fallback. */ }
        };
        DetachedFromVisualTree += (_, _) => { _generation++; _lease?.Dispose(); _lease = null; image.Source = null; };
    }
}
