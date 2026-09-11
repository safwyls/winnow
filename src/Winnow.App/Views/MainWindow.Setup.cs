using System.ComponentModel;
using Winnow.App.ViewModels;

namespace Winnow.App.Views;

public partial class MainWindow
{
    private void OnSetupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FirstRunSetupViewModel.IsOpen) or nameof(FirstRunSetupViewModel.Step))
        {
            _gamepadKeyboard?.Close();
            UpdateSetupPresentation();
        }
    }

    private void UpdateSetupPresentation()
    {
        if (SetupPanel is null || ShellContent is null) return;
        var open = _shell?.Setup.IsOpen == true;
        ShellContent.IsEnabled = !open;
        SetupPanel.IsVisible = open && !IsFullscreen;
        SetupInputHost.IsVisible = open && !IsFullscreen;
    }
}
