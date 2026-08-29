using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for a feed card: sets reason text as mixed-font inlines, drives
/// hover state on the borrowed tile view model, and asks the card's own
/// <see cref="CoverPresenter"/> for art at 108 DIP scaled by render scaling.
/// The card holds its own cover state; the wall cannot blank it.
/// </summary>
public partial class FeedCardView : UserControl
{
    /// <summary>
    /// The cover's declared width — the art column in the XAML above, and
    /// TileMinWidth exactly. It came down from 120 when the rails became
    /// wrapping grids: the card's width is the column's now, and the art must
    /// not be what sets its height on a screen whose payload is the sentence.
    /// </summary>
    private const double CoverWidth = 108;

    private GameTileViewModel? _tile;
    private CoverPresenter? _cover;

    public FeedCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts keyboard focus on the card itself rather than on anything inside
    /// it. The card is the Button; Play and the two feedback controls beside it
    /// are Tab stops the user reaches deliberately, and arrow navigation must
    /// not land them on one by accident. It matters more now than it did: the
    /// controls that can set a game aside are on that line, and an arrow key
    /// that parked focus on one of them would put a dismissal under the next
    /// press of Space.
    /// </summary>
    public void TakeFocus() => Card.Focus(NavigationMethod.Directional);

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        SetHover(true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(false);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Bind(DataContext as FeedCardViewModel);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestCover();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SetHover(false);
    }

    private void Bind(FeedCardViewModel? card)
    {
        _tile = card?.Tile;
        _cover = card?.Cover;

        WriteReason(card);
        RequestCover();
    }

    /// <summary>
    /// Turns the split sentence into inlines. Prose runs inherit everything from
    /// the TextBlock; data runs take the data face at a size that sits on the
    /// body's baseline without looking like a different sentence. The tabular
    /// figures come from the TextBlock's own FontFeatures, which the runs
    /// inherit — so there is one declaration of "tnum" and it is in the markup.
    /// </summary>
    private void WriteReason(FeedCardViewModel? card)
    {
        var inlines = Reason.Inlines;
        if (inlines is null)
        {
            inlines = new InlineCollection();
            Reason.Inlines = inlines;
        }

        inlines.Clear();

        if (card is null)
        {
            return;
        }

        var dataFont = this.TryFindResource("DataFont", out var found) && found is FontFamily family
            ? family
            : Reason.FontFamily;

        foreach (var run in card.ReasonRuns)
        {
            var inline = new Run(run.Text);
            if (run.IsData)
            {
                inline.FontFamily = dataFont;
                inline.FontSize = 12;
            }

            inlines.Add(inline);
        }
    }

    private void RequestCover()
    {
        if (_cover is null || this.GetVisualRoot() is null)
        {
            return;
        }

        // Display resolution, not source resolution (§5.4). The cover cache
        // snaps the width to a bucket, so DPI cannot start a re-decode treadmill.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _cover.Request(CoverWidth * scaling);
    }

    private void SetHover(bool value)
    {
        if (_tile is not null)
        {
            _tile.IsPointerOver = value;
        }
    }
}
