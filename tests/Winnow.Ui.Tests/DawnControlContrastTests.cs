using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Themes;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Lists;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class DawnControlContrastTests
{
    [AvaloniaTheory]
    [InlineData("rose-pine-dawn")]
    [InlineData("silkcircuit-dawn")]
    public async Task Desktop_action_templates_keep_labels_and_focus_visible_in_every_interactive_state(string id)
    {
        using var restore = new RestoreTheme();
        var theme = WinnowThemes.ById(id);
        new ThemeService().OverrideForSession(theme, 0);
        var primary = new Button { Content = "Save changes", Classes = { "act", "primary" } };
        var quiet = new Button { Content = "Cancel", Classes = { "act", "quiet" } };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(primary); panel.Children.Add(quiet);
        var window = new Window { Width = 760, Height = 340, Background = new SolidColorBrush(theme.Surface), Content = panel };
        window.Show();
        try
        {
            await Pump(window);
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            foreach (var button in new[] { primary, quiet })
                await AssertButtonStates(window, button, theme.Surface, $"{id}/{button.Content}", quiet == button);
            Capture(window, $"{id}-desktop-actions");

            // The danger rules are local to this real confirmation view; copying them
            // onto a synthetic button would miss template-specific regressions.
            var prompt = new FeedListPromptView { DataContext = new ActionPromptViewModel("Delete this list?", "Delete list",
                _ => Task.CompletedTask, () => { }, isDestructive: true) };
            window.Height = 480;
            window.Content = prompt;
            await Pump(window);
            var danger = prompt.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("danger"));
            await AssertButtonStates(window, danger, theme.Surface, $"{id}/destructive", false);
            Capture(window, $"{id}-desktop-destructive");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Dawn_selected_segments_and_chip_hover_keep_contrast_over_tinted_fills()
    {
        using var restore = new RestoreTheme();
        var service = new ThemeService();
        var segment = new Button { Classes = { "seg", "tab", "on" }, Content = new TextBlock { Text = "Tracked sessions" } };
        var glyph = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 0.5,0.5 L 6.5,6.5 M 6.5,0.5 L 0.5,6.5") };
        var remove = new Button { Classes = { "chipx" }, Content = glyph };
        var chipWords = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        chipWords.Children.Add(new TextBlock { Classes = { "body" }, Text = "Steam" }); chipWords.Children.Add(remove);
        var chip = new Border { Classes = { "chip" }, Child = chipWords };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Margin = new Thickness(24) };
        controls.Children.Add(segment); controls.Children.Add(chip);
        var window = new Window { Content = controls, Width = 600, Height = 200 };
        window.Show();
        try
        {
            foreach (var id in new[] { "rose-pine-dawn", "silkcircuit-dawn" })
            {
                var theme = WinnowThemes.ById(id); service.OverrideForSession(theme, 0);
                window.Background = new SolidColorBrush(theme.Surface);
                window.MouseMove(new Point(2, 2)); window.FocusManager!.ClearFocus(); await Pump(window);
                var segmentPresenter = segment.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                var segmentFill = Composite(ColorOf(segmentPresenter.Background), theme.Surface);
                Contrast(ColorOf(((TextBlock)segment.Content!).Foreground), segmentFill, 4.5, id + "/selected segment");
                var point = Center(remove, window); window.MouseMove(point); await Pump(window);
                var presenter = remove.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                var chipFill = Composite(ColorOf(chip.Background), theme.Surface);
                var hoverFill = Composite(ColorOf(presenter.Background), chipFill);
                Assert.NotEqual(chipFill, hoverFill);
                Contrast(ColorOf(glyph.Stroke), hoverFill, 3, id + "/chip hover glyph");
                window.FocusManager.ClearFocus(); Assert.True(remove.Focus(NavigationMethod.Tab)); await Pump(window);
                Contrast(ColorOf(presenter.BorderBrush), hoverFill, 3, id + "/chip hover focus");
                Capture(window, id + "-selected-controls");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Fluent_templates_follow_dark_light_dark_switches_without_stale_control_brushes()
    {
        using var restore = new RestoreTheme();
        var service = new ThemeService();
        var input = new TextBox { Text = "Search your library", Width = 320 };
        var choice = new ComboBox { ItemsSource = new[] { "Recently played", "Title" }, SelectedIndex = 0, Width = 320 };
        var panel = new StackPanel { Spacing = 24, Margin = new Thickness(24) };
        panel.Children.Add(input); panel.Children.Add(choice);
        var window = new Window { Content = panel, Width = 420, Height = 260 };
        window.Show();
        try
        {
            foreach (var id in new[] { "winnow", "rose-pine-dawn", "silkcircuit-dawn", "winnow" })
            {
                var theme = WinnowThemes.ById(id);
                service.OverrideForSession(theme, 0);
                window.Background = new SolidColorBrush(theme.Surface);
                window.FocusManager!.ClearFocus(); window.MouseMove(new Point(2, 2));
                await Pump(window);
                Assert.Equal(theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
                Assert.Equal(theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark, input.ActualThemeVariant);
                var inputBorder = input.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_BorderElement");
                var inputFill = Composite(ColorOf(inputBorder.Background), theme.Surface);
                Assert.True(theme.IsLight ? Luminance(inputFill) > .5 : Luminance(inputFill) < .2,
                    $"{id}: TextBox background {inputFill} does not match its light/dark variant.");
                Contrast(ColorOf(input.Foreground), inputFill, 4.5, $"{id}/Fluent TextBox");
                var presenter = choice.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
                var choiceFill = Composite(ColorOf(presenter.Background), theme.Surface);
                Contrast(ColorOf(presenter.Foreground), choiceFill, 4.5, $"{id}/Fluent ComboBox");
                Capture(window, $"{id}-fluent");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("rose-pine-dawn")]
    [InlineData("silkcircuit-dawn")]
    public async Task Fullscreen_real_action_template_keeps_focus_underline_and_labels_readable(string id)
    {
        using var restore = new RestoreTheme();
        var theme = WinnowThemes.ById(id);
        var service = new ThemeService();
        service.OverrideForSession(theme, 0);
        var preview = PreviewData.Shell;
        var shared = new MainWindowViewModel(preview.Library, preview.MergeQueue, preview.Stores,
            new AppearanceViewModel(service), preview.Feed, preview.AccountStats, preview.LibrarySettings);
        using var context = new FullscreenContext(preview.Library, preview.Feed, shared);
        using var shell = new FullscreenView(context);
        using var page = new ContrastPage(context);
        var window = new Window { Content = shell, Width = 1920, Height = 1080 };
        window.Show(); context.Push(page);
        try
        {
            await Pump(window);
            var button = page.Action;
            window.FocusManager!.ClearFocus(); window.MouseMove(new Point(2, 2)); await Pump(window);
            AssertFullscreenState(button, theme.Ground, $"{id}/normal", false);
            var point = Center(button, window);
            window.MouseMove(point); await Pump(window);
            AssertFullscreenState(button, theme.Ground, $"{id}/hover", false);
            window.MouseDown(point, MouseButton.Left); await Pump(window);
            AssertFullscreenState(button, theme.Ground, $"{id}/pressed", true);
            window.MouseMove(new Point(2, 2)); window.MouseUp(new Point(2, 2), MouseButton.Left);
            Assert.True(button.Focus(NavigationMethod.Directional)); await Pump(window);
            AssertFullscreenState(button, theme.Ground, $"{id}/focus", true);
            Capture(window, $"{id}-fullscreen-focus");
            window.FocusManager!.ClearFocus(); button.Classes.Add("current"); await Pump(window);
            AssertFullscreenState(button, theme.Ground, $"{id}/selected", true);
        }
        finally { window.Close(); }
    }

    private static async Task AssertButtonStates(Window window, Button button, Color surface, string label, bool quiet)
    {
        window.FocusManager!.ClearFocus(); window.MouseMove(new Point(2, 2)); await Pump(window);
        Check("normal", quiet);
        var point = Center(button, window);
        window.MouseMove(point); await Pump(window); Check("hover", quiet);
        window.MouseDown(point, MouseButton.Left); await Pump(window); Check("pressed", quiet);
        window.MouseMove(new Point(2, 2)); window.MouseUp(new Point(2, 2), MouseButton.Left);
        window.FocusManager!.ClearFocus();
        Assert.True(button.Focus(NavigationMethod.Tab)); await Pump(window); Check("keyboard focus", true);
        void Check(string state, bool boundary)
        {
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            var fill = Composite(ColorOf(presenter.Background), surface);
            var text = presenter.GetVisualDescendants().OfType<TextBlock>().Single();
            Contrast(ColorOf(text.Foreground), fill, 4.5, $"{label}/{state}/text");
            if (boundary) Contrast(ColorOf(presenter.BorderBrush), fill, 3, $"{label}/{state}/boundary");
        }
    }

    private static void AssertFullscreenState(Button button, Color surface, string label, bool boundary)
    {
        var border = button.GetVisualDescendants().OfType<Border>().First();
        var fill = Composite(ColorOf(border.Background), surface);
        var text = button.GetVisualDescendants().OfType<TextBlock>().Single();
        Contrast(ColorOf(text.Foreground), fill, 4.5, label + "/text");
        if (boundary) Contrast(ColorOf(border.BorderBrush), fill, 3, label + "/underline");
    }

    private static Point Center(Control control, Window window) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    private static async Task Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        await Task.Delay(180);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
    }
    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : Colors.Transparent;
    private static Color Composite(Color front, Color back)
    {
        var alpha = front.A / 255d;
        return Color.FromRgb((byte)Math.Round(front.R * alpha + back.R * (1 - alpha)),
            (byte)Math.Round(front.G * alpha + back.G * (1 - alpha)), (byte)Math.Round(front.B * alpha + back.B * (1 - alpha)));
    }
    private static double Luminance(Color color)
    {
        static double Channel(byte value) { var c = value / 255d; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
    private static void Contrast(Color foreground, Color background, double minimum, string label)
    {
        var first = Luminance(Composite(foreground, background)); var second = Luminance(background);
        var ratio = (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
        Assert.True(ratio >= minimum, $"{label}: {foreground} on {background} is {ratio:F2}:1; expected {minimum}:1.");
    }
    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, name + ".png"));
    }

    private sealed class ContrastPage : FullscreenPage
    {
        public Button Action { get; } = FullscreenUi.Button("Open game", () => { });
        public override string Title => "Control contrast";
        public ContrastPage(FullscreenContext context) : base(context)
        {
            Content = FullscreenUi.Stack(FullscreenUi.Text("Your next game", 48), Action);
            SetFocusRows([Action]);
        }
    }

    private sealed class RestoreTheme : IDisposable
    {
        private readonly Application _app = Application.Current!;
        private readonly ThemeVariant? _variant;
        private readonly Dictionary<object, object?> _direct;
        private readonly List<(SolidColorBrush Brush, Color Color)> _brushes = [];
        private readonly List<(GradientStop Stop, Color Color)> _stops = [];
        public RestoreTheme()
        {
            _variant = _app.RequestedThemeVariant;
            _direct = _app.Resources.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var key in WinnowThemes.All.SelectMany(theme => theme.Tokens(0).Keys).Distinct())
                if (_app.Resources.TryGetResource(key, null, out var resource) && resource is SolidColorBrush brush)
                    _brushes.Add((brush, brush.Color));
            if (_app.Resources.TryGetResource("TileScrim", null, out var scrim) && scrim is LinearGradientBrush gradient)
                foreach (var stop in gradient.GradientStops) _stops.Add((stop, stop.Color));
        }
        public void Dispose()
        {
            foreach (var (brush, color) in _brushes) brush.Color = color;
            foreach (var (stop, color) in _stops) stop.Color = color;
            foreach (var key in _app.Resources.Keys.ToArray()) if (!_direct.ContainsKey(key)) _app.Resources.Remove(key);
            foreach (var (key, value) in _direct) _app.Resources[key] = value;
            _app.RequestedThemeVariant = _variant;
            Dispatcher.UIThread.RunJobs();
        }
    }
}
