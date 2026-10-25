using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Data;
using Winnow.Electron.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Backend.Tests;

public sealed class ActivityRemainingParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("ack", 1)]
    [InlineData("ack-correlated", 2)]
    public async Task Acknowledgement_preserves_the_source_announcement_and_owned_history_over_HTTP(string kind, int eventCount)
    {
        await using var host = await Fixture.StartAsync();
        await ActivityRemainingFixture.SeedSmallAsync(host.Database, kind);
        var before = await host.Details.GetAsync(1);
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(600, game.PlaytimeMinutes);
        Assert.Equal(ActivityRemainingFixture.SourceNow.AddDays(-20), game.LastPlayedAt);
        Assert.Equal(ActivityRemainingFixture.SourceNow.AddYears(-2), Assert.Single(before.Ownerships).AcquiredAt);
        Assert.Equal(eventCount, before.Events.Count);
        var announcement = Assert.Single(before.Events, value => value.Kind == UpdateEventKinds.Announcement);
        Assert.Equal("Exploration update", announcement.Title);
        Assert.Equal(ActivityRemainingFixture.SourceNow.AddDays(-2), announcement.OccurredAt);
        Assert.Empty(before.History[1]);
        Assert.Empty(before.Sessions[1]);
        Assert.Empty(before.Acknowledgements);

        var result = await host.Details.AcknowledgeUpdatesAsync(1, new(before.Events.Select(value => value.Id).ToArray()));
        Assert.Equal(kind == "ack-correlated" ? "Stored" : "NothingToDo", result.Result);
        Assert.Equal(kind == "ack-correlated" ? announcement.OccurredAt : (DateTime?)null, result.AcknowledgedThrough);
        var after = await host.Details.GetAsync(1);
        if (kind == "ack-correlated") Assert.Equal(announcement.OccurredAt, after.Acknowledgements[1]);
        else Assert.Empty(after.Acknowledgements);
        Assert.Equal(JsonSerializer.Serialize(before.Events), JsonSerializer.Serialize(after.Events));
        Assert.Equal(JsonSerializer.Serialize(before.Ownerships), JsonSerializer.Serialize(after.Ownerships));
        Assert.Empty(after.History[1]);
        Assert.Empty(after.Sessions[1]);
        var refreshed = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        // Acknowledgement can change the derived bucket, but not any owned facts.
        Assert.Equal(JsonSerializer.Serialize(game with { Bucket = refreshed.Bucket }), JsonSerializer.Serialize(refreshed));
    }

    [Fact]
    public async Task Sparse_Steam_copy_preserves_ten_hours_without_inventing_dates_or_history_over_HTTP()
    {
        await using var host = await Fixture.StartAsync();
        await ActivityRemainingFixture.SeedSmallAsync(host.Database, "sparse-linked");
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(600, game.PlaytimeMinutes);
        Assert.Null(game.LastPlayedAt);
        Assert.Equal("steam", Assert.Single(game.Entries).Store);
        var details = await host.Details.GetAsync(1);
        Assert.Null(Assert.Single(details.Ownerships).AcquiredAt);
        Assert.Empty(details.History[1]);
        Assert.Empty(details.Sessions[1]);
        Assert.Empty(details.Events);
        Assert.Empty(details.JournalEntries);
    }

    [Fact]
    public async Task Exact_three_monthly_observations_and_two_sessions_reach_Details_over_HTTP()
    {
        await using var host = await Fixture.StartAsync();
        await ActivityRemainingFixture.SeedSmallAsync(host.Database, "range");
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal(960, game.PlaytimeMinutes);
        Assert.Equal(ActivityRemainingFixture.SourceNow.AddDays(-15), game.LastPlayedAt);
        var details = await host.Details.GetAsync(1);
        Assert.Equal(ActivityRemainingFixture.SourceNow.AddYears(-6), Assert.Single(details.Ownerships).AcquiredAt);
        var observations = details.History[1].OrderBy(value => value.ObservedAt).ToArray();
        Assert.Equal(new long[] { 360, 600, 780 }, observations.Select(value => value.PlaytimeMinutes));
        Assert.Equal(new[] { 3, 4, 5 }.Select(month => new DateTime(2022, month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(1).AddSeconds(-1)), observations.Select(value => value.ObservedAt));
        var sessions = details.Sessions[1].OrderBy(value => value.StartedAt).ToArray();
        Assert.Equal(new[] { -20, -15 }.Select(days => ActivityRemainingFixture.SourceNow.AddDays(days)),
            sessions.Select(value => value.StartedAt));
        Assert.Equal(new long?[] { 3600, 7200 }, sessions.Select(value => value.DurationSeconds));
        Assert.All(sessions, value =>
        {
            Assert.Equal(1, value.OwnershipId);
            Assert.Equal("process_watch", value.DetectionMethod);
            Assert.Equal(value.StartedAt.AddSeconds(value.DurationSeconds!.Value), value.EndedAt);
        });
        Assert.Empty(details.Events);
    }

    [Fact]
    public async Task Full_source_large_history_reaches_details_weekly_activity_and_account_summary_over_HTTP()
    {
        await using var host = await Fixture.StartAsync();
        ActivityRemainingFixture.SeedLarge(host.Database);
        var counts = ActivityRemainingFixture.Counts(host.Database);
        Assert.Equal(new Dictionary<string, int>
        {
            ["works"] = 2000, ["releases"] = 2000, ["ownerships"] = 2000,
            ["sessions"] = 125000, ["session_notes"] = 125000, ["playtime_snapshots"] = 14600,
            ["update_events"] = 2000, ["account_transactions"] = 40000,
        }.OrderBy(value => value.Key), counts.OrderBy(value => value.Key));
        Assert.Equal(2000, (await host.Api.GetLibraryAsync()).Games.Count);

        var details = await Measure("details", () => host.Details.GetAsync(1));
        Assert.Equal(1, details.WorkId);
        Assert.Equal(5060, details.Sessions[1].Count);
        Assert.Equal(5060, details.JournalEntries.Count);
        Assert.All(details.Sessions[1], value =>
        {
            Assert.Equal(1, value.OwnershipId);
            Assert.Equal(3600, value.DurationSeconds);
            Assert.Equal("process_watch", value.DetectionMethod);
        });
        Assert.All(details.JournalEntries, value =>
        {
            Assert.Equal("A preserved journal entry", value.Note);
            Assert.Equal(4, value.Rating);
        });
        var history = details.History[1].OrderBy(value => value.PlaytimeMinutes).ToArray();
        Assert.Equal(Enumerable.Range(1, 14600).Select(value => value * 10L), history.Select(value => value.PlaytimeMinutes));
        Assert.Equal("Older update", Assert.Single(details.Events).Title);

        var until = DateTime.UtcNow;
        var week = await Measure("weekly activity", () => host.Details.GetActivityAsync(
            new(until.AddDays(-7), until, ActivitySection.Sessions)));
        Assert.NotEmpty(week.Rows);
        Assert.All(week.Rows, value =>
        {
            Assert.Equal("steam", value.Store);
            Assert.Equal(3600, Assert.IsType<Session>(value.Session).DurationSeconds);
            Assert.Equal("A preserved journal entry", Assert.IsType<SessionNote>(value.Note).Note);
            Assert.Equal(4, value.Note!.Rating);
        });

        var account = await Measure("account summary", () => host.Details.GetAccountStatsAsync("steam"));
        Assert.True(account.HasAnything);
        Assert.Equal(40000, account.TransactionCount);
        Assert.Equal(40000, account.GrossProductTransactionCount);
        Assert.Equal(40000, account.NetProductTransactionCount);
        Assert.Equal(40000000, account.GrossProductSpendCents);
        Assert.Equal(40000000, account.NetProductSpendCents);
        Assert.Equal(0, account.RefundedProductSpendCents);
        Assert.Equal(0, account.LicenseCount);
        Assert.Equal("$", account.CurrencySymbol);
        Assert.Equal(new AccountCurrencyUse("$", 40000), Assert.Single(account.Currencies));

        async Task<T> Measure<T>(string name, Func<Task<T>> read)
        {
            host.Database.Start(name);
            var result = await read();
            var measurement = host.Database.End();
            Assert.False(measurement.Exceeded);
            Assert.True(measurement.RepositoryLeases > 0);
            Assert.Equal(Environment.ProcessId, Assert.Single(measurement.LeaseProcessIds));
            output.WriteLine(JsonSerializer.Serialize(measurement));
            return result;
        }
    }

    private sealed class Fixture(string directory, WebApplication app, WinnowApiClient api,
        ActivityReadTrackingFactory database) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public DetailsClient Details { get; } = new(api);
        public ActivityReadTrackingFactory Database => database;
        public static async Task<Fixture> StartAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-activity-remaining-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new ActivityReadTrackingFactory(new SqliteConnectionFactory(Path.Combine(directory, "winnow.db"), pooling: false));
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"],
                services => services.AddSingleton<ISqliteConnectionFactory>(database));
            await app.StartAsync();
            return new(directory, app, WinnowApiClient.Attach(directory), database);
        }
        public async ValueTask DisposeAsync()
        {
            api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
