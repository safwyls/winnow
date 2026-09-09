using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenSettingsTests
{
    [AvaloniaFact]
    public void Appearance_adjustment_stays_on_its_row_and_leaves_desktop_preferences_alone()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var desktopDimming = context.Shared.Display.DimDormantCovers;
        var page = new FullscreenSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); page.FocusInitial();
            var first = window.FocusManager!.GetFocusedElement();
            Assert.True(page.Handle(GamepadButtons.Right));
            Assert.Equal(1.1, context.TextScale);
            Assert.Same(first, window.FocusManager.GetFocusedElement());
            context.TextScale = 1.4;
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("140", StringComparison.Ordinal) == true);
            Assert.Same(first, window.FocusManager.GetFocusedElement());
            for (var i = 0; i < 20; i++) page.Handle(GamepadButtons.Right);
            Assert.Equal(1.4, context.TextScale);
            for (var i = 0; i < 20; i++) page.Handle(GamepadButtons.Left);
            Assert.Equal(1, context.TextScale);
            Assert.Equal(desktopDimming, context.Shared.Display.DimDormantCovers);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public void Reset_opens_confirmation_without_mutating_preferences()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = 1.3, SafeMarginPercent = 8 };
        FullscreenPage? confirmation = null;
        context.PageRequested += page => confirmation = page;
        var settings = new FullscreenSettingsPage(context);
        Assert.True(settings.Handle(GamepadButtons.Keyboard));
        Assert.NotNull(confirmation);
        Assert.Equal(1.3, context.TextScale);
        Assert.Equal(8, context.SafeMarginPercent);
        confirmation.Dispose(); settings.Dispose();
    }

    [AvaloniaFact]
    public async Task Shared_visibility_change_refreshes_the_independent_fullscreen_library()
    {
        var library = new LibraryViewModel(new PreviewLibraryQueryRepository(), new PreviewOwnershipRepository(), new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var feed = new FeedViewModel(new PreviewFeedService(), library);
        var context = new FullscreenContext(library, feed, PreviewData.Shell);
        var original = context.Shared.Display.ShowNonGameEntries;
        var page = new FullscreenSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Library")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var label = page.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Text == "Non-game entries");
            var toggle = label.GetVisualAncestors().OfType<Button>().First();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.PendingLibraryRefresh;
            Assert.Equal(!original, context.Shared.Display.ShowNonGameEntries);
            Assert.Equal(!original, library.ShowNonGameEntries);
            Assert.NotSame(context.Shared.Library, library);
        }
        finally { context.Shared.Display.ShowNonGameEntries = original; await context.Shared.Display.PendingSave; window.Close(); page.Dispose(); }
    }
}
