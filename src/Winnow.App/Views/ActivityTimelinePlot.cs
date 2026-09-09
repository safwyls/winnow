using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>A shared time axis with keyboard-accessible observations and update groups.</summary>
public sealed class ActivityTimelinePlot : Panel
{
    private const double PlotHeight = 98;
    private const double TotalHeight = 150;
    private const double Inset = 12;
    private const double SessionWidth = 10;
    private readonly Dictionary<Control, Rect> _positions = [];
    private double _layoutWidth = -1;
    private bool _dirty = true;

    public static readonly StyledProperty<ActivityTimelineSeries?> SeriesProperty =
        AvaloniaProperty.Register<ActivityTimelinePlot, ActivityTimelineSeries?>(nameof(Series));
    public static readonly StyledProperty<IReadOnlyList<UpdateEventViewModel>?> UpdatesProperty =
        AvaloniaProperty.Register<ActivityTimelinePlot, IReadOnlyList<UpdateEventViewModel>?>(nameof(Updates));
    public static readonly StyledProperty<bool> IsTrackedSessionsProperty =
        AvaloniaProperty.Register<ActivityTimelinePlot, bool>(nameof(IsTrackedSessions));
    public static readonly StyledProperty<DateTime?> LastPlayedUtcProperty =
        AvaloniaProperty.Register<ActivityTimelinePlot, DateTime?>(nameof(LastPlayedUtc));
    public static readonly StyledProperty<IBrush?> HistoryBrushProperty = AvaloniaProperty.Register<ActivityTimelinePlot, IBrush?>(nameof(HistoryBrush));
    public static readonly StyledProperty<IBrush?> TrackedBrushProperty = AvaloniaProperty.Register<ActivityTimelinePlot, IBrush?>(nameof(TrackedBrush));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<ActivityTimelinePlot, IBrush?>(nameof(LineBrush));
    public static readonly StyledProperty<IBrush?> TextBrushProperty = AvaloniaProperty.Register<ActivityTimelinePlot, IBrush?>(nameof(TextBrush));
    public static readonly StyledProperty<IBrush?> UpdateBrushProperty = AvaloniaProperty.Register<ActivityTimelinePlot, IBrush?>(nameof(UpdateBrush));
    public static readonly StyledProperty<IBrush?> SurfaceBrushProperty = AvaloniaProperty.Register<ActivityTimelinePlot, IBrush?>(nameof(SurfaceBrush));

    static ActivityTimelinePlot() => AffectsRender<ActivityTimelinePlot>(SeriesProperty,
        LastPlayedUtcProperty, HistoryBrushProperty, TrackedBrushProperty, LineBrushProperty,
        TextBrushProperty, UpdateBrushProperty, SurfaceBrushProperty);

