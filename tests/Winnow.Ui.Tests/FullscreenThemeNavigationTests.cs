using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenThemeNavigationTests
{
    [AvaloniaTheory]
    [InlineData(1280, 720)]
    [InlineData(3840, 2160)]
    public async Task Returning_home_after_each_theme_change_keeps_loaded_shelves_navigable(double width, double height)
    {
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(),
            new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        await library.LoadCommand.ExecuteAsync(null);
        var feed = new FeedViewModel(new PreviewFeedService(), library);
        await feed.LoadCommand.ExecuteAsync(null);
        using var context = new FullscreenContext(library, feed, PreviewData.Shell) { TextScale = 1.4, SafeMarginPercent = 2 };
        var originalTheme = context.ThemeId;
        using var television = new FullscreenView(context);
        var window = new Window { Width = width, Height = height, Content = television };
        window.Show();
        try
        {
            foreach (var theme in context.Themes)
            {
                television.Handle(GamepadButtons.Previous);
                Assert.Equal("Settings", television.CurrentPage.Title);
                context.ThemeId = theme.Id;
                television.Handle(GamepadButtons.Next);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("For you", television.CurrentPage.Title);
                Assert.NotEmpty(television.CurrentPage.GetVisualDescendants().OfType<FullscreenCover>());
                var first = window.FocusManager!.GetFocusedElement();
                television.Handle(GamepadButtons.Right);
                Dispatcher.UIThread.RunJobs();
                Assert.NotSame(first, window.FocusManager.GetFocusedElement());
                television.Handle(GamepadButtons.Left);
                Assert.Same(first, window.FocusManager.GetFocusedElement());
            }
        }
        finally { window.Close(); context.ThemeId = originalTheme; }
    }
}
