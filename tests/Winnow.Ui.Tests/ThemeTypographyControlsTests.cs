using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ThemeTypographyControlsTests
{
    [AvaloniaFact]
    public void Desktop_font_pickers_and_size_follow_the_selected_theme()
    {
        var service = new ThemeService();
        var first = service.Theme;
        var view = new AppearanceView { DataContext = new AppearanceViewModel(service) };
        var window = new Window { Width = 1000, Height = 720, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var heading = view.FindControl<ComboBox>("HeadingFontPicker")!;
            var body = view.FindControl<ComboBox>("InterfaceFontPicker")!;
            var data = view.FindControl<ComboBox>("DataFontPicker")!;
            var size = view.FindControl<Slider>("ThemeTextSizeSlider")!;
            heading.SelectedItem = "IBM Plex Mono";
            body.SelectedItem = "Bricolage Grotesque";
            data.SelectedItem = "Plus Jakarta Sans";
            size.Value = 115;
            Dispatcher.UIThread.RunJobs();
            var expected = new ThemeTypography
            {
                HeadingFont = "IBM Plex Mono", InterfaceFont = "Bricolage Grotesque",
                DataFont = "Plus Jakarta Sans", SizePercent = 115,
            };
            Assert.Equal(expected, service.Typography);
            service.SelectTheme(WinnowThemes.All.First(t => t.Id != first.Id));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(service.Theme.Typography.HeadingFont, heading.SelectedItem);
            Assert.Equal(service.Theme.Typography.SizePercent, size.Value);
            service.SelectTheme(first);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, service.Typography);
            Assert.Equal(expected.InterfaceFont, body.SelectedItem);
            Assert.Equal(115, size.Value);
            service.SetTypography(expected with { HeadingFont = "ibm plex mono" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("IBM Plex Mono", heading.SelectedItem);
            Assert.Equal("ibm plex mono", service.Typography.HeadingFont);
        }
        finally { window.Close(); service.ResetTypography(); }
    }

    [AvaloniaFact]
    public void Desktop_maximum_size_keeps_typography_controls_reachable_and_reset_restores_authored_values()
    {
        var service = new ThemeService();
        var authored = ThemeTypography.Default with { HeadingFont = "IBM Plex Mono", SizePercent = 105 };
        service.SelectTheme(service.Theme with { Typography = authored });
        var view = new AppearanceView { DataContext = new AppearanceViewModel(service) };
        var window = new Window { Width = 1000, Height = 720, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            view.FindControl<Slider>("ThemeTextSizeSlider")!.Value = 120;
            Dispatcher.UIThread.RunJobs();
            var scroll = view.FindControl<ScrollViewer>("ScreenScroll")!;
            foreach (var name in new[] { "HeadingFontPicker", "InterfaceFontPicker", "DataFontPicker", "ThemeTextSizeSlider" })
            {
                var control = view.FindControl<Control>(name)!;
                control.Focus(); control.BringIntoView(); Dispatcher.UIThread.RunJobs();
                var point = control.TranslatePoint(default, scroll)!.Value;
                Assert.True(point.X >= -1 && point.X + control.Bounds.Width <= scroll.Bounds.Width + 1, name);
                Assert.True(point.Y >= -1 && point.Y + control.Bounds.Height <= scroll.Bounds.Height + 1, name);
            }
            Capture(window, "desktop-theme-typography-120");
            var reset = FindButton(view, "Reset theme typography");
            reset.BringIntoView(); reset.Focus(); Dispatcher.UIThread.RunJobs();
            Capture(window, "desktop-theme-typography-preview-120");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(authored, service.Typography);
            Assert.Equal(105, view.FindControl<Slider>("ThemeTextSizeSlider")!.Value);
        }
        finally
        {
            window.Close();
            ThemeTypographyResources.Apply(Application.Current!.Resources, ThemeTypography.Default);
        }
    }

    [AvaloniaFact]
    public void Fullscreen_font_actions_and_theme_size_update_shared_appearance_and_keep_controller_focus()
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        var appearance = context.Shared.Appearance;
        var original = appearance.Service.Typography;
        var fullscreenScale = context.TextScale;
        appearance.Service.SetTypography(ThemeTypography.Default);
        using var page = new FullscreenSettingsPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = page };
        FullscreenPage? choices = null;
        context.PageRequested += requested => choices = requested;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var size = FindButton(page, "Theme text size");
            size.Focus();
            page.Handle(GamepadButtons.Right);
            Assert.Equal(105, appearance.ThemeTextSize);
            Assert.Equal(fullscreenScale, context.TextScale);
            Assert.Same(size, window.FocusManager!.GetFocusedElement());
            appearance.ThemeTextSize = 120;
            page.Handle(GamepadButtons.Right);
            Assert.Equal(120, appearance.ThemeTextSize);
            var heading = FindButton(page, "Heading font");
            heading.Focus(); page.Handle(GamepadButtons.Accept);
            Assert.NotNull(choices);
            window.Content = choices; Dispatcher.UIThread.RunJobs();
            FindButton(choices, "IBM Plex Mono").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("IBM Plex Mono", appearance.HeadingFont);
            window.Content = page; Dispatcher.UIThread.RunJobs();
            Assert.Equal("IBM Plex Mono", AutomationProperties.GetItemStatus(FindButton(page, "Heading font")));
            size.BringIntoView(); Dispatcher.UIThread.RunJobs();
            Capture(window, "fullscreen-theme-typography-120");
            FindButton(page, "Reset theme typography").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(appearance.Service.Theme.Typography, appearance.Service.Typography);
        }
        finally
        {
            window.Close(); choices?.Dispose();
            appearance.Service.SetTypography(original);
        }
    }

    private static Button FindButton(Control root, string name) => root.GetVisualDescendants().OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == name);

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } directory) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }
}
