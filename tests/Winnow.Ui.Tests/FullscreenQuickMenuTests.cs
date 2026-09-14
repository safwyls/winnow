using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenQuickMenuTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_start_keeps_one_menu_and_returns_to_the_original_focus(bool nested)
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Content = view, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            if (nested) context.ShowActions("Game actions", [new("First", () => { }), new("Second", () => { })]);
            Dispatcher.UIThread.RunJobs();
            view.CurrentPage.FocusInitial();
            if (nested) view.Handle(GamepadButtons.Down);
            var originalPage = view.CurrentPage;
            var originalFocus = window.FocusManager!.GetFocusedElement();
            Assert.NotNull(originalFocus);

            for (var cycle = 0; cycle < 2; cycle++)
            {
                view.Handle(GamepadButtons.Menu);
                Dispatcher.UIThread.RunJobs();
                var menu = view.CurrentPage;
                Assert.Equal("Quick menu", menu.Title);
                view.Handle(GamepadButtons.Down);
                var menuFocus = window.FocusManager.GetFocusedElement();
                for (var press = 0; press < 5; press++) view.Handle(GamepadButtons.Menu);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(menu, view.CurrentPage);
                Assert.Same(menuFocus, window.FocusManager.GetFocusedElement());
                if (cycle == 0) view.Handle(GamepadButtons.Back);
                else
                {
                    Assert.True(menu.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Resume")).Focus());
                    view.Handle(GamepadButtons.Accept); // Resume
                }
                Dispatcher.UIThread.RunJobs();
                Assert.Same(originalPage, view.CurrentPage);
                Assert.Same(originalFocus, window.FocusManager.GetFocusedElement());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Nested_menu_preserves_the_editor_and_quit_requires_confirmation()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Content = view, Width = 1920, Height = 1080 };
        var quit = false;
        view.QuitRequested += () => quit = true;
        window.Show();
        try
        {
            var draft = new FullscreenBrowseFiltersPage(context);
            context.Push(draft);
            void AskToQuit()
            {
                view.Handle(GamepadButtons.Menu); Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(view.CurrentPage.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Settings"));
                view.CurrentPage.FocusInitial();
                view.Handle(GamepadButtons.Down); view.Handle(GamepadButtons.Down); view.Handle(GamepadButtons.Accept);
                Dispatcher.UIThread.RunJobs();
                Assert.False(quit);
            }
            AskToQuit();
            view.CurrentPage.FocusInitial(); view.Handle(GamepadButtons.Accept);
            Assert.Same(draft, view.CurrentPage);
            AskToQuit();
            view.CurrentPage.FocusInitial(); view.Handle(GamepadButtons.Down); view.Handle(GamepadButtons.Accept);
            Assert.True(quit);
        }
        finally { window.Close(); }
    }
}