    public ActivityTimelineSeries? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public IReadOnlyList<UpdateEventViewModel>? Updates { get => GetValue(UpdatesProperty); set => SetValue(UpdatesProperty, value); }
    public bool IsTrackedSessions { get => GetValue(IsTrackedSessionsProperty); set => SetValue(IsTrackedSessionsProperty, value); }
    public DateTime? LastPlayedUtc { get => GetValue(LastPlayedUtcProperty); set => SetValue(LastPlayedUtcProperty, value); }
    public IBrush? HistoryBrush { get => GetValue(HistoryBrushProperty); set => SetValue(HistoryBrushProperty, value); }
    public IBrush? TrackedBrush { get => GetValue(TrackedBrushProperty); set => SetValue(TrackedBrushProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush? TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public IBrush? UpdateBrush { get => GetValue(UpdateBrushProperty); set => SetValue(UpdateBrushProperty, value); }
    public IBrush? SurfaceBrush { get => GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }

    public event Action<string>? DetailSelected;
    public event Action? TrackedSessionsRequested;

protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeriesProperty || change.Property == UpdatesProperty ||
            change.Property == IsTrackedSessionsProperty || change.Property == LastPlayedUtcProperty ||
            change.Property == HistoryBrushProperty || change.Property == TrackedBrushProperty ||
            change.Property == LineBrushProperty || change.Property == TextBrushProperty ||
            change.Property == UpdateBrushProperty || change.Property == SurfaceBrushProperty)
        {
            _dirty = true;
            InvalidateMeasure();
            InvalidateVisual();
            foreach (var child in Children) child.InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(0, availableSize.Width) : 600;
        if (_dirty || Math.Abs(width - _layoutWidth) > .1)
            BuildChildren(width);
        foreach (var (child, bounds) in _positions)
            child.Measure(bounds.Size);
        return new Size(width, TotalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (child, bounds) in _positions)
            child.Arrange(bounds);
        return finalSize;
    }

    private double X(DateTime date, double width)
    {
        if (Series is not { } series || series.EndUtc <= series.StartUtc) return Inset;
        return Inset + Math.Clamp((date - series.StartUtc).TotalSeconds /
            (series.EndUtc - series.StartUtc).TotalSeconds, 0, 1) * Math.Max(0, width - 2 * Inset);
    }

    private void BuildChildren(double width)
    {
        _dirty = false;
        _layoutWidth = width;
        var focusedName = Children.OfType<Button>().Where(b => b.IsFocused)
            .Select(AutomationProperties.GetName).FirstOrDefault();
        Children.Clear();
        _positions.Clear();
        if (Series is not { } series || series.EndUtc <= series.StartUtc || width <= 2 * Inset) return;
        Add(new PlotBackground(this) { IsHitTestVisible = false }, new Rect(0, 0, width, TotalHeight));

        var spanDays = (series.EndUtc - series.StartUtc).TotalDays;
        // A calendar month keeps the same visible width for both sources, including February.
        var monthWidth = Math.Max(1, 28 / spanDays * (width - 2 * Inset) - 1);
        monthWidth = Math.Min(monthWidth, width - 2 * Inset);
        var groups = new List<List<ActivityTimelineBar>>();
        foreach (var bar in series.Bars.Where(b => b.Hours > 0 && b.EndUtc >= series.StartUtc
                     && b.StartUtc <= series.EndUtc).OrderBy(b => b.StartUtc))
        {
            if (IsTrackedSessions && groups.Count > 0 &&
                X(bar.StartUtc, width) - X(groups[^1][0].StartUtc, width) < SessionWidth + 2)
                groups[^1].Add(bar);
            else groups.Add([bar]);
        }
        var maxHours = series.MaxHours;
        foreach (var group in groups)
        {
            var bar = group[0];
            var hours = group.Sum(b => b.Hours);
            var centre = IsTrackedSessions ? X(bar.StartUtc, width) :
                X(bar.StartUtc.AddTicks((bar.EndUtc - bar.StartUtc).Ticks / 2), width);
            var barWidth = IsTrackedSessions ? SessionWidth : monthWidth;
            var longest = group.Max(b => b.Hours);
            var label = group.Count == 1 ? bar.Label :
                $"{group.Count} observed sessions · {hours:0.##}h total · longest {longest:0.##}h · {bar.StartUtc:d MMM yyyy}–{group[^1].EndUtc:d MMM yyyy}";
            var button = MakeMark(label, bar.IsTracked ? TrackedBrush : HistoryBrush,
                () => DetailSelected?.Invoke(label));
            button.BarFraction = maxHours > 0 ? longest / maxHours : 0;
            button.Count = group.Count;
            button.Classes.Add("activity-bar");
            Add(button, new Rect(Math.Clamp(centre - barWidth / 2, Inset, width - Inset - barWidth),
                1, barWidth, PlotHeight - 2));
        }

        var updates = (Updates ?? []).Where(u => u.OccurredAtUtc >= series.StartUtc &&
            u.OccurredAtUtc <= series.EndUtc).OrderBy(u => u.OccurredAtUtc).ToArray();
        var updateGroups = new List<List<UpdateEventViewModel>>();
        foreach (var update in updates)
        {
            if (updateGroups.Count > 0 && X(update.OccurredAtUtc, width) -
                X(updateGroups[^1][^1].OccurredAtUtc, width) < 26)
                updateGroups[^1].Add(update);
            else updateGroups.Add([update]);
        }
        foreach (var group in updateGroups)
        {
            var unread = group.Count(u => u.IsUnread);
            var label = group.Count == 1 ? group[0].AutomationName :
                $"{group.Count} updates · {unread} unread · {group[0].DateText}–{group[^1].DateText}\n" +
                string.Join("\n", group.Select(u => $"{u.DateText} · {u.Headline}{(u.IsUnread ? " · unread" : "")}"));
            var button = MakeMark(label, unread > 0 ? UpdateBrush : TextBrush, () =>
            {
                if (group.Count > 1 && !IsTrackedSessions) TrackedSessionsRequested?.Invoke();
                DetailSelected?.Invoke(label);
            });
            button.IsUpdate = true;
            button.Count = group.Count;
            button.Classes.Add("activity-update");
            var middle = group[0].OccurredAtUtc.AddTicks((group[^1].OccurredAtUtc - group[0].OccurredAtUtc).Ticks / 2);
            Add(button, new Rect(Math.Clamp(X(middle, width) - 12, 0, width - 24), PlotHeight - 12, 24, 24));
        }
        AddTicks(width);
        if (focusedName is not null)
            Children.OfType<Button>().FirstOrDefault(b => AutomationProperties.GetName(b) == focusedName)?.Focus();
    }

    private MarkButton MakeMark(string label, IBrush? brush, Action select)
    {
        var button = new MarkButton
        {
            Background = brush,
            BorderBrush = TrackedBrush,
            Foreground = SurfaceBrush,
            RingBrush = SurfaceBrush,
            FontFamily = (FontFamily)this.FindResource("DataFont")!,
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => select();
        button.GotFocus += (_, _) => DetailSelected?.Invoke(label);
        return button;
    }

    private void Add(Control child, Rect bounds)
    {
        _positions[child] = bounds;
        Children.Add(child);
    }

    private void AddTicks(double width)
    {
        var series = Series!;
        var rightLabel = Tick("Today");
        rightLabel.Measure(Size.Infinity);
        var right = width - Inset - rightLabel.DesiredSize.Width;
        Add(rightLabel, new Rect(right, PlotHeight + 14, rightLabel.DesiredSize.Width, 16));
        var previousRight = Inset - 8;
        var longRange = (series.EndUtc - series.StartUtc).TotalDays > 730;
        var cursor = longRange ? new DateTime(series.StartUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc) :
            new DateTime(series.StartUtc.Year, series.StartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        while (cursor <= series.EndUtc)
        {
            if (cursor >= series.StartUtc)
            {
                var label = Tick(cursor.ToString(longRange ? "yyyy" : "MMM yy", CultureInfo.CurrentCulture));
                label.Measure(Size.Infinity);
                var left = Math.Max(Inset, X(cursor, width) - label.DesiredSize.Width / 2);
                if (left >= previousRight + 8 && left + label.DesiredSize.Width <= right - 12)
                {
                    Add(label, new Rect(left, PlotHeight + 14, label.DesiredSize.Width, 16));
                    previousRight = left + label.DesiredSize.Width;
                }
            }
            if (cursor.Year >= 9998) break;
            cursor = longRange ? cursor.AddYears(1) : cursor.AddMonths(1);
        }
    }

    private TextBlock Tick(string text) => new()
    {
        Text = text, FontSize = 11, Foreground = TextBrush,
        FontFamily = (FontFamily)this.FindResource("DataFont")!,
        FontFeatures = new FontFeatureCollection { FontFeature.Parse("tnum") },
    };

    private void DrawPlot(DrawingContext context)
    {
        var width = Bounds.Width;
        if (width <= 2 * Inset || Series is not { } series || series.EndUtc <= series.StartUtc) return;
        var left = Inset;
        var right = width - Inset;
        context.DrawLine(new Pen(LineBrush, 1), new Point(left, 0), new Point(right, 0));
        using (context.PushOpacity(.45))
            context.DrawLine(new Pen(LineBrush, 1), new Point(left, PlotHeight / 2), new Point(right, PlotHeight / 2));
        context.DrawLine(new Pen(LineBrush, 2), new Point(left, PlotHeight), new Point(right, PlotHeight));
        if ((LastPlayedUtc ?? series.LastPlayedUtc) is { } last && last >= series.StartUtc && last <= series.EndUtc)
        {
            var x = X(last, width);
            if (TrackedBrush is ISolidColorBrush active && LineBrush is ISolidColorBrush line)
                context.FillRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = [new GradientStop(active.Color, 0), new GradientStop(line.Color, 1)],
                }, new Rect(x, PlotHeight - 1, right - x, 2));
            context.DrawLine(new Pen(TrackedBrush, 2), new Point(x, PlotHeight - 7), new Point(x, PlotHeight + 7));
        }
        // A missing monthly record is a hatch; an observed session never fills this strip.
        var coverage = new Rect(left, 134, right - left, 5);
        using (context.PushClip(coverage))
        {
            for (var x = left - 5; x < right; x += 5)
                context.DrawLine(new Pen(LineBrush, 1), new Point(x, 139), new Point(x + 5, 134));
            foreach (var span in series.Coverage)
            {
                var start = X(span.StartUtc, width);
                var end = X(span.EndUtc, width);
                context.FillRectangle(SurfaceBrush ?? Brushes.Transparent, new Rect(start, 134, Math.Max(0, end - start), 5));
                context.FillRectangle(HistoryBrush ?? Brushes.Transparent, new Rect(start, 134, Math.Max(0, end - start), 5));
            }
        }
    }

    private sealed class PlotBackground(ActivityTimelinePlot owner) : Control
    {
        private AvaloniaObject[] _brushes = [];

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // Theme changes mutate the existing token brushes, preserving their identity.
            _brushes = new[] { owner.LineBrush, owner.TrackedBrush, owner.HistoryBrush, owner.SurfaceBrush }
                .OfType<AvaloniaObject>().Distinct().ToArray();
            foreach (var brush in _brushes) brush.PropertyChanged += BrushChanged;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            foreach (var brush in _brushes) brush.PropertyChanged -= BrushChanged;
            _brushes = [];
            base.OnDetachedFromVisualTree(e);
        }

        private void BrushChanged(object? sender, AvaloniaPropertyChangedEventArgs e) => InvalidateVisual();
        public override void Render(DrawingContext context) => owner.DrawPlot(context);
    }

