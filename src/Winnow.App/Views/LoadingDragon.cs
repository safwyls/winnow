using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;

namespace Winnow.App.Views;

/// <summary>The shared loading mark. A short light follows each closed contour of the dragon.</summary>
public sealed class LoadingDragon : Control
{
    public static readonly StyledProperty<bool> IsTracingProperty =
        AvaloniaProperty.Register<LoadingDragon, bool>(nameof(IsTracing));
    public static readonly StyledProperty<IBrush?> InkProperty =
        AvaloniaProperty.Register<LoadingDragon, IBrush?>(nameof(Ink));
    public static readonly StyledProperty<IBrush?> GlowProperty =
        AvaloniaProperty.Register<LoadingDragon, IBrush?>(nameof(Glow));

    private static readonly Lazy<(Geometry Mark, IReadOnlyList<Geometry> Contours)> Artwork = new(ReadArtwork);
    internal static IReadOnlyList<Geometry> TraceContours => Artwork.Value.Contours;
    private int _generation;
    private TimeSpan? _started;
    private LoadingDragonProgress _progress = new();
    private CompositionCustomVisual? _visual;
    internal bool HasCompletedCircuit => _progress.HasCompletedCircuit;
    internal int RenderedFrameCount => _progress.RenderedFrameCount;
    internal int RenderThreadId => _progress.RenderThreadId;
    private double _phase;
    internal double Phase { get => _visual is not null ? _progress.Phase : _phase; private set => _phase = value; }
    private Action<Action<TimeSpan>>? _frameScheduler;
    internal Action<Action<TimeSpan>>? FrameScheduler
    {
        get => _frameScheduler;
        set
        {
            _frameScheduler = value;
            if (value is not null && _visual is not null)
            {
                _visual.SendHandlerMessage(LoadingDragonRenderer.Stop);
                ElementComposition.SetElementChildVisual(this, null);
                _visual = null;
            }
            Restart();
        }
    }
    private AvaloniaObject? _observedInk, _observedGlow;

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
        ObserveBrushes();
        if (FrameScheduler is null && ElementComposition.GetElementVisual(this) is { } element)
        {
            _visual = element.Compositor.CreateCustomVisual(new LoadingDragonRenderer(ReadPathData()));
            ElementComposition.SetElementChildVisual(this, _visual);
        }
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _generation++;
        _started = null;
        _progress = new();
        _visual?.SendHandlerMessage(LoadingDragonRenderer.Stop);
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        UnobserveBrushes();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTracingProperty) Restart();
        else if (change.Property == InkProperty || change.Property == GlowProperty)
        {
            if (TopLevel.GetTopLevel(this) is not null) ObserveBrushes();
            UpdateRenderer();
        }
        else if (change.Property == BoundsProperty)
            UpdateRenderer();
    }

    private void ObserveBrushes()
    {
        UnobserveBrushes();
        _observedInk = Ink as AvaloniaObject;
        _observedGlow = Glow as AvaloniaObject;
        if (_observedInk is not null) _observedInk.PropertyChanged += BrushChanged;
        if (_observedGlow is not null) _observedGlow.PropertyChanged += BrushChanged;
    }

    private void UnobserveBrushes()
    {
        if (_observedInk is not null) _observedInk.PropertyChanged -= BrushChanged;
        if (_observedGlow is not null) _observedGlow.PropertyChanged -= BrushChanged;
        _observedInk = _observedGlow = null;
    }

    private void BrushChanged(object? sender, AvaloniaPropertyChangedEventArgs e) => UpdateRenderer();

    private void Restart()
    {
        var generation = ++_generation;
        _started = null;
        _progress = new();
        Phase = 0;
        InvalidateVisual();
        UpdateRenderer();
        if (!IsTracing || TopLevel.GetTopLevel(this) is null) return;
        if (FrameScheduler is not null) QueueFrame(generation);
    }

    private void UpdateRenderer()
    {
        if (_visual is null) return;
        _visual.Size = new Vector(Bounds.Width, Bounds.Height);
        _visual.SendHandlerMessage(new LoadingDragonRenderer.State(IsTracing,
            Snapshot(Ink), Snapshot(Glow), _progress));

        static Color Snapshot(IBrush? brush) => brush is ISolidColorBrush solid
            ? Color.FromArgb((byte)Math.Clamp(Math.Round(solid.Color.A * solid.Opacity), 0, 255),
                solid.Color.R, solid.Color.G, solid.Color.B)
            : Colors.Transparent;
    }

    private void QueueFrame(int generation)
    {
        void Frame(TimeSpan now)
        {
            if (generation != _generation || !IsTracing || TopLevel.GetTopLevel(this) is null) return;
            _started ??= now;
            _progress.Frame(now - _started.Value);
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
        if (_visual is not null) return;
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 560;
        if (scale <= 0) return;
        var (mark, contours) = Artwork.Value;
        // Leave space around the original 512-unit drawing for the soft outer strokes.
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation((Bounds.Width - 512 * scale) / 2, (Bounds.Height - 512 * scale) / 2));
        using (context.PushOpacity(IsTracing ? .65 : 1)) context.DrawGeometry(Ink, null, mark);
        if (!IsTracing) return;
        foreach (var contour in contours)
        foreach (var segment in TraceSegments(contour, Phase))
        {
            using (context.PushOpacity(.10)) context.DrawGeometry(null, new Pen(Glow, 32, lineCap: PenLineCap.Round), segment);
            using (context.PushOpacity(.22)) context.DrawGeometry(null, new Pen(Glow, 18, lineCap: PenLineCap.Round), segment);
            using (context.PushOpacity(.65)) context.DrawGeometry(null, new Pen(Glow, 8, lineCap: PenLineCap.Round), segment);
            context.DrawGeometry(null, new Pen(Ink, 3, lineCap: PenLineCap.Round), segment);
        }
    }

    internal static IEnumerable<Geometry> TraceSegments(Geometry contour, double phase)
    {
        var length = contour.ContourLength;
        var head = phase * length;
        var tail = head - length * .13;
        if (head > 0 && contour.TryGetSegment(Math.Max(0, tail), head, true, out var segment))
            yield return segment;
        // Split at the seam so the trail retains its length as its head starts a new lap.
        if (tail < 0 && contour.TryGetSegment(length + tail, length, true, out var wrapped))
            yield return wrapped;
    }

    private static string ReadPathData()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Winnow/Assets/Icons/dragon.svg"));
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var paths = XDocument.Load(reader).Descendants().Where(e => e.Name.LocalName == "path")
            .Select(e => (string)e.Attribute("d")!).ToArray();
        return string.Join(" ", paths);
    }

    private static (Geometry, IReadOnlyList<Geometry>) ReadArtwork()
    {
        var mark = PathGeometry.Parse(ReadPathData());
        mark.FillRule = FillRule.EvenOdd;
        // Give detached pieces and inner details their own perimeter measurement;
        // extracting segments from a combined path can stop at its first contour.
        var contours = mark.Figures!.Select(figure => (Geometry)new PathGeometry
            { Figures = new PathFigures { figure } }).ToArray();
        return (mark, Array.AsReadOnly(contours));
    }
}
