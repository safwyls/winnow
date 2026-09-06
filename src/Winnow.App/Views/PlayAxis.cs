using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// The lifetime axis: this game's release to today, custom-drawn in two zones.
/// Left: a flat band in the far ink with a dashed boundary — an amount with no
/// shape. Right: one bar per stretch, height proportional to the hours gained,
/// positioned at its true place on the axis.
///
/// <para>The bars ride §5.1's dormancy ramp turned on its side, <c>Line</c> at
/// the release end to <c>Volt</c> at today. This runs the opposite direction to
/// <see cref="GapRail"/>'s ramp, and the reason is structural: the gap rail's
/// dormancy begins at a single known moment (the last session) so <c>Volt</c>
/// sits there; the lifetime axis has no such moment and encodes recency, so
/// <c>Volt</c> sits at today.</para>
///
/// <para>The <c>Volt</c> stop mark is the last session, wherever it falls on
/// the axis. Marks are <c>Flare</c> on the baseline, legal here for the same
/// reason §10.2 makes them legal on the gap rail: they are §5.2's unread signal
/// plotted in time.</para>
///
/// <para>Nothing animates and there are no <c>Transitions</c>, so reduced motion
/// has nothing to disable and the control is identical in both motion settings
/// (§8).</para>
/// </summary>
public sealed class PlayAxis : Control
{
    private const double PlotHeight = 46;

    private const double BaselineHeight = 10;

    /// <summary>Height of the flat band in the unmeasured zone.</summary>
    private const double BandHeight = 7;

    /// <summary>
    /// A bar with zero hours still draws this sliver. "Played nothing in March"
    /// is an observation, and an invisible bar would read as a missing month.
    /// </summary>
    private const double MinBarHeight = 2;

    private const double BarGap = 1;

    /// <summary>Width of the Volt stop mark at the last session.</summary>
    private const double StopWidth = 2;

    /// <summary>Flare dot radius on the baseline.</summary>
    private const double MarkRadius = 3.5;

    /// <summary>Ring around a mark, so a dot landing on a bar still reads as a dot.</summary>
    private const double MarkRing = 1.5;

    /// <summary>Gap between the flat band and the dashed boundary.</summary>
    private const double DividerGap = 4;

    static PlayAxis()
    {
        AffectsRender<PlayAxis>(
            BarsProperty,
            UnmeasuredFractionProperty,
            StopFractionProperty,
            MarksProperty,
            NearBrushProperty,
            FarBrushProperty,
            MarkBrushProperty,
            MarkRingBrushProperty);

        AffectsMeasure<PlayAxis>(BarsProperty);
    }

    /// <summary>The per-stretch bars in the measured zone.</summary>
    public static readonly StyledProperty<IReadOnlyList<PlayAxisBar>?> BarsProperty =
        AvaloniaProperty.Register<PlayAxis, IReadOnlyList<PlayAxisBar>?>(nameof(Bars));

    /// <summary>Where the coverage boundary falls, 0-1. Everything left of it is the flat band.</summary>
    public static readonly StyledProperty<double> UnmeasuredFractionProperty =
        AvaloniaProperty.Register<PlayAxis, double>(nameof(UnmeasuredFraction));

    /// <summary>Last session's position on the axis, 0-1. Null draws no stop mark.</summary>
    public static readonly StyledProperty<double?> StopFractionProperty =
        AvaloniaProperty.Register<PlayAxis, double?>(nameof(StopFraction));

    /// <summary>Unread update positions on the axis, 0-1. Drawn as Flare dots on the baseline.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> MarksProperty =
        AvaloniaProperty.Register<PlayAxis, IReadOnlyList<double>?>(nameof(Marks));

    /// <summary>Ink at the today end. Volt: recency.</summary>
    public static readonly StyledProperty<IBrush?> NearBrushProperty =
        AvaloniaProperty.Register<PlayAxis, IBrush?>(nameof(NearBrush));

    /// <summary>Ink at the release end. Line: the room's own colour, i.e. distant past.</summary>
    public static readonly StyledProperty<IBrush?> FarBrushProperty =
        AvaloniaProperty.Register<PlayAxis, IBrush?>(nameof(FarBrush));

    /// <summary>Update marks. Flare, and nothing else in this control may use it.</summary>
    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<PlayAxis, IBrush?>(nameof(MarkBrush));

    /// <summary>The card's own fill, so a mark reads against the bar or baseline it sits on.</summary>
    public static readonly StyledProperty<IBrush?> MarkRingBrushProperty =
        AvaloniaProperty.Register<PlayAxis, IBrush?>(nameof(MarkRingBrush));

