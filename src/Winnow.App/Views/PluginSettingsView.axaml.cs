using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class PluginSettingsView : UserControl
{
    public PluginSettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += async (_, _) =>
        {
            if (DataContext is PluginSettingsViewModel model) await model.LoadAsync();
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
        if (DataContext is PluginSettingsViewModel model) model.ClearSecrets();
    }

    private async void OnOpenPluginsFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PluginSettingsViewModel model) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(model.UserPluginDirectory)
                && TopLevel.GetTopLevel(this)?.Launcher is { } launcher
                && await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(model.UserPluginDirectory))) return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { }
        model.FolderOpenFailed();
    }
}
