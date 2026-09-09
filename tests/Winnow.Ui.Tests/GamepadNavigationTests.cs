using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class GamepadNavigationTests
{
    [AvaloniaFact]
    public void Dpad_moves_between_ordinary_controls_inside_a_flyout()
    {
        var window = new MainWindow();
        var anchor = new Button { Content = "Display" };
        var first = new CheckBox { Content = "First" };
        var second = new CheckBox { Content = "Second" };
        var flyout = new Flyout { Content = new StackPanel { Children = { first, second } } };
        window.Content = anchor;
        try
        {
            window.Show();
            flyout.ShowAt(anchor);
            Dispatcher.UIThread.RunJobs();
            first.Focus(NavigationMethod.Tab);
            window.HandleGamepad(GamepadButtons.Down);
            Assert.Same(second, window.FocusManager!.GetFocusedElement());
            window.HandleGamepad(GamepadButtons.Accept);
            Assert.True(second.IsChecked);
            window.HandleGamepad(GamepadButtons.Back);
            Assert.False(flyout.IsOpen);
        }
        finally { flyout.Hide(); window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Controller_focus_stays_inside_an_open_prompt_and_accept_toggles_checkboxes()
    {
        var window = new MainWindow();
        var behind = new CheckBox { Content = "Behind" };
        var prompt = new FeedListPromptView
        {
            DataContext = new ActionPromptViewModel("Name this list", "Create list",
                _ => Task.CompletedTask, () => { }, inputWatermark: "List name", initialText: "Weekend")
        };
        window.Content = new Grid { Children = { behind, prompt } };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 15; i++)
            {
                window.HandleGamepad(GamepadButtons.Next);
                Assert.Contains(prompt, ((Control)window.FocusManager!.GetFocusedElement()!).GetVisualAncestors());
            }
            for (var i = 0; i < 15; i++)
            {
                window.HandleGamepad(GamepadButtons.Previous);
                Assert.Contains(prompt, ((Control)window.FocusManager!.GetFocusedElement()!).GetVisualAncestors());
            }
            prompt.IsVisible = false;
            behind.Focus(NavigationMethod.Tab);
            window.HandleGamepad(GamepadButtons.Accept);
            Assert.True(behind.IsChecked);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Right_stick_scrolls_long_content_without_changing_focus()
    {
        var window = new MainWindow();
        var button = new Button { Content = "Action" };
        var scroll = new ScrollViewer
        {
            Height = 300,
            Content = new StackPanel { Children = { button, new Border { Height = 2000 } } }
        };
        window.Content = scroll;
        try
        {
            window.Show();
            button.Focus(NavigationMethod.Tab);
            window.HandleGamepad(GamepadButtons.ScrollDown);
            Assert.True(scroll.Offset.Y > 0);
            Assert.Same(button, window.FocusManager!.GetFocusedElement());
            window.HandleGamepad(GamepadButtons.ScrollUp);
            Assert.Equal(0, scroll.Offset.Y);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Shoulder_buttons_move_focus_and_accept_activates_the_focused_control()
    {
        var window = new MainWindow();
        var first = new Button { Content = "First" };
        var second = new Button { Content = "Second" };
        var clicks = 0;
        second.Click += (_, _) => clicks++;
        window.Content = new StackPanel { Children = { first, second } };
        try
        {
            window.Show();
            first.Focus(NavigationMethod.Tab);
            window.HandleGamepad(GamepadButtons.Next);
            Assert.Same(second, window.FocusManager!.GetFocusedElement());
            window.HandleGamepad(GamepadButtons.Accept);
            Assert.Equal(1, clicks);
            window.HandleGamepad(GamepadButtons.Previous);
            Assert.Same(first, window.FocusManager.GetFocusedElement());
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Direction_moves_focus_and_skips_disabled_controls()
    {
        var window = new MainWindow();
        var first = new Button { Content = "First" };
        var disabled = new Button { Content = "Unavailable", IsEnabled = false };
        var last = new Button { Content = "Last" };
        window.Content = new StackPanel { Children = { first, disabled, last } };
        try
        {
            window.Show();
            first.Focus(NavigationMethod.Tab);
            window.HandleGamepad(GamepadButtons.Down);
            Assert.Same(last, window.FocusManager!.GetFocusedElement());
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Controller_opens_keyboard_and_Escape_closes_only_text_entry()
    {
        var shell = PreviewData.Shell;
        shell.Library.Lightbox.CloseCommand.Execute(null);
        shell.Library.CloseDetailsCommand.Execute(null);
        shell.Library.Prompt?.CancelCommand.Execute(null);
        shell.Feed.ListPrompt?.CancelCommand.Execute(null);
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            shell.ShowLibraryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<TextBox>("SearchBox")!;
            search.Text = "Weekend";
            search.Focus(NavigationMethod.Tab);
            window.HandleGamepad(GamepadButtons.Accept);
            Assert.Single(window.GetVisualDescendants().OfType<GamepadKeyboardView>());
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.Empty(window.GetVisualDescendants().OfType<GamepadKeyboardView>());
            Assert.Equal("Weekend", search.Text);
            Assert.Same(search, window.FocusManager!.GetFocusedElement());
            window.HandleGamepad(GamepadButtons.Menu);
            Assert.True(window.IsFullscreen);
            window.HandleGamepad(GamepadButtons.Menu);
            Assert.False(window.IsFullscreen);
        }
        finally
        {
            window.FindControl<TextBox>("SearchBox")!.Text = string.Empty;
            window.ExitFromTray();
        }
    }
}
