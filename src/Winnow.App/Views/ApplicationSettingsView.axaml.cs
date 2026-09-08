using Avalonia.Controls;

namespace Winnow.App.Views;

public partial class ApplicationSettingsView : UserControl
{
    public ApplicationSettingsView()
    {
        InitializeComponent();

        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.ApplicationSettings;
        }
    }
}
