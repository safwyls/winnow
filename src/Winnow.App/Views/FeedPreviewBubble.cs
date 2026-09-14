using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Winnow.App.Views;

/// <summary>A preview surface whose artwork and outline continue through its tile pointer.</summary>
public sealed class FeedPreviewBubble : Decorator
{
    private const double ArrowWidth = 10;
    private const double ArrowHalfHeight = 9;
    private const double Radius = 6;
    private const double ContentPadding = 16;

    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<FeedPreviewBubble, Bitmap?>(nameof(Source));
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<FeedPreviewBubble, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<FeedPreviewBubble, IBrush?>(nameof(BorderBrush));
    public static readonly StyledProperty<bool> ArrowOnRightProperty =
        AvaloniaProperty.Register<FeedPreviewBubble, bool>(nameof(ArrowOnRight));
    public static readonly StyledProperty<double> ArrowOffsetProperty =
        AvaloniaProperty.Register<FeedPreviewBubble, double>(nameof(ArrowOffset), 40);

    static FeedPreviewBubble()
    {
        AffectsRender<FeedPreviewBubble>(SourceProperty, BackgroundProperty, BorderBrushProperty,
            ArrowOnRightProperty, ArrowOffsetProperty);
    }

    public FeedPreviewBubble() => UpdatePadding();

    public Bitmap? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IBrush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
    public bool ArrowOnRight { get => GetValue(ArrowOnRightProperty); set => SetValue(ArrowOnRightProperty, value); }
    public double ArrowOffset { get => GetValue(ArrowOffsetProperty); set => SetValue(ArrowOffsetProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ArrowOnRightProperty) UpdatePadding();
    }

    private void UpdatePadding() => Padding = ArrowOnRight
        ? new Thickness(ContentPadding, ContentPadding, ContentPadding + ArrowWidth, ContentPadding)
        : new Thickness(ContentPadding + ArrowWidth, ContentPadding, ContentPadding, ContentPadding);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= ArrowWidth + Radius * 2 + 1 || Bounds.Height <= Radius * 2 + 1) return;

        var shape = CreateOutline();
        context.DrawGeometry(Background, null, shape);
        if (Source is { } source && source.Size.Width > 0 && source.Size.Height > 0)
        {
            var scale = Math.Max(Bounds.Width / source.Size.Width, Bounds.Height / source.Size.Height);
            var width = source.Size.Width * scale;
            var height = source.Size.Height * scale;
            var destination = new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
            using (context.PushGeometryClip(shape))
            using (context.PushOpacity(0.18))
                context.DrawImage(source, new Rect(source.Size), destination);
        }
        if (BorderBrush is { } border) context.DrawGeometry(null, new Pen(border, 1), shape);
    }

    private StreamGeometry CreateOutline()
    {
        const double inset = 0.5;
        var left = inset + (ArrowOnRight ? 0 : ArrowWidth);
        var right = Bounds.Width - inset - (ArrowOnRight ? ArrowWidth : 0);
        var top = inset;
        var bottom = Bounds.Height - inset;
        var halfArrow = Math.Min(ArrowHalfHeight, Math.Max(0, (bottom - top - 2 * Radius) / 2));
        var offset = double.IsFinite(ArrowOffset) ? ArrowOffset : 40;
        var arrow = Math.Clamp(offset, top + Radius + halfArrow, bottom - Radius - halfArrow);
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        path.BeginFigure(new Point(left + Radius, top), true);
        path.LineTo(new Point(right - Radius, top));
        path.QuadraticBezierTo(new Point(right, top), new Point(right, top + Radius));
        if (ArrowOnRight)
        {
            path.LineTo(new Point(right, arrow - halfArrow));
            path.LineTo(new Point(Bounds.Width - inset, arrow));
            path.LineTo(new Point(right, arrow + halfArrow));
        }
        path.LineTo(new Point(right, bottom - Radius));
        path.QuadraticBezierTo(new Point(right, bottom), new Point(right - Radius, bottom));
        path.LineTo(new Point(left + Radius, bottom));
        path.QuadraticBezierTo(new Point(left, bottom), new Point(left, bottom - Radius));
        if (!ArrowOnRight)
        {
            path.LineTo(new Point(left, arrow + halfArrow));
            path.LineTo(new Point(inset, arrow));
            path.LineTo(new Point(left, arrow - halfArrow));
        }
        path.LineTo(new Point(left, top + Radius));
        path.QuadraticBezierTo(new Point(left, top), new Point(left + Radius, top));
        path.EndFigure(true);
        return geometry;
    }
}
