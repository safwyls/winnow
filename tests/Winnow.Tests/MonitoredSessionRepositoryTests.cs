using Dapper;
using Microsoft.Data.Sqlite;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Xunit;

namespace Winnow.Tests;

public sealed class MonitoredSessionRepositoryTests
{
    private static readonly DateTime T0 = SessionWatcherHarness.Origin;
    private static readonly MonitoredProcessIdentity Process = new(123, T0, "game");
    private static Session Open(string key, long ownershipId = 1) => new()
    {
        OwnershipId = ownershipId, StartedAt = T0, DetectionMethod = DetectionMethods.ProcessWatch,
        AttributedBy = SessionAttributions.Launch, MonitorKey = key,
    };

    [Fact]
    public async Task Recovery_is_exact_and_never_matches_manual_or_legacy_open_rows()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new SessionRepository(db.Factory);
        var manual = Open("manual") with { MonitorKey = null, DetectionMethod = "manual" };
        var manualId = await repository.InsertAsync(manual);
        var legacyId = await repository.InsertAsync(Open("legacy") with { MonitorKey = null });
        Assert.Null(await repository.FindOpenMonitoredAsync(1, [Process]));
        var saved = await repository.SaveMonitoredAsync(Open("new"), [Process]);
        Assert.Equal(saved, await repository.FindOpenMonitoredAsync(1, [Process]));
        Assert.Null(await repository.FindOpenMonitoredAsync(2, [Process]));
        Assert.Null(await repository.FindOpenMonitoredAsync(1, [Process with { StartedAt = T0.AddSeconds(1) }]));
        Assert.Null(await repository.FindOpenMonitoredAsync(1, [Process with { ProcessName = "other" }]));
        Assert.Null(await repository.FindOpenMonitoredAsync(1, [Process with { ProcessId = 124 }]));
        Assert.Equal(manual with { Id = manualId }, await repository.GetAsync(manualId));
        Assert.Null((await repository.GetAsync(legacyId))!.MonitorKey);
    }

    [Fact]
    public async Task Recovery_adopts_the_original_key_and_a_retried_completion_cannot_duplicate_or_reopen_it()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new SessionRepository(db.Factory);
        var first = await repository.SaveMonitoredAsync(Open("first"), [Process]);
        await repository.SetNoteAsync(new SessionNote { SessionId = first.Id, Note = "Original", Rating = 3 });
        var incoming = Open("recovery") with { StartedAt = T0.AddSeconds(5), EndedAt = T0.AddMinutes(10), AttributedBy = null };
        var completed = await repository.SaveMonitoredAsync(incoming, [Process]);
        Assert.Equal(first.Id, completed.Id);
        Assert.Equal("first", completed.MonitorKey);
        Assert.Equal(SessionAttributions.Launch, completed.AttributedBy);
        Assert.Equal(600, completed.DurationSeconds);
        Assert.Equal(completed, await repository.SaveMonitoredAsync(incoming, [Process]));
        Assert.Equal(completed, await repository.SaveMonitoredAsync(Open("recovery"), [Process]));
        Assert.Equal(completed, Assert.Single(await repository.GetByOwnershipAsync(1)));
        Assert.Equal("Original", (await repository.GetNoteAsync(first.Id))!.Note);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_ledger_writes_roll_back_the_row_and_keys_even_when_the_caller_commits(bool ambient)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using (var connection = db.Factory.Open()) connection.Execute("""
            CREATE TRIGGER fail_process BEFORE INSERT ON monitored_session_processes
            BEGIN SELECT RAISE(ABORT, 'injected process failure'); END;
            """);
        var repository = new SessionRepository(db.Factory);
        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(() => repository.SaveMonitoredAsync(Open("first"), [Process]));
            if (ambient)
            {
                await new SettingsRepository(db.Factory).SetAsync("unrelated", "keep");
                outer!.Commit();
            }
        }

        Assert.Empty(await repository.GetByOwnershipAsync(1));
        using var reader = db.Factory.Open();
        Assert.Equal(0, reader.ExecuteScalar<int>("SELECT COUNT(*) FROM monitored_session_keys;"));
        Assert.Equal(ambient ? "keep" : null, await new SettingsRepository(db.Factory).GetAsync("unrelated"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_completion_preserves_the_open_checkpoint_note_and_recovery_identity(bool ambient)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new SessionRepository(db.Factory);
        var first = await repository.SaveMonitoredAsync(Open("first"), [Process]);
        await repository.SetNoteAsync(new SessionNote { SessionId = first.Id, Note = "Keep" });
        using (var connection = db.Factory.Open()) connection.Execute("""
            CREATE TRIGGER fail_completion BEFORE DELETE ON monitored_session_processes
            BEGIN SELECT RAISE(ABORT, 'injected completion failure'); END;
            """);
        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            await Assert.ThrowsAsync<SqliteException>(() => repository.SaveMonitoredAsync(
                Open("second") with { EndedAt = T0.AddMinutes(5) }, [Process]));
            outer?.Commit();
        }

        Assert.Equal(first, await repository.FindOpenMonitoredAsync(1, [Process]));
        Assert.Equal(first, Assert.Single(await repository.GetByOwnershipAsync(1)));
        Assert.Equal("Keep", (await repository.GetNoteAsync(first.Id))!.Note);
    }

    [Fact]
    public async Task Multiple_open_matches_are_refused_without_merging_history()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new SessionRepository(db.Factory);
        var secondProcess = Process with { ProcessId = 124 };
        await repository.SaveMonitoredAsync(Open("first"), [Process]);
        await repository.SaveMonitoredAsync(Open("second"), [secondProcess]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.FindOpenMonitoredAsync(1, [Process, secondProcess]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveMonitoredAsync(Open("third"), [Process, secondProcess]));
        Assert.Equal(2, (await repository.GetByOwnershipAsync(1)).Count);
    }

    [Fact]
    public async Task Caller_rollback_removes_a_successful_checkpoint_and_foreign_ownership_keys_are_refused()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        var repository = new SessionRepository(db.Factory);
        using (db.Factory.Begin()) await repository.SaveMonitoredAsync(Open("rolled-back"), [Process]);
        Assert.Empty(await repository.GetByOwnershipAsync(1));
        await repository.SaveMonitoredAsync(Open("first"), [Process]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveMonitoredAsync(Open("first", 2), [Process]));
        Assert.Empty(await repository.GetByOwnershipAsync(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_after_the_row_write_rolls_back_the_whole_checkpoint(bool ambient)
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using var cancellation = new CancellationTokenSource();
        var repository = new SessionRepository(new CancellingFactory(db.Factory, cancellation));
        using (var connection = db.Factory.Open()) connection.Execute("""
            CREATE TRIGGER cancel_sitting AFTER INSERT ON sessions
            BEGIN SELECT cancel_session_write(); END;
            """);
        using (var outer = ambient ? db.Factory.Begin() : null)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SaveMonitoredAsync(
                Open("first"), [Process], cancellation.Token));
            outer?.Commit();
        }

        Assert.Empty(await repository.GetByOwnershipAsync(1));
        using var reader = db.Factory.Open();
        Assert.Equal(0, reader.ExecuteScalar<int>("SELECT COUNT(*) FROM monitored_session_keys;"));
        Assert.Equal(0, reader.ExecuteScalar<int>("SELECT COUNT(*) FROM monitored_session_processes;"));
    }

    [Fact]
    public async Task Independent_concurrent_writers_share_one_sitting_and_cannot_backdate_the_saved_start()
    {
        using var db = new TempDatabase();
        LibraryReadFixtures.Seed(db, 2);
        using var start = new ManualResetEventSlim();
        Task<Session> Write(string key) => Task.Run(async () =>
        {
            start.Wait();
            return await new SessionRepository(db.Factory).SaveMonitoredAsync(Open(key), [Process]);
        });
        var first = Write("first");
        var second = Write("second");
        start.Set();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(results[0].Id, results[1].Id);
        var repository = new SessionRepository(db.Factory);
        var saved = await repository.SaveMonitoredAsync(Open("third") with
        {
            StartedAt = T0.AddHours(-1), EndedAt = T0.AddMinutes(5),
        }, [Process]);
        Assert.Equal(T0, saved.StartedAt);
        Assert.Equal(300, saved.DurationSeconds);
        Assert.Equal(saved, Assert.Single(await repository.GetByOwnershipAsync(1)));
    }

    private sealed class CancellingFactory(ISqliteConnectionFactory inner, CancellationTokenSource cancellation)
        : ISqliteConnectionFactory
    {
        public string DatabasePath => inner.DatabasePath;
        public string ConnectionString => inner.ConnectionString;
        public IUnitOfWork Begin() => inner.Begin();
        public void ReleasePooledConnections() => inner.ReleasePooledConnections();
        public SqliteConnection Open() => inner.Open();
        public DbLease Lease()
        {
            var lease = inner.Lease();
            lease.Connection.CreateFunction("cancel_session_write", () => { cancellation.Cancel(); return 0; });
            return lease;
        }
    }
}
