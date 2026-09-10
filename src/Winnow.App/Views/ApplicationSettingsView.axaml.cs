using Avalonia;
using Avalonia.Controls;

namespace Winnow.App.Views;

public partial class ApplicationSettingsView : UserControl
{
    public ApplicationSettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => ClearSecret();

        if (Avalonia.Controls.Design.IsDesignMode)
        {
            DataContext = Design.PreviewData.ApplicationSettings;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !change.GetNewValue<bool>()) ClearSecret();
    }

    private void ClearSecret()
    {
        if (DataContext is ViewModels.ApplicationSettingsViewModel model)
            model.Igdb.ClientSecret = "";
    }
}
