using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>Portrait recommendation with independent artwork and an explicitly opened quick view.</summary>
public partial class FeedCardView : UserControl
{
    private FeedCardViewModel? _card;
    private GameTileViewModel? _tile;
    private CoverPresenter? _cover;
    private bool _hovered;
    private bool _focused;
    private readonly Flyout _quickDetails = new() { Placement = PlacementMode.Bottom };

    public FeedCardView()
    {
        InitializeComponent();
        Card.Flyout = _quickDetails;
        _quickDetails.Opening += (_, _) => BuildQuickDetails();
        _quickDetails.Opened += (_, _) =>
        {
            if (_quickDetails.Content is Control content && content.GetVisualAncestors().OfType<FlyoutPresenter>().FirstOrDefault() is { } presenter)
            {
                presenter.Background = Resource<IBrush>("SurfaceRaised", Brushes.DarkSlateGray);
                presenter.BorderBrush = Resource<IBrush>("Line", Brushes.Gray);
                presenter.BorderThickness = new Thickness(1);
                presenter.CornerRadius = new CornerRadius(6);
                presenter.Padding = new Thickness(18);
            }
            Card.Classes.Set("open", true);
            Apply();
        };
        _quickDetails.Closed += (_, _) => { Card.Classes.Set("open", false); Apply(); };
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
        Apply();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
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
        RequestCover();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _quickDetails.Hide();
        _quickDetails.Content = null;
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
        var tile = card.Tile;
        var width = Math.Min(380, Math.Max(200, (TopLevel.GetTopLevel(this)?.Bounds.Width ?? 420) - 48));
        var rows = new StackPanel { Spacing = 12, Width = width };
        TextBlock Text(string? value, int size = 13, bool quiet = false, bool title = false) => new()
        {
            Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            FontFamily = Resource<FontFamily>(title ? "DisplayFont" : "BodyFont", FontFamily.Default),
            FontWeight = title ? FontWeight.Bold : FontWeight.Normal,
            Foreground = Resource<IBrush>(quiet ? "TextDim" : "Text", Brushes.White),
        };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Text("WHY THIS GAME", 11, quiet: true));
        var close = new Button { Name = "CloseQuickDetails", Content = "×", Padding = new Thickness(6, 0) };
        close.Classes.Add("act"); close.Classes.Add("quiet");
        AutomationProperties.SetName(close, "Close quick details");
        close.Click += (_, _) => _quickDetails.Hide();
        Grid.SetColumn(close, 1); heading.Children.Add(close);
        rows.Children.Add(heading);
        rows.Children.Add(Text(tile.Title, 24, title: true));
        rows.Children.Add(Text(card.Reason));
        rows.Children.Add(new Border { Height = 1, Background = Resource<IBrush>("Line", Brushes.Gray) });
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
        primary.Click += (_, _) => _quickDetails.Hide();
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
            _quickDetails.Hide();
            if (tile.OpenDetailsCommand?.CanExecute(tile) == true) tile.OpenDetailsCommand.Execute(tile);
        };
        actions.Children.Add(primary); actions.Children.Add(details);
        rows.Children.Add(actions);
        _quickDetails.Content = new ScrollViewer
        {
            Content = rows, MaxHeight = Math.Max(160, (TopLevel.GetTopLevel(this)?.Bounds.Height ?? 600) - 100),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private T Resource<T>(string name, T fallback) => this.TryFindResource(name, out var value) && value is T resource ? resource : fallback;
}
