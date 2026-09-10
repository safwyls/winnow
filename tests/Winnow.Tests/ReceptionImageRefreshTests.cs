using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb;
using Winnow.Tests.Igdb;
using Winnow.Tests.SteamStore;
using Xunit;

namespace Winnow.Tests;

public sealed class ReceptionImageRefreshTests
{
    [Fact]
    public async Task Existing_enriched_library_refreshes_old_image_payload_and_warm_sync_is_a_noop()
    {
        using var db = new TempDatabase();
        var works = new WorkRepository(db.Factory);
        var workId = await works.InsertAsync(new Work
        {
            Name = "Landscape game", IgdbId = 42, Summary = "Already enriched",
            CoverUrl = "https://images.igdb.com/igdb/image/upload/t_cover_big/cover42.jpg",
            FirstReleaseYear = 2020, Publisher = "Example publisher"
        });
        await new ReleaseRepository(db.Factory).InsertAsync(new Release { WorkId = workId, Name = "Landscape game" });
        var images = new WorkImageRepository(db.Factory);
        await images.UpsertAsync(new WorkImages
        {
            WorkId = workId, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork,
            ImageIds = "art42", ObservedAt = DateTime.UtcNow
        });
        using var igdb = new IgdbTestHost((request, _) => FakeHttpMessageHandler.Json(HttpStatusCode.OK,
            request.Endpoint == "token" ? IgdbFixtures.TokenResponse("test-token") : """
            [{"id":42,"name":"Landscape game","artworks":[
              {"image_id":"art42","width":3840,"height":2160,"alpha_channel":false,"animated":false,"image_type":{"name":"Artwork"}}
            ]}]
            """));
        await igdb.Cache.SetAsync(IgdbClient.CacheProvider, IgdbClient.GameCacheKey(42),
            """{"version":4,"game":{"igdb_id":42,"name":"Landscape game","artwork_image_ids":["art42"]}}""",
            igdb.Clock.GetUtcNow().UtcDateTime);
        using var steam = new SteamStoreTestHost((_, _) => throw new InvalidOperationException("No Steam IDs to fetch."));
        var sync = new ReceptionSyncService(new LibraryQueryRepository(db.Factory),
            new WorkReceptionWriter(images, new WorkRatingRepository(db.Factory), igdb.Clock),
            igdb.Client, steam.Client, NullLogger<ReceptionSyncService>.Instance);

        var first = await sync.SyncAsync();

        Assert.Equal(1, first.WorksWritten);
        var stored = Assert.Single(await images.GetForWorkAsync(workId));
        Assert.Equal("art42", stored.ImageIds);
        var artwork = Assert.Single(stored.Images);
        Assert.Equal(3840, artwork.Width);
        Assert.Equal(2160, artwork.Height);
        Assert.Equal(1, igdb.Handler.CountFor("games"));
        Assert.Empty(steam.Handler.Requests);

        var second = await sync.SyncAsync();
        Assert.Equal(0, second.RowsWritten);
        Assert.Equal(1, igdb.Handler.CountFor("games"));
        Assert.Equal(stored.ObservedAt, Assert.Single(await images.GetForWorkAsync(workId)).ObservedAt);
    }
}
