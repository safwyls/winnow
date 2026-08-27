using Avalonia.Controls;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the Stores panel. There is deliberately none of it beyond
/// <c>InitializeComponent</c>.
///
/// <para>Everything on this screen is either static copy or a bound state on
/// <see cref="ViewModels.StoresViewModel"/>, and the two actions are commands.
/// The one thing a code-behind would otherwise be tempted to own — running the
/// sign-in — belongs to the view model precisely so it can be tested without a
/// browser, a window or an Epic account.</para>
/// </summary>
public partial class StoresView : UserControl
{
    public StoresView()
    {
        InitializeComponent();
    }
}
