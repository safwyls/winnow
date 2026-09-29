using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Xunit;

namespace Winnow.Backend.Tests;

public sealed class LibraryGrainParityTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Unlinked_source_entries_keep_their_identity_figures_and_bucket_over_HTTP()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Seed("Prey", 300, Now.AddDays(-30));
        await fixture.Seed("Prey", 90, Now.AddDays(-400), "epic");
        await fixture.Seed("Dishonored", 0, null);
        await fixture.Seed("Hades", 8000, Now.AddDays(-5), "gog");
        var rows = (await fixture.Api.GetWorkspaceAsync()).Buckets;
        var games = (await fixture.Api.GetLibraryAsync()).Games;
        Assert.Equal(4, games.Count);
        Assert.Equal(rows.Select(row => row.OwnershipId).Order(), games.Select(game => Assert.Single(game.Entries).OwnershipId).Order());
        foreach (var row in rows)
        {
            var game = Assert.Single(games, game => game.Entries[0].OwnershipId == row.OwnershipId);
            Assert.Equal(row.PlaytimeMinutes, game.PlaytimeMinutes);
            Assert.Equal(row.LastPlayedAt, game.LastPlayedAt);
            Assert.Equal(row.Bucket, game.Bucket);
        }
    }

    [Fact]
    public async Task Linked_pairs_keep_primary_order_and_every_original_member_identifier()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, Now.AddDays(-30));
        var epic = await fixture.Seed("Prey Deluxe", 90, Now.AddDays(-400), "epic");
        await fixture.Seed("Dishonored", 0, null);
        await fixture.Link(steam, epic);
        var games = (await fixture.Api.GetLibraryAsync()).Games;
        Assert.Equal(2, games.Count);
        var game = Assert.Single(games, game => game.WorkId == steam.WorkId);
        Assert.Equal("Prey", game.Title);
        Assert.Equal(new[] { "steam", "epic" }, game.Entries.Select(entry => entry.Store));
        Assert.Equal(new[] { steam.OwnershipId, epic.OwnershipId }, game.Entries.Select(entry => entry.OwnershipId));
        Assert.Equal(new[] { steam.ReleaseId, epic.ReleaseId }, game.Entries.Select(entry => entry.ReleaseId));
        Assert.Equal(new[] { steam.WorkId, epic.WorkId }, game.Entries.Select(entry => entry.WorkId));
        foreach (var member in new[] { steam, epic })
        {
            Assert.Same(game, Assert.Single(games, candidate => candidate.Entries.Any(entry => entry.OwnershipId == member.OwnershipId)));
            Assert.Same(game, Assert.Single(games, candidate => candidate.Entries.Any(entry => entry.ReleaseId == member.ReleaseId)));
        }
    }

    [Fact]
    public async Task Repeated_store_licences_remain_distinct_entries_in_one_game()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, Now.AddDays(-30));
        var classic = await fixture.Seed("Prey Classic", 10, null);
        var gog = await fixture.Seed("Prey (GOG)", 5, null, "gog");
        await fixture.Link(steam, classic);
        await fixture.Link(steam, gog);
        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal(3, game.Entries.Count);
        Assert.Equal(new[] { "steam", "steam", "gog" }, game.Entries.Select(entry => entry.Store));
        Assert.Equal(new[] { "steam", "gog" }, game.Entries.Select(entry => entry.Store).Distinct());
    }

    [Fact]
    public async Task Group_totals_sum_minutes_and_choose_the_latest_date_without_crossing_entry_facts()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, Now.AddDays(-400));
        var epic = await fixture.Seed("Prey", 40, Now.AddDays(-10), "epic");
        await fixture.Link(steam, epic);
        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal(340, game.PlaytimeMinutes);
        Assert.Equal(Now.AddDays(-10), game.LastPlayedAt);
        var steamEntry = Assert.Single(game.Entries, entry => entry.Store == "steam");
        Assert.Equal(300, steamEntry.PlaytimeMinutes);
        Assert.Equal(Now.AddDays(-400), steamEntry.LastPlayedAt);
        var epicEntry = Assert.Single(game.Entries, entry => entry.Store == "epic");
        Assert.Equal(40, epicEntry.PlaytimeMinutes);
        Assert.Equal(Now.AddDays(-10), epicEntry.LastPlayedAt);
    }

    [Fact]
    public async Task Group_bucket_is_classified_from_the_sum_even_when_each_copy_is_below_the_refund_line()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 70, Now.AddDays(-300));
        var epic = await fixture.Seed("Prey", 70, Now.AddDays(-200), "epic");
        Assert.All((await fixture.Api.GetWorkspaceAsync()).Buckets, row => Assert.Equal(LibraryBuckets.Active, row.Bucket));
        await fixture.Link(steam, epic);
        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal(140, game.PlaytimeMinutes);
        Assert.Equal(LibraryBucketRules.Classify(140, Now.AddDays(-200), null, BucketThresholds.Default), game.Bucket);
        Assert.Equal(LibraryBuckets.Bounced, game.Bucket);
    }

    [Fact]
    public async Task Recent_play_on_a_secondary_store_becomes_the_group_date()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, Now.AddYears(-4));
        var epic = await fixture.Seed("Prey", 10, Now.AddDays(-2), "epic");
        var before = await fixture.Api.GetGameAsync(steam.WorkId);
        await fixture.Link(steam, epic);
        var after = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal(Now.AddYears(-4), before.LastPlayedAt);
        Assert.Equal(Now.AddDays(-2), after.LastPlayedAt);
        Assert.Equal(310, after.PlaytimeMinutes);
    }

    [Fact]
    public async Task Details_reads_major_updates_from_a_secondary_release_and_preserves_its_ownership()
    {
        await using var fixture = await Fixture.Create();
        var steam = await fixture.Seed("Prey", 300, Now.AddYears(-3));
        var epic = await fixture.Seed("Prey", 10, Now.AddYears(-3), "epic", installed: true);
        var updates = fixture.Services.GetRequiredService<IUpdateEventRepository>();
        foreach (var kind in new[] { UpdateEventKinds.BuildPush, UpdateEventKinds.Announcement })
            await updates.InsertAsync(new UpdateEvent { ReleaseId = epic.ReleaseId, Kind = kind, OccurredAt = Now.AddMonths(-2).AddDays(kind == UpdateEventKinds.Announcement ? 1 : 0), Title = "Epic build" });
        await fixture.Link(steam, epic);
        var game = Assert.Single((await fixture.Api.GetLibraryAsync()).Games);
        Assert.Equal(LibraryBuckets.StaleButPatched, game.Bucket);
        Assert.Equal(epic.OwnershipId, Assert.Single(game.Entries, entry => entry.Installed).OwnershipId);
        Assert.False(game.Entries[0].Installed);
        var details = await fixture.Api.GetAsync<GameDetailsResponse>($"games/{game.WorkId}/details");
        Assert.Equal(2, details.Events.Count);
        Assert.All(details.Events, item => { Assert.Equal(epic.ReleaseId, item.ReleaseId); Assert.Equal("Epic build", item.Title); });
        Assert.Equal(new[] { steam.OwnershipId, epic.OwnershipId }.Order(), details.Ownerships.Select(entry => entry.Id).Order());
    }

    private sealed record Entry(long WorkId, long ReleaseId, long OwnershipId);
    private sealed class Fixture(WebApplication app, string directory) : IAsyncDisposable
    {
        public IServiceProvider Services => app.Services;
        public WinnowApiClient Api { get; } = WinnowApiClient.Attach(directory);
        public static async Task<Fixture> Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-library-grain-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"]);
            try { await app.StartAsync(); return new(app, directory); }
            catch { await app.DisposeAsync(); Directory.Delete(directory, recursive: true); throw; }
        }
        public async Task<Entry> Seed(string title, long minutes, DateTime? played, string store = "steam", bool installed = false)
        {
            var work = await Services.GetRequiredService<IWorkRepository>().InsertAsync(new Work { Name = title, FirstReleaseYear = 2017 });
            var release = await Services.GetRequiredService<IReleaseRepository>().InsertAsync(new Release { WorkId = work, Name = title, Platform = "windows" });
            var ownership = await Services.GetRequiredService<IOwnershipRepository>().InsertAsync(new Ownership { ReleaseId = release, Store = store, Installed = installed });
            await Services.GetRequiredService<IPlayRecordRepository>().InsertAsync(new PlayRecord { OwnershipId = ownership, PlaytimeMinutes = minutes, LastPlayedAt = played, Source = "steam_localconfig", ObservedAt = Now });
            return new(work, release, ownership);
        }
        public Task<IdentityActResponse> Link(Entry parent, Entry child) => Api.LinkGamesAsync(new(parent.WorkId, [child.WorkId], new Dictionary<long, long> { [parent.WorkId] = parent.WorkId, [child.WorkId] = child.WorkId }, IdentityLinkKinds.SameGame));
        public async ValueTask DisposeAsync()
        {
            Api.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
