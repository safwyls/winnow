using Avalonia;
using Avalonia.Controls;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class EnrichmentSettingsView : UserControl
{
    public EnrichmentSettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => ClearSecrets();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !change.GetNewValue<bool>()) ClearSecrets();
    }

    private void ClearSecrets()
    {
        if (DataContext is EnrichmentSettingsViewModel model) model.ClearSecrets();
    }
}
