using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Winnow.Api.Contracts.Details;
using Winnow.Api.Contracts.Library;
using Winnow.App.Services;
using Winnow.Application.Library;
using Winnow.Core.Domain;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.Igdb.Credentials;

namespace Winnow.Electron.Fixtures;

internal sealed class UpdateAcknowledgementFixture(ISqliteConnectionFactory database,
    ILibraryApplication library, LibraryChangePublisher publisher, SteamReadingOfflineHttp offline)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string? _kind;
    private bool _pushed;
    private readonly ConcurrentQueue<AcknowledgementCall> _calls = new();

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<UpdateAcknowledgementFixture>();
        services.RemoveAll<ICoverSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, OwnershipNoCredentials>();
        services.AddSingleton<SteamReadingOfflineHttp>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(provider => provider.GetRequiredService<SteamReadingOfflineHttp>());
    }

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<UpdateAcknowledgementFixture>();
        app.Use(async (context, next) =>
        {
            var parts = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
            if (parts.Length != 5 || parts[0] != "api" || parts[2] != "releases"
                || context.Request.Method != "POST" || !long.TryParse(parts[3], out var releaseId)
                || parts[4] is not ("acknowledge-updates" or "restore-updates"))
            {
                await next(context);
                return;
            }
            long[] ids = [];
            if (parts[4] == "acknowledge-updates")
            {
                context.Request.EnableBuffering();
                var body = await JsonSerializer.DeserializeAsync<UpdateAcknowledgementRequest>(context.Request.Body, Json);
                ids = body?.ObservedEventIds?.ToArray() ?? [];
                context.Request.Body.Position = 0;
            }
            var call = new AcknowledgementCall { Operation = parts[4], ReleaseId = releaseId, ObservedEventIds = ids };
            fixture._calls.Enqueue(call);
            var original = context.Response.Body;
            await using var captured = new MemoryStream();
            context.Response.Body = captured;
            try
            {
                await next(context);
                call.StatusCode = context.Response.StatusCode;
                captured.Position = 0;
                if (captured.Length > 0 && context.Response.StatusCode is >= 200 and < 300)
                {
                    var response = await JsonSerializer.DeserializeAsync<AcknowledgementResponse>(captured, Json);
                    call.Result = response?.Result;
                    call.AcknowledgedThrough = response?.AcknowledgedThrough;
                }
                captured.Position = 0;
                await captured.CopyToAsync(original);
                call.Completed = true;
            }
            finally { context.Response.Body = original; }
        });
        app.MapPost("/__fixture/update-acknowledgement/seed", (AcknowledgementSeed seed) => fixture.SeedAsync(seed));
        app.MapPost("/__fixture/update-acknowledgement/change", async (AcknowledgementChange change) =>
        {
            await fixture.ChangeAsync(change);
            return Results.NoContent();
        });
        app.MapGet("/__fixture/update-acknowledgement/state", () => fixture.StateAsync());
    }

    public async Task<AcknowledgementState> SeedAsync(AcknowledgementSeed seed)
    {
        if (_kind is not null) throw new InvalidOperationException("Seed the acknowledgement fixture only once.");
        if (seed.Kind is not ("baseline" or "grouped" or "multiple" or "failure" or "refusal-all" or "partial" or "refuse-release2" or "counts"))
            throw new ArgumentException("Unknown acknowledgement fixture kind.");
        using (var connection = database.Open())
        {
            connection.Execute("""
                INSERT INTO works (id, name, sort_name) VALUES (1, 'Grouped game', 'Grouped game'), (2, 'Never played', 'Never played');
                INSERT INTO releases (id, work_id, name, platform) VALUES (1, 1, 'Steam copy', 'windows'), (2, 1, 'Epic copy', 'windows'), (3, 2, 'Never played', 'windows');
                INSERT INTO ownerships (id, release_id, store, installed) VALUES (1, 1, 'steam', 0), (2, 2, 'epic', 0), (3, 3, 'gog', 0);
                INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at) VALUES
                    (1, 600, '2024-01-01', 'steam_local', '2026-09-01'), (2, 600, '2025-01-01', 'epic_local', '2026-09-01');
                INSERT INTO update_events (release_id, kind, occurred_at) VALUES
                    (1, 'build_push', '2024-07-01'), (1, 'announcement', '2024-07-02'),
                    (1, 'build_push', '2025-01-01'), (1, 'announcement', '2025-01-02'),
                    (1, 'build_push', '2026-06-01'), (1, 'announcement', '2026-06-02'),
                    (2, 'build_push', '2026-06-01'), (2, 'announcement', '2026-06-02'),
                    (2, 'build_push', '2026-08-01'), (2, 'announcement', '2026-08-02'),
                    (3, 'build_push', '2026-06-01'), (3, 'announcement', '2026-06-02');
                """);
            if (seed.Kind == "multiple")
                connection.Execute("""
                    INSERT INTO works (id, name, sort_name) VALUES (4, 'Selected game', 'Selected game'), (5, 'Unselected game', 'Unselected game');
                    INSERT INTO releases (id, work_id, name, platform) VALUES (4, 4, 'Selected game', 'windows'), (5, 5, 'Unselected game', 'windows');
                    INSERT INTO ownerships (id, release_id, store, installed) VALUES (4, 4, 'steam', 0), (5, 5, 'steam', 0);
                    INSERT INTO play_records (ownership_id, playtime_minutes, last_played_at, source, observed_at) VALUES
                        (4, 600, '2025-01-01', 'steam_local', '2026-09-01'), (5, 600, '2025-01-01', 'steam_local', '2026-09-01');
                    INSERT INTO update_events (release_id, kind, occurred_at) VALUES
                        (4, 'build_push', '2026-08-01'), (4, 'announcement', '2026-08-02'),
                        (5, 'build_push', '2026-08-01'), (5, 'announcement', '2026-08-02');
                    """);
            if (seed.Kind == "counts")
            {
                connection.Execute("DELETE FROM ownerships WHERE id=2");
                connection.Execute("UPDATE play_records SET playtime_minutes=@minutes,last_played_at=@played WHERE ownership_id=1",
                    new { minutes = seed.Minutes, played = seed.HasDate ? "2025-01-01 00:00:00" : null });
            }
        }
        _kind = seed.Kind;
        if (seed.Kind is "failure" or "refusal-all") SetRefusal(null);
        if (seed.Kind is "partial" or "refuse-release2") SetRefusal(2);
        await publisher.PublishAsync(CancellationToken.None);
        return await StateAsync();
    }

    public async Task ChangeAsync(AcknowledgementChange change)
    {
        switch (change.Stage)
        {
            case "later-push":
            case "push":
                if (_pushed) throw new InvalidOperationException("The original later pair is inserted only once.");
                var repository = new UpdateEventRepository(database);
                var later = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
                await repository.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.BuildPush, OccurredAt = later });
                await repository.InsertAsync(new UpdateEvent { ReleaseId = 1, Kind = UpdateEventKinds.Announcement, OccurredAt = later.AddDays(1) });
                _pushed = true;
                break;
            case "clear-refusal":
            case "allow-writes":
                using (var connection = database.Open()) connection.Execute("DROP TRIGGER IF EXISTS refuse_acknowledgement");
                break;
            case "refuse-all": SetRefusal(null); break;
            case "refuse-release2": SetRefusal(2); break;
            case "publish": break;
            default: throw new ArgumentException("Unknown acknowledgement fixture stage.");
        }
        // Lack of publication models the source's insert between snapshot and click.
        // The real acknowledgement route publishes its own successful writes normally.
        if (change.Publish || change.Stage == "publish") await publisher.PublishAsync(CancellationToken.None);
    }

    private void SetRefusal(long? release)
    {
        using var connection = database.Open();
        connection.Execute("DROP TRIGGER IF EXISTS refuse_acknowledgement");
        connection.Execute(release is null
            ? "CREATE TRIGGER refuse_acknowledgement BEFORE INSERT ON update_acknowledgements BEGIN SELECT RAISE(FAIL, 'simulated storage failure'); END;"
            : "CREATE TRIGGER refuse_acknowledgement BEFORE INSERT ON update_acknowledgements WHEN NEW.release_id=2 BEGIN SELECT RAISE(FAIL, 'simulated storage failure'); END;");
    }

    public async Task<AcknowledgementState> StateAsync()
    {
        var workspace = await library.GetWorkspaceAsync();
        var events = new List<UpdateEvent>();
        long[] releases;
        using (var connection = database.Open()) releases = connection.Query<long>("SELECT DISTINCT release_id FROM update_events ORDER BY release_id").ToArray();
        foreach (var release in releases)
            events.AddRange(await new UpdateEventRepository(database).GetByReleaseAsync(release));
        IReadOnlyList<UpdateAcknowledgement> acknowledgements;
        using (var connection = database.Open())
            acknowledgements = connection.Query<UpdateAcknowledgement>("""
                SELECT id AS Id,release_id AS ReleaseId,acknowledged_through AS AcknowledgedThrough,
                    created_at AS CreatedAt,revoked_at AS RevokedAt FROM update_acknowledgements ORDER BY id
                """).AsList();
        return new(_kind, events.OrderBy(item => item.Id).ToArray(), acknowledgements,
            workspace.Buckets.Select(bucket => bucket.Game).DistinctBy(game => game.ResolvedWorkId).OrderBy(game => game.ResolvedWorkId).ToArray(),
            await library.GetLibraryAsync(), _calls.ToArray(), offline.Requests.ToArray());
    }
}

internal sealed record AcknowledgementSeed(string Kind = "baseline", long Minutes = 600, bool HasDate = true);
internal sealed record AcknowledgementChange(string Stage, bool Publish = false);
internal sealed record AcknowledgementState(string? Kind, IReadOnlyList<UpdateEvent> Events,
    IReadOnlyList<UpdateAcknowledgement> Acknowledgements, IReadOnlyList<WorkspaceGameGrouping> Groups,
    LibraryResponse Library, IReadOnlyList<AcknowledgementCall> Calls, IReadOnlyList<string> ProviderRequests);
internal sealed class AcknowledgementCall
{
    public string Operation { get; init; } = "";
    public long ReleaseId { get; init; }
    public IReadOnlyList<long> ObservedEventIds { get; init; } = [];
    public int StatusCode { get; set; }
    public string? Result { get; set; }
    public DateTime? AcknowledgedThrough { get; set; }
    public bool Completed { get; set; }
}