    private sealed class MarkButton : Button
    {
        public double BarFraction { get; set; }
        public bool IsUpdate { get; set; }
        public int Count { get; set; }
        public IBrush? RingBrush { get; init; }

        public MarkButton()
        {
            MinWidth = MinHeight = 0;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            FocusAdorner = null;
            Template = new FuncControlTemplate<Button>((_, _) => new Border { Background = Brushes.Transparent });
            GotFocus += (_, _) => InvalidateVisual();
            LostFocus += (_, _) => InvalidateVisual();
            PointerEntered += (_, _) => InvalidateVisual();
            PointerExited += (_, _) => InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (IsUpdate)
            {
                var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
                var radius = Count > 1 ? 10 : 4;
                context.DrawEllipse(RingBrush, null, centre, radius + 2, radius + 2);
                context.DrawEllipse(Background, null, centre, radius, radius);
                if (Count > 1)
                {
                    var count = new FormattedText(Count > 99 ? "99+" : Count.ToString(CultureInfo.CurrentCulture),
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), 10, Foreground);
                    context.DrawText(count, new Point(centre.X - count.Width / 2, centre.Y - count.Height / 2));
                }
            }
            else
            {
                var height = Bounds.Height * Math.Clamp(BarFraction, 0, 1);
                context.FillRectangle(Background ?? Brushes.Transparent, new Rect(0, Bounds.Height - height, Bounds.Width, height));
                if (Count > 1)
                {
                    var count = new FormattedText(Count > 9 ? "9+" : Count.ToString(CultureInfo.CurrentCulture),
                        CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), 8, Foreground);
                    count.SetFontFeatures(new FontFeatureCollection { FontFeature.Parse("tnum") });
                    var top = Math.Max(0, Bounds.Height - height);
                    context.FillRectangle(Background ?? Brushes.Transparent, new Rect(0, top, Bounds.Width, count.Height));
                    context.DrawText(count, new Point((Bounds.Width - count.Width) / 2, top));
                }
            }
            if (IsFocused || IsPointerOver)
                context.DrawRectangle(null, new Pen(BorderBrush, 1), new Rect(.5, .5,
                    Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1)));
        }
    }
}
