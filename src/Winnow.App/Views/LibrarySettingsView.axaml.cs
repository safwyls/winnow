using Avalonia.Controls;
using Avalonia.Input;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for SETTINGS › LIBRARY. Every piece of state lives on
/// <see cref="ViewModels.LibrarySettingsViewModel"/> and every interaction is a
/// command, so there is nothing here a test cannot reach without a window —
/// except the two things that are facts about the window rather than about the
/// screen: how many device pixels a candidate thumbnail is drawn at, and where
/// the focus ring ended up inside a scrolling list.
/// </summary>
public partial class LibrarySettingsView : UserControl
{
    public LibrarySettingsView()
    {
        InitializeComponent();

        // The previewer gets the populated screen; runtime leaves the
        // DataContext to the shell. See Design/PreviewData.cs.
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.LibrarySettings;
        }
    }

    /// <summary>
    /// Hands the window's render scaling to the view model so candidate
    /// thumbnails decode at the width they are drawn at. The scaling belongs
    /// to the window, not to the view model.
    ///
    /// <para>Both entry points are needed and neither is enough alone: the
    /// screen is built the first time it is opened (<see cref="LazyPane"/>) and
    /// a control built from a template is attached BEFORE its DataContext
    /// binding resolves — measured, not assumed — so the attach may find no
    /// view model, while the context change may arrive before there is a
    /// window to read a scaling from. The same pair, for the same reason, is in
    /// <see cref="GameMetadataEditorView"/>.</para>
    /// </summary>
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
        if (DataContext is LibrarySettingsViewModel library)
        {
            library.SetCoverScaling(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
        }
    }

    /// <summary>
    /// A candidate row reached by Tab may sit outside the bounded list's
    /// viewport. BringIntoView scrolls it in so the focus ring is visible
    /// where the user is. The handler is on the ScrollViewer because GotFocus
    /// bubbles from the rows.
    /// </summary>
    private void OnCandidateGotFocus(object? sender, GotFocusEventArgs e)
    {
        (e.Source as Control)?.BringIntoView();
    }
}
