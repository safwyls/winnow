using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FullscreenScaleTests
{
    [AvaloniaTheory]
    [InlineData(1920, 1080, false)]
    [InlineData(3440, 1440, true)]
    public void Scale_changes_all_content_with_fixed_physical_margins_and_full_backdrop(double width, double height, bool wide)
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell);
        context.SetFitUltrawide(wide);
        using var view = new FullscreenView(context);
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var canvas = Assert.IsType<Grid>(Assert.IsType<Viewbox>(view.Content).Child);
            var navigation = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "FullscreenRootNavigation");
            var baseline = PhysicalWidth(navigation, window);
            Assert.Equal(1, context.UiScale);
            Assert.Equal(1080 / .85, canvas.Height, 5);
            Assert.Equal((wide ? 1080 * width / height : 1920) / .85, canvas.Width, 5);
            // Canvas layout rounds to device pixels before the Viewbox applies its transform.
            Assert.InRange(Math.Abs(height / 1080 * .85 - navigation.TransformToVisual(window)!.Value.M11) * canvas.Height, 0, 1);
            foreach (var scale in new[] { .8, 1.2, 1d })
            {
                context.UiScale = scale; Dispatcher.UIThread.RunJobs();
                Assert.Equal(1080 / (.85 * scale), canvas.Height, 5);
                Assert.InRange(Math.Abs(baseline * scale - PhysicalWidth(navigation, window)), 0, 1);
                var backdrop = view.GetVisualDescendants().OfType<ContentControl>().Single(p => p.Name == "FullscreenPageBackdrop");
                Assert.InRange(Math.Abs(width - PhysicalWidth(backdrop, window)), 0, 1);
                var safe = canvas.Children.OfType<Grid>().Single();
                Assert.InRange(Math.Abs(width * .05 - safe.TranslatePoint(default, window)!.Value.X), 0, 1);
                Assert.InRange(Math.Abs(height * .05 - safe.TranslatePoint(default, window)!.Value.Y), 0, 1);
            }
            context.UiScale = 1.2;
            context.EditText(new TextBox()); Dispatcher.UIThread.RunJobs();
            var keyboard = view.GetVisualDescendants().OfType<GamepadKeyboardView>().Single();
            var start = keyboard.TranslatePoint(default, window)!.Value;
            var end = keyboard.TranslatePoint(new Point(keyboard.Bounds.Width, keyboard.Bounds.Height), window)!.Value;
            Assert.InRange(start.X, 0, width); Assert.InRange(start.Y, 0, height);
            Assert.InRange(end.X, 0, width); Assert.InRange(end.Y, 0, height);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Scale_persists_clamps_and_resets_without_changing_text_size_until_confirmed()
    {
        var settings = new MemorySettings();
        using var services = new ServiceCollection().AddSingleton<ISettingsRepository>(settings).BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        Assert.Equal(1, context.UiScale);
        context.UiScale = 1.15;
        Assert.Equal("1.15", settings.Values["fullscreen.ui-scale-v2"]);
        using var reloaded = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        await reloaded.LoadAsync(); Assert.Equal(1.15, reloaded.UiScale);
        context.UiScale = 5; Assert.Equal(1.2, context.UiScale);
        context.UiScale = 0; Assert.Equal(.8, context.UiScale);
        context.UiScale = double.NaN; Assert.Equal(1, context.UiScale);
        context.UiScale = 1.2; context.TextScale = 1.3;
        using var view = new FullscreenView(context);
        var window = new Window { Width = 1920, Height = 1080, Content = view };
        try
        {
            window.Show(); view.Handle(GamepadButtons.Previous); Dispatcher.UIThread.RunJobs();
            var page = Assert.IsType<FullscreenSettingsPage>(view.CurrentPage);
            var row = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Interface scale");
            row.Focus(); page.Handle(GamepadButtons.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.15, context.UiScale); Assert.True(row.IsFocused);
            var decrease = page.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Decrease interface scale");
            var point = decrease.TranslatePoint(new Point(decrease.Bounds.Width / 2, decrease.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.1, context.UiScale); Assert.Equal(1.3, context.TextScale);
            page.Handle(GamepadButtons.Keyboard); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.1, context.UiScale);
            view.Handle(GamepadButtons.Accept); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, context.UiScale); Assert.Equal(1, context.TextScale);
            Assert.Equal("1", settings.Values["fullscreen.ui-scale-v2"]);
            Assert.Equal(.85, context.EffectiveUiScale, 5);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("0.85")]
    [InlineData("1.2")]
    public async Task Legacy_scale_switches_to_new_baseline_while_other_preferences_and_new_adjustments_survive(string legacyScale)
    {
        var settings = new MemorySettings();
        settings.Values["fullscreen.ui-scale"] = legacyScale;
        settings.Values["fullscreen.text-scale"] = "1.3";
        settings.Values["fullscreen.safe-margin"] = "3";
        using var services = new ServiceCollection().AddSingleton<ISettingsRepository>(settings).BuildServiceProvider();
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        await context.LoadAsync();
        Assert.Equal(1, context.UiScale);
        Assert.Equal(.85, context.EffectiveUiScale, 5);
        Assert.Equal(1.3, context.TextScale);
        Assert.Equal(3, context.SafeMarginPercent);

        context.UiScale = 1.1;
        using var reloaded = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell, services);
        await reloaded.LoadAsync();
        Assert.Equal(1.1, reloaded.UiScale);
        Assert.Equal(.935, reloaded.EffectiveUiScale, 5);
        Assert.Equal(1.3, reloaded.TextScale);
        Assert.Equal(3, reloaded.SafeMarginPercent);
    }

    private static double PhysicalWidth(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width, 0), window)!.Value.X - control.TranslatePoint(default, window)!.Value.X;

    private sealed class MemorySettings : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = [];
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken ct = default) { Values[key] = value; return Task.CompletedTask; }
    }
}
