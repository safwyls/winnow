using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Every surface that draws cover art holds a lease for as long as it draws it,
/// and lets the lease go when it stops. That is the contract the decoded-memory
/// cache's disposal rule rests on: it frees what it evicts unless a lease says a
/// surface is still drawing it, so a surface that kept a bare bitmap would
/// either pin the pixels forever or be handed freed ones.
///
/// <para>Before TASK-152.2 the detail modal, the screenshot strip, the lightbox,
/// the IGDB candidate rows, the metadata previews and the merge rows all held
/// raw <c>Bitmap</c> references outside the pool. These tests count live pool
/// slots, which is the one place that would show a lease nobody released.</para>
/// </summary>
public sealed class CoverLeaseLifetimeTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

    private static readonly CoverKey Cover = CoverKey.Steam("620");

    /// <summary>A cache that never answers: a lease's lifetime is not its art's arrival.</summary>
    private sealed class SilentCache : ICoverCache
    {
        public bool TryGet(CoverKey key, double displayWidthPixels, CoverLayers layers, out CoverArt art)
        {
            art = null!;
            return false;
        }

        public Task<CoverArt?> GetAsync(
            CoverKey key, double displayWidthPixels, CoverLayers layers, CancellationToken ct = default)
            => new TaskCompletionSource<CoverArt?>().Task;
    }

    private static WorkImages Screenshots(string ids) => new()
    {
        WorkId = 1,
        Source = ImageSources.Igdb,
        Kind = ImageKinds.Screenshot,
        ImageIds = ids,
        ObservedAt = Now,
    };

    private static GameDetailsViewModel Details(ICoverLeases leases, WorkImages? images = null)
        => new(
            TileFixture.Tile(Now, coverKey: Cover, covers: leases),
            "Never played",
            [],
            Now,
            covers: leases,
            images: images is null ? null : [images]);

    [Fact]
    public void Closing_the_detail_modal_releases_its_cover()
    {
        var pool = new CoverLeasePool(new SilentCache());
        var details = Details(pool);

        details.RequestCover(GameDetailsViewModel.CoverWidth);
        Assert.Equal(1, pool.LiveSlots);

        // The modal draws the art at full saturation (§5.5), so the floor
        // variant is never decoded for it.
        details.Dispose();

        Assert.Equal(0, pool.LiveSlots);
        Assert.Null(details.Cover);
    }

    [Fact]
    public void Closing_the_modal_releases_every_screenshot_thumbnail()
    {
        var pool = new CoverLeasePool(new SilentCache());
        var details = Details(pool, Screenshots("aa11,bb22,cc33"));

        Assert.NotNull(details.Screenshots);
        details.Screenshots!.RequestThumbnails(1.0);

        // Three thumbnails, one cover: four slots, none of them the modal's
        // until it asks.
        Assert.Equal(3, pool.LiveSlots);

        details.Dispose();

        Assert.Equal(0, pool.LiveSlots);
        Assert.All(details.Screenshots.Shots, shot => Assert.Null(shot.Image));
    }

    /// <summary>
    /// The lightbox decodes at 1280 px, the widest rendition in the app. One
    /// lease at a time: navigating a strip releases the previous shot's, and
    /// closing releases the last.
    /// </summary>
    [Fact]
    public void The_lightbox_holds_one_shot_at_a_time_and_none_when_closed()
    {
        var pool = new CoverLeasePool(new SilentCache());
        var lightbox = new ScreenshotLightboxViewModel();
        var strip = GameScreenshotsViewModel.From([Screenshots("aa11,bb22")], pool, lightbox);

        Assert.NotNull(strip);
        strip!.SelectCommand.Execute(strip.Shots[0]);

        Assert.True(lightbox.IsOpen);
        Assert.Equal(1, pool.LiveSlots);

        lightbox.Next();
        Assert.Equal(1, pool.LiveSlots);

        lightbox.Close();
        Assert.Equal(0, pool.LiveSlots);
        Assert.Null(lightbox.Image);
    }

    /// <summary>
    /// A merge row fades on the same rule its tile does, so it is the one
    /// non-wall surface that does need both layers — and it releases them when
    /// a reload replaces the card.
    /// </summary>
    [Fact]
    public void A_merge_row_leases_both_layers_and_releases_them()
    {
        var pool = new CoverLeasePool(new SilentCache());
        var side = new MergeSideViewModel(
            releaseId: 1, title: "Fixture", coverKey: Cover, covers: pool);

        side.RequestCover(MergeQueueViewModel.CoverWidth);

        Assert.Equal(1, pool.LiveSlots);

        side.Dispose();

        Assert.Equal(0, pool.LiveSlots);
        Assert.Null(side.Cover);
        Assert.Null(side.CoverFloor);
    }
}
