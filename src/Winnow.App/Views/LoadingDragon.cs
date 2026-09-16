using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;

namespace Winnow.App.Views;

/// <summary>The shared loading mark. A short light follows the dragon's outer contour.</summary>
public sealed class LoadingDragon : Control
{
    public static readonly StyledProperty<bool> IsTracingProperty =
        AvaloniaProperty.Register<LoadingDragon, bool>(nameof(IsTracing));
    public static readonly StyledProperty<IBrush?> InkProperty =
        AvaloniaProperty.Register<LoadingDragon, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> GlowProperty =
        AvaloniaProperty.Register<LoadingDragon, IBrush?>(nameof(Glow));

    private static readonly Lazy<(Geometry Mark, Geometry Outline)> Artwork = new(ReadArtwork);
    private int _generation;
    private TimeSpan? _started;
    internal double Phase { get; private set; }
    internal Action<Action<TimeSpan>>? FrameScheduler { get; set; }

    public bool IsTracing { get => GetValue(IsTracingProperty); set => SetValue(IsTracingProperty, value); }
    public IBrush? Ink { get => GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public IBrush? Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }

    static LoadingDragon() => AffectsRender<LoadingDragon>(InkProperty, GlowProperty);

    public LoadingDragon()
    {
        IsHitTestVisible = false;
        this[!InkProperty] = new DynamicResourceExtension("Text");
        this[!GlowProperty] = new DynamicResourceExtension("Volt");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _generation++;
        _started = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTracingProperty) Restart();
    }

    private void Restart()
    {
        var generation = ++_generation;
        _started = null;
        Phase = 0;
        InvalidateVisual();
        if (!IsTracing || TopLevel.GetTopLevel(this) is null) return;
        QueueFrame(generation);
    }

    private void QueueFrame(int generation)
    {
        void Frame(TimeSpan now)
        {
            if (generation != _generation || !IsTracing || TopLevel.GetTopLevel(this) is null) return;
            _started ??= now;
            Phase = (now - _started.Value).TotalMilliseconds % 1800 / 1800;
            InvalidateVisual();
            QueueFrame(generation);
        }
        if (FrameScheduler is { } scheduler) scheduler(Frame);
        else TopLevel.GetTopLevel(this)?.RequestAnimationFrame(Frame);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 560;
        if (scale <= 0) return;
        var (mark, outline) = Artwork.Value;
        // Leave space around the original 512-unit drawing for the soft outer strokes.
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((Bounds.Width - 512 * scale) / 2, (Bounds.Height - 512 * scale) / 2));
        using (context.PushOpacity(IsTracing ? .65 : 1)) context.DrawGeometry(Ink, null, mark);
        if (!IsTracing) return;
        var length = outline.ContourLength;
        var head = Phase * length;
        var tail = head - length * .13;
        DrawTrace(Math.Max(0, tail), head);
        if (tail < 0) DrawTrace(length + tail, length);

        void DrawTrace(double from, double to)
        {
            if (to <= from || !outline.TryGetSegment(from, to, true, out var segment)) return;
            using (context.PushOpacity(.10)) context.DrawGeometry(null, new Pen(Glow, 32, lineCap: PenLineCap.Round), segment);
            using (context.PushOpacity(.22)) context.DrawGeometry(null, new Pen(Glow, 18, lineCap: PenLineCap.Round), segment);
            using (context.PushOpacity(.65)) context.DrawGeometry(null, new Pen(Glow, 8, lineCap: PenLineCap.Round), segment);
            context.DrawGeometry(null, new Pen(Ink, 3, lineCap: PenLineCap.Round), segment);
        }
    }

    private static (Geometry, Geometry) ReadArtwork()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Winnow/Assets/Icons/dragon.svg"));
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var paths = XDocument.Load(reader).Descendants().Where(e => e.Name.LocalName == "path")
            .Select(e => (string)e.Attribute("d")!).ToArray();
        var mark = PathGeometry.Parse(string.Join(" ", paths));
        mark.FillRule = FillRule.EvenOdd;
        // The second bundled path is the head; its first contour excludes eye/jaw detail.
        var head = PathGeometry.Parse(paths[1]);
        var outline = new PathGeometry { Figures = new PathFigures { head.Figures![0] } };
        return (mark, outline);
    }
}
