using Avalonia.Controls;

namespace Winnow.App.Views;

/// <summary>
/// Code-behind for the Steam account import screen. There is deliberately
/// none of it beyond <c>InitializeComponent</c>.
///
/// <para>Everything on this screen is bound copy or a command. Running a
/// route is the one thing a code-behind would be tempted to own; it belongs
/// to the view model so it can be tested without a browser, a window or a
/// Steam account.</para>
/// </summary>
public partial class SteamAccountImportView : UserControl
{
    public SteamAccountImportView()
    {
        InitializeComponent();
    }
}
