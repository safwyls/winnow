using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamGridDb;
using Xunit;

namespace Winnow.Tests;

public sealed class SteamGridDbSyncTests
{
    [Fact]
    public async Task Sync_keeps_sources_separate_and_a_warm_pass_does_not_rewrite_observation()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var images = new WorkImageRepository(db.Factory);
        var igdb = Row(1, ImageSources.Igdb);
        await images.UpsertAsync(igdb);
        var client = new Client(new Dictionary<string, IReadOnlyList<GameImage>?> { ["1"] = [Hero], ["2"] = [] });
        var sync = new SteamGridDbSyncService(new LibraryQueryRepository(db.Factory), images, client,
            NullLogger<SteamGridDbSyncService>.Instance);
        Assert.Equal(1, await sync.SyncAsync());
        var stored = (await images.GetForWorkAsync(1)).Single(row => row.Source == ImageSources.SteamGridDb);
        Assert.Equal(Hero, Assert.Single(stored.Images));
        Assert.Equal(0, await sync.SyncAsync());
        Assert.Equal(stored, (await images.GetForWorkAsync(1)).Single(row => row.Source == ImageSources.SteamGridDb)
            with { Images = stored.Images });
        Assert.Contains(await images.GetForWorkAsync(1), row => row.Source == ImageSources.Igdb && row.ImageIds == igdb.ImageIds);
        Assert.Empty(await images.GetForWorkAsync(2));
    }

    [Fact]
    public async Task Unavailable_preserves_observation_and_confirmed_empty_removes_only_its_source()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var images = new WorkImageRepository(db.Factory);
        await images.UpsertAsync(Row(1, ImageSources.SteamGridDb));
        await images.UpsertAsync(Row(1, ImageSources.Igdb));
        var responses = new Dictionary<string, IReadOnlyList<GameImage>?> { ["1"] = null };
        var sync = new SteamGridDbSyncService(new LibraryQueryRepository(db.Factory), images, new Client(responses),
            NullLogger<SteamGridDbSyncService>.Instance);
        Assert.Equal(0, await sync.SyncAsync());
        Assert.Equal(2, (await images.GetForWorkAsync(1)).Count);
        responses["1"] = [];
        Assert.Equal(1, await sync.SyncAsync());
        Assert.Equal(ImageSources.Igdb, Assert.Single(await images.GetForWorkAsync(1)).Source);
    }

    [Fact]
    public async Task Display_shares_only_current_members_heroes_without_copying_or_merging_galleries()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var images = new WorkImageRepository(db.Factory);
        await images.UpsertAsync(Row(1, ImageSources.Igdb));
        await images.UpsertAsync(Row(2, ImageSources.Igdb));
        await images.UpsertAsync(Row(2, ImageSources.SteamGridDb));
        var linked = await BackdropImages.LoadAsync(images, 1, [1, 2, 2]);
        Assert.Equal(2, linked.Count);
        Assert.Contains(linked, row => row.WorkId == 2 && row.Source == ImageSources.SteamGridDb);
        Assert.DoesNotContain(linked, row => row.WorkId == 2 && row.Source == ImageSources.Igdb);
        Assert.Single(await images.GetForWorkAsync(1));
        var unlinked = await BackdropImages.LoadAsync(images, 1, [1]);
        Assert.Single(unlinked);
        Assert.DoesNotContain(unlinked, row => row.Source == ImageSources.SteamGridDb);
    }

    private static readonly GameImage Hero = new()
    {
        ImageId = "42", Width = 3840, Height = 1240, Animated = false,
        Url = "https://cdn2.steamgriddb.com/hero/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
    };

    private static WorkImages Row(long id, string source) => new()
    {
        WorkId = id, Source = source, Kind = ImageKinds.Artwork, ImageIds = "42", Images = [Hero],
        ObservedAt = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)
    };

    private sealed class Client(Dictionary<string, IReadOnlyList<GameImage>?> responses) : ISteamGridDbClient
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<IReadOnlyList<GameImage>?> GetHeroesAsync(string steamAppId, CancellationToken ct = default) =>
            Task.FromResult(responses.GetValueOrDefault(steamAppId));
    }
}
