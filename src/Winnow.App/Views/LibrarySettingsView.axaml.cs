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
    }

    /// <summary>
    /// Hands the window's render scaling to the view model so candidate
    /// thumbnails decode at the width they are drawn at. The scaling belongs
    /// to the window, not to the view model.
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

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
