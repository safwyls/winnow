using System.Threading.Channels;
using Dapper;
using Winnow.App.ViewModels;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class GameplayStatsViewModelTests
{
    [Fact]
    public async Task Hidden_scope_reload_cancels_and_rejects_an_obsolete_result()
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        using var model = fixture.Model();
        var oldRead = model.ActivateAsync();
        var old = await fixture.Reads.NextAsync();
        Assert.Equal(2, old.Request.Ownerships.Count);
        await new HiddenGameRepository(fixture.Db.Factory).HideAsync(2);
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        var current = await fixture.Reads.NextAsync();
        Assert.True(old.Token.IsCancellationRequested);
        Assert.Equal(1, Assert.Single(current.Request.Ownerships).OwnershipId);
        current.Complete(3600);
        await model.PendingRefresh;
        var committed = model.HoursText;
        old.Complete(999999);
        await oldRead;
        Assert.Equal(committed, model.HoursText);
        Assert.True(model.HasData);
    }

    [Fact]
    public async Task Identity_reload_updates_game_mapping_and_store_removal_resets_selection()
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        using var model = fixture.Model();
        model.SelectedStore = Assert.Single(model.StoreOptions, s => s.Key == "gog");
        var first = model.ActivateAsync();
        (await fixture.Reads.NextAsync()).Complete();
        await first;
        await new IdentityLinkRepository(fixture.Db.Factory).LinkAsync(new() { ParentWorkId = 1, ChildWorkIds = [2] });
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        var linked = await fixture.Reads.NextAsync();
        Assert.Equal("gog", linked.Request.Store);
        Assert.Equal(2, linked.Request.Ownerships.Count);
        Assert.All(linked.Request.Ownerships, o => Assert.Equal(1, o.ResolvedWorkId));
        linked.Complete();
        await model.PendingRefresh;
        using (var connection = fixture.Db.Factory.Open())
            connection.Execute("DELETE FROM ownerships WHERE id=2;");
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        var removed = await fixture.Reads.NextAsync();
        Assert.Null(removed.Request.Store);
        Assert.Equal(string.Empty, model.SelectedStore.Key);
        Assert.Single(removed.Request.Ownerships);
        removed.Complete();
        await model.PendingRefresh;
    }

    [Fact]
    public async Task Search_does_not_filter_statistics_and_deactivation_rejects_pending_data()
    {
        using var fixture = new Fixture();
        await fixture.Library.LoadCommand.ExecuteAsync(null);
        fixture.Library.SearchText = "no matching game";
        using var model = fixture.Model();
        var pending = model.ActivateAsync();
        var read = await fixture.Reads.NextAsync();
        Assert.Equal(2, read.Request.Ownerships.Count);
        model.Deactivate();
        Assert.True(read.Token.IsCancellationRequested);
        read.Complete(36000);
        await pending;
        Assert.False(model.HasData);
        Assert.False(model.IsLoading);
    }

    [Theory]
    [InlineData("2026-03-08", 23)]
    [InlineData("2026-11-01", 25)]
    public async Task Custom_local_day_uses_inclusive_calendar_dates_and_real_DST_duration(string date, int hours)
    {
        using var fixture = new Fixture();
        using var model = fixture.Model(TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles"));
        model.SelectedPeriod = "Custom";
        model.CustomFrom = date;
        model.CustomUntil = date;
        var pending = model.ActivateAsync();
        var read = await fixture.Reads.NextAsync();
        Assert.Equal(hours, (read.Request.UntilUtc - read.Request.FromUtc).TotalHours);
        var bin = Assert.Single(read.Request.TimeBins);
        Assert.Equal(read.Request.FromUtc, bin.FromUtc);
        Assert.Equal(read.Request.UntilUtc, bin.UntilUtc);
        Assert.Equal(DateTimeKind.Utc, bin.FromUtc.Kind);
        read.Complete();
        await pending;
    }

    [Theory]
    [InlineData("bad", "2026-01-01")]
    [InlineData("2026-01-02", "2026-01-01")]
    [InlineData("2027-01-01", "2027-01-01")]
    [InlineData("1899-12-31", "1900-01-01")]
    [InlineData("2000-01-01", "2026-01-01")]
    public async Task Invalid_custom_dates_do_not_query_or_retain_obsolete_results(string from, string until)
    {
        using var fixture = new Fixture();
        using var model = fixture.Model();
        var oldRead = model.ActivateAsync();
        var old = await fixture.Reads.NextAsync();
        model.Deactivate();
        model.SelectedPeriod = "Custom";
        model.CustomFrom = from;
        model.CustomUntil = until;
        await model.ActivateAsync();
        Assert.NotNull(model.Problem);
        Assert.False(model.IsLoading);
        Assert.False(fixture.Reads.HasPending);
        old.Complete(999999);
        await oldRead;
        Assert.False(model.HasData);
    }

    private sealed class Fixture : IDisposable
    {
        public TempDatabase Db { get; } = new();
        public ControlledRepository Reads { get; } = new();
        public LibraryViewModel Library { get; }
        public Fixture()
        {
            LibraryReadFixtures.Seed(Db, 2);
            using (var connection = Db.Factory.Open()) connection.Execute("UPDATE ownerships SET store='gog' WHERE id=2;");
            Library = new(new LibraryQueryRepository(Db.Factory), new OwnershipRepository(Db.Factory),
                new ReleaseRepository(Db.Factory), new WorkRepository(Db.Factory), new UpdateEventRepository(Db.Factory),
                identityLinks: new IdentityLinkRepository(Db.Factory));
        }
        public GameplayStatsViewModel Model(TimeZoneInfo? zone = null) => new(Reads, Library, new Clock(), zone ?? TimeZoneInfo.Utc);
        public void Dispose() { Library.Dispose(); Db.Dispose(); }
    }
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed record Read(GameplayStatsRequest Request, CancellationToken Token)
    {
        public TaskCompletionSource<GameplayStats> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Deliberately ignore cancellation: the view model must reject late completions itself.
        public void Complete(double seconds = 0) => Result.SetResult(new() { RecordedSeconds = seconds });
    }
    private sealed class ControlledRepository : IGameplayStatsRepository
    {
        private readonly Channel<Read> _reads = Channel.CreateUnbounded<Read>();
        public bool HasPending => _reads.Reader.TryPeek(out _);
        public Task<Read> NextAsync() => _reads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        public Task<GameplayStats> GetAsync(GameplayStatsRequest request, CancellationToken ct = default)
        {
            var read = new Read(request, ct);
            _reads.Writer.TryWrite(read);
            return read.Result.Task;
        }
    }
}
