using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.App.Services;
using Winnow.Electron.Fixtures;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class RecommendationStateParityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_cold_engine_preserves_canonical_title_and_selected_release_through_impression_verdict_and_undo(bool installedSibling)
    {
        await using var host = await Host.StartAsync(new("composition", InstalledSibling: installedSibling));
        var feed = await host.ReadAsync();
        var shelf = Assert.Single(feed.Shelves);
        Assert.Equal(installedSibling ? "ready_to_play" : "waiting_to_be_opened", shelf.Id);
        Assert.False(feed.Failed);
        var card = Assert.Single(shelf.Items);
        var release = installedSibling ? 2 : 1;
        Assert.Equal(release, card.ReleaseId);
        Assert.Equal(release, card.OwnershipId);
        Assert.False(string.IsNullOrWhiteSpace(card.Reason));
        Assert.DoesNotContain("bought", card.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paid for", card.Reason, StringComparison.OrdinalIgnoreCase);
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal("Kept title", game.Title);
        Assert.Equal(1, game.WorkId);
        Assert.Equal(new long[] { 1, 2 }, game.Entries.Select(entry => entry.ReleaseId));
        Assert.Equal(new[] { false, installedSibling }, game.Entries.Select(entry => entry.Installed));
        Assert.Empty(host.Rows("surfacings"));
        await host.Feed.RecordSurfacedAsync(release, shelf.Id);
        var seen = Assert.Single(host.Rows("surfacings"));
        Assert.Equal(release, seen.GetProperty("releaseId").GetInt64());
        Assert.Equal(shelf.Id, seen.GetProperty("shelfId").GetString());
        Assert.Equal("2026-08-27", seen.GetProperty("surfacedOn").GetString());
        Assert.True((await host.Feed.RecordVerdictAsync(release, FeedVerdictKind.NotInterested)).Saved);
        var history = Assert.Single(await host.Feed.GetHistoryAsync());
        Assert.Equal(release, history.ReleaseId);
        Assert.Equal(FeedVerdictStatus.Active, history.Status);
        Assert.Null(history.ExpiresAt);
        Assert.True(await host.Feed.RevokeVerdictAsync(release, FeedVerdictKind.NotInterested));
        Assert.Equal(FeedVerdictStatus.Undone, Assert.Single(await host.Feed.GetHistoryAsync()).Status);
        Assert.NotEqual(JsonValueKind.Null, Assert.Single(host.Rows("verdicts")).GetProperty("revokedAt").ValueKind);
    }

    [Fact]
    public async Task An_absent_feedback_store_removes_capability_without_removing_the_game_or_its_other_actions()
    {
        await using var host = await Host.StartAsync(new("history"), noFeedback: true);
        var snapshot = await host.ReadAsync();
        var shelf = Assert.Single(snapshot.Shelves);
        Assert.False(shelf.SupportsFeedback);
        Assert.Equal("Pitch.", shelf.Blurb);
        Assert.Equal(new FeedItem(1, 1, "Deep Rock Galactic 1", "You put 2.8 hours into this in 2021 and it has had an update since."), Assert.Single(shelf.Items));
        Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.False((await host.Feed.RecordVerdictAsync(1, FeedVerdictKind.NotInterested)).Saved);
        await host.Feed.RecordSurfacedAsync(1, shelf.Id);
        Assert.Empty(host.Rows("verdicts"));
        Assert.Empty(host.Rows("surfacings"));
    }

    [Fact]
    public async Task Five_source_shelves_keep_all_titles_pitches_reasons_and_engine_candidate_count_over_HTTP()
    {
        await using var host = await Host.StartAsync(new("five"));
        var feed = await host.ReadAsync();
        Assert.Equal(997, feed.CandidateCount);
        Assert.Equal(FeedConfidence.Settling, feed.Confidence);
        Assert.False(feed.Failed);
        Assert.Equal(new[] { "patched_while_away", "worth_another_look", "ready_to_play", "barely_touched", "on_your_taste" }, feed.Shelves.Select(shelf => shelf.Id));
        Assert.Equal(new[] { "Patched while you were away", "Worth another look", "Installed and waiting", "Barely gave it a chance", "Never opened, right up your alley" }, feed.Shelves.Select(shelf => shelf.Title));
        Assert.Equal(new[]
        {
            "Major updates landed after you stopped playing.",
            "You committed real hours past the refund line, then drifted off mid-story.",
            "Already on your disk with nothing sunk.",
            "Under 2 hours in — you opened the door and never walked through.",
            "Sitting sealed in your library, and it matches where your hours actually go.",
        }, feed.Shelves.Select(shelf => shelf.Blurb));
        Assert.Equal(new[]
        {
            "You put 2.8 hours into this in 2021 and it has had an update since, most recently \"PATCH NOTES - S06.05.02\". This matches your taste in Survival games.",
            "You put 2.5 hours in — past the refund line — then let it go — that was 2022.",
            "Never opened since it joined your library. It's installed and ready to launch.",
            "You tried it for 104 minutes and never went back — that was 2017.",
            "Never opened since it joined your library. This matches your taste in Sandbox games.",
        }, feed.Shelves.Select(shelf => Assert.Single(shelf.Items).Reason));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, feed.Shelves.Select(shelf => Assert.Single(shelf.Items).ReleaseId));
        Assert.Equal(6, host.Rows("lookupRows").Length); // Final99 is lookup-only until explicitly selected.
        Assert.Empty(host.Rows("surfacings"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_eight_item_order_survives_primary_or_optional_transport_without_recording_impressions(bool supplemental)
    {
        await using var host = await Host.StartAsync(new("excess8", Supplemental: supplemental));
        var feed = await host.ReadAsync();
        var shelf = supplemental ? Assert.Single((await host.Api.GetAsync<FeedSupplement>("feed/supplement")).Shelves) : Assert.Single(feed.Shelves);
        Assert.Equal(supplemental ? "plugin:extra" : "patched", shelf.Id);
        Assert.Equal("Shelf", shelf.Title);
        Assert.Equal("", shelf.Blurb);
        Assert.Equal(Enumerable.Range(1, 8).Select(id => (long)id), shelf.Items.Concat(shelf.Reserve).Select(item => item.ReleaseId));
        Assert.Equal(Enumerable.Range(1, 8).Select(id => $"Reason {id}"), shelf.Items.Concat(shelf.Reserve).Select(item => item.Reason));
        Assert.Equal(6, shelf.Items.Count);
        Assert.Equal(new long[] { 7, 8 }, shelf.Reserve.Select(item => item.ReleaseId));
        Assert.Empty(host.Rows("surfacings"));
    }

    [Fact]
    public async Task Source_reserve_backfill_keeps_the_exact_next_population_separate_from_display_and_feedback()
    {
        await using var host = await Host.StartAsync(new("reserve"));
        var initial = Assert.Single((await host.ReadAsync()).Shelves);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 101 }, initial.Items.Select(item => item.ReleaseId));
        Assert.Equal("Installed and waiting", initial.Title);
        Assert.Equal("Already on your disk, nothing sunk.", initial.Blurb);
        Assert.Equal("Never opened since it joined your library. (Held 101)", initial.Items[^1].Reason);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 101, 200, 201, 202, 203, 204, 300 }, host.Rows("lookupRows").Select(row => row.GetProperty("releaseId").GetInt64()));
        host.Fixture.Next("reserve-next");
        await host.Feed.RecordVerdictAsync(1, FeedVerdictKind.NotInterested);
        var next = await host.ReadAsync();
        Assert.Equal(997, next.CandidateCount);
        Assert.Equal(new long[] { 200, 201, 202, 203, 204, 300 }, Assert.Single(next.Shelves).Items.Select(item => item.ReleaseId));
        Assert.Equal("Never opened since it joined your library. (Held 300)", next.Shelves[0].Items[^1].Reason);
        Assert.Empty(host.Rows("surfacings"));
        Assert.Equal(new long[] { 1 }, host.Controls.Calls.Last().Request!.NotInterestedReleaseIds);
        await host.Feed.RecordSurfacedAsync(101, initial.Id);
        await host.Feed.RecordSurfacedAsync(101, initial.Id);
        Assert.Equal(101, Assert.Single(host.Rows("surfacings")).GetProperty("releaseId").GetInt64());
    }

    [Fact]
    public async Task All_thirteen_viewport_candidates_and_recent_history_semantics_cross_the_actual_API()
    {
        await using (var host = await Host.StartAsync(new("viewport13")))
        {
            var feed = await host.ReadAsync();
            Assert.Equal(12, feed.CandidateCount);
            Assert.Equal(FeedConfidence.Established, feed.Confidence);
            Assert.Equal(new[] { "shelf-0", "shelf-1" }, feed.Shelves.Select(shelf => shelf.Id));
            Assert.All(feed.Shelves, shelf => { Assert.Equal("A shelf", shelf.Title); Assert.Equal("Games to revisit", shelf.Blurb); Assert.Equal(6, shelf.Items.Count); });
            Assert.Equal(Enumerable.Range(1, 12).Select(id => (long)id), feed.Shelves.SelectMany(shelf => shelf.Items).Select(item => item.ReleaseId));
            Assert.All(feed.Shelves.SelectMany(shelf => shelf.Items), item => Assert.Equal("You last played this game a long time ago.", item.Reason));
            Assert.Equal(new FeedItem(13, 13, "Fixture 13", "A new reason for a held card."), Assert.Single(feed.Shelves[1].Reserve));
            Assert.Empty(host.Rows("surfacings"));
        }
        await using (var host = await Host.StartAsync(new("recent")))
        {
            var feed = await host.ReadAsync();
            Assert.Equal(new[] { "recently_played", "recommended" }, feed.Shelves.Select(shelf => shelf.Id));
            Assert.False(feed.Shelves[0].SupportsFeedback);
            Assert.True(feed.Shelves[1].SupportsFeedback);
            Assert.Equal(new FeedItem(1, 1, "First game", "Last played today."), Assert.Single(feed.Shelves[0].Items));
            await host.Feed.RecordSurfacedAsync(1, "recently_played");
            Assert.Empty(host.Rows("surfacings"));
            await host.Feed.RecordSurfacedAsync(1, "recommended");
            Assert.Single(host.Rows("surfacings"));
        }
    }

    [Fact]
    public async Task Held_primary_captures_its_generation_while_three_library_changes_leave_the_final_snapshot_available()
    {
        await using var host = await Host.StartAsync(new("invalidation", HoldBuiltin: true));
        using var cancellation = new CancellationTokenSource();
        var old = host.ReadAsync(cancellation.Token);
        await WaitAsync(() => host.Controls.Calls.Count == 1);
        var call = Assert.Single(host.Controls.Calls);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, call.ReleaseIds);
        host.Fixture.Next("final");
        await host.Fixture.PublishAsync(3);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        await WaitAsync(() => call.Canceled);
        Assert.False(call.Completed);
        var final = await host.ReadAsync();
        Assert.Equal(new FeedItem(99, 99, "Deep Rock Galactic 99", "The final library state."), Assert.Single(Assert.Single(final.Shelves).Items));
        Assert.Equal(2, host.Controls.Calls.Count);
        host.Controls.Release(call.GateId);
        await WaitAsync(() => call.Completed);
        Assert.True(old.IsCanceled);
    }

    [Theory]
    [InlineData("optional", "First game", "Baseline reason", "Second game", "Optional reason", "Optional")]
    [InlineData("optional-stale", "Deep Rock Galactic 1", "Baseline", "Deep Rock Galactic 2", "Optional", "Extra")]
    public async Task Authenticated_optional_source_task_preserves_exact_payload_and_observes_cancellation_without_blocking_primary(
        string kind, string first, string baseline, string second, string optional, string title)
    {
        await using var host = await Host.StartAsync(new(kind));
        using var anonymous = new HttpClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(host.Address + "/api/v1/feed/supplement")).StatusCode);
        Assert.Empty(host.Controls.Calls);
        using var cancellation = new CancellationTokenSource();
        var old = host.Api.GetAsync<FeedSupplement>("feed/supplement", cancellation.Token);
        await WaitAsync(() => host.Controls.Calls.Count == 1);
        var call = Assert.Single(host.Controls.Calls);
        Assert.Equal("supplement", call.Operation);
        var primary = await host.ReadAsync();
        Assert.Equal(new FeedItem(1, 1, first, baseline), Assert.Single(Assert.Single(primary.Shelves).Items));
        Assert.False(old.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        await WaitAsync(() => call.Canceled);
        Assert.False(call.Completed);
        host.Controls.Release(call.GateId);
        await WaitAsync(() => call.Completed);
        var retry = await host.Api.GetAsync<FeedSupplement>("feed/supplement");
        Assert.Equal(2, retry.CandidateCount);
        var shelf = Assert.Single(retry.Shelves);
        Assert.Equal("plugin:extra", shelf.Id);
        Assert.Equal(title, shelf.Title);
        Assert.Equal(new FeedItem(2, 2, second, optional), Assert.Single(shelf.Items));
        Assert.Empty(host.Rows("surfacings"));
    }

    [Fact]
    public async Task Replacing_the_optional_generation_does_not_mutate_the_already_captured_held_source_task()
    {
        await using var host = await Host.StartAsync(new("optional-stale"));
        var old = host.Api.GetAsync<FeedSupplement>("feed/supplement");
        await WaitAsync(() => host.Controls.Calls.Count == 1);
        var call = Assert.Single(host.Controls.Calls);
        host.Fixture.Next("builtin");
        var current = await host.Api.GetAsync<FeedSupplement>("feed/supplement");
        Assert.Empty(current.Shelves);
        Assert.False(old.IsCompleted);
        Assert.True((await host.Feed.RecordVerdictAsync(1, FeedVerdictKind.NotInterested)).Saved);
        await host.ReadAsync();
        Assert.Equal(new long[] { 1 }, host.Controls.Calls.Last().Request!.NotInterestedReleaseIds);
        host.Controls.Release(call.GateId);
        var stale = await old;
        Assert.Equal(new FeedItem(2, 2, "Deep Rock Galactic 2", "Optional"), Assert.Single(Assert.Single(stale.Shelves).Items));
        Assert.True(call.Completed);
        Assert.Empty((await host.Api.GetAsync<FeedSupplement>("feed/supplement")).Shelves);
    }

    [Fact]
    public async Task A_throwing_source_engine_returns_the_production_unavailable_contract_with_the_source_empty_library()
    {
        await using var host = await Host.StartAsync(new("throwing"));
        var feed = await host.ReadAsync();
        Assert.True(feed.Failed);
        Assert.Empty(feed.Shelves);
        Assert.Equal(0, feed.CandidateCount);
        Assert.True(Assert.Single(host.Controls.Calls).Failed);
        Assert.Empty((await host.Api.GetLibraryAsync()).Games);
        Assert.Empty(host.Rows("surfacings"));
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, deadline.Token);
    }
    private sealed class Host(string directory, WebApplication app, WinnowApiClient api) : IAsyncDisposable
    {
        public string Address => app.Urls.Single();
        public WinnowApiClient Api => api;
        public ApiFeedService Feed { get; } = new(api);
        public RecommendationStateFixture Fixture => app.Services.GetRequiredService<RecommendationStateFixture>();
        public RecommendationControls Controls => app.Services.GetRequiredService<RecommendationControls>();
        public Task<FeedSnapshot> ReadAsync(CancellationToken ct = default) => api.GetAsync<FeedSnapshot>("feed", ct);
        public JsonElement[] Rows(string property) => JsonSerializer.SerializeToElement(Fixture.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web)).GetProperty(property).EnumerateArray().ToArray();
        public static async Task<Host> StartAsync(RecommendationSeed seed, bool noFeedback = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-recommendation-state-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services => RecommendationStateFixture.Register(services, noFeedback));
            RecommendationStateFixture.Map(app);
            await app.StartAsync();
            await app.Services.GetRequiredService<RecommendationStateFixture>().SeedAsync(seed with { Publish = false });
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
