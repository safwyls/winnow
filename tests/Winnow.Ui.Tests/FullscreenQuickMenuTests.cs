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
