using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dapper;
using Winnow.App.Design;
using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.App.ViewModels.Filters;
using Winnow.App.Views;
using Winnow.App.Views.Fullscreen;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Tests;
using Xunit;

namespace Winnow.Ui.Tests;

public sealed class MissingFilterOptionsParityTests
{
    [AvaloniaTheory]
    [InlineData(false, "account")]
    [InlineData(true, "account")]
    [InlineData(false, "hide")]
    [InlineData(true, "hide")]
    [InlineData(false, "remove")]
    [InlineData(true, "remove")]
    [InlineData(false, "facets")]
    [InlineData(true, "facets")]
    public async Task Saved_and_current_rules_remain_restrictive_when_matching_choices_disappear(bool fullscreen, string change)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open()) connection.Execute("""
            UPDATE ownerships SET store='gog' WHERE id=2;
            UPDATE works SET first_release_year=2010 WHERE id=1;
            """);
        var facets = new FacetRepository(db.Factory);
        await facets.SetWorkFacetsAsync(1, [new(FacetKinds.Genre, "RPG")]);
        var genre = Assert.Single(await facets.GetVocabularyAsync(), facet => facet.Kind == FacetKinds.Genre);
        var filter = new LibraryFilter { Stores = ["steam"], GenreIds = [genre.Id], YearFrom = 2000 };
        var lists = new GameListRepository(db.Factory);
        var id = await lists.InsertAsync(GameList.Live("Steam RPGs", filter));
        using var first = Library();
        await first.LoadCommand.ExecuteAsync(null);
        first.OpenListCommand.Execute(first.Lists.All.Single(list => list.Id == id));
        Assert.Single(first.VisibleTiles);
        if (change == "account")
        {
            var settings = new SettingsRepository(db.Factory);
            await new OwnershipAccountRepository(db.Factory).UpsertAsync(new(1, "10002", 0, null, "steam_local", DateTime.UtcNow));
            var inventories = new OwnershipInventoryRepository(db.Factory);
            var attempt = await inventories.BeginAttemptAsync("steam", "10001", OwnershipInventorySources.SteamOwnedGames);
            await inventories.CompleteAsync(attempt, DateTime.UtcNow, 0);
            await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "10001");
            await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        }
        else if (change == "hide") await new HiddenGameRepository(db.Factory).HideAsync(1);
        else if (change == "remove") { using var connection = db.Factory.Open(); connection.Execute("DELETE FROM ownerships WHERE id=1;"); }
        else await facets.SetWorkFacetsAsync(1, []);
        await first.LoadCommand.ExecuteAsync(null);
        Assert.Empty(first.VisibleTiles);
        Assert.Equal(filter, first.Filters.ToFilter());
        Assert.False(first.IsLiveListEdited);

        // A fresh view has never seen the missing choices. Saved rules still survive.
        using var library = Library();
        await library.LoadCommand.ExecuteAsync(null);
        library.OpenListCommand.Execute(library.Lists.All.Single(list => list.Id == id));
        Assert.Equal(filter, library.Filters.ToFilter());
        Assert.Empty(library.VisibleTiles);
        Assert.Equal(0, library.Lists.Open!.Count);
        Assert.False(library.IsLiveListEdited);
        using var context = new FullscreenContext(library, PreviewData.Feed, PreviewData.Shell);
        using var page = new FullscreenBrowseFiltersPage(context);
        FullscreenPage? groupPage = null;
        var window = new Window { Width = 1920, Height = 1080, Content = fullscreen ? page : new FilterPanelView { DataContext = library.Filters } };
        context.PageRequested += value => { groupPage = value; window.Content = value; };
        context.BackRequested += () => window.Content = page;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            if (fullscreen) page.Handle(GamepadButtons.Keyboard);
            await library.UpdateLiveListCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(filter, (await lists.GetAsync(id))!.Filter);
            Assert.False(library.IsLiveListEdited);
            if (fullscreen)
            {
                var open = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "GENRE · 1 selected");
                Assert.True(open.Focus()); page.Handle(GamepadButtons.Accept); Dispatcher.UIThread.RunJobs();
                var selected = Assert.Single(groupPage!.GetVisualDescendants().OfType<Button>(), button => AutomationProperties.GetName(button) == "RPG, 0 matching titles");
                Assert.True(selected.IsEnabled);
                Assert.Equal("Selected", AutomationProperties.GetItemStatus(selected));
                Assert.True(selected.Focus()); groupPage!.Handle(GamepadButtons.Accept);
                groupPage.Handle(GamepadButtons.Keyboard);
            }
            else
            {
                var selected = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(), box => AutomationProperties.GetName(box) == "RPG, 0 matching titles");
                Assert.True(selected.IsEnabled);
                Assert.True(selected.IsChecked);
                selected.IsChecked = false;
            }
            Assert.True(library.IsLiveListEdited);
            Assert.Empty(library.Filters.ToFilter().GenreIds);
            Assert.Equal(new[] { "steam" }, library.Filters.ToFilter().Stores);
            Assert.Equal(2000, library.Filters.YearFrom);
            Assert.Equal(change == "facets" ? 1 : 0, library.VisibleTiles.Count);
        }
        finally { groupPage?.Dispose(); window.Close(); }

        LibraryViewModel Library() => new(new LibraryQueryRepository(db.Factory), new OwnershipRepository(db.Factory),
            new ReleaseRepository(db.Factory), new WorkRepository(db.Factory), new UpdateEventRepository(db.Factory),
            lists: lists, facets: facets);
    }

    [AvaloniaFact]
    public void Unknown_saved_facet_retains_an_explicit_clearable_zero_count_rule()
    {
        var panel = new FilterPanelViewModel(() => { });
        panel.Rebuild([], FacetSnapshot.Empty);
        panel.Apply(new LibraryFilter { GenreIds = [999] });
        var group = Assert.Single(panel.VisibleGroups, group => group.Key == FilterPanelViewModel.GenreKey);
        var option = Assert.Single(group.Checked);
        Assert.Equal("Unavailable value (999)", option.Label);
        Assert.True(option.IsAvailable);
        Assert.Equal(0, option.Count);
        Assert.Equal(new long[] { 999 }, panel.ToFilter().GenreIds);
        panel.BuildChips().Single().RemoveCommand.Execute(null);
        Assert.Empty(panel.ToFilter().GenreIds);
    }

    [AvaloniaFact]
    public void Removing_year_chip_clears_the_applied_rule_even_after_all_dated_games_disappear()
    {
        var panel = new FilterPanelViewModel(() => { });
        panel.Apply(new LibraryFilter { YearFrom = 2000 });
        panel.Rebuild([], FacetSnapshot.Empty);
        Assert.True(panel.ShowYearRange);
        panel.BuildChips().Single().RemoveCommand.Execute(null);
        Assert.Null(panel.YearFrom);
        Assert.False(panel.ShowYearRange);
    }
}
