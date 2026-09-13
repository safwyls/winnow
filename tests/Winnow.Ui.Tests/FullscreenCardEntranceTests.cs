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

public sealed class FullscreenCardEntranceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entrance_preserves_focus_and_finishes(bool reducedMotion)
    {
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        feed.Shelves.Add(new FeedShelfViewModel("entrance", "Ready to play", "",
            Enumerable.Range(0, 6).Select(_ => new FeedCardViewModel(PreviewData.Tile, "An update arrived."))));
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell)
            { ReducedMotion = reducedMotion };
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var covers = view.CurrentPage.GetVisualDescendants().OfType<FullscreenCover>().ToArray();
            Assert.NotEmpty(covers);
            view.Handle(GamepadButtons.Right);
            Assert.NotNull(window.FocusManager!.GetFocusedElement());
            if (reducedMotion) Assert.All(covers, cover => Assert.Equal(1, cover.Child!.Opacity));
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            Assert.All(covers, cover => Assert.Equal(1, cover.Child!.Opacity));
            if (Environment.GetEnvironmentVariable("WINNOW_SCREENSHOT_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"fullscreen-entrance-{reducedMotion}.png"));
            }
        }
        finally { window.Close(); context.ReducedMotion = false; }
    }

    [AvaloniaFact]
    public async Task Changing_motion_or_detaching_restores_card_opacity()
    {
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library);
        using var context = new FullscreenContext(PreviewData.Library, feed, PreviewData.Shell);
        var cover = new FullscreenCover(PreviewData.Tile);
        var title = new TextBlock { Text = "Game" };
        FullscreenCardEntrance.Attach(context, cover, title, 5);
        var content = new StackPanel { Children = { cover, title } };
        var window = new Window { Width = 400, Height = 600, Content = content };
        try
        {
            window.Show();
            Assert.Equal(0, cover.Child!.Opacity);
            context.ReducedMotion = true;
            Assert.Equal(1, cover.Child.Opacity);
            Assert.Equal(1, title.Opacity);
            window.Content = null;
            context.ReducedMotion = false;
            window.Content = content;
            Assert.Equal(0, cover.Child.Opacity);
            window.Content = null;
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, cover.Child.Opacity);
            Assert.Equal(1, title.Opacity);
        }
        finally { window.Close(); context.ReducedMotion = false; }
    }
}
