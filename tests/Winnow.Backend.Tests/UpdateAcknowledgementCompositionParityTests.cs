using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Electron.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Winnow.Backend.Tests;

public sealed class UpdateAcknowledgementCompositionParityTests(ITestOutputHelper output)
{
    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Original_grouped_Details_marks_and_restores_only_its_two_displayed_release_watermarks()
    {
        await using var host = await Host.StartAsync(new());
        var source = await host.State();
        Assert.Equal(12, source.Events.Count);
        Assert.Equal(Enumerable.Range(1, 12).Select(value => (long)value), source.Events.Select(value => value.Id));
        Assert.Equal(new long[] { 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3 }, source.Events.Select(value => value.ReleaseId));
        Assert.Equal(new[] { Utc(2024, 7, 1), Utc(2024, 7, 2), Utc(2025, 1, 1), Utc(2025, 1, 2),
            Utc(2026, 6, 1), Utc(2026, 6, 2), Utc(2026, 6, 1), Utc(2026, 6, 2), Utc(2026, 8, 1),
            Utc(2026, 8, 2), Utc(2026, 6, 1), Utc(2026, 6, 2) }, source.Events.Select(value => value.OccurredAt));
        Assert.Equal(Enumerable.Range(0, 12).Select(index => index % 2 == 0 ? "build_push" : "announcement"), source.Events.Select(value => value.Kind));
        Assert.Equal(2, Group(source, 1).UnreadUpdateCount); Assert.Equal(0, Group(source, 2).UnreadUpdateCount);
        Assert.Equal(Utc(2025, 1, 1), Group(source, 1).LastPlayedAt); Assert.Equal(1200, Group(source, 1).PlaytimeMinutes);
        Assert.All(Assert.Single(source.Library.Games, game => game.WorkId == 1).Entries, entry => Assert.Equal(600, entry.PlaytimeMinutes));
        var opened = await host.Details();
        Assert.Equal(10, opened.Events.Count); Assert.Equal(new long[] { 1, 2 }, opened.Ownerships.Select(owner => owner.ReleaseId));
        Assert.Empty(opened.Acknowledgements);
        var feedBefore = await host.Api.GetAsync<FeedSnapshot>("feed");
        var first = await host.Acknowledge(1, opened);
        var second = await host.Acknowledge(2, opened);
        Assert.Equal(new AcknowledgementResponse("Stored", Utc(2026, 6, 1)), first);
        Assert.Equal(new AcknowledgementResponse("Stored", Utc(2026, 8, 1)), second);
        var read = await host.State();
        Assert.Equal(new long[] { 1, 2 }, read.Acknowledgements.Select(ack => ack.ReleaseId));
        Assert.All(read.Acknowledgements, ack => Assert.Null(ack.RevokedAt));
        Assert.Equal(0, Group(read, 1).UnreadUpdateCount); Assert.NotEqual(LibraryBuckets.StaleButPatched, Group(read, 1).Bucket);
        var after = await host.Details();
        Assert.Equal(Utc(2026, 6, 1), after.Acknowledgements[1]); Assert.Equal(Utc(2026, 8, 1), after.Acknowledgements[2]);
        Assert.Equal(JsonSerializer.Serialize(opened.Events), JsonSerializer.Serialize(after.Events));
        var feedRead = await host.Api.GetAsync<FeedSnapshot>("feed");
        Assert.Equal("Stored", (await host.Restore(1)).Result); Assert.Equal("Stored", (await host.Restore(2)).Result);
        var restored = await host.State();
        Assert.Equal(2, restored.Acknowledgements.Count); Assert.All(restored.Acknowledgements, ack => Assert.NotNull(ack.RevokedAt));
        Assert.Equal(2, Group(restored, 1).UnreadUpdateCount); Assert.Equal(LibraryBuckets.StaleButPatched, Group(restored, 1).Bucket);
        Assert.Empty((await host.Details()).Acknowledgements);
        Assert.All(restored.Calls, call => { Assert.True(call.Completed); Assert.Equal(200, call.StatusCode); Assert.Equal("Stored", call.Result); });
        Assert.Empty(restored.ProviderRequests);
        output.WriteLine("Actual source-population feed before: " + JsonSerializer.Serialize(feedBefore));
        output.WriteLine("Actual source-population feed after reading: " + JsonSerializer.Serialize(feedRead));
    }

