using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the cover tile. Two jobs, both forced by container recycling
/// in <see cref="CoverWall"/>.
/// <para>Hover: the view model's <see cref="GameTileViewModel.IsPointerOver"/>
/// drives <see cref="GameTileViewModel.DisplayAlpha"/>, which the vivid art
/// layer animates over 140ms (§5.1 — "the game wakes up under the cursor").
/// Avalonia's <c>:pointerover</c> pseudo-class can't reach a view-model
/// property, so the two pointer events do it explicitly.</para>
/// <para>Cover art: realization is the load trigger. The wall only gives a
/// container a data context while its cell is on screen (plus a buffer row), so
/// "visible first" falls out of the context swap for free — and the swap
/// retargets this container's <see cref="Cover"/> presenter, which drops the
/// previous game's art and lease. Detaching releases it. That is what keeps the
/// cache's memory bound honest with 616 tiles virtualized.</para>
/// </summary>
public partial class GameTileView : UserControl
{
    /// <summary>Fallback tile width when the container is measured after attach (the wall's density minimum).</summary>
    private const double NominalTileWidth = 148;

    /// <summary>The view model this container is currently showing art for.</summary>
    private GameTileViewModel? _bound;

    public GameTileView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// This container's own cover state, retargeted when the wall recycles it
    /// onto another game. The same tile appears on the wall and on a feed card
    /// at once, so recycling this container may only drop what this container
    /// is showing.
    /// </summary>
    public CoverPresenter Cover { get; } = new();

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

        // Containers are recycled: a tile scrolled away while hovered would
        // otherwise hand its stale vivid state to the next game.
        SetHover(IsPointerOver);

        // Recycling swaps the data context without ever detaching, so this — not
        // OnDetachedFromVisualTree — is the common path that retargets the
        // presenter. Missing it leaves this container's presenter holding a
        // lease on the previous game's art, and the cache's memory bound stops
        // meaning anything.
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

        // Display resolution, not source resolution (§5.4). The cover cache
        // snaps this to a bucket, so DPI and the density slider cannot start a
        // re-decode treadmill.
        var width = Bounds.Width > 0 ? Bounds.Width : NominalTileWidth;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        Cover.Request(width * scaling);
    }

    private void SetHover(bool value)
    {
        if (DataContext is GameTileViewModel tile)
        {
            tile.IsPointerOver = value;
        }
    }

    // Play / Install moved off this class in M3b. It is a command on the view
    // model now (GameTileViewModel.PrimaryActionCommand), because a launch has
    // to tell the session watcher WHICH GAME it is, and a Click handler holding
    // a URI cannot: it knows a string, not an ownership. The URI still reaches
    // the OS's own handler and the app still never shells out to steam.exe by
    // name — that moved to Services/TopLevelUriDispatcher, which is now the one
    // place a URI leaves this application.
}
