using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Winnow.App.ViewModels;

namespace Winnow.App.Views.Fullscreen;

/// <summary>A full-composition screenshot leased at its displayed pixel width.</summary>
internal sealed class FullscreenScreenshotPreview : Border
{
    private readonly GameScreenshotsViewModel _screenshots;
    private readonly GameScreenshotViewModel _shot;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private LeasedCover? _preview;
    private TopLevel? _top;
    private double _aspect = 16d / 9;
    internal double AspectRatio => _aspect;

    public FullscreenScreenshotPreview(GameScreenshotsViewModel screenshots, GameScreenshotViewModel shot)
    {
        _screenshots = screenshots;
        _shot = shot;
        Child = _image;
        SetImage(shot.Image);
        AttachedToVisualTree += (_, _) =>
        {
            _top = TopLevel.GetTopLevel(this);
            if (_top is not null) _top.PropertyChanged += TopChanged;
            SetImage(_shot.Image);
            _preview = _screenshots.CreatePreview(_shot, art => SetImage(art?.Vivid));
            RequestArt();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_top is not null) _top.PropertyChanged -= TopChanged;
            _top = null;
            _image.Source = null;
            _preview?.Dispose();
            _preview = null;
        };
        // Fullscreen UI scaling changes an ancestor transform without changing our bounds.
        LayoutUpdated += (_, _) => RequestArt();
        SizeChanged += (_, _) => RequestArt();
    }

    private void TopChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(TopLevel.RenderScaling)) RequestArt();
    }

    private void RequestArt()
    {
        if (_top is null || Bounds.Width <= 0) return;
        var scale = Math.Abs(this.TransformToVisual(_top)?.M11 ?? 1) * _top.RenderScaling;
        // LeasedCover upgrades only when the physical width crosses a decode bucket.
        _preview?.Request(Bounds.Width * scale);
    }

    private void SetImage(Bitmap? bitmap)
    {
        _image.Source = bitmap;
        var aspect = bitmap is { PixelSize.Width: > 0, PixelSize.Height: > 0 }
            ? (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height : 16d / 9;
        if (Math.Abs(_aspect - aspect) < .00001) return;
        _aspect = aspect;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 480;
        if (double.IsFinite(availableSize.Height)) width = Math.Min(width, availableSize.Height * _aspect);
        var size = new Size(Math.Max(0, width), Math.Max(0, width / _aspect));
        base.MeasureOverride(size);
        return size;
    }
}
