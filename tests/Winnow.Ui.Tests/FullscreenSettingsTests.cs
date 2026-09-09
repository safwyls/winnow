using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Automation;
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
    public void Switches_report_state_and_direction_sets_a_value_without_repeated_toggling()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var page = new FullscreenSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var toggle = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Fit ultrawide displays");
            Assert.Contains("tv-toggle", toggle.Classes);
            toggle.Focus();
            page.Handle(GamepadButtons.Right); page.Handle(GamepadButtons.Right);
            Assert.True(context.FitUltrawide);
            Assert.Equal("On", AutomationProperties.GetItemStatus(toggle));
            Assert.Same(toggle, window.FocusManager!.GetFocusedElement());
            page.Handle(GamepadButtons.Left);
            Assert.False(context.FitUltrawide);
            Assert.Equal("Off", AutomationProperties.GetItemStatus(toggle));
            page.Handle(GamepadButtons.Accept);
            Assert.True(context.FitUltrawide);
        }
        finally { window.Close(); page.Dispose(); }
    }

    [AvaloniaFact]
    public void Controller_diagram_keeps_text_and_scroll_reachable_at_large_text()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = 1.4 };
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1280, Height = 720, Content = television };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 3; i++) television.Handle(GamepadButtons.Next);
            Dispatcher.UIThread.RunJobs();
            var settings = Assert.IsType<FullscreenSettingsPage>(television.CurrentPage);
            settings.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Controller")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(settings.GetVisualDescendants().OfType<Canvas>(), c => c.Width == 800);
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "D-pad or left stick.");
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Open a game or choice.");
            Capture(window, "controller-large-text");
            settings.Handle(GamepadButtons.ScrollDown); Dispatcher.UIThread.RunJobs();
            var scrolling = settings.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scrolling.Extent.Height <= scrolling.Viewport.Height || scrolling.Offset.Y > 0);
            Capture(window, "controller-large-text-scrolled");
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(directory, $"fullscreen-{name}.png"));
    }

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
