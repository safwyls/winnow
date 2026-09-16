using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace Winnow.App.Views;

// Only scalar progress crosses back to the UI thread. Paths and paints belong to
// the compositor, so layout or collection publication cannot interrupt the trace.
internal sealed class LoadingDragonProgress
{
    internal const double CircuitMilliseconds = 1800;
    private int _complete, _frames, _thread;
    private double _phase;
    internal double Phase => Volatile.Read(ref _phase);
    internal bool HasCompletedCircuit => Volatile.Read(ref _complete) != 0;
    internal int RenderedFrameCount => Volatile.Read(ref _frames);
    internal int RenderThreadId => Volatile.Read(ref _thread);
    internal void Frame(TimeSpan elapsed)
    {
        Volatile.Write(ref _thread, Environment.CurrentManagedThreadId);
        Volatile.Write(ref _phase, elapsed.TotalMilliseconds % CircuitMilliseconds / CircuitMilliseconds);
        Interlocked.Increment(ref _frames);
        if (elapsed.TotalMilliseconds >= CircuitMilliseconds) Volatile.Write(ref _complete, 1);
    }
}

internal sealed class LoadingDragonRenderer(string pathData) : CompositionCustomVisualHandler
{
    internal static readonly object Stop = new();
    internal sealed record State(bool Tracing, Color Ink, Color Glow, LoadingDragonProgress Progress);
    private State? _state;
    private TimeSpan? _started;
    private SKPath? _mark;
    private SKPath? _segment;
    private SKPathMeasure? _measure;
    private SKPaint? _paint;
    private bool _queued;

    public override void OnMessage(object message)
    {
        if (ReferenceEquals(message, Stop))
        {
            _state = null;
            _mark?.Dispose(); _mark = null;
            _segment?.Dispose(); _segment = null;
            _measure?.Dispose(); _measure = null;
            _paint?.Dispose(); _paint = null;
            return;
        }
        if (message is not State next) return;
        if (!ReferenceEquals(next.Progress, _state?.Progress)) _started = null;
        _state = next;
        Invalidate();
        QueueFrame();
    }

    private void QueueFrame()
    {
        if (_queued || _state?.Tracing != true) return;
        _queued = true;
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        _queued = false;
        if (_state?.Tracing != true) return;
        Invalidate();
        QueueFrame();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_state is not { } state || EffectiveSize.X <= 0 || EffectiveSize.Y <= 0) return;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (feature is null) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        _mark ??= SKPath.ParseSvgPathData(pathData);
        _mark.FillType = SKPathFillType.EvenOdd;
        _segment ??= new SKPath();
        _measure ??= new SKPathMeasure();
        _paint ??= new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round };
        var scale = (float)(Math.Min(EffectiveSize.X, EffectiveSize.Y) / 560);
        canvas.Save();
        try
        {
            canvas.Translate((float)(EffectiveSize.X - 512 * scale) / 2, (float)(EffectiveSize.Y - 512 * scale) / 2);
            canvas.Scale(scale);
            _paint.Style = SKPaintStyle.Fill;
            _paint.Color = Color(state.Ink, (state.Tracing ? .65 : 1) * lease.CurrentOpacity);
            canvas.DrawPath(_mark, _paint);
            if (!state.Tracing) return;
            _started ??= CompositionNow;
            var elapsed = CompositionNow - _started.Value;
            var phase = elapsed.TotalMilliseconds % LoadingDragonProgress.CircuitMilliseconds / LoadingDragonProgress.CircuitMilliseconds;
            _measure.SetPath(_mark, false);
            do
            {
                var length = _measure.Length;
                var head = (float)(phase * length);
                var tail = head - length * .13f;
                _segment.Reset();
                if (head > 0) _measure.GetSegment(Math.Max(0, tail), head, _segment, true);
                if (tail < 0) _measure.GetSegment(length + tail, length, _segment, true);
                Stroke(state.Glow, 32, .10);
                Stroke(state.Glow, 18, .22);
                Stroke(state.Glow, 8, .65);
                Stroke(state.Ink, 3, 1);
            } while (_measure.NextContour());
            state.Progress.Frame(elapsed);
        }
        finally { canvas.Restore(); }

        void Stroke(Color color, float width, double opacity)
        {
            _paint.Style = SKPaintStyle.Stroke;
            _paint.StrokeWidth = width;
            _paint.Color = Color(color, opacity * lease.CurrentOpacity);
            canvas.DrawPath(_segment, _paint);
        }
    }

    private static SKColor Color(Color color, double opacity) =>
        new(color.R, color.G, color.B, (byte)Math.Clamp(Math.Round(color.A * opacity), 0, 255));
}
