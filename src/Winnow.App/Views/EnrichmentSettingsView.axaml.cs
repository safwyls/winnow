using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class EnrichmentSettingsView : UserControl
{
    public EnrichmentSettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is EnrichmentSettingsViewModel model) await model.Plugins.LoadAsync();
        };
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

    private async void OnOpenPluginsFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EnrichmentSettingsViewModel model) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(model.Plugins.UserPluginDirectory)
                && TopLevel.GetTopLevel(this)?.Launcher is { } launcher
                && await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(model.Plugins.UserPluginDirectory))) return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        model.Plugins.FolderOpenFailed();
    }
}