    public IReadOnlyList<PlayAxisBar>? Bars
    {
        get => GetValue(BarsProperty);
        set => SetValue(BarsProperty, value);
    }

    public double UnmeasuredFraction
    {
        get => GetValue(UnmeasuredFractionProperty);
        set => SetValue(UnmeasuredFractionProperty, value);
    }

    public double? StopFraction
    {
        get => GetValue(StopFractionProperty);
        set => SetValue(StopFractionProperty, value);
    }

    public IReadOnlyList<double>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public IBrush? NearBrush
    {
        get => GetValue(NearBrushProperty);
        set => SetValue(NearBrushProperty, value);
    }

    public IBrush? FarBrush
    {
        get => GetValue(FarBrushProperty);
        set => SetValue(FarBrushProperty, value);
    }

    public IBrush? MarkBrush
    {
        get => GetValue(MarkBrushProperty);
        set => SetValue(MarkBrushProperty, value);
    }

    public IBrush? MarkRingBrush
    {
        get => GetValue(MarkRingBrushProperty);
        set => SetValue(MarkRingBrushProperty, value);
    }

    /// <summary>Height is the plot plus the baseline; width is whatever the row gives it.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        return new Size(width, PlotHeight + BaselineHeight);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var near = (NearBrush as ISolidColorBrush)?.Color ?? Colors.White;
        var far = (FarBrush as ISolidColorBrush)?.Color ?? Colors.Gray;
        var baseY = PlotHeight;

        var unmeasured = Math.Clamp(UnmeasuredFraction, 0.0, 1.0);
        var divider = width * unmeasured;

        if (divider > DividerGap)
        {
            context.FillRectangle(
                new ImmutableSolidColorBrush(far),
                new Rect(0, baseY - BandHeight, Math.Max(0, divider - DividerGap), BandHeight),
                1f);

            var pen = new ImmutablePen(
                new ImmutableSolidColorBrush(far),
                1,
                new ImmutableDashStyle([3, 3], 0));

            context.DrawLine(pen, new Point(divider, 4), new Point(divider, baseY));
        }

        DrawBars(context, width, baseY, near, far);
        DrawStop(context, width, baseY, near);
        DrawMarks(context, width, baseY);
    }

    private void DrawBars(DrawingContext context, double width, double baseY, Color near, Color far)
    {
        if (Bars is not { Count: > 0 } bars)
        {
            return;
        }

        var tallest = 0.0;
        foreach (var bar in bars)
        {
            tallest = Math.Max(tallest, bar.Hours);
        }

        if (tallest <= 0)
        {
            tallest = 1;
        }

        var start = Math.Clamp(UnmeasuredFraction, 0.0, 1.0);
        var measured = Math.Max(1e-6, 1 - start);

        foreach (var bar in bars)
        {
            var left = width * Math.Clamp(bar.Start, 0.0, 1.0);
            var right = width * Math.Clamp(bar.End, 0.0, 1.0);
            var barWidth = Math.Max(1, right - left - BarGap);

            var height = bar.Hours <= 0
                ? MinBarHeight
                : Math.Max(4, bar.Hours / tallest * (PlotHeight - 6));

            var centre = Math.Clamp(((bar.Start + bar.End) / 2 - start) / measured, 0.0, 1.0);

            context.FillRectangle(
                new ImmutableSolidColorBrush(Blend(far, near, centre)),
                new Rect(left, baseY - height, barWidth, height),
                1f);
        }
    }

    private void DrawStop(DrawingContext context, double width, double baseY, Color near)
    {
        if (StopFraction is not { } stop)
        {
            return;
        }

        var x = width * Math.Clamp(stop, 0.0, 1.0);
        context.FillRectangle(
            new ImmutableSolidColorBrush(near),
            new Rect(Math.Min(x, width - StopWidth), 0, StopWidth, baseY),
            1f);
    }

    private void DrawMarks(DrawingContext context, double width, double baseY)
    {
        if (Marks is not { Count: > 0 } marks)
        {
            return;
        }

        var ring = MarkRingBrush ?? Brushes.Black;
        var mark = MarkBrush ?? Brushes.White;
        var y = baseY + (BaselineHeight / 2);

        foreach (var fraction in marks)
        {
            var x = Math.Clamp(width * Math.Clamp(fraction, 0.0, 1.0), MarkRadius, width - MarkRadius);
            var centre = new Point(x, y);
            context.DrawEllipse(ring, null, centre, MarkRadius + MarkRing, MarkRadius + MarkRing);
            context.DrawEllipse(mark, null, centre, MarkRadius, MarkRadius);
        }
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + ((b.A - a.A) * t)),
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));
}