    [Fact]
    public async Task Captured_event_ids_cannot_acknowledge_the_unpublished_later_push_and_reopen_reads_standing_watermarks()
    {
        await using var host = await Host.StartAsync(new());
        var opened = await host.Details();
        var watermark = Group(await host.State(), 1).MajorUpdateAt;
        await host.Change("later-push");
        await host.Acknowledge(1, opened); await host.Acknowledge(2, opened);
        var state = await host.State();
        Assert.Equal(14, state.Events.Count); Assert.Equal(2, state.Acknowledgements.Count);
        Assert.All(state.Acknowledgements, ack => Assert.True(ack.AcknowledgedThrough <= watermark));
        Assert.Equal(1, Group(state, 1).UnreadUpdateCount); Assert.Equal(LibraryBuckets.StaleButPatched, Group(state, 1).Bucket);
        Assert.All(state.Calls, call => Assert.DoesNotContain(call.ObservedEventIds, id => id > 10));
        var reopened = await host.Details();
        var unread = UpdateReading.CorrelatedPushes(reopened.Events, BucketThresholds.Default.UpdateCorrelationWindowDays)
            .Where(push => UpdateReading.SincePlay(push.OccurredAt, Group(state, 1).LastPlayedAt, Group(state, 1).PlaytimeMinutes)
                && UpdateReading.AfterWatermark(push.OccurredAt, reopened.Acknowledgements.GetValueOrDefault(push.ReleaseId))).ToArray();
        var remaining = Assert.Single(unread); Assert.Equal(1, remaining.ReleaseId); Assert.Equal(Utc(2026, 9, 1), remaining.OccurredAt);
        Assert.All(reopened.Events.Where(row => row.ReleaseId == 2), row => Assert.True(row.OccurredAt <= Utc(2026, 8, 2)));
    }

    [Fact]
    public async Task Original_multiple_selection_reads_grouped_and_second_game_leaves_unselected_and_later_patch_unread()
    {
        await using var host = await Host.StartAsync(new("multiple"));
        var initial = await host.State();
        Assert.Equal(new long[] { 1, 4, 5 }, initial.Groups.Where(group => group.Bucket == LibraryBuckets.StaleButPatched).Select(group => group.ResolvedWorkId));
        var grouped = await host.Details(); var second = await host.Details(4);
        await host.Acknowledge(1, grouped); await host.Acknowledge(2, grouped); await host.Acknowledge(4, second);
        var read = await host.State();
        Assert.Equal(new long[] { 1, 2, 4 }, read.Acknowledgements.Select(ack => ack.ReleaseId));
        Assert.Equal(0, Group(read, 1).UnreadUpdateCount); Assert.Equal(0, Group(read, 4).UnreadUpdateCount);
        Assert.Equal(5, Assert.Single(read.Groups, group => group.Bucket == LibraryBuckets.StaleButPatched).ResolvedWorkId);
        Assert.Equal(1, Group(read, 5).UnreadUpdateCount);
        await host.Change("later-push", publish: true);
        var later = await host.State();
        Assert.Equal(new long[] { 1, 5 }, later.Groups.Where(group => group.Bucket == LibraryBuckets.StaleButPatched).Select(group => group.ResolvedWorkId));
        Assert.Equal(1, Group(later, 1).UnreadUpdateCount); Assert.Equal(0, Group(later, 4).UnreadUpdateCount);
    }

    [Fact]
    public async Task Original_SQLite_refusal_returns_NotStored_and_keeps_every_grouped_patch_unread()
    {
        await using var host = await Host.StartAsync(new("refusal-all"));
        var displayed = await host.Details();
        Assert.Equal("NotStored", (await host.Acknowledge(1, displayed)).Result);
        Assert.Equal("NotStored", (await host.Acknowledge(2, displayed)).Result);
        var state = await host.State(); Assert.Empty(state.Acknowledgements);
        Assert.Equal(2, Group(state, 1).UnreadUpdateCount); Assert.Equal(LibraryBuckets.StaleButPatched, Group(state, 1).Bucket);
        Assert.All(state.Calls, call => { Assert.True(call.Completed); Assert.Equal("NotStored", call.Result); });
        Assert.Equal(JsonSerializer.Serialize(displayed.Events), JsonSerializer.Serialize((await host.Details()).Events));
    }

    [Fact]
    public async Task Partial_release_write_survives_a_sibling_failure_and_retry_of_only_failed_release_has_no_duplicate_success()
    {
        await using var host = await Host.StartAsync(new("partial"));
        var displayed = await host.Details();
        Assert.Equal("Stored", (await host.Acknowledge(1, displayed)).Result);
        Assert.Equal("NotStored", (await host.Acknowledge(2, displayed)).Result);
        var partial = await host.State(); Assert.Equal(1, Assert.Single(partial.Acknowledgements).ReleaseId);
        Assert.Equal(2, Group(partial, 1).UnreadUpdateCount);
        await host.Change("clear-refusal");
        Assert.Equal("Stored", (await host.Acknowledge(2, displayed)).Result);
        var final = await host.State(); Assert.Equal(new long[] { 1, 2 }, final.Acknowledgements.Select(ack => ack.ReleaseId));
        Assert.Equal(new long[] { 1, 2, 2 }, final.Calls.Select(call => call.ReleaseId));
        Assert.Equal(new[] { "Stored", "NotStored", "Stored" }, final.Calls.Select(call => call.Result));
        Assert.Equal(0, Group(final, 1).UnreadUpdateCount);
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(600, false, 3)]
    [InlineData(0, true, 1)]
    [InlineData(600, true, 1)]
    public async Task Original_count_matrix_reaches_workspace_with_never_played_unknown_date_and_strict_effective_boundary(long minutes, bool hasDate, int expected)
    {
        await using var host = await Host.StartAsync(new("counts", minutes, hasDate));
        var workspace = await host.Api.GetAsync<LibraryWorkspaceResponse>("library/workspace");
        Assert.DoesNotContain(workspace.Ownerships, owner => owner.Id == 2);
        var owner = Assert.Single(workspace.Buckets, bucket => bucket.ReleaseId == 1);
        Assert.Equal(minutes, owner.Game.PlaytimeMinutes); Assert.Equal(hasDate ? Utc(2025, 1, 1) : (DateTime?)null, owner.Game.LastPlayedAt);
        Assert.Equal(expected, owner.Game.UnreadUpdateCount);
        Assert.Equal(12, (await host.State()).Events.Count); Assert.Empty((await host.State()).Acknowledgements);
    }

