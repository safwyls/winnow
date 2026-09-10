using Avalonia;
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
    public void Dormancy_toggle_tracks_desktop_changes_and_controller_changes_update_desktop()
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var original = context.Shared.Display.DimDormantCovers;
        using var page = new FullscreenSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var toggle = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Dim dormant covers");
            context.Shared.Display.DimDormantCovers = false;
            Assert.Equal("Off", AutomationProperties.GetItemStatus(toggle));
            toggle.Focus();
            page.Handle(GamepadButtons.Right);
            Assert.True(context.Shared.Display.DimDormantCovers);
            Assert.True(context.Library.Ramp.DimsDormantCovers);
            Assert.Equal("On", AutomationProperties.GetItemStatus(toggle));
            page.Handle(GamepadButtons.Left);
            Assert.False(context.Shared.Display.DimDormantCovers);
            Assert.Equal("Off", AutomationProperties.GetItemStatus(toggle));
            FullscreenPage? confirmation = null;
            context.PageRequested += requested => confirmation = requested;
            page.Handle(GamepadButtons.Keyboard);
            Assert.NotNull(confirmation);
            window.Content = confirmation;
            Dispatcher.UIThread.RunJobs();
            confirmation.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetName(b) == "Reset fullscreen appearance")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(context.Shared.Display.DimDormantCovers);
            confirmation.Dispose();
        }
        finally { context.Shared.Display.DimDormantCovers = original; window.Close(); }
    }

    [AvaloniaFact]
    public void Fullscreen_startup_setting_is_shared_with_desktop_application_toggle()
    {
        var settings = PreviewData.ApplicationSettings;
        settings.StartInFullscreen = false;
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var page = new FullscreenSettingsPage(context);
        var desktop = new Winnow.App.Views.ApplicationSettingsView { DataContext = settings };
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        var desktopWindow = new Window { Content = desktop };
        try
        {
            window.Show(); desktopWindow.Show(); Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 4; i++) page.Handle(GamepadButtons.PageNext);
            Dispatcher.UIThread.RunJobs();
            var tvToggle = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Start in fullscreen");
            tvToggle.Focus(); page.Handle(GamepadButtons.Accept);
            Assert.True(settings.StartInFullscreen);
            var desktopToggle = desktop.GetVisualDescendants().OfType<ToggleSwitch>().Single(b => AutomationProperties.GetName(b) == "Start in fullscreen");
            Assert.True(desktopToggle.IsChecked);
            desktopToggle.IsChecked = false;
            Assert.False(settings.StartInFullscreen);
            // Reopening Application reads the same shared state.
            page.Handle(GamepadButtons.PagePrevious); page.Handle(GamepadButtons.PageNext);
            Dispatcher.UIThread.RunJobs();
            tvToggle = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Start in fullscreen");
            Assert.Equal("Off", AutomationProperties.GetItemStatus(tvToggle));
        }
        finally { settings.StartInFullscreen = false; desktopWindow.Close(); window.Close(); page.Dispose(); }
    }

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

    [AvaloniaTheory]
    [InlineData(1920, 1080, 1, 5)]
    [InlineData(1280, 720, .7, 5)]
    [InlineData(1280, 720, 1.4, 5)]
    [InlineData(1280, 720, 1.4, 10)]
    public void Controller_guide_fits_one_screen_at_16_by_9(double width, double height, double textScale, double margins)
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = textScale, SafeMarginPercent = margins };
        using var television = new FullscreenView(context);
        var window = new Window { Width = width, Height = height, Content = television };
        try
        {
            window.Show();
            // Mount the guide before draining frames; this fixture does not exercise the feed.
            for (var i = 0; i < 3; i++) television.Handle(GamepadButtons.Next);
            var settings = Assert.IsType<FullscreenSettingsPage>(television.CurrentPage);
            settings.Handle(GamepadButtons.PageNext);
            Dispatcher.UIThread.RunJobs();
            var diagram = Assert.Single(settings.GetVisualDescendants().OfType<Canvas>(), c => c.Name == "FullscreenControllerDiagram");
            Assert.Equal(Avalonia.Media.Stretch.Uniform, Assert.IsType<Viewbox>(diagram.Parent).Stretch);
            var outline = Assert.IsType<Avalonia.Controls.Shapes.Path>(diagram.Children[0]);
            Assert.InRange(outline.Data!.Bounds.Width / outline.Data.Bounds.Height, 1.4, 1.5);
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Move");
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Select");
            Assert.Empty(settings.GetVisualDescendants().OfType<ScrollViewer>());
            foreach (var text in settings.GetVisualDescendants().OfType<TextBlock>())
            {
                var position = text.TranslatePoint(default, settings)!.Value;
                Assert.True(position.Y >= 0 && position.Y + text.Bounds.Height <= settings.Bounds.Height + 1,
                    $"{text.Text}: bottom {position.Y + text.Bounds.Height}, page {settings.Bounds.Height}");
            }
            Capture(window, margins > 5 ? "controller-large-text-safe-area" : textScale > 1 ? "controller-large-text" : "controller");
            settings.Handle(GamepadButtons.PageNext); Dispatcher.UIThread.RunJobs();
            Assert.Contains(settings.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == "Journal after playing");
            settings.Handle(GamepadButtons.PagePrevious); Dispatcher.UIThread.RunJobs();
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Tabs & shelves");
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
            Assert.Equal(.7, context.TextScale);
            var increase = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Increase text size");
            var decrease = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Decrease text size");
            void Click(Button button)
            {
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            Click(increase);
            Assert.Equal(.8, context.TextScale);
            Click(decrease);
            Assert.Equal(.7, context.TextScale);
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
