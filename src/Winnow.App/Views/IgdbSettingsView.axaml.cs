using Avalonia.Controls;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class IgdbSettingsView : UserControl
{
    public IgdbSettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) =>
        {
            if (DataContext is IgdbSettingsViewModel model) model.ClientSecret = "";
        };
    }
}
