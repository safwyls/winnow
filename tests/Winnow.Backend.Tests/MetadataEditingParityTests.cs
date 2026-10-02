using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winnow.Api.Client;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Xunit;

namespace Winnow.Backend.Tests;

/// <summary>Original metadata and Details refresh facts through the authenticated production API.</summary>
public sealed class MetadataEditingParityTests
{
    private static readonly DateTime Observed = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LastPlayed = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RefreshNow = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Prey_exposes_six_fields_and_saves_only_the_named_work_field_and_user_source_over_HTTP()
    {
        await using var host = await Host.Start();
        var entry = await host.SeedPrey();
        var before = await host.Details.GetMetadataAsync(entry.WorkId);
        Assert.True(before.Available);
        Assert.False(before.IsPinned);
        Assert.Equal(WorkFields.All, before.Fields.Select(field => field.Field));
        Assert.Equal(new string?[] { "Prey", "2006", "A Cherokee garage mechanic is abducted.", null, "2K Games", null },
            before.Fields.Select(field => field.Value));
        Assert.All(before.Fields, field => Assert.Null(field.Source));
        var original = Assert.Single((await host.Api.GetLibraryAsync()).Games);
        Assert.Equal("Applied", (await host.Details.SetMetadataAsync(entry.WorkId,
            new(WorkFields.Name, "Prey (2006)", before.Revision))).Outcome);

        using var reopened = host.Reopen();
        var metadata = await new DetailsClient(reopened).GetMetadataAsync(entry.WorkId);
        Assert.Equal(entry.WorkId, metadata.WorkId);
        Assert.Equal("Prey (2006)", metadata.Title);
        Assert.Equal(new MetadataFieldResponse(WorkFields.Name, "Prey (2006)", FieldSources.User), metadata.Fields[0]);
        Assert.Equal(before.Fields.Skip(1), metadata.Fields.Skip(1));
        var sources = await host.Get<IWorkFieldSourceRepository>().GetSourcesAsync(entry.WorkId);
        Assert.Equal(new KeyValuePair<string, string>(WorkFields.Name, FieldSources.User), Assert.Single(sources));
        var game = Assert.Single((await reopened.GetLibraryAsync()).Games);
        Assert.Equivalent(original with { Title = "Prey (2006)" }, game, strict: true);
        Assert.Equal((120L, LastPlayed), (game.PlaytimeMinutes, game.LastPlayedAt));
        Assert.Equal(entry.OwnershipId, Assert.Single(game.Entries).OwnershipId);
        Assert.Equal("Prey (2006)", Assert.Single((await reopened.GetWorkspaceAsync()).Works).Name);
    }

    [Fact]
    public async Task Resetting_Preys_publisher_returns_only_that_field_to_automatic_over_HTTP()
    {
        await using var host = await Host.Start();
        var entry = await host.SeedPrey();
        var original = await host.Details.GetMetadataAsync(entry.WorkId);
        await host.Save(entry.WorkId, WorkFields.Publisher, "Human Head Studios");
        var saved = await host.Details.GetMetadataAsync(entry.WorkId);
        Assert.Equal(new MetadataFieldResponse(WorkFields.Publisher, "Human Head Studios", FieldSources.User),
            Assert.Single(saved.Fields, field => field.Field == WorkFields.Publisher));
        Assert.Equal("Applied", (await host.Details.ResetMetadataAsync(entry.WorkId,
            new(WorkFields.Publisher, saved.Revision))).Outcome);
        var reset = await host.Details.GetMetadataAsync(entry.WorkId);
        Assert.Equal(new MetadataFieldResponse(WorkFields.Publisher, null, null),
            Assert.Single(reset.Fields, field => field.Field == WorkFields.Publisher));
        Assert.Equal(original.Fields.Where(field => field.Field != WorkFields.Publisher),
            reset.Fields.Where(field => field.Field != WorkFields.Publisher));
        Assert.Null((await host.Get<IWorkRepository>().GetAsync(entry.WorkId))!.Publisher);
        Assert.Empty(await host.Get<IWorkFieldSourceRepository>().GetSourcesAsync(entry.WorkId));
        Assert.Equal(entry.OwnershipId, Assert.Single(Assert.Single((await host.Api.GetLibraryAsync()).Games).Entries).OwnershipId);
    }

