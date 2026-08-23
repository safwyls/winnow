using Avalonia.Controls;
using Avalonia.Input;
using Hoard.App.ViewModels;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the cover tile. The only logic here is hover state: the
/// view model's <see cref="GameTileViewModel.IsPointerOver"/> drives
/// <see cref="GameTileViewModel.DisplayAlpha"/>, which the vivid art layer
/// animates over 140ms (§5.1 — "the game wakes up under the cursor").
/// Avalonia's <c>:pointerover</c> pseudo-class can't reach a view-model
/// property, so the two pointer events do it explicitly.
/// </summary>
public partial class GameTileView : UserControl
{
    public GameTileView()
    {
        InitializeComponent();
    }

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

        // ItemsRepeater recycles containers: a tile scrolled away while hovered
        // would otherwise hand its stale vivid state to the next game.
        SetHover(IsPointerOver);
    }

    private void SetHover(bool value)
    {
        if (DataContext is GameTileViewModel tile)
        {
            tile.IsPointerOver = value;
        }
    }
}
