using Avalonia.Controls;

namespace Winnow.App.Views;

public partial class ActionBarView : UserControl
{
    public ActionBarView()
    {
        InitializeComponent();
        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.Library;
            _ = Design.PreviewData.LoadShellAsync();
        }
    }
}
