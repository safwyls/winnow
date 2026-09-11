using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FirstRunSetupFullscreenTests
{
    private static MainWindowViewModel Shell()
    {
        var preview = PreviewData.Shell;
        return new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            preview.Appearance, preview.Feed, preview.AccountStats, preview.LibrarySettings,
            applicationSettings: new ApplicationSettingsViewModel());
    }

    [AvaloniaFact]
    public async Task Controller_setup_blocks_root_navigation_and_returns_from_provider_to_same_step()
    {
        var shell = Shell();
        await shell.Setup.ReopenCommand.ExecuteAsync(null);
        using var view = new FullscreenView(new FullscreenContext(shell.Library, shell.Feed, shell));
        var window = new Window { Content = view, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenSetupPage>(view.CurrentPage);
            view.Handle(GamepadButtons.Next);
            view.Handle(GamepadButtons.Menu);
            view.Handle(GamepadButtons.Back);
            Assert.IsType<FullscreenSetupPage>(view.CurrentPage);
            Assert.Equal(FirstRunStep.Welcome, shell.Setup.Step);
            Capture(window, "welcome");
            await shell.Setup.NextCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "igdb");
            view.Handle(GamepadButtons.Accept);
            Assert.IsType<FullscreenIgdbSettingsPage>(view.CurrentPage);
            Capture(window, "igdb-editor");
            view.Handle(GamepadButtons.Back);
            Assert.IsType<FullscreenSetupPage>(view.CurrentPage);
            Assert.Equal(FirstRunStep.Igdb, shell.Setup.Step);
            await shell.Setup.SkipStepCommand.ExecuteAsync(null);
            Assert.Equal(FirstRunStep.Steam, shell.Setup.Step);
            await shell.Setup.SkipAllCommand.ExecuteAsync(null);
            Assert.False(shell.Setup.IsOpen);
            Assert.IsType<FullscreenBrowsePage>(view.CurrentPage);
            Assert.True(view.GetVisualDescendants().OfType<StackPanel>()
                .Single(p => p.Name == "FullscreenRootNavigation").IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Application_settings_replay_opens_setup_instead_of_leaving_settings_stack_active()
    {
        var shell = Shell();
        var context = new FullscreenContext(shell.Library, shell.Feed, shell);
        using var view = new FullscreenView(context);
        var window = new Window { Content = view, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            context.Push(new FullscreenSettingsPage(context, "Application"));
            Dispatcher.UIThread.RunJobs();
            var replay = view.CurrentPage.GetVisualDescendants().OfType<Button>()
                .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Run setup again");
            replay.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.Setup.IsOpen);
            Assert.IsType<FullscreenSetupPage>(view.CurrentPage);
            Assert.Equal(FirstRunStep.Welcome, shell.Setup.Step);
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string step)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, $"fullscreen-setup-{step}.png"));
    }
}
