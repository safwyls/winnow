using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Data;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>Portrait recommendation with an art-backed hover preview and independent artwork leases.</summary>
public partial class FeedCardView : UserControl
{
    private FeedCardViewModel? _card;
    private GameTileViewModel? _tile;
    private CoverPresenter? _cover;
    private bool _hovered;
    private bool _focused;
    private bool _hoverSuppressed;
    private static WeakReference<FeedCardView>? _activePreview;
    private FeedPreviewBubble? _bubble;
    private readonly Flyout _quickDetails = new()
    {
        Placement = PlacementMode.RightEdgeAlignedTop,
        ShowMode = FlyoutShowMode.Transient,
        OverlayDismissEventPassThrough = true,
    };

    public FeedCardView()
    {
        InitializeComponent();
        FlyoutBase.SetAttachedFlyout(Card, _quickDetails);
        Card.Click += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, Card)) return;
            SuppressPreview();
            if (_tile is { } tile && tile.OpenDetailsCommand?.CanExecute(tile) == true)
                tile.OpenDetailsCommand.Execute(tile);
        };
        Card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) SuppressPreview();
        };
        _quickDetails.Opening += (_, _) => BuildQuickDetails();
        _quickDetails.Opened += (_, _) =>
        {
            if (_quickDetails.Content is Control content && content.GetVisualAncestors().OfType<FlyoutPresenter>().FirstOrDefault() is { } presenter)
            {
                presenter.Background = Brushes.Transparent;
                presenter.BorderThickness = new Thickness(0);
                presenter.Padding = new Thickness(0);
            }
            UpdateBubblePointer();
            Card.Classes.Set("open", true);
            Apply();
        };
        _quickDetails.Closed += (_, _) =>
        {
            _card?.ReleaseBackdrop();
            if (_activePreview?.TryGetTarget(out var active) == true && ReferenceEquals(active, this)) _activePreview = null;
            Card.Classes.Set("open", false);
            Apply();
        };
        SizeChanged += (_, _) => RequestCover();
        if (Avalonia.Controls.Design.IsDesignMode) DataContext = Design.PreviewData.FeedCard;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 180;
        CoverFrame.Height = Math.Max(1, width) * 1.5;
        return base.MeasureOverride(availableSize);
    }

    public void TakeFocus() => Card.Focus(NavigationMethod.Directional);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hovered = true;
        if (!_quickDetails.IsOpen && !_hoverSuppressed && IsEffectivelyVisible && _card?.ShowActions == true)
            _quickDetails.ShowAt(Card);
        Apply();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        _hoverSuppressed = false;
        _quickDetails.Hide();
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsKeyboardFocusWithinProperty)
        {
            _focused = change.NewValue is true;
            Apply();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _quickDetails.Hide();
        _quickDetails.Content = null;
        _bubble = null;
        _hoverSuppressed = false;
        if (_card is not null)
        {
            _card.IsPointerOver = false;
            _card.IsFocusWithin = false;
        }
        _card = DataContext as FeedCardViewModel;
        _tile = _card?.Tile;
        _cover = _card?.Cover;
        Apply();
        WriteReason(_card);
        RequestCover();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _quickDetails.OverlayInputPassThroughElement = TopLevel.GetTopLevel(this);
        RequestCover();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _quickDetails.Hide();
        _quickDetails.Content = null;
        _bubble = null;
        _hovered = false;
        _focused = false;
        Apply();
        base.OnDetachedFromVisualTree(e);
    }

    private void WriteReason(FeedCardViewModel? card)
    {
        var inlines = Reason.Inlines ??= new InlineCollection();
        inlines.Clear();
        if (card is null) return;
        var dataFont = this.TryFindResource("DataFont", out var found) && found is FontFamily family ? family : Reason.FontFamily;
        foreach (var run in card.ReasonRuns)
        {
            var inline = new Run(run.Text);
            if (run.IsData) { inline.FontFamily = dataFont; inline.FontSize = 12; }
            inlines.Add(inline);
        }
    }

    private void RequestCover()
    {
        if (_cover is null || this.GetVisualRoot() is null || CoverFrame.Bounds.Width <= 0) return;
        _cover.Request(CoverFrame.Bounds.Width * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
    }

    private void Apply()
    {
        if (_tile is not null) _tile.IsPointerOver = _hovered;
        if (_card is not null)
        {
            _card.IsPointerOver = _hovered;
            _card.IsFocusWithin = _focused || _quickDetails.IsOpen;
        }
    }

    private void BuildQuickDetails()
    {
        if (_card is not { } card) return;
        if (_activePreview?.TryGetTarget(out var previous) == true && !ReferenceEquals(previous, this)) previous._quickDetails.Hide();
        _activePreview = new WeakReference<FeedCardView>(this);
        var tile = card.Tile;
        var top = TopLevel.GetTopLevel(this);
        _quickDetails.OverlayInputPassThroughElement = top;
        var origin = top is not null ? CoverFrame.TranslatePoint(default, top) ?? default : default;
        var rightSpace = (top?.Bounds.Width ?? 900) - origin.X - CoverFrame.Bounds.Width;
        var leftSide = rightSpace < 362 && origin.X > rightSpace;
        _quickDetails.Placement = leftSide ? PlacementMode.LeftEdgeAlignedTop : PlacementMode.RightEdgeAlignedTop;
        var width = Math.Clamp((leftSide ? origin.X : rightSpace) - 46, 180, 320);
        var rows = new StackPanel { Spacing = 10, Width = width };
        TextBlock Text(string? value, int size = 13, bool quiet = false, bool title = false) => new()
        {
            Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontFamily = Resource<FontFamily>(title ? "DisplayFont" : "BodyFont", FontFamily.Default),
            FontWeight = title ? FontWeight.Bold : FontWeight.Normal,
            Foreground = Resource<IBrush>(quiet ? "TextDim" : "Text", Brushes.White),
        };
        rows.Children.Add(Text(tile.Title, 22, title: true));
        rows.Children.Add(Text($"{tile.StoreNames} · {tile.StatText}", quiet: true));
        if (!string.IsNullOrWhiteSpace(tile.Summary))
        {
            var summary = Text(tile.Summary);
            summary.MaxLines = 4;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            rows.Children.Add(summary);
        }
        _bubble = new FeedPreviewBubble
        {
            Name = "FeedPreviewBubble", ArrowOnRight = leftSide,
            Background = Resource<IBrush>("SurfaceRaised", Brushes.DarkSlateGray),
            BorderBrush = Resource<IBrush>("Line", Brushes.Gray),
            Child = new ScrollViewer
            {
                Content = rows, MaxHeight = Math.Max(120, (top?.Bounds.Height ?? 600) - 100),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        _bubble.Bind(FeedPreviewBubble.SourceProperty, new Binding(nameof(FeedCardViewModel.Backdrop)) { Source = card });
        _bubble.PointerEntered += (_, _) => _quickDetails.Hide();
        _bubble.LayoutUpdated += (_, _) => UpdateBubblePointer();
        _quickDetails.Content = _bubble;
        var scaling = top?.RenderScaling ?? 1;
        card.RequestBackdrop((width + 32) * scaling, 220 * scaling);
    }

    private void SuppressPreview()
    {
        _hoverSuppressed = true;
        _quickDetails.Hide();
    }

    private void UpdateBubblePointer()
    {
        if (_bubble is not { Bounds.Width: > 0, Bounds.Height: > 0 } bubble || bubble.GetVisualRoot() is null || CoverFrame.GetVisualRoot() is null) return;
        var anchor = CoverFrame.PointToScreen(new Point(CoverFrame.Bounds.Width / 2, CoverFrame.Bounds.Height / 2));
        var panel = bubble.PointToScreen(default);
        var scale = TopLevel.GetTopLevel(bubble)?.RenderScaling ?? 1;
        bubble.ArrowOnRight = panel.X < anchor.X;
        bubble.ArrowOffset = (anchor.Y - panel.Y) / scale;
    }

    private T Resource<T>(string name, T fallback) => this.TryFindResource(name, out var value) && value is T resource ? resource : fallback;
}
