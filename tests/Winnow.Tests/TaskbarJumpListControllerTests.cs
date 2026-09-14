using Winnow.App.Services;
using Winnow.Core.Domain;
using Xunit;

namespace Winnow.Tests;

public sealed class TaskbarJumpListControllerTests
{
    [Fact]
    public void Recent_games_are_launchable_ordered_and_limited_to_ten()
    {
        var now = DateTime.UtcNow;
        var tiles = Enumerable.Range(1, 12).Select(id => TileFixture.Tile(now,
            ownershipId: id, releaseId: id, title: $"Game {id}", lastPlayedUtc: now.AddDays(-id),
            steamAppId: id.ToString(), ownership: new Ownership { ReleaseId = id, Store = "steam", Installed = true })).ToList();
        tiles.Add(TileFixture.Tile(now, ownershipId: 50, lastPlayedUtc: now, steamAppId: "50",
            ownership: new Ownership { ReleaseId = 50, Store = "steam", Installed = false }));
        tiles.Add(TileFixture.Tile(now, ownershipId: 51, steamAppId: "51",
            ownership: new Ownership { ReleaseId = 51, Store = "steam", Installed = true }));
        var games = TaskbarJumpListController.RecentGames(tiles);
        Assert.Equal(Enumerable.Range(1, 10).Select(id => (long)id), games.Select(game => game.OwnershipId));
        Assert.Equal("Game 1", games[0].Title);
    }
}
