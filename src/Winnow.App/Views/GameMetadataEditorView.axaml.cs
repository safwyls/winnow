using Avalonia.Controls;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the metadata editor. Hands the view model the window's
/// render scaling so art previews are decoded at the resolution they will
/// actually be drawn at rather than at full size.
/// </summary>
public partial class GameMetadataEditorView : UserControl
{
    public GameMetadataEditorView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ApplyCoverScaling();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        ApplyCoverScaling();
    }

    private void ApplyCoverScaling()
    {
        if (DataContext is not GameMetadataEditorViewModel editor)
        {
            return;
        }

        editor.SetCoverScaling(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
    }
}
