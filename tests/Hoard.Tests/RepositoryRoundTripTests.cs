using Hoard.Core.Domain;
using Hoard.Data.Repositories;
using Xunit;

namespace Hoard.Tests;

public class RepositoryRoundTripTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    [Fact]
    public async Task Work_release_ownership_play_record_round_trip()
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var ownerships = new OwnershipRepository(_db.Factory);
        var playRecords = new PlayRecordRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work
        {
            IgdbId = 1942,
            Name = "The Elder Scrolls V: Skyrim",
            SortName = "elder scrolls v skyrim",
            FirstReleaseYear = 2011,
            Summary = "Dragons.",
            CoverUrl = "https://example/cover.jpg",
        });

        var releaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            IgdbVersionId = 12345,
            Name = "Skyrim Special Edition",
            Platform = "windows",
            EditionNote = "2016 remaster; separate achievement set",
        });

        var ownershipId = await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = ExternalIdProviders.Steam,
            AccountRef = "steam3:12345678",
            AcquiredAt = Utc(2016, 10, 28),
            LicenseType = "purchase",
            PricePaidCents = 3999,
            PriceSource = "manual",
            InstallPath = @"C:\Games\Steam\steamapps\common\Skyrim Special Edition",
            Installed = true,
        });

        var playRecordId = await playRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 5423,
            LastPlayedAt = Utc(2024, 3, 1, 21, 15, 0),
            Source = "steam_local",
            ObservedAt = Utc(2026, 8, 1, 12, 0, 0),
        });

        var work = await works.GetAsync(workId);
        Assert.NotNull(work);
        Assert.Equal(1942, work.IgdbId);
        Assert.Equal("The Elder Scrolls V: Skyrim", work.Name);
        Assert.Equal(2011, work.FirstReleaseYear);

        var release = await releases.GetAsync(releaseId);
        Assert.NotNull(release);
        Assert.Equal(workId, release.WorkId);
        Assert.Equal("Skyrim Special Edition", release.Name);
        Assert.Equal(12345, release.IgdbVersionId);

        var ownership = await ownerships.GetAsync(ownershipId);
        Assert.NotNull(ownership);
        Assert.Equal(releaseId, ownership.ReleaseId);
        Assert.Equal("steam", ownership.Store);
        Assert.Equal(Utc(2016, 10, 28), ownership.AcquiredAt);
        Assert.Equal(3999, ownership.PricePaidCents);
        Assert.True(ownership.Installed);

        var latest = await playRecords.GetLatestAsync(ownershipId);
        Assert.NotNull(latest);
        Assert.Equal(playRecordId, latest.Id);
        Assert.Equal(5423, latest.PlaytimeMinutes);
        Assert.Equal(Utc(2024, 3, 1, 21, 15, 0), latest.LastPlayedAt);
        Assert.Equal("steam_local", latest.Source);
    }

    [Fact]
    public async Task Latest_play_record_wins_by_observed_at()
    {
        var (_, _, ownershipId) = await SeedOwnershipAsync();
        var playRecords = new PlayRecordRepository(_db.Factory);

        await playRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 100,
            LastPlayedAt = Utc(2025, 1, 1),
            Source = "steam_local",
            ObservedAt = Utc(2025, 1, 2),
        });
        await playRecords.InsertAsync(new PlayRecord
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 250,
            LastPlayedAt = Utc(2025, 6, 1),
            Source = "steam_local",
            ObservedAt = Utc(2025, 6, 2),
        });

        var latest = await playRecords.GetLatestAsync(ownershipId);
        Assert.NotNull(latest);
        Assert.Equal(250, latest.PlaytimeMinutes);

        var all = await playRecords.GetByOwnershipAsync(ownershipId);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task External_id_hard_join_finds_release()
    {
        var (_, releaseId, _) = await SeedOwnershipAsync();
        var releases = new ReleaseRepository(_db.Factory);

        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Steam,
            ProviderId = "489830",
        });
        await releases.AddExternalIdAsync(new ExternalId
        {
            ReleaseId = releaseId,
            Provider = ExternalIdProviders.Igdb,
            ProviderId = "12345",
        });

        var found = await releases.FindByExternalIdAsync("steam", "489830");
        Assert.NotNull(found);
        Assert.Equal(releaseId, found.Id);

        Assert.Null(await releases.FindByExternalIdAsync("gog", "489830"));

        var ids = await releases.GetExternalIdsAsync(releaseId);
        Assert.Equal(2, ids.Count);
    }

    [Fact]
    public async Task Playtime_snapshots_round_trip_in_order()
    {
        var (_, _, ownershipId) = await SeedOwnershipAsync();
        var snapshots = new PlaytimeSnapshotRepository(_db.Factory);

        await snapshots.InsertAsync(new PlaytimeSnapshot
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 200,
            ObservedAt = Utc(2026, 2, 1),
        });
        await snapshots.InsertAsync(new PlaytimeSnapshot
        {
            OwnershipId = ownershipId,
            PlaytimeMinutes = 100,
            ObservedAt = Utc(2026, 1, 1),
        });

        var history = await snapshots.GetByOwnershipAsync(ownershipId);
        Assert.Equal(2, history.Count);
        Assert.Equal(100, history[0].PlaytimeMinutes); // oldest first
        Assert.Equal(200, history[1].PlaytimeMinutes);
    }

    [Fact]
    public async Task Session_with_note_round_trips()
    {
        var (_, _, ownershipId) = await SeedOwnershipAsync();
        var sessions = new SessionRepository(_db.Factory);

        var sessionId = await sessions.InsertAsync(new Session
        {
            OwnershipId = ownershipId,
            StartedAt = Utc(2026, 8, 20, 20, 0, 0),
            EndedAt = Utc(2026, 8, 20, 22, 30, 0),
            DurationSeconds = 9000,
            DetectionMethod = DetectionMethods.ProcessWatch,
        });

        await sessions.SetNoteAsync(new SessionNote
        {
            SessionId = sessionId,
            Note = "Finally beat the bridge boss.",
            Rating = 4,
        });
        await sessions.SetNoteAsync(new SessionNote
        {
            SessionId = sessionId,
            Note = "Finally beat the bridge boss!",
            Rating = 5,
        });

        var session = await sessions.GetAsync(sessionId);
        Assert.NotNull(session);
        Assert.Equal(9000, session.DurationSeconds);
        Assert.Equal(DetectionMethods.ProcessWatch, session.DetectionMethod);

        var note = await sessions.GetNoteAsync(sessionId);
        Assert.NotNull(note);
        Assert.Equal("Finally beat the bridge boss!", note.Note);
        Assert.Equal(5, note.Rating);
    }

    [Fact]
    public async Task Update_events_round_trip_in_occurrence_order()
    {
        var (_, releaseId, _) = await SeedOwnershipAsync();
        var updates = new UpdateEventRepository(_db.Factory);

        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = releaseId,
            Kind = UpdateEventKinds.Announcement,
            OccurredAt = Utc(2026, 5, 2),
            Title = "Patch 2.0 out now",
            RawJson = """{"gid":"1"}""",
        });
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = releaseId,
            Kind = UpdateEventKinds.BuildPush,
            BuildId = "1234567",
            OccurredAt = Utc(2026, 5, 1),
        });

        var events = await updates.GetByReleaseAsync(releaseId);
        Assert.Equal(2, events.Count);
        Assert.Equal(UpdateEventKinds.BuildPush, events[0].Kind); // oldest first
        Assert.Equal(UpdateEventKinds.Announcement, events[1].Kind);
    }

    [Fact]
    public async Task Lists_and_items_round_trip()
    {
        var (_, releaseId, _) = await SeedOwnershipAsync();
        var lists = new GameListRepository(_db.Factory);

        var listId = await lists.InsertAsync(new GameList
        {
            Name = "Backlog",
            Description = "Play these",
        });

        await lists.AddItemAsync(new ListItem { ListId = listId, ReleaseId = releaseId, Position = 5 });
        await lists.AddItemAsync(new ListItem { ListId = listId, ReleaseId = releaseId, Position = 1 }); // upsert moves position

        var items = await lists.GetItemsAsync(listId);
        var item = Assert.Single(items);
        Assert.Equal(1, item.Position);

        await lists.RemoveItemAsync(listId, releaseId);
        Assert.Empty(await lists.GetItemsAsync(listId));

        var list = await lists.GetAsync(listId);
        Assert.NotNull(list);
        Assert.Equal("Backlog", list.Name);
        Assert.False(list.IsSmart);
    }

    [Fact]
    public async Task Merge_candidates_queue_and_resolve()
    {
        var (workId, leftReleaseId, _) = await SeedOwnershipAsync();
        var releases = new ReleaseRepository(_db.Factory);
        var rightReleaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = "Prey (2017)",
            Platform = "windows",
        });

        var candidates = new MergeCandidateRepository(_db.Factory);
        var id = await candidates.InsertAsync(new MergeCandidate
        {
            LeftReleaseId = leftReleaseId,
            RightReleaseId = rightReleaseId,
            Score = 0.83,
            SignalsJson = """{"title_sim":0.91,"year_delta":11}""",
        });

        var pending = await candidates.GetPendingAsync();
        Assert.Equal(id, Assert.Single(pending).Id);

        await candidates.SetStatusAsync(id, MergeCandidateStatuses.Rejected);
        Assert.Empty(await candidates.GetPendingAsync());
    }

    private async Task<(long WorkId, long ReleaseId, long OwnershipId)> SeedOwnershipAsync()
    {
        var works = new WorkRepository(_db.Factory);
        var releases = new ReleaseRepository(_db.Factory);
        var ownerships = new OwnershipRepository(_db.Factory);

        var workId = await works.InsertAsync(new Work { Name = "Prey", FirstReleaseYear = 2006 });
        var releaseId = await releases.InsertAsync(new Release
        {
            WorkId = workId,
            Name = "Prey (2006)",
            Platform = "windows",
        });
        var ownershipId = await ownerships.InsertAsync(new Ownership
        {
            ReleaseId = releaseId,
            Store = "steam",
        });
        return (workId, releaseId, ownershipId);
    }
}
