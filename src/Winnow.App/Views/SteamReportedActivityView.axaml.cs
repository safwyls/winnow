using Avalonia.Controls;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class SteamReportedActivityView : UserControl
{
    public SteamReportedActivityView()
    {
        InitializeComponent();
        if (Avalonia.Controls.Design.IsDesignMode)
            DataContext = global::Winnow.App.Design.PreviewSteamReportedActivity.Create();
        AttachedToVisualTree += async (_, _) =>
        { if (DataContext is SteamReportedActivityViewModel model) await model.RefreshAsync(); };
    }
}
