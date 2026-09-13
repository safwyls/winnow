using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedCardActionTests
{
    [AvaloniaFact]
    public async Task Built_in_shelf_headers_explain_their_membership_in_tooltips()
    {
        await PreviewData.LoadShellAsync();
        var model = PreviewData.Feed;
        await model.LoadCommand.ExecuteAsync(null);
        var view = new FeedView { DataContext = model };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var explanations = view.GetVisualDescendants().OfType<StackPanel>()
                .Select(ToolTip.GetTip).OfType<string>().ToArray();
            Assert.All(model.Shelves, shelf => Assert.Contains(shelf.Blurb, explanations));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(420)]
    [InlineData(560)]
    public void Install_card_groups_feedback_beside_primary_action(double width)
    {
        var tile = TileFixture.Tile(DateTime.UtcNow, title: "A long game title that needs installing",
            steamAppId: "123", ownership: new Ownership { ReleaseId = 1, Store = "steam" });
        using var model = new FeedCardViewModel(tile, "You have never opened this game.",
            new FeedbackService(), _ => { });
        var view = new FeedCardView { DataContext = model };
        var window = new Window { Width = width, Height = 320, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var primary = view.FindControl<Button>("PrimaryAction")!;
            Assert.Equal("Install", primary.Content);
            var names = new[] { "AddToList", "NotNow", "NotInterested" };
            var labels = new[] { "Add to list", "Not now", "Not interested" };
            Rect? previous = null;
            var primaryRect = new Rect(primary.TranslatePoint(default, view)!.Value, primary.Bounds.Size);
            for (var i = 0; i < names.Length; i++)
            {
                var button = view.FindControl<Button>(names[i])!;
                Assert.True(button.IsEffectivelyVisible);
                Assert.Equal(labels[i], AutomationProperties.GetName(button));
                Assert.Equal(labels[i], ToolTip.GetTip(button));
                var bounds = new Rect(button.TranslatePoint(default, view)!.Value, button.Bounds.Size);
                Assert.True(bounds.Width >= 32 && bounds.Height >= 32);
                Assert.True(bounds.Right <= view.Bounds.Width);
                Assert.True(bounds.Left >= primaryRect.Right);
                Assert.False(bounds.Intersects(primaryRect));
                if (previous is { } earlier)
                {
                    Assert.Equal(earlier.Top, bounds.Top);
                    Assert.True(bounds.Left >= earlier.Right);
                }
                previous = bounds;
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hero_art_does_not_resize_the_card_or_cover_its_actions(bool feedback)
    {
        var tile = TileFixture.Tile(DateTime.UtcNow, title: "Aloft", steamAppId: "123",
            ownership: new Ownership { ReleaseId = 1, Store = "steam" });
        var added = 0;
        tile.OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
        using var model = new FeedCardViewModel(tile, "Last played on 7 Sep 2026.",
            feedback ? new FeedbackService() : null, _ => added++);
        var view = new FeedCardView { DataContext = model, Width = 470, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 510, Height = 240, Content = new Border { Padding = new Thickness(16), Child = view } };
        using var art = new RenderTargetBitmap(new PixelSize(1600, 700));
        var scene = new Canvas { Width = 1600, Height = 700, Background = Brushes.SteelBlue };
        scene.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,600 L450,170 L800,510 L1200,80 L1600,500 L1600,700 L0,700 Z"), Fill = Brushes.LightSeaGreen });
        scene.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0,700 L620,420 L960,610 L1500,300 L1600,700 Z"), Fill = Brushes.DarkSlateGray });
        scene.Measure(new Size(1600, 700)); scene.Arrange(new Rect(0, 0, 1600, 700)); art.Render(scene);
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var before = view.Bounds.Size;
            model.Backdrop = art; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            // Let the compositor publish the new window before testing pointer hits.
            await Task.Delay(40);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(before, view.Bounds.Size);
            var bookmark = view.FindControl<Button>("AddToList")!;
            var point = bookmark.TranslatePoint(new Point(16, 16), window)!.Value;
            Assert.True(bookmark.IsEffectivelyVisible);
            Assert.True(bookmark.IsEffectivelyEnabled);
            Assert.True(window.InputHitTest(point) is Control hit &&
                (ReferenceEquals(hit, bookmark) || hit.GetVisualAncestors().Contains(bookmark)),
                $"Hit {window.InputHitTest(point)} at {point}");
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);

            Assert.False(model.IsSetAside);
            if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is { } directory)
            {
                Directory.CreateDirectory(directory); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(directory, $"feed-card-hero-{(feedback ? "recommendation" : "recent")}.png"));
            }
            Assert.Equal(1, added);
        }
        finally { window.Close(); }
    }

    private sealed class FeedbackService : IFeedService
    {
        public Task<FeedSnapshot> GetShelvesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSurfacedAsync(long releaseId, string shelfId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<FeedVerdictOutcome> RecordVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(new FeedVerdictOutcome(true, null));
        public Task<bool> RevokeVerdictAsync(long releaseId, FeedVerdictKind kind, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<FeedVerdictRecord>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FeedVerdictRecord>>([]);
    }
}
