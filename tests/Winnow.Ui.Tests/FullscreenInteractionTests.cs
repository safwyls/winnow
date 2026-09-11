using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenInteractionTests
{
    [AvaloniaFact]
    public void Hover_does_not_underline_actions_but_selected_sections_keep_a_neutral_underline()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = television };
        window.Show();
        try
        {
            television.Handle(GamepadButtons.Previous);
            Dispatcher.UIThread.RunJobs();
            var actions = television.CurrentPage.GetVisualDescendants().OfType<Button>().ToArray();
            var margins = actions.Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Screen margins");
            var point = margins.TranslatePoint(new Point(margins.Bounds.Width / 2, margins.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            margins.Focus();
            television.Handle(GamepadButtons.Down);
            Dispatcher.UIThread.RunJobs();
            Assert.NotSame(margins, window.FocusManager!.GetFocusedElement());
            Assert.Equal(0, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(margins.BorderBrush).Color.A);
            foreach (var current in actions.Where(b => b.Classes.Contains("current") && !b.IsFocused))
                Assert.NotEqual(0, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(current.BorderBrush).Color.A);
            var focused = Assert.IsAssignableFrom<Button>(window.FocusManager.GetFocusedElement());
            Assert.NotEqual(0, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(focused.BorderBrush).Color.A);
            Assert.All(actions.Where(b => b.Classes.Contains("current") && !b.IsFocused), current =>
                Assert.NotEqual(((Avalonia.Media.ISolidColorBrush)focused.BorderBrush!).Color,
                    ((Avalonia.Media.ISolidColorBrush)current.BorderBrush!).Color));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Main_navigation_stays_centered_with_varying_controller_status_and_clock()
    {
        var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1280, Height = 720, Content = television };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var header = television.GetVisualDescendants().OfType<Grid>().Single(c => c.Name == "FullscreenHeader");
            var nav = television.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "FullscreenRootNavigation");
            var status = television.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "FullscreenControllerStatus");
            var clock = television.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "FullscreenClock");
            foreach (var text in new[] { "", "Controller disconnected", "Controller connected · Battery 100%" })
            {
                status.Text = text;
                clock.Text = text.Length == 0 ? "1:01" : "11:59 PM";
                Dispatcher.UIThread.RunJobs();
                Assert.InRange(Math.Abs(nav.Bounds.Center.X - header.Bounds.Width / 2), 0, .01);
                var clockEnd = clock.TranslatePoint(new Point(clock.Bounds.Width, 0), header)!.Value.X;
                Assert.InRange(clockEnd, header.Bounds.Width - 1, header.Bounds.Width + 1);
                var statusStart = status.TranslatePoint(default, header)!.Value.X;
                Assert.True(statusStart >= nav.Bounds.Right);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Details_backdrop_fills_canvas_and_back_header_restores_its_origin()
    {
        var library = new App.ViewModels.LibraryViewModel(new PreviewLibraryQueryRepository(),
            new PreviewOwnershipRepository(), new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        await library.LoadCommand.ExecuteAsync(null);
        var context = new FullscreenContext(library, new App.ViewModels.FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = television };
        window.Show();
        try
        {
            television.Handle(GamepadButtons.Next);
            Dispatcher.UIThread.RunJobs();
            await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);
            context.Push(new FullscreenDetailsPage(context, library.Details!));
            Dispatcher.UIThread.RunJobs();
            var nav = television.GetVisualDescendants().OfType<StackPanel>().Single(c => c.Name == "FullscreenRootNavigation");
            var back = television.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "FullscreenBack");
            var art = television.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "FullscreenPageBackdrop");
            Assert.False(nav.IsVisible);
            Assert.True(back.IsVisible);
            Assert.Equal("Back to Library", Avalonia.Automation.AutomationProperties.GetName(back));
            Assert.Same(television.CurrentPage.Backdrop, art.Content);
            Assert.NotNull(art.Content);
            Assert.Equal(new Size(1920, 1080), art.Bounds.Size);
            var action = Assert.IsAssignableFrom<Button>(window.FocusManager!.GetFocusedElement());
            Assert.Contains(television.CurrentPage, action.GetVisualAncestors());
            television.Handle(GamepadButtons.Back);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Library", television.CurrentPage.Title);
            Assert.True(nav.IsVisible);
            Assert.False(back.IsVisible);
            Assert.Same(television.CurrentPage.Backdrop, art.Content);
            Assert.NotNull(art.Content);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Controller_hides_cursor_until_mouse_moves_and_exit_restores_it()
    {
        var window = new MainWindow { DataContext = PreviewData.Shell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var search = window.FindControl<TextBox>("SearchBox")!;
            var searchPoint = search.TranslatePoint(new Point(search.Bounds.Width / 2, search.Bounds.Height / 2), window)!.Value;
            window.MouseMove(searchPoint);
            var hovered = Assert.IsAssignableFrom<InputElement>(window.GetValue(TopLevel.PointerOverElementProperty));
            using var originalCursor = new Cursor(StandardCursorType.Ibeam);
            hovered.Cursor = originalCursor;
            window.HandleGamepad(GamepadButtons.Down);
            Assert.NotSame(originalCursor, hovered.Cursor);
            window.MouseMove(searchPoint + new Vector(1, 0));
            Assert.Same(originalCursor, hovered.Cursor);
            window.ToggleFullscreen();
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(new Point(20, 20));
            var mouseCursor = window.Cursor;
            window.HandleGamepad(GamepadButtons.Down);
            Assert.NotEqual(mouseCursor, window.Cursor);
            window.MouseMove(new Point(30, 20));
            Assert.Equal(mouseCursor, window.Cursor);
            window.HandleGamepad(GamepadButtons.Down);
            window.ToggleFullscreen();
            Assert.Equal(mouseCursor, window.Cursor);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Ultrawide_fit_expands_canvas_without_stretching_type_and_restores_reference_layout()
    {
        var library = new App.ViewModels.LibraryViewModel(new PreviewLibraryQueryRepository(),
            new PreviewOwnershipRepository(), new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        var context = new FullscreenContext(library, new App.ViewModels.FeedViewModel(new PreviewFeedService(), library), PreviewData.Shell);
        using var television = new FullscreenView(context);
        var window = new Window { Width = 2560, Height = 1080, Content = television };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var canvas = Assert.IsType<Grid>(Assert.IsType<Viewbox>(television.Content).Child);
            Assert.Equal(1920, canvas.Width);
            context.SetFitUltrawide(true);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(canvas.Width, 2559, 2561);
            Assert.Equal(1080, canvas.Height);
            Capture(window, "ultrawide");
            window.Width = 3440; window.Height = 1440;
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(canvas.Width, 2579, 2581);
            context.SetFitUltrawide(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1920, canvas.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Fullscreen_hosts_a_separate_interface_and_search_keyboard_at_minimum_window_size()
    {
        var window = new MainWindow { DataContext = PreviewData.Shell, Width = 1200, Height = 688 };
        try
        {
            window.Show();
            window.ToggleFullscreen();
            Dispatcher.UIThread.RunJobs();
            var television = Assert.IsType<FullscreenView>(window.FindControl<ContentControl>("TvHost")!.Content);
            Assert.False(window.FindControl<TextBox>("SearchBox")!.IsEffectivelyVisible);
            Assert.DoesNotContain(television.GetVisualDescendants(), control => control is GameTileView or FeedCardView);
            window.HandleGamepad(GamepadButtons.Search);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenBrowseSearchPage>(television.CurrentPage);
            window.HandleGamepad(GamepadButtons.Keyboard);
            Dispatcher.UIThread.RunJobs();
            var keyboard = Assert.Single(television.GetVisualDescendants().OfType<GamepadKeyboardView>());
            var position = keyboard.TranslatePoint(default, window)!.Value;
            Assert.InRange(position.X, -1, window.ClientSize.Width);
            Assert.InRange(position.Y, -1, window.ClientSize.Height);
            window.HandleGamepad(GamepadButtons.Back);
            Assert.Empty(television.GetVisualDescendants().OfType<GamepadKeyboardView>());
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Fullscreen_restores_each_window_state_and_desktop_visibility()
    {
        var window = new MainWindow { DataContext = PreviewData.Shell };
        try
        {
            window.Show();
            foreach (var state in new[] { WindowState.Normal, WindowState.Maximized })
            {
                window.WindowState = state;
                window.ToggleFullscreen();
                Assert.Equal(WindowState.FullScreen, window.WindowState);
                Assert.False(window.FindControl<Border>("TitleBar")!.IsEffectivelyVisible);
                Assert.True(window.FindControl<ContentControl>("TvHost")!.IsVisible);
                window.ToggleFullscreen();
                Assert.Equal(state, window.WindowState);
                Assert.True(window.FindControl<Border>("TitleBar")!.IsEffectivelyVisible);
                Assert.False(window.FindControl<ContentControl>("TvHost")!.IsVisible);
            }
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Entry_button_and_F11_toggle_without_a_library_context()
    {
        var window = new MainWindow();
        try
        {
            window.Show();
            var entry = window.FindControl<Button>("EnterFullscreenButton")!;
            var settings = window.FindControl<Button>("SettingsButton")!;
            Assert.IsType<Avalonia.Controls.Shapes.Path>(entry.Content);
            Assert.Equal("Fullscreen", Avalonia.Automation.AutomationProperties.GetName(entry));
            Assert.Equal(settings.Parent, entry.Parent);
            Assert.Equal(settings.Bounds.Center.Y, entry.Bounds.Center.Y, 1);
            Assert.True(entry.Bounds.Right <= settings.Bounds.Left);
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.IsFullscreen);
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Assert.False(window.IsFullscreen);
            window.KeyPressQwerty(PhysicalKey.F11, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.F11, RawInputModifiers.None);
            Assert.True(window.IsFullscreen);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void Controller_quick_menu_and_browse_navigation_leave_desktop_state_untouched()
    {
        var shell = PreviewData.Shell;
        var originalSearch = shell.Library.SearchText;
        var originalSort = shell.Library.Sort;
        var originalBucket = shell.Library.SelectedBucket;
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            Capture(window, "desktop-fullscreen-entry");
            window.ToggleFullscreen();
            Dispatcher.UIThread.RunJobs();
            var television = Assert.IsType<FullscreenView>(window.FindControl<ContentControl>("TvHost")!.Content);
            window.HandleGamepad(GamepadButtons.Next);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Library", television.CurrentPage.Title);
            window.HandleGamepad(GamepadButtons.Keyboard);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenBrowseFiltersPage>(television.CurrentPage);
            window.HandleGamepad(GamepadButtons.Back);
            window.HandleGamepad(GamepadButtons.Menu);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Quick menu", television.CurrentPage.Title);
            Assert.True(window.IsFullscreen);
            window.HandleGamepad(GamepadButtons.Back);
            Assert.Equal("Library", television.CurrentPage.Title);
            Assert.Equal(originalSearch, shell.Library.SearchText);
            Assert.Equal(originalSort, shell.Library.Sort);
            Assert.Same(originalBucket, shell.Library.SelectedBucket);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public void External_restore_hides_tv_and_controller_status_omits_unknown_battery()
    {
        var window = new MainWindow { DataContext = PreviewData.Shell };
        try
        {
            window.Show();
            window.ToggleFullscreen();
            Dispatcher.UIThread.RunJobs();
            var television = Assert.IsType<FullscreenView>(window.FindControl<ContentControl>("TvHost")!.Content);
            window.UpdateGamepadStatus("Controller connected");
            Assert.Contains(television.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Controller connected");
            window.UpdateGamepadStatus(null);
            Assert.Contains(television.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Controller disconnected");
            window.WindowState = WindowState.Normal;
            Assert.False(window.FindControl<ContentControl>("TvHost")!.IsVisible);
            Assert.True(window.FindControl<Border>("TitleBar")!.IsEffectivelyVisible);
        }
        finally { window.ExitFromTray(); }
    }

    [AvaloniaFact]
    public async Task Main_screens_render_at_reference_and_smaller_size()
    {
        var library = new App.ViewModels.LibraryViewModel(new PreviewLibraryQueryRepository(),
            new PreviewOwnershipRepository(), new PreviewReleaseRepository(), new PreviewWorkRepository(), new PreviewUpdateEventRepository());
        await library.LoadCommand.ExecuteAsync(null);
        var feed = new App.ViewModels.FeedViewModel(new PreviewFeedService(), library);
        await feed.LoadCommand.ExecuteAsync(null);
        var context = new FullscreenContext(library, feed, PreviewData.Shell);
        using var television = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = television };
        window.Show();
        try
        {
            foreach (var title in new[] { "For you", "Library", "Activity", "Settings" })
            {
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(title, television.CurrentPage.Title);
                var focused = Assert.IsType<Button>(window.FocusManager!.GetFocusedElement());
                Assert.Contains(television.CurrentPage, focused.GetVisualAncestors());
                if (title is "For you" or "Library")
                {
                    var cover = Assert.Single(focused.GetVisualDescendants().OfType<FullscreenCover>());
                    Assert.True(Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(cover.BorderBrush).Color.A > 0);
                }
                Capture(window, title.Replace(' ', '-').ToLowerInvariant());
                television.Handle(GamepadButtons.Next);
            }
            await library.OpenDetailsCommand.ExecuteAsync(library.VisibleTiles[0]);
            context.Push(new FullscreenDetailsPage(context, library.Details!));
            Dispatcher.UIThread.RunJobs();
            Capture(window, "details");
            television.Back();
            television.Handle(GamepadButtons.Search);
            television.Handle(GamepadButtons.Keyboard);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "keyboard");
            television.Back();
            television.Back();
            window.Width = 1280; window.Height = 720;
            Dispatcher.UIThread.RunJobs();
            Capture(window, "home-1280");
            television.Handle(GamepadButtons.Next);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "library-1280");
            context.TextScale = 1.4;
            Dispatcher.UIThread.RunJobs();
            Capture(window, "library-large-text-1280");
            var enlarged = television.CurrentPage.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == library.VisibleTiles[0].Title);
            var fontSize = enlarged.FontSize;
            context.TextScale = 1.4;
            Assert.Equal(fontSize, enlarged.FontSize);
            Assert.InRange(fontSize, 24, 34);
            AssertFocusFits(window);
            television.Handle(GamepadButtons.Next);
            television.Handle(GamepadButtons.Next);
            Dispatcher.UIThread.RunJobs();
            Capture(window, "settings-large-text-1280");
            AssertFocusFits(window);
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, $"fullscreen-{name}.png"));
    }

    private static void AssertFocusFits(Window window)
    {
        var focused = Assert.IsAssignableFrom<Control>(window.FocusManager!.GetFocusedElement());
        var topLeft = focused.TranslatePoint(default, window)!.Value;
        var bottomRight = focused.TranslatePoint(new Point(focused.Bounds.Width, focused.Bounds.Height), window)!.Value;
        Assert.InRange(topLeft.X, 0, window.ClientSize.Width);
        Assert.InRange(topLeft.Y, 0, window.ClientSize.Height);
        Assert.InRange(bottomRight.X, 0, window.ClientSize.Width + 1);
        Assert.InRange(bottomRight.Y, 0, window.ClientSize.Height + 1);
    }
}
