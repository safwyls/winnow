using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.Views;
using Winnow.Covers;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class FeedShelfVisualTests
{
    [AvaloniaTheory]
    [InlineData(1600)]
    [InlineData(900)]
    public void Capture_cover_shelves_and_quick_details(int width)
    {
        if (Environment.GetEnvironmentVariable("WINNOW_UI_CAPTURE_DIR") is not { } output) return;
        var source = Environment.GetEnvironmentVariable("WINNOW_FEED_CAPTURE_COVERS");
        var titles = new[] { "Disco Elysium", "Hollow Knight", "Outer Wilds", "Hades", "Subnautica", "Tunic" };
        var ids = new[] { "632470", "367520", "753640", "1145360", "264710", "553420" };
        var reasons = new[] { "2 hours played, then left untouched for 8 months.", "An unfinished journey through Hallownest.",
            "You played 48 minutes last winter. There is still a whole solar system to unravel.", "One more escape attempt.",
            "Your last dive was a year ago.", "You only scratched the surface." };
        using var feed = new FeedViewModel(new PreviewFeedService(), PreviewData.Library) { IsLoading = false, Message = null };
        var bitmaps = new List<Bitmap>();
        for (var shelf = 0; shelf < 2; shelf++)
        {
            var cards = new List<FeedCardViewModel>();
            for (var i = 0; i < 5; i++)
            {
                var file = Path.Combine(source ?? "", $"steam_{ids[i]}.src.jpg");
                var bitmap = source is not null && File.Exists(file) ? new Bitmap(file) : null;
                if (bitmap is not null) bitmaps.Add(bitmap);
                var heroFile = Path.Combine(source ?? "", $"steam-hero_{ids[i]}.src.jpg");
                var hero = source is not null && File.Exists(heroFile) ? new Bitmap(heroFile) : null;
                if (hero is not null) bitmaps.Add(hero);
                var tile = TileFixture.Tile(DateTime.UtcNow, title: titles[i], steamAppId: ids[i],
                    ownership: new Winnow.Core.Domain.Ownership { ReleaseId = i + 1, Store = "steam", Installed = true },
                    work: new Winnow.Core.Domain.Work { Id = i + 1, Name = titles[i], Summary = i == 3 ? "Defy the god of the dead as you battle out of the Underworld." : null },
                    covers: bitmap is null ? null : new ArtLeaseSource(bitmap, hero), coverKey: CoverKey.Steam(ids[i]));
                tile.OpenDetailsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
                cards.Add(new FeedCardViewModel(tile, reasons[i], new PreviewFeedService(), _ => { }));
            }
            feed.Shelves.Add(new FeedShelfViewModel($"visual-{shelf}", shelf == 0 ? "Worth another look" : "Still waiting for their first session",
                shelf == 0 ? "Games you started, with a reason to come back." : "A fresh start, already in your library.", cards));
        }
        var view = new FeedView { DataContext = feed };
        var window = new Window { Width = width, Height = 1000, Content = view,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0F1C1E")) };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var cards = view.GetVisualDescendants().OfType<FeedCardView>().ToArray();
            Assert.Equal(12, cards.Length);
            Save("shelves");
            var card = cards[3].FindControl<Button>("Card")!;
            window.MouseMove(card.TranslatePoint(new Point(40, 40), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.IsType<Flyout>(FlyoutBase.GetAttachedFlyout(card)).IsOpen);
            Save("quick-details");
        }
        finally
        {
            window.Close();
            feed.Dispose();
            foreach (var bitmap in bitmaps) bitmap.Dispose();
        }
        void Save(string mode)
        {
            Directory.CreateDirectory(output);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(output, $"feed-{mode}-{width}.png"));
        }
    }

    private sealed class ArtLeaseSource(Bitmap bitmap, Bitmap? hero) : ICoverLeases
    {
        public ICoverLease Acquire(CoverKey key, double displayWidthPixels, CoverLayers layers = CoverLayers.VividAndFloor) =>
            new Lease(key, CoverImaging.SnapWidth(displayWidthPixels), layers,
                key.Provider == CoverProviders.Steam ? new CoverArt(bitmap, bitmap) : hero is null ? null : new CoverArt(hero, hero));
    }
    private sealed class Lease(CoverKey key, int width, CoverLayers layers, CoverArt? art) : ICoverLease
    {
        public CoverKey Key => key;
        public int Width => width;
        public CoverLayers Layers => layers;
        public bool TryGetArt(out CoverArt value) { value = art!; return art is not null; }
        public Task<CoverArt?> GetAsync(CancellationToken ct = default) => Task.FromResult<CoverArt?>(art);
        public void Dispose() { }
    }
}

