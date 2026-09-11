using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FirstRunSetupShellTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_install_opens_setup_after_startup_in_the_selected_presentation(bool fullscreen)
    {
        var preview = PreviewData.Shell;
        var progress = new FirstRunSetupService();
        await progress.InitializeAsync(false, false);
        var app = new ApplicationSettingsViewModel { StartInFullscreen = fullscreen };
        var setup = new FirstRunSetupViewModel(preview.Stores, preview.Appearance, app, preview.LibrarySettings, progress);
        var shell = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            preview.Appearance, preview.Feed, preview.AccountStats, preview.LibrarySettings,
            applicationSettings: app, setup: setup);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(setup.IsOpen) && setup.IsOpen) opened.TrySetResult(); };
        var window = new MainWindow { DataContext = shell };
        window.Show();
        try
        {
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(FirstRunStep.Welcome, setup.Step);
            Assert.Equal(!fullscreen, window.FindControl<LazyPane>("SetupPanel")!.IsEffectivelyVisible);
            if (fullscreen)
            {
                var television = Assert.IsType<Winnow.App.Views.Fullscreen.FullscreenView>(window.FindControl<ContentControl>("TvHost")!.Content);
                Assert.IsType<Winnow.App.Views.Fullscreen.FullscreenSetupPage>(television.CurrentPage);
            }
            await setup.SkipAllCommand.ExecuteAsync(null);
        }
        finally { window.ExitFromTray(); }
    }

    private static MainWindowViewModel Shell()
    {
        var preview = PreviewData.Shell;
        return new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            preview.Appearance, preview.Feed, preview.AccountStats, preview.LibrarySettings,
            applicationSettings: new ApplicationSettingsViewModel());
    }

    [AvaloniaFact]
    public async Task Setup_keyboard_is_above_enabled_overlay_and_types_into_masked_field()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 640 };
        window.Show();
        try
        {
            await shell.Setup.ReopenCommand.ExecuteAsync(null);
            await shell.Setup.NextCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var secret = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "IGDB client secret");
            secret.Focus();
            window.HandleGamepad(GamepadButtons.Keyboard);
            Dispatcher.UIThread.RunJobs();
            var keyboard = Assert.Single(window.GetVisualDescendants().OfType<GamepadKeyboardView>());
            Assert.True(keyboard.IsEffectivelyEnabled);
            Assert.Contains(window.FindControl<Panel>("SetupInputHost")!, keyboard.GetVisualAncestors());
            Assert.False(window.FindControl<Grid>("ShellContent")!.IsEnabled);
            Assert.Equal('●', secret.PasswordChar);
            window.HandleGamepad(GamepadButtons.Accept);
            Assert.False(string.IsNullOrEmpty(secret.Text));
            window.HandleGamepad(GamepadButtons.Back);
            Assert.Empty(window.GetVisualDescendants().OfType<GamepadKeyboardView>());
            Assert.Equal(FirstRunStep.Igdb, shell.Setup.Step);
            window.ToggleFullscreen();
            Assert.Empty(shell.ApplicationSettings.Igdb.ClientSecret);
            window.ToggleFullscreen();
            Dispatcher.UIThread.RunJobs();
            var setup = Assert.Single(window.GetVisualDescendants().OfType<FirstRunSetupView>());
            Assert.Contains(window.FocusManager!.GetFocusedElement() as Control, setup.GetVisualDescendants());
            await shell.Setup.SkipAllCommand.ExecuteAsync(null);
            Assert.True(window.FindControl<Grid>("ShellContent")!.IsEnabled);
        }
        finally { shell.Stores.CloseModalCommand.Execute(null); window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public async Task Controller_stays_in_steam_consent_and_back_closes_only_that_layer()
    {
        var shell = Shell();
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 640 };
        window.Show();
        try
        {
            await shell.Setup.ReopenCommand.ExecuteAsync(null);
            await shell.Setup.NextCommand.ExecuteAsync(null);
            await shell.Setup.SkipStepCommand.ExecuteAsync(null);
            shell.Stores.OpenSignInConsentCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var modal = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.IsEffectivelyVisible && b.Classes.Contains("modal"));
            Assert.True(shell.Setup.IsBusy);
            foreach (var direction in Enumerable.Repeat(new[] { GamepadButtons.Next, GamepadButtons.Down,
                GamepadButtons.Right, GamepadButtons.Previous }, 4).SelectMany(x => x))
            {
                window.HandleGamepad(direction);
                var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
                Assert.Contains(modal, focused.GetVisualAncestors());
            }
            window.HandleGamepad(GamepadButtons.Back);
            Assert.False(shell.Stores.IsAnyModalOpen);
            Assert.Equal(FirstRunStep.Steam, shell.Setup.Step);
            await shell.Setup.SkipAllCommand.ExecuteAsync(null);
        }
        finally { shell.Stores.CloseModalCommand.Execute(null); window.ExitFromTray(); }
    }
}
