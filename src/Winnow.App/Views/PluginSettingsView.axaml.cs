using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class PluginSettingsView : UserControl
{
    private bool _attached;
    private PluginSettingsViewModel? _model;

    public PluginSettingsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            _attached = true;
            LoadIfVisible();
        };
        DataContextChanged += (_, _) =>
        {
            if (!ReferenceEquals(_model, DataContext)) _model?.ClearSecrets();
            _model = DataContext as PluginSettingsViewModel;
            LoadIfVisible();
        };
        DetachedFromVisualTree += (_, _) => { _attached = false; ClearSecrets(); };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (change.GetNewValue<bool>()) LoadIfVisible();
            else ClearSecrets();
        }
    }

    private async void LoadIfVisible()
    {
        // LazyPane can attach this view before its DataContext binding resolves.
        if (_attached && IsVisible && DataContext is PluginSettingsViewModel model)
            await model.LoadAsync();
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
