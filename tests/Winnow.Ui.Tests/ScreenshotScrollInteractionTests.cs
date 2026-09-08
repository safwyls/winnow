using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class ScreenshotScrollInteractionTests
{
    [AvaloniaFact]
    public void Wheel_scrolls_screenshots_sideways_without_moving_the_details_body()
    {
        var now = DateTime.UtcNow;
        using var model = new GameDetailsViewModel(
            TileFixture.Tile(now, ownershipId: 1, bucket: LibraryBuckets.NeverPlayed, title: "Bluebird"),
            "Never played", [], now, images:
            [new WorkImages
            {
                WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot,
                ImageIds = "aa1,aa2,aa3,aa4,aa5,aa6,aa7,aa8,aa9,aa10", ObservedAt = now,
            }]);
        var view = new GameDetailsView { DataContext = model };
        var window = new Window { Width = 1200, Height = 640, Content = view };
        window.Show();
        Flush();
        try
        {
            var strip = view.FindControl<ScrollViewer>("ScreenshotScroll")!;
            strip.BringIntoView();
            Flush();
            var body = strip.GetVisualAncestors().OfType<ScrollViewer>().First();
            var bodyOffset = body.Offset;
            Assert.True(strip.Extent.Width > strip.Viewport.Width);
            var point = strip.TranslatePoint(new Point(30, 30), window)!.Value;

            window.MouseWheel(point, new Vector(0, -1));
            Flush();
            var wheelOffset = strip.Offset.X;
            Assert.True(wheelOffset > 0);
            Assert.Equal(bodyOffset, body.Offset);

            strip.Offset = default;
            window.MouseWheel(point, new Vector(0, -1), RawInputModifiers.Shift);
            Flush();
            Assert.Equal(wheelOffset, strip.Offset.X);

            strip.Offset = default;
            window.MouseWheel(point, new Vector(-1, 0));
            Flush();
            Assert.Equal(wheelOffset, strip.Offset.X);

            strip.Offset = new Vector(strip.Extent.Width, 0);
            Flush();
            var end = strip.Offset;
            window.MouseWheel(point, new Vector(0, -1));
            Flush();
            Assert.Equal(end, strip.Offset);
            Assert.Equal(bodyOffset, body.Offset);

            window.MouseWheel(point, new Vector(0, 1));
            Flush();
            Assert.True(strip.Offset.X < end.X);
            Assert.Equal(bodyOffset, body.Offset);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
