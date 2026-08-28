using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Winnow.App.Views;

/// <summary>
/// The stretch between your last session and now, with the updates you missed
/// marked on it. The detail view's signature, and the one drawing in the app
/// that states design-system.md §1 outright rather than encoding it.
///
/// <para><b>Why this and not a playtime chart.</b> §1 names longitudinal
/// playtime as the thing storefronts discard, and the obvious move is a line
/// through <c>playtime_snapshots</c>. On a real library that table holds one
/// reading per game — a line through one point is a decoration pretending to be
/// evidence, and §7 does not allow the interface to imply data it does not
/// hold. The join that IS always available, and that nothing else in the world
/// performs, is <c>play_records.last_played_at</c> against
/// <c>update_events.occurred_at</c>: you stopped here, these shipped, that was
/// this long ago. That is the rail.</para>
///
/// <para><b>The rule is the dormancy ramp turned on its side.</b> It runs from
/// <c>Volt</c> at the last-played end to <c>Line</c> at today — the same
/// recede §5.1 applies to the cover art, drawn once at a size where it can be
/// read as a legend for the whole grid. The user has been looking at faded
/// capsules for weeks; this is the one screen with room to say why.</para>
///
/// <para>Marks are <c>Flare</c>, which is legal here and only here in this
/// panel: they are literally the unread signal §5.2 defines — an update that
/// landed after the last session — plotted in time instead of stacked in a
/// corner. Everything the rail draws is restated in text beneath it (§8: the
/// encoding is decorative-redundant), so a user who cannot resolve a 7px dot
/// loses nothing.</para>
///
/// <para>Custom-drawn rather than assembled from panels because the marks are
/// positioned by fraction of an arbitrary width, which no stock Avalonia layout
/// expresses without either a measure-time width or a Canvas full of bindings.
/// There is no animation and no hit testing: it is a diagram.</para>
/// </summary>
public sealed class GapRail : Control
{
    /// <summary>Rule thickness. Two device-independent pixels reads as drawn, not as a hairline.</summary>
    private const double RuleHeight = 2;

    private const double MarkRadius = 4;

    /// <summary>Ring around a mark, so a dot landing on the rule still reads as a dot.</summary>
    private const double MarkRing = 1.5;

    /// <summary>End caps: a tall tick at "you stopped", a short one at "now".</summary>
    private const double StartCapHeight = 14;

    private const double EndCapHeight = 9;

    private const double CapWidth = 2;

    static GapRail()
    {
        AffectsRender<GapRail>(MarksProperty, StartBrushProperty, EndBrushProperty, MarkBrushProperty, MarkRingBrushProperty);
        AffectsMeasure<GapRail>(MarksProperty);
    }

    /// <summary>
    /// Where each missed update sits between the last session (0) and now (1).
    /// An empty list is the common, correct case: nothing shipped while you
    /// were away, and the rail draws an unbroken run.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> MarksProperty =
        AvaloniaProperty.Register<GapRail, IReadOnlyList<double>?>(nameof(Marks));

    /// <summary>Ink at the last-played end. Volt: the moment the game was current.</summary>
    public static readonly StyledProperty<IBrush?> StartBrushProperty =
        AvaloniaProperty.Register<GapRail, IBrush?>(nameof(StartBrush));

    /// <summary>Ink at "now". Line: the room's own colour, i.e. dormant.</summary>
    public static readonly StyledProperty<IBrush?> EndBrushProperty =
        AvaloniaProperty.Register<GapRail, IBrush?>(nameof(EndBrush));

    /// <summary>Update marks. Flare, and nothing else in this control may use it.</summary>
    public static readonly StyledProperty<IBrush?> MarkBrushProperty =
        AvaloniaProperty.Register<GapRail, IBrush?>(nameof(MarkBrush));

    /// <summary>The card's own fill, so a mark reads against the rule it sits on.</summary>
    public static readonly StyledProperty<IBrush?> MarkRingBrushProperty =
        AvaloniaProperty.Register<GapRail, IBrush?>(nameof(MarkRingBrush));

    public IReadOnlyList<double>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    public IBrush? StartBrush
    {
        get => GetValue(StartBrushProperty);
        set => SetValue(StartBrushProperty, value);
    }

    public IBrush? EndBrush
    {
        get => GetValue(EndBrushProperty);
        set => SetValue(EndBrushProperty, value);
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

    protected override Size MeasureOverride(Size availableSize)
    {
        // Height is the tallest thing drawn; width is whatever the row gives it.
        var width = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
        return new Size(width, StartCapHeight + 2);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var midY = Math.Round(height / 2);

        // Inset by the cap width so the end ticks sit inside the control rather
        // than half-clipped by it.
        var left = CapWidth / 2;
        var right = width - (CapWidth / 2);
        var span = Math.Max(1, right - left);

        var start = (StartBrush as ISolidColorBrush)?.Color ?? Colors.White;
        var end = (EndBrush as ISolidColorBrush)?.Color ?? Colors.Gray;

        // The rule: the §5.1 recede, drawn along time instead of over art.
        var rule = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(start, 0),
                // Half way along, half way faded. An earlier, faster fall put
                // the rail's whole dynamic range in the first third and left
                // most of the line invisible against Surface — the same mistake
                // §5.1's brightness floor records for the cover ramp.
                new GradientStop(Blend(start, end, 0.5), 0.5),
                new GradientStop(end, 1),
            },
        };

        context.FillRectangle(
            rule,
            new Rect(left, midY - (RuleHeight / 2), span, RuleHeight),
            (float)(RuleHeight / 2));

        // End caps. The left one is taller: it is the anchor the whole rail is
        // measured from, and "now" is just where the line runs out.
        context.FillRectangle(
            new ImmutableSolidColorBrush(start),
            new Rect(left - (CapWidth / 2), midY - (StartCapHeight / 2), CapWidth, StartCapHeight),
            1f);

        context.FillRectangle(
            new ImmutableSolidColorBrush(end),
            new Rect(right - (CapWidth / 2), midY - (EndCapHeight / 2), CapWidth, EndCapHeight),
            1f);

        if (Marks is not { Count: > 0 } marks)
        {
            return;
        }

        var ring = MarkRingBrush ?? Brushes.Black;
        var mark = MarkBrush ?? Brushes.White;

        foreach (var fraction in marks)
        {
            var x = left + (span * Math.Clamp(fraction, 0, 1));
            var centre = new Point(x, midY);
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
