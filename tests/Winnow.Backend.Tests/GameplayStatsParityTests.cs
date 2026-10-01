using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Core.Queries;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class GameplayStatsParityTests
{
    private static GameplayStatisticsRequest Request(string? store = null) => new(
        GameplayStatsFixture.Now.AddDays(-30), GameplayStatsFixture.Now, GameplayStatsFixture.Now,
        [new(GameplayStatsFixture.Now.AddDays(-30), GameplayStatsFixture.Now)], store);

    [Fact]
    public async Task Hidden_scope_rebuilds_real_ownerships_while_an_ignored_cancelled_read_cannot_complete_its_HTTP_client()
    {
        await using var host = await Host.StartAsync("scope");
        host.Controls.Arm(new(Target: "any"));
        using var cancel = new CancellationTokenSource();
        var old = host.Details.GetGameplayStatsAsync(Request(), cancel.Token);
        await WaitAsync(() => host.Controls.Calls.Count == 1);
        var oldCall = host.Controls.Calls[0];
        Assert.Equal(new long[] { 1, 2 }, oldCall.Request.Ownerships.Select(x => x.OwnershipId).Order());
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        await WaitAsync(() => oldCall.Token.IsCancellationRequested);
        await host.Fixture.ChangeAsync("hide2");
        host.Controls.Arm(new("value", 3600, Target: "any"));
        var current = await host.Details.GetGameplayStatsAsync(Request());
        Assert.Equal(3600, current.RecordedSeconds);
        Assert.Equal(new GameplayOwnershipScope(1, 1), Assert.Single(host.Controls.Calls[1].Request.Ownerships));
        host.Controls.Release(oldCall.GateId, 999999);
        await WaitAsync(() => oldCall.Completed);
        Assert.True(old.IsCanceled);
        Assert.Equal(1, Assert.Single((await host.Api.GetLibraryAsync()).Games).WorkId);
    }

    [Theory]
    [InlineData("gog")]
    [InlineData("plugin:xbox")]
    public async Task Same_game_links_and_removed_store_preserve_the_source_full_ownership_mapping_over_HTTP(string store)
    {
        await using var host = await Host.StartAsync("scope", store);
        await host.Details.GetGameplayStatsAsync(Request(store));
        Assert.Equal(2, host.Controls.Calls[0].Request.Ownerships.Count);
        await host.Fixture.ChangeAsync("link1-2");
        await host.Details.GetGameplayStatsAsync(Request(store));
        var linked = host.Controls.Calls[1].Request;
        Assert.Equal(store, linked.Store);
        Assert.Equal(2, linked.Ownerships.Count);
        Assert.All(linked.Ownerships, item => Assert.Equal(1, item.ResolvedWorkId));
        Assert.Equal(2, Assert.Single((await host.Api.GetLibraryAsync()).Games).Entries.Count);
        await host.Fixture.ChangeAsync("remove2");
        await host.Details.GetGameplayStatsAsync(Request());
        var removed = host.Controls.Calls[2].Request;
        Assert.Null(removed.Store);
        Assert.Equal(new GameplayOwnershipScope(1, 1), Assert.Single(removed.Ownerships));
        Assert.Equal("steam", Assert.Single(Assert.Single((await host.Api.GetLibraryAsync()).Games).Entries).Store);
    }

    [Fact]
    public async Task Matching_replacement_reads_share_current_facts_while_nonmatching_and_obsolete_reads_remain_distinct()
    {
        await using var host = await Host.StartAsync("scope");
        host.Controls.Arm(new(Target: "any"));
        using var cancel = new CancellationTokenSource();
        var old = host.Details.GetGameplayStatsAsync(Request(), cancel.Token);
        await WaitAsync(() => host.Controls.Calls.Count == 1);
        var oldCall = host.Controls.Calls[0];
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        await WaitAsync(() => oldCall.Token.IsCancellationRequested);

        host.Controls.Arm(new("value", 3600, Target: "any", Repeat: true, Match: new(null, [new(1, 1)])));
        Assert.Equal(0, (await host.Details.GetGameplayStatsAsync(Request())).RecordedSeconds);
        await host.Fixture.ChangeAsync("hide2");
        Assert.Equal(0, (await host.Details.GetGameplayStatsAsync(Request("gog"))).RecordedSeconds);
        Assert.Equal(3600, (await host.Details.GetGameplayStatsAsync(Request())).RecordedSeconds);
        Assert.Equal(3600, (await host.Details.GetGameplayStatsAsync(Request())).RecordedSeconds);

        var calls = host.Controls.Calls;
        Assert.Null(calls[1].GateId);
        Assert.Null(calls[2].GateId);
        Assert.NotNull(calls[3].GateId);
        Assert.Equal(calls[3].GateId, calls[4].GateId);
        Assert.Equal(new GameplayOwnershipScope(1, 1), Assert.Single(calls[4].Request.Ownerships));
        Assert.False(oldCall.Completed);
        host.Controls.Release(oldCall.GateId, 999999);
        await WaitAsync(() => oldCall.Completed);
        Assert.True(old.IsCanceled);
    }

    [Fact]
    public async Task History_reads_ending_at_as_of_do_not_consume_dashboard_gates()
    {
        await using var host = await Host.StartAsync("scope");
        host.Controls.Arm(new());
        var history = await host.Details.GetGameplayStatsAsync(Request());
        Assert.Equal(0, history.RecordedSeconds);
        var historyCall = Assert.Single(host.Controls.Calls);
        Assert.False(historyCall.IsDashboard);
        Assert.Null(historyCall.GateId);

        var dashboardEnd = GameplayStatsFixture.Now.Date.AddDays(1);
        var dashboard = host.Details.GetGameplayStatsAsync(Request() with
        {
            UntilUtc = dashboardEnd,
            TimeBins = [new(GameplayStatsFixture.Now.AddDays(-30), dashboardEnd)],
        });
        await WaitAsync(() => host.Controls.Calls.Count == 2);
        var dashboardCall = host.Controls.Calls[1];
        Assert.True(dashboardCall.IsDashboard);
        Assert.NotNull(dashboardCall.GateId);
        Assert.False(dashboardCall.Completed);
        Assert.False(dashboard.IsCompleted);
        host.Controls.Release(dashboardCall.GateId, 3600);
        Assert.Equal(3600, (await dashboard).RecordedSeconds);
    }

    [Theory]
    [InlineData("2026-03-08", 23)]
    [InlineData("2026-11-01", 25)]
    public async Task Exact_Los_Angeles_calendar_days_reach_the_repository_as_one_UTC_bin(string date, int hours)
    {
        await using var host = await Host.StartAsync("scope");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var day = DateOnly.Parse(date);
        var from = TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), zone);
        var until = TimeZoneInfo.ConvertTimeToUtc(day.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        await host.Details.GetGameplayStatsAsync(new(from, until, GameplayStatsFixture.Now, [new(from, until)]));
        var request = Assert.Single(host.Controls.Calls).Request;
        Assert.Equal(hours, (request.UntilUtc - request.FromUtc).TotalHours);
        Assert.Equal(new GameplayTimeBin(from, until), Assert.Single(request.TimeBins));
        Assert.Equal(DateTimeKind.Utc, request.TimeBins[0].FromUtc.Kind);
        Assert.Equal(GameplayStatsFixture.Now, request.AsOfUtc);
    }

    [Fact]
    public async Task Exact_full_preview_library_and_statistics_formula_cross_the_real_HTTP_projection()
    {
        await using var host = await Host.StartAsync("preview");
        var library = await host.Api.GetLibraryAsync();
        Assert.Equal(8, library.Games.Count);
        Assert.Equal(9, library.Games.Sum(game => game.Entries.Count));
        Assert.Equal(new long[] { 203, 230 }, library.Games.Single(game => game.WorkId == 3).Entries.Select(entry => entry.OwnershipId).Order());
        Assert.Equal(new[] { "Baldur's Gate 3", "Celeste", "Disco Elysium", "Hollow Knight", "Portal 2", "Slay the Spire", "Stardew Valley", "The Witcher 3: Wild Hunt" }, library.Games.Select(game => game.Title));
        var request = Request() with { TimeBins = [new(GameplayStatsFixture.Now.AddDays(-30), GameplayStatsFixture.Now.AddDays(-15)), new(GameplayStatsFixture.Now.AddDays(-15), GameplayStatsFixture.Now)] };
        var stats = await host.Details.GetGameplayStatsAsync(request);
        Assert.Equal(113400, stats.RecordedSeconds);
        Assert.Equal((6, 24, 23, 2700d, 1), (stats.GamesPlayedCount, stats.OverlappingSessionCount, stats.StartedSessionCount, stats.MedianSessionSeconds, stats.ExcludedSessionCount));
        Assert.Equal(new double[] { 32400, 27000, 21600, 16200, 10800, 5400 }, stats.TopGames.Select(row => row.RecordedSeconds));
        Assert.Equal(new double[] { 37800, 75600 }, stats.Periods.Select(row => row.RecordedSeconds));
        Assert.Equal(new[] { 5, 9, 6, 3 }, stats.SessionLengths.Select(row => row.Count));
        Assert.Equal(9, Assert.Single(host.Controls.Calls).Request.Ownerships.Count);
        Assert.Equal(8, host.Controls.Calls[0].Request.Ownerships.Select(item => item.ResolvedWorkId).Distinct().Count());
        var account = await host.Details.GetAccountStatsAsync("steam");
        Assert.Equal(2000, account.NetProductSpendCents);
        Assert.Equal(1, account.TransactionCount);
        Assert.Equal("$", account.CurrencySymbol);
        Assert.Equal(new AccountSpendYear(2026, 1, 2000), Assert.Single(account.SpendByYear));
    }

    [Fact]
    public async Task Xbox_import_changes_real_library_store_counts_without_inventing_sessions()
    {
        await using var host = await Host.StartAsync("xbox");
        Assert.All((await host.Api.GetLibraryAsync()).Games.SelectMany(game => game.Entries), entry => Assert.Equal("steam", entry.Store));
        await host.Fixture.ChangeAsync("import-xbox");
        var library = await host.Api.GetLibraryAsync();
        Assert.Equal(new[] { "plugin:xbox", "steam" }, library.Games.SelectMany(game => game.Entries).Select(entry => entry.Store).Order());
        var stats = await host.Details.GetGameplayStatsAsync(Request("plugin:xbox"));
        Assert.Equal(0, stats.RecordedSeconds);
        Assert.Null(stats.MedianSessionSeconds);
        Assert.Empty(stats.TopGames);
        Assert.Equal(2, Assert.Single(host.Controls.Calls).Request.Ownerships.Count);
        Assert.Equal("plugin:xbox", host.Controls.Calls[0].Request.Store);
    }

    [Fact]
    public async Task Real_recorded_sessions_obey_store_visibility_and_resolved_game_scope()
    {
        await using var host = await Host.StartAsync("actual");
        var all = await host.Details.GetGameplayStatsAsync(Request());
        Assert.Equal((10800d, 2, 2, 5400d), (all.RecordedSeconds, all.GamesPlayedCount, all.StartedSessionCount, all.MedianSessionSeconds));
        Assert.Equal(7200, (await host.Details.GetGameplayStatsAsync(Request("gog"))).RecordedSeconds);
        await host.Fixture.ChangeAsync("link1-2");
        var linked = await host.Details.GetGameplayStatsAsync(Request());
        Assert.Equal(new GameplayGameTotal(1, 10800), Assert.Single(linked.TopGames));
        Assert.Equal(1, linked.GamesPlayedCount);
        await host.Fixture.ChangeAsync("remove2");
        var removed = await host.Details.GetGameplayStatsAsync(Request());
        Assert.Equal(3600, removed.RecordedSeconds);
        Assert.Equal(1, removed.StartedSessionCount);
        Assert.Equal("steam", Assert.Single(removed.Stores).Store);
    }

    [Fact]
    public async Task Source_reader_failure_retry_and_cancellation_do_not_replace_a_new_success()
    {
        await using var host = await Host.StartAsync("preview");
        host.Controls.Arm(new("fail", Target: "any"));
        var error = await Assert.ThrowsAsync<BackendApiException>(() => host.Details.GetGameplayStatsAsync(Request()));
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        host.Controls.Arm(new(Target: "any"));
        using var cancel = new CancellationTokenSource();
        var old = host.Details.GetGameplayStatsAsync(Request(), cancel.Token);
        await WaitAsync(() => host.Controls.Calls.Count == 2);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        await WaitAsync(() => host.Controls.Calls[1].Token.IsCancellationRequested);
        var current = await host.Details.GetGameplayStatsAsync(Request());
        Assert.Equal(113400, current.RecordedSeconds);
        host.Controls.Release(seconds: 0);
        await WaitAsync(() => host.Controls.Calls[1].Completed);
        Assert.True(old.IsCanceled);
        Assert.Equal(3, host.Controls.Calls.Count);
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, deadline.Token);
    }
    private sealed class Host(string directory, WebApplication app, WinnowApiClient api) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public DetailsClient Details { get; } = new(api);
        public GameplayStatsFixture Fixture => app.Services.GetRequiredService<GameplayStatsFixture>();
        public GameplayFixtureControls Controls => app.Services.GetRequiredService<GameplayFixtureControls>();
        public static async Task<Host> StartAsync(string kind, string store = "gog")
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-gameplay-stats-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], GameplayStatsFixture.Register);
            await app.StartAsync();
            await app.Services.GetRequiredService<GameplayStatsFixture>().SeedAsync(kind, store);
            return new(directory, app, WinnowApiClient.Attach(directory));
        }
        public async ValueTask DisposeAsync()
        {
            Controls.Release(); api.Dispose();
            await app.StopAsync(); await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
