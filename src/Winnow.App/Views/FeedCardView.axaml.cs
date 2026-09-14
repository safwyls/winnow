using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Data;
using Avalonia.Threading;
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
    private bool _previewHovered;
    private bool _hoverSuppressed;
    private bool _keyboardOpened;
    private readonly DispatcherTimer _openTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
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
        Card.Flyout = _quickDetails;
        _openTimer.Tick += (_, _) =>
        {
            _openTimer.Stop();
            if (_hovered && !_hoverSuppressed && IsEffectivelyVisible && _card?.ShowActions == true)
            {
                _keyboardOpened = false;
                _quickDetails.ShowMode = FlyoutShowMode.Transient;
                _quickDetails.ShowAt(Card);
            }
        };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!_hovered && !_previewHovered && !(_keyboardOpened && (_focused || _bubble?.IsKeyboardFocusWithin == true)))
                _quickDetails.Hide();
        };
        Card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space && ReferenceEquals(e.Source, Card))
            {
                _openTimer.Stop();
                _keyboardOpened = true;
                _quickDetails.ShowMode = FlyoutShowMode.Standard;
            }
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
            _openTimer.Stop(); _closeTimer.Stop();
            _previewHovered = false;
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
        _closeTimer.Stop();
        if (!_quickDetails.IsOpen && !_hoverSuppressed) _openTimer.Start();
        Apply();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        _hoverSuppressed = false;
        _openTimer.Stop();
        _closeTimer.Start();
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
        _openTimer.Stop(); _closeTimer.Stop();
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
        _openTimer.Stop(); _closeTimer.Stop();
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
        _openTimer.Stop(); _closeTimer.Stop();
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
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Text(tile.Title, 22, title: true));
        var close = new Button { Name = "CloseQuickDetails", Content = "×", Padding = new Thickness(6, 0) };
        close.Classes.Add("act"); close.Classes.Add("quiet");
        AutomationProperties.SetName(close, "Close quick details");
        close.Click += (_, _) => SuppressPreview();
        Grid.SetColumn(close, 1); heading.Children.Add(close);
        rows.Children.Add(heading);
        rows.Children.Add(Text($"{tile.StoreNames} · {tile.StatText}", quiet: true));
        if (!string.IsNullOrWhiteSpace(tile.Summary))
        {
            var summary = Text(tile.Summary);
            summary.MaxLines = 4;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            rows.Children.Add(summary);
        }
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 12, LineSpacing = 8 };
        var primary = new Button
        {
            Name = "PrimaryAction", Content = tile.PrimaryActionLabel,
            IsVisible = tile.HasPrimaryAction, Command = tile.PrimaryActionCommand, CommandParameter = tile,
        };
        primary.Classes.Add("act"); primary.Classes.Add("primary");
        primary.Background = Resource<IBrush>("Volt", Brushes.Aquamarine);
        primary.Foreground = Resource<IBrush>("Ground", Brushes.Black);
        primary.FontWeight = FontWeight.SemiBold;
        primary.Padding = new Thickness(18, 9);
        ToolTip.SetTip(primary, tile.PrimaryActionHint);
        primary.Click += (_, _) => SuppressPreview();
        var details = new Button { Name = "OpenDetails", Content = "Open details →" };
        details.Classes.Add("act"); details.Classes.Add("quiet");
        details.Background = Brushes.Transparent;
        details.BorderThickness = new Thickness(0);
        details.Foreground = Resource<IBrush>("Text", Brushes.White);
        foreach (var button in new[] { close, primary, details })
        {
            button.FontFamily = Resource<FontFamily>("BodyFont", FontFamily.Default);
            button.FontSize = 13;
        }
        details.Click += (_, _) =>
        {
            SuppressPreview();
            if (tile.OpenDetailsCommand?.CanExecute(tile) == true) tile.OpenDetailsCommand.Execute(tile);
        };
        actions.Children.Add(primary); actions.Children.Add(details);
        rows.Children.Add(actions);
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
        _bubble.PointerEntered += (_, _) => { _previewHovered = true; _closeTimer.Stop(); };
        _bubble.PointerExited += (_, _) => { _previewHovered = false; _closeTimer.Start(); };
        _bubble.KeyDown += (_, e) => { if (e.Key == Key.Escape) SuppressPreview(); };
        _bubble.LayoutUpdated += (_, _) => UpdateBubblePointer();
        _quickDetails.Content = _bubble;
        var scaling = top?.RenderScaling ?? 1;
        card.RequestBackdrop((width + 32) * scaling, 220 * scaling);
    }

    private void SuppressPreview()
    {
        _hoverSuppressed = true;
        _openTimer.Stop(); _closeTimer.Stop();
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
