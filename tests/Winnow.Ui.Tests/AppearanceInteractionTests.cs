using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class AppearanceInteractionTests
{
    [AvaloniaFact]
    public void Restored_focus_keeps_scrolled_position_but_keyboard_focus_reveals_control()
    {
        var view = new AppearanceView { DataContext = new AppearanceViewModel(new ThemeService()) };
        var window = new Window { Width = 1000, Height = 640, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("ScreenScroll")!;
            var button = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("themecard"));
            button.Focus(NavigationMethod.Pointer);
            Dispatcher.UIThread.RunJobs();
            window.FocusManager!.ClearFocus();
            scroll.Offset = new Vector(0, 400);
            Dispatcher.UIThread.RunJobs();
            var offset = scroll.Offset;
            Assert.True(offset.Y > 0);

            button.Focus(NavigationMethod.Unspecified);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(offset, scroll.Offset);

            window.FocusManager.ClearFocus();
            button.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y < offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Fresh_appearance_uses_first_choices_and_collapses_warnings()
    {
        var service = new ThemeService();
        await service.LoadAsync();
        var viewModel = new AppearanceViewModel(service);
        Assert.Equal(OperatingSystem.IsWindows() ? 30 : 0, service.Transparency);
        Assert.Equal(WinnowBackdrop.Acrylic, service.Backdrop);
        Assert.True(service.WallTranslucent);
        Assert.Equal(WinnowLayout.Floating, service.Layout);
        Assert.True(viewModel.Layouts[0].IsSelected);
        Assert.True(viewModel.Reach[0].IsSelected);
        Assert.False(viewModel.ShowThemeWarnings);
        viewModel.ToggleThemeWarningsCommand.Execute(null);
        Assert.True(viewModel.ShowThemeWarnings);
    }
}
