using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The general key/value store over the §6 <c>settings</c> table. No migration
/// of its own — <c>settings(key, value)</c> has existed since 0001 — so these
/// run against a plain migrated database.
/// </summary>
public class SettingsRepositoryTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private SettingsRepository Repository => new(_db.Factory);

    [Fact]
    public async Task An_absent_key_reads_null()
        => Assert.Null(await Repository.GetAsync("library.view_mode"));

    [Fact]
    public async Task A_value_round_trips()
    {
        await Repository.SetAsync("library.view_mode", "grid");

        Assert.Equal("grid", await Repository.GetAsync("library.view_mode"));
    }

    [Fact]
    public async Task Setting_an_existing_key_overwrites_it()
    {
        var repository = Repository;
        await repository.SetAsync("library.sort", "dormant_longest");
        await repository.SetAsync("library.sort", "name_ascending");

        Assert.Equal("name_ascending", await repository.GetAsync("library.sort"));

        // Upsert, not insert-a-second-row: the table's PRIMARY KEY would reject
        // the duplicate, but a caller reading a stale first row is the failure
        // this actually guards against.
        Assert.Equal(1, await CountRowsAsync("library.sort"));
    }

    [Fact]
    public async Task Keys_do_not_collide()
    {
        var repository = Repository;
        await repository.SetAsync("library.view_mode", "list");
        await repository.SetAsync("library.sort", "recently_played");

        Assert.Equal("list", await repository.GetAsync("library.view_mode"));
        Assert.Equal("recently_played", await repository.GetAsync("library.sort"));
    }

    /// <summary>
    /// Null means "unset", never "empty" — a caller that deliberately stores an
    /// empty string must be able to tell its own value apart from absence.
    /// </summary>
    [Fact]
    public async Task An_empty_value_is_not_an_absent_key()
    {
        var repository = Repository;
        await repository.SetAsync("library.filter", string.Empty);

        Assert.Equal(string.Empty, await repository.GetAsync("library.filter"));
        Assert.Null(await repository.GetAsync("library.filter.other"));
    }

    /// <summary>
    /// The resolver keeps its own narrow contract over the same table. The two
    /// must not tread on each other: this is why keys are namespaced by module.
    /// </summary>
    [Fact]
    public async Task It_shares_the_table_with_the_resolver_without_collision()
    {
        var completedAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        await new ResolveStateRepository(_db.Factory).SetLastSoftMatchSweepAsync(completedAt);

        var repository = Repository;
        await repository.SetAsync("library.view_mode", "grid");

        Assert.Equal(
            completedAt,
            await new ResolveStateRepository(_db.Factory).GetLastSoftMatchSweepAsync());
        Assert.Equal("grid", await repository.GetAsync("library.view_mode"));

        // And the generic store can read what the typed one wrote — same table,
        // one namespace, no magic.
        Assert.NotNull(await repository.GetAsync(ResolveStateRepository.LastSoftMatchSweepKey));
    }

    [Fact]
    public async Task A_write_inside_a_unit_of_work_rolls_back_with_it()
    {
        var repository = Repository;

        using (_db.Factory.Begin())
        {
            await repository.SetAsync("library.view_mode", "list");
            // Disposed without Commit: the lease enlisted in the ambient
            // transaction, so the preference goes with it.
        }

        Assert.Null(await repository.GetAsync("library.view_mode"));
    }

    private async Task<long> CountRowsAsync(string key)
    {
        using var lease = _db.Factory.Lease();
        return await Dapper.SqlMapper.ExecuteScalarAsync<long>(
            lease.Connection,
            "SELECT COUNT(*) FROM settings WHERE key = @key;",
            new { key },
            lease.Transaction);
    }
}
