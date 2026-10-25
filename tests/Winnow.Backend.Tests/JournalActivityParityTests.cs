using System.Net;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class JournalActivityParityTests
{
    [Theory]
    [InlineData("journal-vm", 7, 3, "First route.", 2, "Found the shortcut.", 4)]
    [InlineData("details", 42, 1, "Looking for the key.", 3, "Found the key behind the waterfall.", 3)]
    [InlineData("fullscreen-existing", 1, 1, "Old note", 4, "Try the other route next time.", 4)]
    public async Task Exact_source_journal_edits_and_deletes_keep_session_identity_and_rating_over_HTTP(
        string kind, long sessionId, long owner, string original, int rating, string replacement, int changedRating)
    {
        await using var host = await Host.StartAsync(kind);
        Assert.Equal((sessionId, owner), (host.Seed.SessionId, host.Seed.OwnershipId));
        var before = await host.Details.GetJournalAsync(sessionId);
        Assert.Equal(original, before.Note);
        Assert.Equal(rating, before.Rating);
        var initialEntry = Assert.Single((await host.Details.GetAsync(owner)).JournalEntries);
        Assert.Equal(sessionId, initialEntry.SessionId);
        Assert.Equal(owner, initialEntry.OwnershipId);
        Assert.Equal(host.Seed.StartedAt, initialEntry.SessionAt);
        if (kind == "details") Assert.Equal("Bluebird", (await host.Api.GetLibraryAsync()).Games.Single(game => game.WorkId == 1).Title);

        var saved = await host.Details.SaveJournalAsync(sessionId, new(replacement, changedRating, before.Revision));
        Assert.Equal(replacement, saved.Note);
        Assert.Equal(changedRating, saved.Rating);
        Assert.NotEqual(before.Revision, saved.Revision);
        var persisted = await host.Sessions.GetNoteAsync(sessionId);
        Assert.Equal((sessionId, replacement, changedRating), (persisted!.SessionId, persisted.Note, persisted.Rating));
        var after = Assert.Single((await host.Details.GetAsync(owner)).JournalEntries);
        Assert.Equal(initialEntry.SessionId, after.SessionId);
        Assert.Equal(initialEntry.SessionAt, after.SessionAt);
        Assert.Equal(replacement, after.Note);
        Assert.Equal(changedRating, after.Rating);

        var stale = await Assert.ThrowsAsync<BackendApiException>(() => host.Details.DeleteJournalAsync(sessionId, before.Revision));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(saved, await host.Details.GetJournalAsync(sessionId));
        await host.Details.DeleteJournalAsync(sessionId, saved.Revision);
        Assert.Null(await host.Sessions.GetNoteAsync(sessionId));
        Assert.Empty((await host.Details.GetAsync(owner)).JournalEntries);
        Assert.Equal(sessionId, Assert.Single(await host.Sessions.GetByOwnershipAsync(owner)).Id);
        Assert.Equal(1, host.Controls.Calls.Count(call => call.Operation == "write"));
        Assert.Equal(1, host.Controls.Calls.Count(call => call.Operation == "delete"));
    }

    [Theory]
    [InlineData("editor-empty", null, null)]
    [InlineData("editor-existing", "Original note", 4)]
    public async Task Both_original_editor_states_accept_the_validated_note_and_rating_without_changing_the_sitting_over_HTTP(
        string kind, string? initialNote, int? initialRating)
    {
        await using var host = await Host.StartAsync(kind);
        var session = await host.Sessions.GetAsync(7);
        Assert.Equal(3, session!.OwnershipId);
        Assert.Equal(new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc), session.StartedAt);
        Assert.Null(session.EndedAt);
        Assert.Null(session.DurationSeconds);
        var before = await host.Details.GetJournalAsync(7);
        Assert.Equal(initialNote, before.Note);
        Assert.Equal(initialRating, before.Rating);
        // Empty-note rejection and trimming are the shared editor's contract.
        // This boundary receives the successfully validated source payload.
        var saved = await host.Details.SaveJournalAsync(7, new("Remember the other route.", 5, before.Revision));
        Assert.Equal("Remember the other route.", saved.Note);
        Assert.Equal(5, saved.Rating);
        Assert.Equal(session, await host.Sessions.GetAsync(7));
        Assert.Equal(7, Assert.Single((await host.Details.GetAsync(3)).JournalEntries).SessionId);
    }

    [Fact]
    public async Task Refused_source_save_keeps_original_note_and_retry_commits_once_after_release_over_HTTP()
    {
        await using var host = await Host.StartAsync("editor-existing");
        var before = await host.Details.GetJournalAsync(7);
        host.Controls.Arm(new("write", Behavior: "fail"));
        await Assert.ThrowsAsync<BackendApiException>(() => host.Details.SaveJournalAsync(7, new("Keep my draft.", 4, before.Revision)));
        Assert.Equal(before, await host.Details.GetJournalAsync(7));
        host.Controls.Arm(new("write"));
        var pending = host.Details.SaveJournalAsync(7, new("Keep my draft.", 4, before.Revision));
        await WaitAsync(() => host.Controls.Calls.Count(call => call.Operation == "write") == 2);
        Assert.False(pending.IsCompleted);
        Assert.Equal("Original note", (await host.Sessions.GetNoteAsync(7))!.Note);
        host.Controls.Release();
        var saved = await pending;
        Assert.Equal("Keep my draft.", saved.Note);
        Assert.Equal(4, saved.Rating);
        Assert.Equal(2, host.Controls.Calls.Count(call => call.Operation == "write"));
        Assert.Equal(7, Assert.Single(Assert.Single((await host.Details.GetAsync(3)).Sessions.Values)).Id);
    }

    [Fact]
    public async Task Exact_single_row_pages_retry_the_same_cursor_and_edit_the_older_session_without_an_implicit_first_page_read()
    {
        await using var host = await Host.StartAsync("paging");
        var from = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
        var request = new ActivityRequest(from, from.AddDays(7), ActivitySection.Sessions);
        var first = await host.Details.GetActivityAsync(request);
        Assert.Equal(1, Assert.Single(first.Rows).Session!.Id);
        Assert.Equal(60, first.Rows[0].Session!.DurationSeconds);
        Assert.Equal(new ActivityCursor(from.AddDays(1), 1), first.Next);
        host.Controls.Arm(new("activity", "append", "fail"));
        await Assert.ThrowsAsync<BackendApiException>(() => host.Details.GetActivityAsync(request with { After = first.Next }));
        var second = await host.Details.GetActivityAsync(request with { After = first.Next });
        Assert.Equal(2, Assert.Single(second.Rows).Session!.Id);
        Assert.Equal(120, second.Rows[0].Session!.DurationSeconds);
        Assert.Null(second.Next);
        var reads = host.Controls.Calls.Where(call => call.Operation == "activity").ToArray();
        Assert.Equal(3, reads.Length);
        Assert.All(reads, call => { Assert.Equal(50, call.PageSize); Assert.Equal(from, call.From); Assert.Equal(from.AddDays(7), call.Until); });
        Assert.Equal(reads[1].After, reads[2].After);
        var before = await host.Details.GetJournalAsync(2);
        await host.Details.SaveJournalAsync(2, new("Continue from the old session.", null, before.Revision));
        Assert.Equal(3, host.Controls.Calls.Count(call => call.Operation == "activity"));
        Assert.Equal("Continue from the old session.", (await host.Sessions.GetNoteAsync(2))!.Note);
        Assert.Equal(2, Assert.Single((await host.Details.GetAsync(1)).JournalEntries).SessionId);
    }

    [Theory]
    [InlineData("activity", "paging-disposal")]
    [InlineData("account", "account")]
    public async Task Cancellation_reaches_the_held_backend_reader_even_when_it_returns_after_release(string operation, string kind)
    {
        await using var host = await Host.StartAsync(kind);
        host.Controls.Arm(new(operation));
        using var cancelled = new CancellationTokenSource();
        var read = operation == "activity"
            ? (Task)host.Details.GetActivityAsync(new(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, ActivitySection.Sessions), cancelled.Token)
            : host.Details.GetAccountStatsAsync("steam", cancelled.Token);
        await WaitAsync(() => host.Controls.Calls.Any(call => call.Operation == operation));
        var call = Assert.Single(host.Controls.Calls, call => call.Operation == operation);
        Assert.False(call.Completed);
        Assert.Equal(Environment.ProcessId, call.ProcessId);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await WaitAsync(() => call.Cancellation.IsCancellationRequested);
        host.Controls.Release();
        await WaitAsync(() => call.Completed);
        Assert.True(call.IgnoreCancellation);
        Assert.Equal(3, (await host.Api.GetLibraryAsync()).Games.Count);
    }

    [Fact]
    public async Task Account_retry_preserves_the_source_one_transaction_with_no_invented_money()
    {
        await using var host = await Host.StartAsync("account");
        host.Controls.Arm(new("account", Behavior: "fail"));
        await Assert.ThrowsAsync<BackendApiException>(() => host.Details.GetAccountStatsAsync("steam"));
        var result = await host.Details.GetAccountStatsAsync("steam");
        Assert.Equal("steam", result.Source);
        Assert.Equal(1, result.TransactionCount);
        Assert.True(result.HasAnything);
        Assert.Equal(0, result.GrossProductSpendCents);
        Assert.Equal(0, result.NetProductSpendCents);
        Assert.Empty(result.CurrencyGroups);
        Assert.Empty(result.Currencies);
        Assert.Null(result.CurrencySymbol);
        Assert.Equal(2, host.Controls.Calls.Count(call => call.Operation == "account"));
    }

    [Fact]
    public async Task Recovered_process_sitting_keeps_one_session_one_note_and_one_activity_record_over_HTTP()
    {
        await using var host = await Host.StartAsync("recovered");
        var original = Assert.Single(await host.Sessions.GetByOwnershipAsync(1));
        Assert.Null(original.EndedAt);
        Assert.Null(original.DurationSeconds);
        Assert.Equal("sitting", original.MonitorKey);
        var request = new ActivityRequest(original.StartedAt.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1), ActivitySection.Sessions);
        var before = await host.Details.GetActivityAsync(request);
        Assert.Equal(original.Id, Assert.Single(before.Rows).Session!.Id);
        Assert.Equal("Same sitting, same note", before.Rows[0].Note!.Note);
        Assert.Equal(4, before.Rows[0].Note!.Rating);
        await host.Fixture.CompleteRecoveryAsync();
        await host.Fixture.CompleteRecoveryAsync();
        var completed = Assert.Single(await host.Sessions.GetByOwnershipAsync(1));
        Assert.Equal(original.Id, completed.Id);
        Assert.Equal(original.StartedAt, completed.StartedAt);
        Assert.Equal(original.StartedAt.AddMinutes(10), completed.EndedAt);
        Assert.Equal(600, completed.DurationSeconds);
        var details = await host.Details.GetAsync(1);
        Assert.Equal(original.Id, Assert.Single(details.Sessions[1]).Id);
        Assert.Equal(original.Id, Assert.Single(details.JournalEntries).SessionId);
        Assert.Equal("Same sitting, same note", details.JournalEntries[0].Note);
        Assert.Equal(4, details.JournalEntries[0].Rating);
        var activity = Assert.Single((await host.Details.GetActivityAsync(request)).Rows);
        Assert.Equal(original.Id, activity.Session!.Id);
        Assert.Equal(600, activity.Session.DurationSeconds);
        Assert.Equal("Same sitting, same note", activity.Note!.Note);
        Assert.Equal(1, (await new LibraryHistoryStatsRepository(host.Database).GetAsync()).SessionCount);
        using var raw = host.Database.Open();
        Assert.Equal(1, raw.ExecuteScalar<int>("SELECT COUNT(*) FROM session_notes"));
        Assert.Equal(1, raw.ExecuteScalar<int>("SELECT COUNT(*) FROM monitored_session_keys"));
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, deadline.Token);
    }
    private sealed class Host(string directory, WebApplication app, WinnowApiClient api, JournalActivitySeedResult seed) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public DetailsClient Details { get; } = new(api);
        public JournalActivitySeedResult Seed => seed;
        public JournalActivityFixture Fixture => app.Services.GetRequiredService<JournalActivityFixture>();
        public JournalActivityControls Controls => app.Services.GetRequiredService<JournalActivityControls>();
        public ISqliteConnectionFactory Database => app.Services.GetRequiredService<ISqliteConnectionFactory>();
        public ISessionRepository Sessions => app.Services.GetRequiredService<ISessionRepository>();
        public static async Task<Host> StartAsync(string kind)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-journal-activity-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], JournalActivityFixture.Register);
            await app.StartAsync();
            var seed = await app.Services.GetRequiredService<JournalActivityFixture>().SeedAsync(kind);
            return new(directory, app, WinnowApiClient.Attach(directory), seed);
        }
        public async ValueTask DisposeAsync()
        {
            Controls.Release();
            api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
