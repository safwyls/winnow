using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.Views.Fullscreen;
using Winnow.App.Views;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ThemeTypographyRuntimeTests
{
    [AvaloniaFact]
    public void Desktop_text_and_reading_line_height_follow_live_resources_without_scaling_geometry()
    {
        var heading = new TextBlock { Text = "Your library" };
        heading.Classes.Add("display-l");
        var paragraph = new TextBlock { Text = "A paragraph that stays readable." };
        paragraph.Classes.Add("para");
        var compact = new TextBlock { Text = "Compact label" };
        compact.Classes.Add("body");
        ThemeTypographyResources.BindSize(compact, 11);
        var button = new Button { Content = "Apply", Width = 120 };
        button.Classes.Add("act");
        var window = new Window { Width = 640, Height = 480,
            Content = new StackPanel { Children = { heading, paragraph, compact, button } } };
        try
        {
            ThemeTypographyResources.Apply(window.Resources, ThemeTypography.Default);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var buttonSize = button.FontSize;
            foreach (var percent in new[] { 120, 80, 100, 120 })
            {
                var typography = ThemeTypography.Default with { SizePercent = percent, InterfaceFont = "IBM Plex Mono" };
                ThemeTypographyResources.Apply(window.Resources, typography);
                Dispatcher.UIThread.RunJobs();
                var scale = percent / 100.0;
                Assert.Equal(22 * scale, heading.FontSize, 5);
                Assert.Equal(12 * scale, paragraph.FontSize, 5);
                Assert.Equal(18 * scale, paragraph.LineHeight, 5);
                Assert.Equal(11 * scale, compact.FontSize, 5);
                Assert.Equal(buttonSize * scale, button.FontSize, 5);
                Assert.Equal(120, button.Bounds.Width);
                Assert.Contains("IBM Plex Mono", paragraph.FontFamily.Name);
                Assert.Contains("Bricolage Grotesque", heading.FontFamily.Name);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Missing_font_falls_back_by_role_and_installed_fonts_are_available()
    {
        var resources = new ResourceDictionary();
        ThemeTypographyResources.Apply(resources, ThemeTypography.Default with
        {
            HeadingFont = "Winnow missing heading 913872", InterfaceFont = "Winnow missing interface 913872",
            DataFont = "Winnow missing data 913872"
        });
        Assert.Contains("Bricolage Grotesque", ((FontFamily)resources["DisplayFont"]!).Name);
        Assert.Contains("Plus Jakarta Sans", ((FontFamily)resources["BodyFont"]!).Name);
        Assert.Contains("IBM Plex Mono", ((FontFamily)resources["DataFont"]!).Name);
        Assert.True(ThemeTypographyResources.IsFontAvailable("IBM Plex Mono"));
        Assert.False(ThemeTypographyResources.IsFontAvailable("Winnow missing data 913872"));
        var systemFont = FontManager.Current.SystemFonts.FirstOrDefault();
        if (systemFont is not null) Assert.True(ThemeTypographyResources.IsFontAvailable(systemFont.Name));
    }

    [AvaloniaFact]
    public void Plain_inputs_follow_theme_face_and_size()
    {
        TemplatedControl[] inputs = [new Button { Content = "Choose" }, new TextBox { Text = "Name" },
            new ComboBox { ItemsSource = new[] { "Choice" }, SelectedIndex = 0 }, new CheckBox { Content = "Enabled" },
            new ToggleSwitch { Content = "On" }, new NumericUpDown { Value = 10 }];
        var stack = new StackPanel();
        foreach (var input in inputs) stack.Children.Add(input);
        var window = new Window { Width = 600, Height = 600, Content = stack };
        try
        {
            window.Show();
            foreach (var percent in new[] { 100, 120, 80, 100 })
            {
                ThemeTypographyResources.Apply(window.Resources, ThemeTypography.Default with { SizePercent = percent, InterfaceFont = "IBM Plex Mono" });
                Dispatcher.UIThread.RunJobs();
                foreach (var input in inputs)
                {
                    Assert.Equal(14 * percent / 100.0, input.FontSize, 5);
                    Assert.Contains("IBM Plex Mono", input.FontFamily.Name);
                }
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Native_fullscreen_popout_sizes_consent_hints_and_keyboard_from_stable_baselines()
    {
        var copy = new TextBlock { Text = "Allow this provider to read your library." };
        copy.Classes.Add("para");
        var accept = new Button { Content = "Continue" };
        var consent = new StackPanel { Children = { copy, accept } };
        var hint = FullscreenUi.Text("Choose a field", 24);
        var keyboard = new GamepadKeyboardView(new TextBox(), television: true);
        var root = new StackPanel { Children = { consent, hint, keyboard } };
        var window = new Window { Width = 1920, Height = 1080, Content = root };
        using var typography = new NativeThemeTypography(root, consent);
        try
        {
            ThemeTypographyResources.Apply(window.Resources, ThemeTypography.Default with { SizePercent = 120 });
            window.Show();
            foreach (var percent in new[] { 120, 80, 100, 120 })
            {
                ThemeTypographyResources.Apply(window.Resources, ThemeTypography.Default with { SizePercent = percent });
                Dispatcher.UIThread.RunJobs();
                var scale = percent / 100.0;
                Assert.Equal(28 * scale, copy.FontSize, 5);
                Assert.Equal(42 * scale, copy.LineHeight, 5);
                Assert.Equal(28 * scale, accept.FontSize, 5);
                Assert.Equal(24 * scale, hint.FontSize, 5);
                Assert.Equal(28 * scale, keyboard.GetVisualDescendants().OfType<Button>().First().FontSize, 5);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Fullscreen_shell_keeps_typography_settings_reachable_at_maximum_theme_size()
    {
        var service = PreviewData.Shell.Appearance.Service;
        var original = service.Typography;
        service.SetTypography(ThemeTypography.Default with { SizePercent = 120 });
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        try
        {
            window.Show();
            view.Handle(GamepadButtons.Previous);
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<FullscreenSettingsPage>(view.CurrentPage);
            foreach (var name in new[] { "Heading font", "Interface font", "Data font", "Theme text size", "Reset theme typography" })
            {
                var button = view.CurrentPage.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
                button.Focus(); button.BringIntoView(); Dispatcher.UIThread.RunJobs();
                Assert.Same(button, window.FocusManager!.GetFocusedElement());
                var top = button.TranslatePoint(default, window)!.Value;
                var bottom = button.TranslatePoint(new Avalonia.Point(button.Bounds.Width, button.Bounds.Height), window)!.Value;
                Assert.True(top.X >= 0 && bottom.X <= window.Width, name);
                Assert.True(top.Y >= 0 && bottom.Y <= window.Height, name);
            }
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, "fullscreen-shell-theme-typography-120.png"));
            }
        }
        finally { window.Close(); service.SetTypography(original); }
    }

    [AvaloniaFact]
    public void Fullscreen_combines_theme_and_page_scale_without_accumulation_or_scaling_icons()
    {
        var service = PreviewData.Shell.Appearance.Service;
        var original = service.Typography;
        service.SetTypography(ThemeTypography.Default);
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = 1.4 };
        using var view = new FullscreenView(context);
        var page = new TypographyPage(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        try
        {
            window.Show();
            context.Push(page);
            Dispatcher.UIThread.RunJobs();
            foreach (var percent in new[] { 120, 80, 100, 120 })
            {
                service.SetTypography(ThemeTypography.Default with { SizePercent = percent });
                Dispatcher.UIThread.RunJobs();
                var scale = percent / 100.0;
                Assert.Equal(28 * 1.4 * scale, page.Copy.FontSize, 5);
                Assert.Equal(64 * scale, page.Heading.FontSize, 5);
                Assert.Equal(28 * 1.4 * scale, page.Action.FontSize, 5);
                Assert.Equal(24 * scale, view.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "FullscreenClock").FontSize, 5);
                Assert.Equal(32, page.Icon.Width);
            }
        }
        finally { window.Close(); service.SetTypography(original); }
    }

    private sealed class TypographyPage : FullscreenPage
    {
        public TextBlock Copy { get; } = FullscreenUi.Text("Comfortable reading", 28);
        public TextBlock Heading { get; } = FullscreenUi.Text("Library", 64);
        public Button Action { get; } = FullscreenUi.Button("Select", () => { });
        public Control Icon { get; } = FullscreenGlyphs.Icon("A", 32);
        public TypographyPage(FullscreenContext context) : base(context)
        {
            Content = FullscreenUi.Stack(Heading, Copy, Action, Icon);
            SetFocusRows([Action]);
        }
    }
}
