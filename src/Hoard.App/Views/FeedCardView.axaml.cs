using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for a feed card. Three jobs, and none of them is decoration.
///
/// <para><b>The sentence.</b> The reason is set as inlines rather than as a
/// single string so that every number in it renders in IBM Plex Mono with
/// tabular figures (§3) while the sentence itself keeps the body face. The split
/// is <see cref="ReasonText"/>'s and is unit-tested there; this class only turns
/// runs into <see cref="Run"/>s.</para>
///
/// <para><b>Hover.</b> The view model's <c>IsPointerOver</c> drives
/// <c>DisplayAlpha</c>, which the vivid art layer animates over 140ms —
/// Avalonia's <c>:pointerover</c> pseudo-class cannot reach a view-model
/// property, so the pointer events do it explicitly, exactly as the wall's tiles
/// do.</para>
///
/// <para><b>Cover art, and why it is requested but never released.</b> The feed
/// renders the LIBRARY's tile view models (see
/// <see cref="IGameTileSource"/>), and the wall releases a tile's decoded
/// bitmaps whenever it recycles the container showing it — which happens while
/// the user is scrolling the library, with the feed hidden behind it. A feed
/// card would then come back to a blank cover. So the card watches its tile and
/// re-requests when the bitmaps go, which is normally a synchronous hit in the
/// cover cache's memory tier. It does NOT release on the way out: fewer than
/// fifty cards exist, the cache is the real owner of the pixels, and releasing
/// here would blank the wall from the other direction.</para>
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

    public FeedCardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Puts keyboard focus on the card itself rather than on anything inside
    /// it. The card is the Button; the Play button in its corner is a second
    /// Tab stop the user reaches deliberately, and arrow navigation must not
    /// land them on it by accident.
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

        // Stop watching, but leave the bitmaps alone — see the class remarks.
        if (_tile is not null)
        {
            _tile.PropertyChanged -= OnTileChanged;
        }

        SetHover(false);
    }

    private void Bind(FeedCardViewModel? card)
    {
        if (_tile is not null)
        {
            _tile.PropertyChanged -= OnTileChanged;
        }

        _tile = card?.Tile;
        if (_tile is not null)
        {
            _tile.PropertyChanged += OnTileChanged;
        }

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

    private void OnTileChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The wall recycled a container that was showing this game and released
        // its bitmaps. Ask again: the cache usually answers from memory, and the
        // card is on screen either way.
        if (e.PropertyName == nameof(GameTileViewModel.VividCover) && _tile?.VividCover is null)
        {
            RequestCover();
        }
    }

    private void RequestCover()
    {
        if (_tile is null || this.GetVisualRoot() is null)
        {
            return;
        }

        // Display resolution, not source resolution (§5.4). The cover cache
        // snaps the width to a bucket, so DPI cannot start a re-decode treadmill.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _tile.RequestCover(CoverWidth * scaling);
    }

    private void SetHover(bool value)
    {
        if (_tile is not null)
        {
            _tile.IsPointerOver = value;
        }
    }
}
