using Dapper;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;

namespace Winnow.Electron.Fixtures;

// Seed the frozen metadata-modal and Details-refresh facts. All user edits and
// snapshot publication use production services over the fixture's isolated database.
internal sealed class MetadataEditingFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher)
{
    internal static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
    private long? _oldSession;

    public async Task<object> SeedAsync(string kind)
    {
        using (var check = database.Open())
            if (check.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Metadata fixture requires an empty library.");
        if (kind is "refresh" or "background")
        {
            // LibraryReadFixtures.Seed(2), including its static list membership.
            using (var seed = database.Open()) seed.Execute("""
                INSERT INTO works(id,name,sort_name,first_release_year,summary,publisher)
                    VALUES(1,'Game 1','Game 1',2006,'Original summary','Original publisher'),
                          (2,'Game 2','Game 2',1990,NULL,NULL);
                INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Game 1','windows'),(2,2,'Game 2','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'steam',0);
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','1'),(2,'steam','2');
                INSERT INTO lists(id,name,is_smart) VALUES(1,'Try next',0);
                INSERT INTO list_items(list_id,release_id,position) VALUES(1,2,0),(1,1,1);
                """);
            await AddPlayAsync(120, Now.AddDays(-90), Now.AddDays(-30));
            if (kind == "background") _oldSession = await AddSessionAsync(Now.AddDays(-90), "Old saved note");
        }
        else if (kind == "sort")
        {
            await AddPreyAsync("Alpha Protocol");
            await AddPreyAsync("Zeno Clash");
        }
        else if (kind == "prey") await AddPreyAsync("Prey");
        else throw new ArgumentException("Unknown metadata fixture kind.", nameof(kind));
        await PublishAsync();
        return new { WorkId = 1, OldSessionId = _oldSession };
    }

    private async Task AddPreyAsync(string title)
    {
        var work = await new WorkRepository(database).InsertAsync(new Work
        {
            Name = title, FirstReleaseYear = 2006, Publisher = "2K Games",
            Summary = "A Cherokee garage mechanic is abducted.",
        });
        var release = await new ReleaseRepository(database).InsertAsync(new Release
        {
            WorkId = work, Name = title, Platform = "windows",
        });
        var ownership = await new OwnershipRepository(database).InsertAsync(new Ownership
        {
            ReleaseId = release, Store = "gog",
        });
        await new PlayRecordRepository(database).InsertAsync(new PlayRecord
        {
            OwnershipId = ownership, PlaytimeMinutes = 120,
            LastPlayedAt = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), Source = "gog",
            ObservedAt = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        });
    }

    public async Task BackgroundAsync()
    {
        if (_oldSession is null) throw new InvalidOperationException("Seed the background fixture first.");
        using (var check = database.Open())
            if (check.ExecuteScalar<int>("SELECT COUNT(*) FROM sessions") != 1)
                throw new InvalidOperationException("The background update is a single fixture transition.");
        await AddPlayAsync(180, Now.AddDays(-5), Now);
        await AddSessionAsync(Now.AddDays(-5), "A new saved note");
        await AddPatchAsync(Now.AddDays(-1), "New patch");
        await new WorkRatingRepository(database).UpsertAsync(new WorkRating
        {
            WorkId = 1, Source = RatingSources.IgdbUsers, Score = 90, RatingCount = 20, ObservedAt = Now,
        });
        await new WorkImageRepository(database).UpsertAsync(new WorkImages
        {
            WorkId = 1, Source = ImageSources.Igdb, Kind = ImageKinds.Screenshot,
            ImageIds = "newshot", ObservedAt = Now,
        });
        await PublishAsync();
    }

    public async Task NewestPatchAsync()
    {
        await AddPatchAsync(Now, "Newest patch");
        await PublishAsync();
    }

    private async Task AddPlayAsync(int minutes, DateTime lastPlay, DateTime observed)
    {
        await new PlayRecordRepository(database).InsertAsync(new PlayRecord
        {
            OwnershipId = 1, PlaytimeMinutes = minutes, LastPlayedAt = lastPlay,
            ObservedAt = observed, Source = "steam",
        });
        await new PlaytimeSnapshotRepository(database).InsertAsync(new PlaytimeSnapshot
        {
            OwnershipId = 1, PlaytimeMinutes = minutes, ObservedAt = observed,
        });
    }

    private async Task<long> AddSessionAsync(DateTime at, string note)
    {
        var sessions = new SessionRepository(database);
        var id = await sessions.InsertAsync(new Session
        {
            OwnershipId = 1, StartedAt = at, EndedAt = at.AddHours(1),
            DurationSeconds = 3600, DetectionMethod = "manual",
        });
        await sessions.SetNoteAsync(new SessionNote { SessionId = id, Note = note, Rating = 3 });
        return id;
    }

    private async Task AddPatchAsync(DateTime at, string title)
    {
        var updates = new UpdateEventRepository(database);
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = at, BuildId = at.Ticks.ToString(),
        });
        await updates.InsertAsync(new UpdateEvent
        {
            ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = at, Title = title,
            Url = "https://store.steampowered.com/news/app/1/view/" + at.Ticks,
        });
    }

    public async Task PublishAsync() => await publisher.PublishAsync(default);

    public object Snapshot()
    {
        using var read = database.Open();
        var tables = new[] { "works", "releases", "ownerships", "play_records", "playtime_snapshots", "work_field_sources",
            "sessions", "session_notes", "update_events", "work_images", "work_ratings", "lists", "list_items" };
        return new
        {
            Rows = tables.ToDictionary(table => table, table => read.Query("SELECT * FROM " + table).ToArray()),
            OldSessionId = _oldSession,
        };
    }
}
