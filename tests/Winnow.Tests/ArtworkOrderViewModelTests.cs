using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Enrich.Igdb.Storage;
using Xunit;

namespace Winnow.Tests;

public sealed class ArtworkOrderViewModelTests
{
    [Fact]
    public async Task Move_commands_persist_and_reload_order_and_refuse_endpoints()
    {
        using var db = new TempDatabase();
        var store = new SqliteSettingsStore(db.Factory);
        var model = new ArtworkOrderViewModel(new ArtworkPreferences(store));
        Assert.False(model.MoveUpCommand.CanExecute(ArtworkPreferences.Steam));
        Assert.False(model.MoveDownCommand.CanExecute(ArtworkPreferences.Igdb));
        await model.MoveUpCommand.ExecuteAsync(ArtworkPreferences.SteamGridDb);
        Assert.Equal([ArtworkPreferences.SteamGridDb, ArtworkPreferences.Steam, ArtworkPreferences.Igdb], model.Sources.Select(row => row.SourceId));
        var reloaded = new ArtworkOrderViewModel(new ArtworkPreferences(store));
        await reloaded.LoadAsync();
        Assert.Equal(model.Sources.Select(row => row.SourceId), reloaded.Sources.Select(row => row.SourceId));
        await reloaded.MoveDownCommand.ExecuteAsync(ArtworkPreferences.SteamGridDb);
        Assert.Equal(ArtworkPreferences.Steam, reloaded.Sources[0].SourceId);
        Assert.Contains("saved", reloaded.Status);
    }
}
