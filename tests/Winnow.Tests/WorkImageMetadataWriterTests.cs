using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Model;
using Xunit;

namespace Winnow.Tests;

public sealed class WorkImageMetadataWriterTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Metadata_only_changes_are_saved_and_unchanged_runs_are_noops()
    {
        var workId = await new WorkRepository(_db.Factory).InsertAsync(new Work { Name = "Canned game", IgdbId = 100440 });
        var images = new WorkImageRepository(_db.Factory);
        var writer = new WorkReceptionWriter(images, new WorkRatingRepository(_db.Factory), new IgdbObservationWriter(_db.Factory));
        var legacy = new IgdbGame(100440, "Canned game", null, null, null, [], [], [])
        {
            ArtworkImageIds = ["ar1"],
            ScreenshotImageIds = ["sc1"],
        };
        Assert.Equal(2, await writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 100440, 0), legacy));
        var enriched = legacy with
        {
            ArtworkImages = [new GameImage { ImageId = "ar1", Width = 3840, Height = 2160, AlphaChannel = false, Animated = false, ImageType = "Artwork" }],
            ScreenshotImages = [new GameImage { ImageId = "sc1", Width = 1920, Height = 1080 }],
        };
        Assert.Equal(2, await writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 100440, 0), enriched));
        var stored = await images.GetForWorkAsync(workId);
        Assert.Equal(enriched.ArtworkImages, Assert.Single(stored, row => row.Kind == ImageKinds.Artwork).Images);
        Assert.Equal(enriched.ScreenshotImages, Assert.Single(stored, row => row.Kind == ImageKinds.Screenshot).Images);
        Assert.Equal(0, await writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 100440, 0), enriched));
        Assert.Equal(0, await writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 100440, 0), legacy));

        var corrected = enriched with { ArtworkImages = [enriched.ArtworkImages[0] with { Width = 4096 }] };
        Assert.Equal(1, await writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 100440, 0), corrected));
        Assert.Equal(0, await writer.ApplyIgdbAsync(new IgdbMappingVersion(workId, 100440, 0), corrected));
    }
}