    [Fact]
    public async Task Observed_ids_do_not_cross_release_correlation_or_invent_a_watermark_without_a_pair()
    {
        await using var host = await Host.StartAsync(new());
        Assert.Equal("NothingToDo", (await host.AcknowledgeIds(1, [5, 8, 999])).Result);
        Assert.Equal("NothingToDo", (await host.AcknowledgeIds(1, [])).Result);
        Assert.Empty((await host.State()).Acknowledgements);
        Assert.Equal(new AcknowledgementResponse("Stored", Utc(2026, 6, 1)), await host.AcknowledgeIds(1, [5, 6, 7, 8, 999]));
        Assert.Equal(1, Assert.Single((await host.State()).Acknowledgements).ReleaseId);
    }

    [Fact]
    public async Task Real_bearer_authentication_protects_fixture_controls_and_release_writes()
    {
        await using var host = await Host.StartAsync(new());
        using var anonymous = new HttpClient { BaseAddress = host.Http.BaseAddress };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("__fixture/update-acknowledgement/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/v1/releases/1/acknowledge-updates", new UpdateAcknowledgementRequest([5, 6]))).StatusCode);
        Assert.Empty((await host.State()).Acknowledgements);
    }

    private static WorkspaceGameGrouping Group(AcknowledgementState state, long id) => Assert.Single(state.Groups, game => game.ResolvedWorkId == id);

    private sealed class Host(string directory) : IAsyncDisposable
    {
        private WebApplication _app = null!;
        public HttpClient Http { get; private set; } = null!;
        public WinnowApiClient Api { get; private set; } = null!;
        public static async Task<Host> StartAsync(AcknowledgementSeed seed)
        {
            var host = new Host(Path.Combine(Path.GetTempPath(), "winnow-acknowledgement-api-" + Guid.NewGuid().ToString("N")));
            try
            {
                host._app = BackendApplication.Build(["--data-dir", host.directory, "--no-sync"], UpdateAcknowledgementFixture.Register);
                UpdateAcknowledgementFixture.Map(host._app);
                await host._app.StartAsync();
                var endpoint = await BackendConnection.ReadAsync(host.directory);
                host.Http = new HttpClient { BaseAddress = new(endpoint.Address) };
                host.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
                host.Api = WinnowApiClient.Attach(host.directory);
                (await host.Http.PostAsJsonAsync("__fixture/update-acknowledgement/seed", seed)).EnsureSuccessStatusCode();
                return host;
            }
            catch { await host.DisposeAsync(); throw; }
        }
        private readonly string directory = directory;
        public async Task<AcknowledgementState> State() => (await Http.GetFromJsonAsync<AcknowledgementState>("__fixture/update-acknowledgement/state"))!;
        public async Task Change(string stage, bool publish = false) => (await Http.PostAsJsonAsync("__fixture/update-acknowledgement/change", new AcknowledgementChange(stage, publish))).EnsureSuccessStatusCode();
        public Task<GameDetailsResponse> Details(long workId = 1) => Api.GetAsync<GameDetailsResponse>($"games/{workId}/details");
        public Task<AcknowledgementResponse> Acknowledge(long releaseId, GameDetailsResponse displayed) => AcknowledgeIds(releaseId, displayed.Events.Where(row => row.ReleaseId == releaseId).Select(row => row.Id).ToArray());
        public Task<AcknowledgementResponse> AcknowledgeIds(long releaseId, IReadOnlyList<long> ids) => Api.SendAsync<UpdateAcknowledgementRequest, AcknowledgementResponse>(HttpMethod.Post,
            $"releases/{releaseId}/acknowledge-updates", new(ids));
        public Task<AcknowledgementResponse> Restore(long releaseId) => Api.SendAsync<object?, AcknowledgementResponse>(HttpMethod.Post, $"releases/{releaseId}/restore-updates", null);
        public async ValueTask DisposeAsync()
        {
            Api?.Dispose(); Http?.Dispose();
            if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
