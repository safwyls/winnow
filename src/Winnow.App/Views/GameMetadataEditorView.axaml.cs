using Avalonia.Controls;
using Avalonia.Input;
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
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 820;
            MetadataFormGrid.ColumnDefinitions = new(narrow ? "*" : "3*,2*");
            MetadataFormGrid.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
            Grid.SetColumn(MetadataArtworkFields, narrow ? 0 : 1);
            Grid.SetRow(MetadataArtworkFields, narrow ? 1 : 0);
        };
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ApplyCoverScaling();
    }

    private void OnFieldGotFocus(object? sender, GotFocusEventArgs e)
        => (e.Source as Control)?.BringIntoView();

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
