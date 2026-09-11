using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.App.ViewModels.Lists;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class ListAtomicWriteTests
{
    [Theory]
    [InlineData("create", false)]
    [InlineData("create", true)]
    [InlineData("append", false)]
    [InlineData("append", true)]
    [InlineData("remove", false)]
    [InlineData("remove", true)]
    public async Task A_failed_bulk_change_leaves_no_partial_list_even_when_an_outer_transaction_commits(string action, bool ambient)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 4);
        var repository = new GameListRepository(db.Factory);
        var before = await repository.GetAllItemsAsync();
        using (var connection = db.Factory.Open())
            connection.Execute(action == "remove" ? """
                CREATE TRIGGER fail_list BEFORE DELETE ON list_items WHEN OLD.release_id=1
                BEGIN SELECT RAISE(ABORT, 'after first removal'); END;
                """ : """
                CREATE TRIGGER fail_list BEFORE INSERT ON list_items WHEN NEW.release_id=3
                BEGIN SELECT RAISE(ABORT, 'after first addition'); END;
                """);
        using (var transaction = ambient ? db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(async () =>
            {
                if (action == "create") await repository.CreateManualAsync("Failed", [2, 3]);
                else if (action == "append") await repository.AppendItemsAsync(1, [2, 3]);
                else await repository.RemoveItemsAsync(1, [4, 1]);
            });
            transaction?.Commit();
        }
        Assert.Single(await repository.GetAllAsync());
        Assert.Equal(before, await repository.GetAllItemsAsync());
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("filter")]
    [InlineData("delete")]
    [InlineData("reorder")]
    public async Task Failed_mutations_keep_the_last_committed_model_and_open_context(string action)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 4);
        var repository = new GameListRepository(db.Factory);
        var model = new ListsViewModel(repository);
        await model.LoadAsync();
        var list = action == "filter"
            ? (await model.CreateLiveListAsync("Saved rules", new LibraryFilter { YearFrom = 2000 }))!
            : Assert.Single(model.Lists);
        model.Open = list;
        var oldFilter = list.Filter;
        var oldOrder = list.ReleaseIds.ToArray();
        var oldName = list.Name;
        using (var connection = db.Factory.Open())
            connection.Execute(action switch
            {
                "delete" => "CREATE TRIGGER fail_list BEFORE DELETE ON lists BEGIN SELECT RAISE(ABORT, 'failure'); END;",
                "reorder" => "CREATE TRIGGER fail_list BEFORE UPDATE ON list_items WHEN NEW.position=1 BEGIN SELECT RAISE(ABORT, 'after first move'); END;",
                _ => "CREATE TRIGGER fail_list BEFORE UPDATE ON lists BEGIN SELECT RAISE(ABORT, 'failure'); END;"
            });
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            if (action == "rename") await model.RenameAsync(list, "Rejected");
            else if (action == "filter") await model.UpdateFilterAsync(list, new LibraryFilter { YearFrom = 2020 });
            else if (action == "delete") await model.DeleteAsync(list);
            else await model.MoveAsync(list, 4, 1);
        });
        Assert.Same(list, model.Open);
        Assert.Contains(list, model.All);
        Assert.Equal(oldName, list.Name);
        Assert.Equal(oldFilter, list.Filter);
        Assert.Equal(oldOrder, list.ReleaseIds);
        Assert.Equal(oldName, (await repository.GetAsync(list.Id))!.Name);
        Assert.Equal(oldOrder, (await repository.GetItemsAsync(list.Id)).Select(item => item.ReleaseId));
        Assert.False(model.IsBusy);
        Assert.Equal(GameListsCopy.SaveFailed, model.Problem);
    }
}
