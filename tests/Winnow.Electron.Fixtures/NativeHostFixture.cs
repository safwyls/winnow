using System.Collections.Concurrent;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Winnow.App.Services;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Enrich.Igdb.Credentials;

namespace Winnow.Electron.Fixtures;

internal sealed class NativeHostFixture(ISqliteConnectionFactory database, CoverCacheOptions covers)
{
    private readonly ConcurrentQueue<NativeHostCall> _calls = new();
    private readonly TaskCompletionSource _imagesReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _root = "";
    private bool _fullscreen, _authored;

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<NativeHostFixture>();
        services.RemoveAll<ICoverSource>();
        services.AddSingleton<ICoverSource, NativeHostArtSource>();
        services.RemoveAll<IIgdbCredentialProvider>();
        services.AddSingleton<IIgdbCredentialProvider, StartupNoCredentials>();
    }

    public async Task InitializeAsync(string directory)
    {
        _root = directory;
        _fullscreen = Path.GetFileName(directory).Contains("-fullscreen-", StringComparison.Ordinal);
        _authored = Path.GetFileName(directory).Contains("-authored-", StringComparison.Ordinal);
        using (var connection = database.Open())
        {
            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Native host fixtures require an empty library.");
            connection.Execute("""
                INSERT INTO works(id,name,sort_name) VALUES(1,'Jump List smoke game','Jump List smoke game');
                INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Jump List smoke game','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(42,1,'steam',1);
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','42');
                INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at)
                    VALUES(42,20,@now,'steam_local',@now);
                INSERT OR REPLACE INTO settings(key,value) VALUES('appearance.theme','hoard');
                INSERT OR REPLACE INTO settings(key,value) VALUES('application.start_in_fullscreen',@mode);
                INSERT OR REPLACE INTO settings(key,value) VALUES('fullscreen.reduced-motion','true');
                """, new { now = DateTime.UtcNow, mode = _fullscreen ? "true" : "false" });
        }
        var themes = Path.Combine(directory, "themes");
        Directory.CreateDirectory(themes);
        if (_authored)
            await File.WriteAllTextAsync(Path.Combine(themes, "hoard.json"), JsonSerializer.Serialize(new
            {
                schemaVersion = 1, id = "hoard", name = "My Hoard", reason = "A theme written by a test.",
                seeds = new
                {
                    ground = "#0F1C1E", surface = "#16282A", text = "#F0EDE7", flare = "#FF4D93",
                    volt = "#4DE8C2", amber = "#FFB63D", azure = "#57A8F0", danger = "#E04B45"
                }
            }));
    }

    public object State() => new
    {
        Root = _root, DatabasePath = database.DatabasePath, CoverCacheDirectory = covers.CacheDirectory,
        UserThemeDirectory = Path.Combine(_root, "themes"), Theme = "hoard", Authored = _authored,
        Fullscreen = _fullscreen, WorkId = 1, HeaderWorkId = 1, OwnershipId = 42,
        ExpectedArtKey = CoverKey.Steam("42"), Calls = _calls.ToArray(), ProcessId = Environment.ProcessId
    };

    public static void Map(WebApplication app)
    {
        var fixture = app.Services.GetRequiredService<NativeHostFixture>();
        app.Use(async (context, next) =>
        {
            var route = context.Request.Path.Value ?? "";
            var image = route == "/api/v1/artwork/image";
            var art = route is "/api/v1/works/1/artwork/Cover" or "/api/v1/works/1/artwork/Icon";
            if (!image && !art) { await next(context); return; }
            int.TryParse(context.Request.Query["width"], out var width);
            var call = new NativeHostCall
            {
                Operation = image ? "image" : "artworkState", WorkId = art ? 1 : null,
                Slot = art ? route[(route.LastIndexOf('/') + 1)..] : null,
                Provider = image ? context.Request.Query["provider"].ToString() : null,
                Id = image ? context.Request.Query["id"].ToString() : null,
                Width = image ? width : null,
                Held = image && width == 128 && !fixture._imagesReleased.Task.IsCompleted
            };
            fixture._calls.Enqueue(call);
            var original = context.Response.Body;
            await using var captured = new MemoryStream();
            context.Response.Body = captured;
            try
            {
                await next(context);
                call.Status = context.Response.StatusCode;
                call.ResponseReady = true;
                if (call.Held) await fixture._imagesReleased.Task.WaitAsync(context.RequestAborted);
                context.Response.Body = original;
                captured.Position = 0;
                await captured.CopyToAsync(original, context.RequestAborted);
            }
            finally
            {
                context.Response.Body = original;
                call.Canceled = context.RequestAborted.IsCancellationRequested;
                call.Completed = true;
            }
        });
        app.MapGet("/__fixture/native-host/state", () => fixture.State());
        app.MapPost("/__fixture/native-host/release", () =>
        {
            fixture._imagesReleased.TrySetResult();
            return Microsoft.AspNetCore.Http.Results.NoContent();
        });
        app.Lifetime.ApplicationStopping.Register(() => fixture._imagesReleased.TrySetCanceled());
    }
}

internal sealed class NativeHostCall
{
    public required string Operation { get; init; }
    public long? WorkId { get; init; }
    public string? Slot { get; init; }
    public string? Provider { get; init; }
    public string? Id { get; init; }
    public int? Width { get; init; }
    public bool Held { get; init; }
    public bool ResponseReady { get; set; }
    public bool Completed { get; set; }
    public bool Canceled { get; set; }
    public int Status { get; set; }
}

internal sealed class NativeHostArtSource : ICoverSource
{
    public string Name => "native-host-fixture";
    public bool CanHandle(CoverKey key) => key == CoverKey.Steam("42");
    public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var bitmap = new SKBitmap(64, 96);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        return Task.FromResult<byte[]?>(bytes.ToArray());
    }
}
