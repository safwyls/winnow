using Avalonia.Controls;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the list row's cover. Holds this row's own cover state
/// and retargets it when the ListBox recycles the container onto another
/// game. The wall, the feed card and the list row each own their lease, so
/// no surface can blank another's art.
/// </summary>
public partial class RowCoverView : UserControl
{
    /// <summary>The cover's declared width in DIP, matching the XAML column.</summary>
    private const double CoverWidth = 24;

    /// <summary>The view model this container is currently showing art for.</summary>
    private GameTileViewModel? _bound;

    public RowCoverView()
    {
        InitializeComponent();

        // The previewer gets a populated row; runtime leaves the DataContext
        // to the list's container recycling. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.Tile;
        }
    }

    /// <summary>
    /// This row's own cover state. A game can appear on the wall, on a
    /// feed card and in a list row at once, so each surface owns its lease
    /// and recycling one never drops what another is showing.
    /// </summary>
    public CoverPresenter Cover { get; } = new();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Recycling swaps the data context without detaching, so this is
        // the common path that retargets the presenter.
        if (!ReferenceEquals(_bound, DataContext))
        {
            _bound = DataContext as GameTileViewModel;
            Cover.Target(_bound?.CoverKey, _bound?.Leases);
        }

        if (this.GetVisualRoot() is not null)
        {
            RequestCover();
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _bound = DataContext as GameTileViewModel;
        Cover.Target(_bound?.CoverKey, _bound?.Leases);
        RequestCover();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Cover.Release();
    }

    private void RequestCover()
    {
        if (DataContext is not GameTileViewModel)
        {
            return;
        }

        // Display resolution, not source resolution (§5.4). CoverImaging
        // snaps this to a width bucket, so 24 DIP at 1x lands in the same
        // 160px bucket a 148 DIP tile uses — rows share cache entries with
        // the grid rather than adding a second decode size.
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        Cover.Request(CoverWidth * scaling);
    }
}
