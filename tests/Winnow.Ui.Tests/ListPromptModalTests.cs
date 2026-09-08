using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ListPromptModalTests
{
    [AvaloniaFact]
    public void Many_long_lists_scroll_without_pushing_actions_out_of_a_short_window()
    {
        var choices = Enumerable.Range(1, 40).Select(index => new GameListViewModel(
            GameList.Manual($"List {index}: games for a very long weekend with friends and family") with { Id = index })).ToArray();
        using var fixture = new PromptFixture(new ActionPromptViewModel(
            "Add a game with a long title to a list", "Create list", _ => Task.CompletedTask, () => { },
            inputWatermark: "New list name", choices: choices, choose: _ => Task.CompletedTask));
        var scroll = fixture.View.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(view => view.Content is ItemsControl);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.True(scroll.Viewport.Height > 0);

        var listButtons = scroll.GetVisualDescendants().OfType<Button>()
            .Where(button => button.DataContext is GameListViewModel).ToArray();
        Assert.Equal(choices.Length, listButtons.Length);
        foreach (var button in listButtons)
            Assert.True(button.Bounds.Width <= scroll.Viewport.Width);
        Assert.True(listButtons[^1].Focus(NavigationMethod.Tab));
        Flush();
        Assert.True(scroll.Offset.Y > 0);
        var lastPosition = listButtons[^1].TranslatePoint(default, scroll)!.Value;
        Assert.InRange(lastPosition.Y, -1, scroll.Bounds.Height);
        Assert.True(lastPosition.Y + listButtons[^1].Bounds.Height <= scroll.Bounds.Height + 1);
        AssertInsideWindow(fixture.View.FindControl<TextBox>("PromptInput")!, fixture.Window);
        AssertInsideWindow(fixture.View.FindControl<Button>("CancelButton")!, fixture.Window);
        AssertInsideWindow(fixture.View.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, "Create list")), fixture.Window);
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = fixture.Window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(directory, "winnow-161-modal.png"));
        }
    }

    [AvaloniaFact]
    public void Escape_cancels_from_the_name_field_without_confirming()
    {
        var cancelled = 0;
        var confirmed = 0;
        using var fixture = new PromptFixture(new ActionPromptViewModel(
            "Name this list", "Create list", _ => { confirmed++; return Task.CompletedTask; },
            () => cancelled++, inputWatermark: "List name", initialText: "Weekend"));
        Assert.Same(fixture.View.FindControl<TextBox>("PromptInput"), fixture.Window.FocusManager!.GetFocusedElement());
        Press(fixture.Window, PhysicalKey.Escape);
        Assert.Equal(1, cancelled);
        Assert.Equal(0, confirmed);
    }

    [AvaloniaFact]
    public void Tab_cycles_inside_the_modal_in_both_directions()
    {
        using var fixture = new PromptFixture(new ActionPromptViewModel(
            "Name this list", "Create list", _ => Task.CompletedTask, () => { },
            inputWatermark: "List name", initialText: "Weekend"));
        var input = fixture.View.FindControl<TextBox>("PromptInput")!;
        var visited = new HashSet<IInputElement>();
        var returnedToInput = false;
        for (var step = 0; step < 12; step++)
        {
            Press(fixture.Window, PhysicalKey.Tab);
            var focus = fixture.Window.FocusManager!.GetFocusedElement();
            var control = Assert.IsAssignableFrom<Control>(focus);
            Assert.Contains(fixture.View, control.GetVisualAncestors());
            visited.Add(control);
            if (ReferenceEquals(control, input)) { returnedToInput = true; break; }
        }
        Assert.True(returnedToInput);
        Assert.True(visited.Count >= 3);
        Press(fixture.Window, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.NotSame(input, fixture.Window.FocusManager!.GetFocusedElement());
        Press(fixture.Window, PhysicalKey.Tab);
        Assert.Same(input, fixture.Window.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public void Delete_without_a_name_field_starts_on_cancel()
    {
        var cancelled = 0;
        var deleted = 0;
        using var fixture = new PromptFixture(new ActionPromptViewModel(
            "Delete Weekend? The titles stay in your library.", "Delete list",
            _ => { deleted++; return Task.CompletedTask; }, () => cancelled++, isDestructive: true));
        Assert.Same(fixture.View.FindControl<Button>("CancelButton"), fixture.Window.FocusManager!.GetFocusedElement());
        Press(fixture.Window, PhysicalKey.Enter);
        Assert.Equal(1, cancelled);
        Assert.Equal(0, deleted);
    }

    private static void AssertInsideWindow(Control control, Window window)
    {
        var position = control.TranslatePoint(default, window)!.Value;
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.InRange(position.X, 0, window.ClientSize.Width);
        Assert.InRange(position.Y, 0, window.ClientSize.Height);
        Assert.True(position.X + control.Bounds.Width <= window.ClientSize.Width);
        Assert.True(position.Y + control.Bounds.Height <= window.ClientSize.Height);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        Flush();
    }

    private static void Flush() => Dispatcher.UIThread.RunJobs();

    private sealed class PromptFixture : IDisposable
    {
        public FeedListPromptView View { get; } = new();
        public Window Window { get; }

        public PromptFixture(ActionPromptViewModel prompt)
        {
            var behind = new Button { Content = "Library action" };
            Window = new Window { Width = 800, Height = 600, Content = new Grid { Children = { behind, View } } };
            Window.Show();
            behind.Focus(NavigationMethod.Tab);
            View.DataContext = prompt;
            Flush();
        }

        public void Dispose() => Window.Close();
    }
}
