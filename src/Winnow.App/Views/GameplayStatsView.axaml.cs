using Avalonia.Controls;
namespace Winnow.App.Views;
public partial class GameplayStatsView : UserControl
{
    public GameplayStatsView()
    {
        InitializeComponent();
        if (Avalonia.Controls.Design.IsDesignMode) { DataContext = global::Winnow.App.Design.PreviewData.Gameplay; _ = LoadPreviewAsync(); }
    }
    private static async Task LoadPreviewAsync()
    {
        await global::Winnow.App.Design.PreviewData.LoadShellAsync();
        await global::Winnow.App.Design.PreviewData.Gameplay.ActivateAsync();
    }
}
