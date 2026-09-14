using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
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
    private readonly GameHoverPreview _preview;
    private Flyout _quickDetails => _preview.Flyout;

    public FeedCardView()
    {
        InitializeComponent();
        _preview = new GameHoverPreview(this, Card);
        Card.Click += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, Card)) return;
            _preview.Suppress();
            if (_tile is { } tile && tile.OpenDetailsCommand?.CanExecute(tile) == true)
                tile.OpenDetailsCommand.Execute(tile);
        };
        Card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) _preview.Suppress();
        };
        _quickDetails.Opened += (_, _) =>
        {
            Card.Classes.Set("open", true);
            Apply();
        };
        _quickDetails.Closed += (_, _) =>
        {
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
        if (_card?.ShowActions == true) _preview.Show();
        Apply();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = false;
        _preview.Exit();
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
        _preview.Target(null);
        if (_card is not null)
        {
            _card.IsPointerOver = false;
            _card.IsFocusWithin = false;
        }
        _card = DataContext as FeedCardViewModel;
        _tile = _card?.Tile;
        _cover = _card?.Cover;
        _preview.Target(_card?.Preview);
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
        _preview.Hide();
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

}
