using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Xunit;
using Winnow.Tests;

namespace Winnow.Ui.Tests;

public sealed class FullscreenNavigationLayoutTests
{
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.4)]
    public void Details_tabs_keep_geometry_with_unread_update_badge(double textScale)
    {
        var now = DateTime.UtcNow;
        using var details = new GameDetailsViewModel(TileFixture.Tile(now), "Started",
            [UpdateEventViewModel.Create(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.Announcement,
                OccurredAt = now, Title = "New update" }, now.AddDays(-1), 120)], now);
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = textScale };
        using var view = new FullscreenView(context);
        var page = new FullscreenDetailsPage(context, details);
        context.Push(page);
        var window = new Window { Width = 2560, Height = 1440, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var tabs = page.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("tv-details-tab")).ToArray();
            var original = tabs.Select(t => t.Bounds).ToArray();
            var strip = Assert.IsType<StackPanel>(tabs[0].Parent);
            var stripBounds = strip.Bounds;
            for (var i = 0; i < 4; i++)
            {
                page.Handle(GamepadButtons.PageNext); Dispatcher.UIThread.RunJobs();
                Assert.Equal(original, tabs.Select(t => t.Bounds).ToArray());
                Assert.Equal(stripBounds, strip.Bounds);
                Assert.Contains(tabs[1].GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Updates 1");
                Assert.Contains(tabs[1].GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "●");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(.7)]
    [InlineData(1)]
    [InlineData(1.4)]
    public void Selection_and_focus_keep_every_tab_and_trigger_stationary(double textScale)
    {
        using var context = new FullscreenContext(PreviewData.Library, PreviewData.Feed, PreviewData.Shell) { TextScale = textScale };
        using var view = new FullscreenView(context);
        var page = new NavigationPage(context);
        context.Push(page);
        var window = new Window { Width = 2560, Height = 1440, Content = view };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var controls = page.GetVisualDescendants().OfType<Control>()
                .Where(c => c is Button || c.Name is "SectionPreviousTrigger" or "SectionNextTrigger").ToArray();
            var original = controls.Select(c => new Rect(c.TranslatePoint(default, page)!.Value, c.Bounds.Size)).ToArray();
            foreach (var selected in page.Tabs)
            {
                foreach (var tab in page.Tabs) tab.Classes.Set("current", tab == selected);
                selected.Focus(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(FontWeight.Bold, selected.FontWeight);
                foreach (var (control, bounds) in controls.Zip(original))
                    Assert.Equal(bounds, new Rect(control.TranslatePoint(default, page)!.Value, control.Bounds.Size));
                var label = Assert.Single(selected.GetVisualDescendants().OfType<TextBlock>());
                Assert.Equal(FontWeight.Bold, label.FontWeight);
                Assert.True(label.Bounds.Width <= selected.Bounds.Width);
            }
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(directory, $"navigation-{textScale}.png"));
            }
        }
        finally { window.Close(); }
    }

    private sealed class NavigationPage : FullscreenPage
    {
        public Button[] Tabs { get; }
        public NavigationPage(FullscreenContext context) : base(context)
        {
            Tabs = new[] { "Overview", "Updates", "Journal", "Library", "Metadata & artwork" }
                .Select(label => FullscreenUi.Tab(label, () => { })).ToArray();
            var choices = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
            foreach (var tab in Tabs) choices.Children.Add(tab);
            Content = FullscreenUi.TriggerNavigation(choices);
            SetFocusRows(Tabs);
        }
    }
}
