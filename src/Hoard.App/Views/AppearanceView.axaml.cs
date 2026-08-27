using Avalonia.Controls;

namespace Hoard.App.Views;

/// <summary>
/// Code-behind for the Appearance screen. There is deliberately none of it
/// beyond <c>InitializeComponent</c>: everything here is either static copy or
/// bound state on <see cref="ViewModels.AppearanceViewModel"/>, and picking a
/// theme is a command (§5.1).
/// </summary>
public partial class AppearanceView : UserControl
{
    public AppearanceView()
    {
        InitializeComponent();
    }
}
