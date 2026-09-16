using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Media;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>Shared, window-bounded hover preview for desktop game covers.</summary>
internal sealed class GameHoverPreview
{
    private readonly Control _owner;
    private readonly Control _anchor;
    private GamePreviewViewModel? _model;
    private bool _hoverSuppressed;
    private bool _opening;
    private static WeakReference<GameHoverPreview>? _activePreview;
    private GamePreviewBubble? _bubble;
    private readonly Flyout _quickDetails = new()
    {
        Placement = PlacementMode.Custom,
        ShowMode = FlyoutShowMode.Transient,
        OverlayDismissEventPassThrough = true,
    };
    public Flyout Flyout => _quickDetails;

    public GameHoverPreview(Control owner, Control anchor)
    {
        _owner = owner;
        _anchor = anchor;
        FlyoutBase.SetAttachedFlyout(anchor, _quickDetails);
        _quickDetails.CustomPopupPlacementCallback = PlacePreview;
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
        };
        _quickDetails.Closed += (_, _) =>
        {
            _model?.ReleaseBackdrop();
            if (_activePreview?.TryGetTarget(out var active) == true && ReferenceEquals(active, this)) _activePreview = null;
        };
    }

    public void Target(GamePreviewViewModel? model)
    {
        Hide();
        _model = model;
        _hoverSuppressed = false;
    }
    public void Show()
    {
        if (_opening || _hoverSuppressed || _quickDetails.IsOpen || _model is null || !_owner.IsEffectivelyVisible) return;
        _opening = true;
        try { _quickDetails.ShowAt(_anchor); }
        finally { _opening = false; }
    }
    public void Exit() { _hoverSuppressed = false; Hide(); }
    public void Hide()
    {
        _quickDetails.Hide();
        _model?.ReleaseBackdrop();
        _quickDetails.Content = null;
        _bubble = null;
    }
    private void BuildQuickDetails()
    {
        if (_model is not { } card) return;
        if (_activePreview?.TryGetTarget(out var previous) == true && !ReferenceEquals(previous, this)) previous.Hide();
        _activePreview = new WeakReference<GameHoverPreview>(this);
        var tile = card.Tile;
        var top = TopLevel.GetTopLevel(_owner);
        _quickDetails.OverlayInputPassThroughElement = top;
        var origin = top is not null ? _anchor.TranslatePoint(default, top) ?? default : default;
        var rightSpace = (top?.Bounds.Width ?? 900) - origin.X - _anchor.Bounds.Width;
        var leftSide = rightSpace < 362 && origin.X > rightSpace;
        var width = Math.Min(Math.Clamp((leftSide ? origin.X : rightSpace) - 46, 180, 320),
            Math.Max(1, (top?.Bounds.Width ?? 900) - 58));
        var rows = new StackPanel { Spacing = 10, Width = width };
        TextBlock Text(string? value, int size = 13, bool quiet = false, bool title = false)
        {
            var block = new TextBlock
            {
                Text = value, TextWrapping = TextWrapping.Wrap,
                FontWeight = title ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Resource<IBrush>(quiet ? "TextDim" : "Text", Brushes.White),
            };
            block[!TextBlock.FontFamilyProperty] = new DynamicResourceExtension(title ? "DisplayFont" : "BodyFont");
            ThemeTypographyResources.BindSize(block, size);
            return block;
        }
        rows.Children.Add(Text(tile.Title, 22, title: true));
        rows.Children.Add(Text($"{tile.StoreNames} · {tile.StatText}", quiet: true));
        var ratings = Text(null, 12, quiet: true);
        ratings.Name = "PreviewRatings";
        ratings.TextWrapping = TextWrapping.NoWrap;
        ratings.TextTrimming = TextTrimming.CharacterEllipsis;
        ratings.Bind(TextBlock.TextProperty, new Binding("Reception.CompactText") { Source = card });
        ratings.Bind(AutomationProperties.NameProperty, new Binding("Reception.CompactAutomationName") { Source = card });
        ratings.Bind(Visual.IsVisibleProperty, new Binding("Reception.HasFigures") { Source = card, FallbackValue = false });
        rows.Children.Add(ratings);
        if (!string.IsNullOrWhiteSpace(tile.Summary))
        {
            var summary = Text(tile.Summary);
            summary.MaxLines = 4;
            summary.TextTrimming = TextTrimming.CharacterEllipsis;
            rows.Children.Add(summary);
        }
        _bubble = new GamePreviewBubble
        {
            Name = "GamePreviewBubble", ArrowOnRight = leftSide,
            Background = Resource<IBrush>("SurfaceRaised", Brushes.DarkSlateGray),
            BorderBrush = Resource<IBrush>("Line", Brushes.Gray),
            Child = new ScrollViewer
            {
                Content = rows, MaxHeight = Math.Max(1, (top?.Bounds.Height ?? 600) - 48),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        _bubble.Bind(GamePreviewBubble.SourceProperty, new Binding(nameof(GamePreviewViewModel.Backdrop)) { Source = card });
        _bubble.PointerEntered += (_, _) => _quickDetails.Hide();
        _bubble.LayoutUpdated += (_, _) => UpdateBubblePointer();
        _quickDetails.Content = _bubble;
        var scaling = top?.RenderScaling ?? 1;
        card.RequestBackdrop((width + 32) * scaling, 220 * scaling);
        card.RequestRatings();
    }

    public void Suppress()
    {
        _hoverSuppressed = true;
        _quickDetails.Hide();
    }

    private void PlacePreview(CustomPopupPlacement placement)
    {
        if (TopLevel.GetTopLevel(_owner) is not { } top) return;
        var origin = _anchor.TranslatePoint(default, top) ?? default;
        var right = origin.X + _anchor.Bounds.Width;
        var leftSide = top.Bounds.Width - right < placement.PopupSize.Width + 8 && origin.X > top.Bounds.Width - right;
        var x = leftSide ? origin.X - placement.PopupSize.Width : right;
        // Native popup constraints use the monitor; clamp to our client area first.
        x = Math.Clamp(x, 8, Math.Max(8, top.Bounds.Width - placement.PopupSize.Width - 8));
        var y = Math.Clamp(origin.Y, 8, Math.Max(8, top.Bounds.Height - placement.PopupSize.Height - 8));
        placement.AnchorRectangle = new Rect(x, y, 1, 1);
        placement.Anchor = PopupAnchor.TopLeft;
        placement.Gravity = PopupGravity.BottomRight;
        placement.Offset = default;
    }

    private void UpdateBubblePointer()
    {
        if (_bubble is not { Bounds.Width: > 0, Bounds.Height: > 0 } bubble || bubble.GetVisualRoot() is null || _anchor.GetVisualRoot() is null) return;
        var anchor = _anchor.PointToScreen(new Point(_anchor.Bounds.Width / 2, _anchor.Bounds.Height / 2));
        var panel = bubble.PointToScreen(default);
        var scale = TopLevel.GetTopLevel(bubble)?.RenderScaling ?? 1;
        bubble.ArrowOnRight = panel.X < anchor.X;
        bubble.ArrowOffset = (anchor.Y - panel.Y) / scale;
    }

    private T Resource<T>(string name, T fallback) => _owner.TryFindResource(name, out var value) && value is T resource ? resource : fallback;
}
