using Avalonia.Controls;
namespace Winnow.App.Views;
public partial class StatsView : UserControl
{
    public StatsView()
    {
        InitializeComponent();
        if (Avalonia.Controls.Design.IsDesignMode) { DataContext = global::Winnow.App.Design.PreviewData.Stats; _ = LoadPreviewAsync(); }
    }
    private static async Task LoadPreviewAsync()
    {
        await global::Winnow.App.Design.PreviewData.LoadShellAsync();
        await global::Winnow.App.Design.PreviewData.Stats.ActivateAsync();
    }
}
