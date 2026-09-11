using Dapper;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class WorkImageMetadataTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Source_metadata_round_trips_with_unknown_attributes_and_source_order()
    {
        var workId = await new WorkRepository(_db.Factory).InsertAsync(new Work { Name = "Artwork game" });
        var repository = new WorkImageRepository(_db.Factory);
        GameImage[] metadata =
        [
            new() { ImageId = "wide", Width = 3840, Height = 2160, AlphaChannel = false, Animated = false, ImageType = "Promotional Artwork" },
            new() { ImageId = "unknown" },
            new() { ImageId = "transparent", Width = 900, Height = 1600, AlphaChannel = true, Animated = true },
        ];
        var observation = new WorkImages
        {
            WorkId = workId, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork,
            ImageIds = "wide,unknown,transparent", Images = metadata, ObservedAt = DateTime.UtcNow,
        };

        await repository.UpsertAsync(observation);
        var stored = Assert.Single(await repository.GetForWorkAsync(workId));

        Assert.Equal(observation.ImageIds, stored.ImageIds);
        Assert.Equal(metadata, stored.Images);
        Assert.Equal(observation.ObservedAt, stored.ObservedAt);
        Assert.Equal(DateTimeKind.Utc, stored.ObservedAt.Kind);

        GameImage[] replacement = [new() { ImageId = "replacement", Width = 2560, Height = 1440 }];
        await repository.UpsertAsync(observation with { ImageIds = "replacement", Images = replacement });
        stored = Assert.Single(await repository.GetForWorkAsync(workId));
        Assert.Equal("replacement", stored.ImageIds);
        Assert.Equal(replacement, stored.Images);
    }

    [Fact]
    public async Task SteamGridDb_url_round_trips_without_changing_igdb_metadata()
    {
        var workId = await new WorkRepository(_db.Factory).InsertAsync(new Work { Name = "Hero game" });
        var repository = new WorkImageRepository(_db.Factory);
        var observed = new WorkImages
        {
            WorkId = workId, Source = ImageSources.SteamGridDb, Kind = ImageKinds.Artwork,
            ImageIds = "50584", ObservedAt = DateTime.UtcNow,
            Images = [new() { ImageId = "50584", Width = 1920, Height = 620,
                Url = "https://cdn2.steamgriddb.com/hero/61ba87bf4177f576150389d84d14bb01.png" }],
        };
        await repository.UpsertAsync(observed);
        await repository.UpsertAsync(observed with
        {
            Source = ImageSources.Igdb, ImageIds = "ar1", Images = [new() { ImageId = "ar1" }],
        });
        var rows = await repository.GetForWorkAsync(workId);
        Assert.Equal(observed.Images, Assert.Single(rows, row => row.Source == ImageSources.SteamGridDb).Images);
        Assert.Null(Assert.Single(Assert.Single(rows, row => row.Source == ImageSources.Igdb).Images).Url);
    }

    [Fact]
    public async Task Migration_preserves_existing_ids_and_supplies_empty_metadata()
    {
        var workId = await new WorkRepository(_db.Factory).InsertAsync(new Work { Name = "Legacy artwork" });
        using (var connection = _db.Factory.Open())
        {
            // Recreate the pre-migration shape around an existing observation.
            await connection.ExecuteAsync("ALTER TABLE work_images DROP COLUMN images_json;");
            await connection.ExecuteAsync("""
                INSERT INTO work_images (work_id, source, kind, image_ids, observed_at)
                VALUES (@workId, 'igdb', 'artwork', 'original,second', '2026-09-10 12:00:00');
                DELETE FROM SchemaVersions WHERE ScriptName LIKE '%0030_work_image_metadata.sql';
                """, new { workId });
        }

        _db.Initializer.Initialize();

        var stored = Assert.Single(await new WorkImageRepository(_db.Factory).GetForWorkAsync(workId));
        Assert.Equal(["original", "second"], stored.Ids);
        Assert.Empty(stored.Images);
    }
}
