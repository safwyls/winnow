using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests;

public sealed class ArtworkPreferencesTests
{
    [Fact]
    public async Task Saved_order_survives_restart_and_invalid_entries_keep_all_sources()
    {
        using var database = new TempDatabase();
        var store = new SqliteSettingsStore(database.Factory);
        var preferences = new ArtworkPreferences(store);
        await preferences.LoadAsync();
        Assert.Equal(["steam", "steamgriddb", "igdb"], preferences.SourceOrder);
        var changes = 0;
        preferences.Changed += () => changes++;
        string[] requested = [" IGDB ", "unknown", "igdb", "steamgriddb"];
        await preferences.SaveAsync(requested);
        requested[0] = "steam";
        Assert.Equal(["igdb", "steamgriddb", "steam"], preferences.SourceOrder);
        Assert.Equal("igdb,steamgriddb,steam", await store.GetAsync(ArtworkPreferences.SettingKey));
        var restarted = new ArtworkPreferences(store);
        await restarted.LoadAsync();
        Assert.Equal(preferences.SourceOrder, restarted.SourceOrder);
        await preferences.SaveAsync(["igdb", "steamgriddb", "steam"]);
        Assert.Equal(1, changes);
        await store.SetAsync(ArtworkPreferences.SettingKey, "bad, STEAMGRIDDB ,steamgriddb");
        await restarted.LoadAsync();
        Assert.Equal(["steamgriddb", "steam", "igdb"], restarted.SourceOrder);
    }

    [Fact]
    public async Task Failed_write_does_not_publish_or_notify()
    {
        var preferences = new ArtworkPreferences(new FailingStore());
        var before = preferences.SourceOrder;
        var changes = 0;
        preferences.Changed += () => changes++;
        await Assert.ThrowsAsync<IOException>(() => preferences.SaveAsync(["igdb", "steam", "steamgriddb"]));
        Assert.Same(before, preferences.SourceOrder);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Source_blocks_reorder_while_saved_art_and_standard_hero_keep_their_positions()
    {
        const string url = "https://cdn2.steamgriddb.com/hero/61ba87bf4177f576150389d84d14bb01.png";
        WorkImages[] images =
        [
            new() { WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Artwork,
                ImageIds = "art", ObservedAt = DateTime.UtcNow,
                Images = [new() { ImageId = "art", Width = 3840, Height = 2160 }] },
            new() { WorkId = 1, Source = ImageSources.SteamGridDb, Kind = ImageKinds.Artwork,
                ImageIds = "50584", ObservedAt = DateTime.UtcNow,
                Images = [new() { ImageId = "50584", Width = 1920, Height = 620, Url = url }] },
        ];
        var sources = new[] { "steam", "steamgriddb", "igdb" };
        foreach (var first in sources)
        foreach (var second in sources.Where(source => source != first))
        {
            var order = new[] { first, second, sources.Single(source => source != first && source != second) };
            var blockKeys = new Dictionary<string, CoverKey>
            {
                ["steam"] = CoverKey.SteamHero("42"),
                ["steamgriddb"] = SteamGridDbHeroUrl.Key(url)!.Value,
                ["igdb"] = CoverKey.IgdbBackdrop("art"),
            };
            Assert.Equal(new[] { CoverKey.User("saved") }.Concat(order.Select(source => blockKeys[source]))
                .Append(CoverKey.SteamHeroStandard("42")),
                BackdropSelection.Candidates(UserArtRef.Format("saved"), images, steamAppIds: ["42"], sourceOrder: order));
        }
    }

    private sealed class FailingStore : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => throw new IOException("Test failure");
        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
