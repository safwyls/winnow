using Avalonia.Controls;
using Avalonia.Input;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the Stores panel. There is deliberately almost none of it.
///
/// <para>Everything on this screen is either static copy or a bound state on
/// <see cref="ViewModels.StoresViewModel"/>, and the actions are commands. The
/// one thing a code-behind would otherwise be tempted to own — running the
/// sign-in — belongs to the view model precisely so it can be tested without a
/// browser, a window or an Epic account.</para>
///
/// <para>The single exception is scrim dismissal, which is a pointer gesture on
/// a specific visual and has no meaning to the view model; the game details
/// modal handles its own the same way. It closes through the view model's own
/// command, so the state still has one owner.</para>
/// </summary>
public partial class StoresView : UserControl
{
    public StoresView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Clicking outside a modal closes it, the keyboard half of which is the
    /// shell's Escape.
    ///
    /// <para>Attached only to the two modals that INFORM. The consent modal has
    /// no scrim handler at all: a click that happened to land outside it must
    /// never be able to read as the consent its Continue button grants, so
    /// backing out of that one is explicit (ROADMAP §4.7 condition 3).</para>
    /// </summary>
    private void OnModalScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is StoresViewModel stores)
        {
            stores.CloseModalCommand.Execute(null);
            e.Handled = true;
        }
    }
}
