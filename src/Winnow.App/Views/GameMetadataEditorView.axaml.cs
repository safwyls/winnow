using Avalonia.Controls;
using Avalonia.Interactivity;
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

    /// <summary>
    /// Raised after the section's own close control folds it, so the host can
    /// hand focus to the More trigger — the control the section was opened
    /// from. The close button lives inside this control and vanishes with
    /// the section, so focus management belongs to the host.
    /// </summary>
    public event EventHandler? CloseRequested;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        ApplyCoverScaling();
    }

    /// <summary>
    /// The view model's <see cref="GameMetadataEditorViewModel.CloseCommand"/>
    /// folds the section; this handler only announces it to the host via
    /// <see cref="CloseRequested"/>. <c>e.Handled</c> is deliberately left
    /// alone — Avalonia's Button runs its Command after raising Click, and
    /// marking it handled would suppress the command.
    /// </summary>
    private void OnClosePressed(object? sender, RoutedEventArgs e)
        => CloseRequested?.Invoke(this, EventArgs.Empty);

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