    [Fact]
    public async Task Renaming_Alpha_Protocol_reorders_the_current_library_after_Zeno_Clash_over_HTTP()
    {
        await using var host = await Host.Start();
        var alpha = await host.SeedPrey("Alpha Protocol");
        await host.SeedPrey("Zeno Clash");
        Assert.Equal(new[] { "Alpha Protocol", "Zeno Clash" }, (await host.Api.GetLibraryAsync()).Games.Select(game => game.Title));
        await host.Save(alpha.WorkId, WorkFields.Name, "Zzz Protocol");
        var after = (await host.Api.GetLibraryAsync()).Games;
        Assert.Equal(new[] { "Zeno Clash", "Zzz Protocol" }, after.Select(game => game.Title));
        Assert.Equal(alpha.WorkId, after[1].WorkId);
        Assert.Equal(alpha.OwnershipId, Assert.Single(after[1].Entries).OwnershipId);
        Assert.Equal("Zzz Protocol", (await host.Details.GetMetadataAsync(alpha.WorkId)).Title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Optional_metadata_service_availability_preserves_Details_and_refuses_unavailable_writes_over_HTTP(bool available)
    {
        await using var host = await Host.Start(available);
        var entry = await host.SeedPrey();
        var before = await host.Get<IWorkRepository>().GetAsync(entry.WorkId);
        var metadata = await host.Details.GetMetadataAsync(entry.WorkId);
        Assert.Equal(available, metadata.Available);
        Assert.Equal("Prey", metadata.Title);
        Assert.Equal(available ? 6 : 0, metadata.Fields.Count);
        Assert.Equal(entry.WorkId, (await host.Details.GetAsync(entry.WorkId)).WorkId);
        Assert.Equal(metadata.Revision, (await host.Details.GetMetadataAsync(entry.WorkId)).Revision);
        var wire = await host.Api.GetAsync<JsonElement>($"games/{entry.WorkId}/metadata");
        Assert.Equal(available, wire.GetProperty("available").GetBoolean());
        if (available) return;

        Assert.Equal("Unavailable", (await host.Details.SetMetadataAsync(entry.WorkId,
            new(WorkFields.Name, "Must not save", metadata.Revision))).Outcome);
        Assert.Equal("Unavailable", (await host.Details.ResetMetadataAsync(entry.WorkId,
            new(WorkFields.Publisher, metadata.Revision))).Outcome);
        Assert.Equal("Unavailable", (await host.Api.SendAsync<UploadMetadataArtRequest, MutationOutcome>(HttpMethod.Post,
            $"games/{entry.WorkId}/metadata/art-upload", new(WorkFields.CoverUrl, [1, 2, 3], metadata.Revision))).Outcome);
        Assert.Equal("Unavailable", (await host.Api.SendAsync<DownloadMetadataArtRequest, MutationOutcome>(HttpMethod.Post,
            $"games/{entry.WorkId}/metadata/art-download", new(WorkFields.BackgroundUrl, "https://example.test/no-network.png", metadata.Revision))).Outcome);
        Assert.Equal(before, await host.Get<IWorkRepository>().GetAsync(entry.WorkId));
        Assert.Empty(await host.Get<IWorkFieldSourceRepository>().GetSourcesAsync(entry.WorkId));
        Assert.Equal(entry.OwnershipId, Assert.Single(Assert.Single((await host.Api.GetLibraryAsync()).Games).Entries).OwnershipId);
    }

    [Theory]
    [InlineData(WorkFields.Summary, "A saved summary.")]
    [InlineData(WorkFields.Publisher, "A saved publisher")]
    [InlineData(WorkFields.FirstReleaseYear, "2017")]
    public async Task Saving_source_Game_1_fields_refreshes_library_facts_and_keeps_2006_live_list_rules_over_HTTP(string field, string value)
    {
        await using var host = await Host.Start();
        await host.SeedRefreshLibrary();
        var list = await host.Api.CreateLiveListAsync(new("2006 only", new LibraryFilter { YearFrom = 2006, YearTo = 2006 }));
        Assert.True(list.IsLive);
        Assert.Empty(list.ReleaseIds);
        Assert.NotNull(list.Filter);
        Assert.Equal((2006, 2006), (list.Filter.YearFrom, list.Filter.YearTo));
        var before = await host.Details.GetMetadataAsync(1);
        await host.Save(1, field, value);
        var metadata = await host.Details.GetMetadataAsync(1);
        Assert.Equal(new MetadataFieldResponse(field, value, FieldSources.User), Assert.Single(metadata.Fields, row => row.Field == field));
        Assert.Equal(before.Fields.Where(row => row.Field != field), metadata.Fields.Where(row => row.Field != field));
        var library = await host.Api.GetLibraryAsync();
        var game = Assert.Single(library.Games, row => row.WorkId == 1);
        Assert.Equal(field == WorkFields.Summary ? value : "Original summary", game.Summary);
        Assert.Equal(field == WorkFields.Publisher ? value : "Original publisher", game.Publisher);
        Assert.Equal(field == WorkFields.FirstReleaseYear ? 2017 : 2006, game.FirstReleaseYear);
        Assert.Equal(1, Assert.Single(game.Entries).OwnershipId);
        // Live membership is computed by the renderer from these rules and refreshed work facts.
        Assert.Equivalent(list, Assert.Single(library.Lists, item => item.Id == list.Id), strict: true);
        Assert.Equal(1990, Assert.Single(library.Games, row => row.WorkId == 2).FirstReleaseYear);
        Assert.Equal("Game 1", (await host.Details.GetMetadataAsync(1)).Title);
    }

    [Fact]
    public async Task Background_reads_publish_new_sessions_updates_images_and_reception_without_changing_the_old_journal_note_over_HTTP()
    {
        await using var host = await Host.Start();
        await host.SeedRefreshLibrary();
        var oldSession = await host.AddSession(RefreshNow.AddDays(-90), "Old saved note");
        var before = await host.Details.GetAsync(1);
        var originalEntry = Assert.Single(before.JournalEntries);
        Assert.Equal(oldSession, originalEntry.SessionId);
        var last = RefreshNow.AddDays(-5);
        await host.Get<IPlayRecordRepository>().InsertAsync(new()
            { OwnershipId = 1, PlaytimeMinutes = 180, LastPlayedAt = last, ObservedAt = RefreshNow, Source = "steam" });
        await host.Get<IPlaytimeSnapshotRepository>().InsertAsync(new()
            { OwnershipId = 1, PlaytimeMinutes = 180, ObservedAt = RefreshNow });
        var newSession = await host.AddSession(last, "A new saved note");
        await host.AddPatch(RefreshNow.AddDays(-1), "New patch");
        await host.Get<IWorkRatingRepository>().UpsertAsync(new()
            { WorkId = 1, Source = RatingSources.IgdbUsers, Score = 90, RatingCount = 20, ObservedAt = RefreshNow });
        await host.Get<IWorkImageRepository>().UpsertAsync(new()
            { WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot, ImageIds = "newshot", ObservedAt = RefreshNow });
        var fresh = await host.Details.GetAsync(1);
        Assert.Equal(originalEntry, Assert.Single(fresh.JournalEntries, row => row.SessionId == oldSession));
        Assert.Equal(new long[] { oldSession, newSession }.Order(), fresh.JournalEntries.Select(row => row.SessionId).Order());
        Assert.Equal(new long[] { 120, 180 }, fresh.History[1].OrderBy(row => row.ObservedAt).Select(row => row.PlaytimeMinutes));
        Assert.Equal(2, fresh.Sessions[1].Count);
        Assert.Equal(2, fresh.Events.Count);
        var announcement = Assert.Single(fresh.Events, row => row.Kind == UpdateEventKinds.Announcement);
        Assert.Equal("New patch", announcement.Title);
        var rating = Assert.Single(fresh.Ratings);
        Assert.Equal((RatingSources.IgdbUsers, 90d, 20), (rating.Source, rating.Score, rating.RatingCount));
        Assert.Equal("newshot", Assert.Single(fresh.Images).ImageIds);
        var game = Assert.Single((await host.Api.GetLibraryAsync()).Games, row => row.WorkId == 1);
        Assert.Equal((180L, last), (game.PlaytimeMinutes, game.LastPlayedAt));
        Assert.Equal("Old saved note", (await host.Details.GetJournalAsync(oldSession)).Note);

        await host.AddPatch(RefreshNow, "Newest patch");
        var later = await host.Details.GetAsync(1);
        Assert.Equal(4, later.Events.Count);
        Assert.Equal(2, later.Events.Count(row => row.Kind == UpdateEventKinds.Announcement));
        Assert.Equal(announcement, Assert.Single(later.Events, row => row.Id == announcement.Id));
        Assert.Equal("Newest patch", later.Events.First(row => row.Kind == UpdateEventKinds.Announcement).Title);
    }

    private sealed record Entry(long WorkId, long ReleaseId, long OwnershipId);
    private sealed class Host(string directory, WebApplication app, WinnowApiClient api) : IAsyncDisposable
    {
        public WinnowApiClient Api => api;
        public DetailsClient Details { get; } = new(api);
        public T Get<T>() where T : notnull => app.Services.GetRequiredService<T>();
        public WinnowApiClient Reopen() => WinnowApiClient.Attach(directory);
        public static async Task<Host> Start(bool available = true)
        {
            var directory = Path.Combine(Path.GetTempPath(), "winnow-metadata-editing-parity", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var app = BackendApplication.Build(["--data-dir", directory, "--no-sync"], services =>
            {
                if (!available) services.RemoveAll<IWorkMetadataEditService>();
            });
            await app.StartAsync();
            return new(directory, app, WinnowApiClient.Attach(directory));
        }
        public async Task<Entry> SeedPrey(string title = "Prey")
        {
            var work = await Get<IWorkRepository>().InsertAsync(new()
                { Name = title, FirstReleaseYear = 2006, Publisher = "2K Games", Summary = "A Cherokee garage mechanic is abducted." });
            var release = await Get<IReleaseRepository>().InsertAsync(new() { WorkId = work, Name = title, Platform = "windows" });
            var ownership = await Get<IOwnershipRepository>().InsertAsync(new() { ReleaseId = release, Store = "gog" });
            await Get<IPlayRecordRepository>().InsertAsync(new()
                { OwnershipId = ownership, PlaytimeMinutes = 120, LastPlayedAt = LastPlayed, Source = "gog", ObservedAt = Observed });
            return new(work, release, ownership);
        }
        public async Task SeedRefreshLibrary()
        {
            using (var connection = Get<ISqliteConnectionFactory>().Open())
                await connection.ExecuteAsync("""
                    INSERT INTO works(id,name,sort_name,first_release_year,summary,publisher)
                    VALUES(1,'Game 1','Game 1',2006,'Original summary','Original publisher'),(2,'Game 2','Game 2',1990,NULL,NULL);
                    INSERT INTO releases(id,work_id,name,platform) SELECT id,id,name,'windows' FROM works;
                    INSERT INTO ownerships(id,release_id,store,installed) SELECT id,id,'steam',0 FROM releases;
                    INSERT INTO external_ids(release_id,provider,provider_id) SELECT id,'steam',CAST(id AS TEXT) FROM releases;
                    INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
                    INSERT INTO list_items(list_id,release_id,position) VALUES(1,2,0),(1,1,1);
                    """);
            await Get<IPlayRecordRepository>().InsertAsync(new()
                { OwnershipId = 1, PlaytimeMinutes = 120, LastPlayedAt = RefreshNow.AddDays(-90), ObservedAt = RefreshNow.AddDays(-30), Source = "steam" });
            await Get<IPlaytimeSnapshotRepository>().InsertAsync(new()
                { OwnershipId = 1, PlaytimeMinutes = 120, ObservedAt = RefreshNow.AddDays(-30) });
        }
        public async Task Save(long work, string field, string value)
        {
            var current = await Details.GetMetadataAsync(work);
            Assert.Equal("Applied", (await Details.SetMetadataAsync(work, new(field, value, current.Revision))).Outcome);
        }
        public async Task<long> AddSession(DateTime at, string note)
        {
            var id = await Get<ISessionRepository>().InsertAsync(new()
                { OwnershipId = 1, StartedAt = at, EndedAt = at.AddHours(1), DurationSeconds = 3600, DetectionMethod = "manual" });
            await Get<ISessionRepository>().SetNoteAsync(new() { SessionId = id, Note = note, Rating = 3 });
            return id;
        }
        public async Task AddPatch(DateTime at, string title)
        {
            await Get<IUpdateEventRepository>().InsertAsync(new()
                { ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = at, BuildId = at.Ticks.ToString() });
            await Get<IUpdateEventRepository>().InsertAsync(new()
                { ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = at, Title = title, Url = "https://store.steampowered.com/news/app/1/view/" + at.Ticks });
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
