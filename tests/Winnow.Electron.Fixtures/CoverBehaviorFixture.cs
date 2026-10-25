using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SkiaSharp;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Covers;
using Winnow.Data;
using Winnow.Recommend;

namespace Winnow.Electron.Fixtures;

internal sealed record CoverBehaviorSeed(string Kind, string Art = "steam", string? Mode = null, int ShotCount = 3);
internal sealed record CoverBehaviorArm(string Operation, long? WorkId = null, string? Slot = null,
    string? Provider = null, string? Id = null, bool IgnoreCancellation = true, bool ThrowOnCancel = false, int? Width = null);

internal sealed class CoverBehaviorFixture(ISqliteConnectionFactory database, IWorkImageRepository images,
    ArtworkPreferences preferences, UserArtStore userArt, CoverDiskCache disk,
    LibraryChangePublisher publisher, PreviewResponseControls controls, CoverBehaviorSource source)
{
    public static readonly DateTime Now = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);
    public static CoverKey Plugin => PluginArtRef.Key("fixture", "https://images.example.test/cover.jpg")!.Value;
    public string Kind { get; private set; } = "startup";
    public string Art { get; private set; } = "steam";
    public bool Ready { get; private set; }
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<CoverBehaviorFixture>();
        services.AddSingleton<PreviewResponseControls>();
        services.AddSingleton<CoverBehaviorSource>();
        services.RemoveAll<ICoverSource>();
        services.AddSingleton<ICoverSource>(p => p.GetRequiredService<CoverBehaviorSource>());
        services.AddSingleton<ICoverSource, UserArtCoverSource>();
        services.AddSingleton<IRecommendationEngine, CoverBehaviorEngine>();
    }
    public static void Map(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var fixture = context.RequestServices.GetRequiredService<CoverBehaviorFixture>();
            var state = context.RequestServices.GetRequiredService<PreviewResponseControls>();
            var call = state.Begin(context, fixture.Ready);
            if (call is null) { await next(context); return; }
            var body = context.Response.Body;
            await using var captured = new MemoryStream();
            context.Response.Body = captured;
            try
            {
                await next(context);
                var bytes = captured.ToArray();
                call.StatusCode = context.Response.StatusCode;
                call.ResponseBytes = bytes.Length;
                call.ResponseSha256 = Convert.ToHexString(SHA256.HashData(bytes));
                call.Json = context.Response.ContentType?.Contains("json", StringComparison.Ordinal) == true ? Encoding.UTF8.GetString(bytes) : null;
                call.ResponseReady = true;
                await state.WaitAsync(call, context.RequestAborted);
                context.Response.Body = body;
                await body.WriteAsync(bytes, context.RequestAborted);
                call.Delivered = true;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally { context.Response.Body = body; call.Completed = true; }
        });
        const string prefix = "/__fixture/cover-behavior";
        app.MapPost(prefix + "/seed", (CoverBehaviorSeed input, CoverBehaviorFixture fixture) => fixture.SeedAsync(input));
        app.MapGet(prefix + "/state", (CoverBehaviorFixture fixture) => fixture.State());
        app.MapPost(prefix + "/arm", (CoverBehaviorArm input, PreviewResponseControls responses, CoverBehaviorSource source) =>
            input.Operation == "fetch" ? source.Arm(input) : responses.Arm(new(input.Operation, input.WorkId, input.Slot, input.Provider, input.Id, input.IgnoreCancellation, input.Width)));
        app.MapPost(prefix + "/release", (PreviewRelease input, PreviewResponseControls responses, CoverBehaviorSource source) =>
        { responses.Release(input.GateId); source.Release(input.GateId); return Results.NoContent(); });
        app.MapPost(prefix + "/publish", async (CoverBehaviorFixture fixture) =>
        { await fixture.PublishAsync(); return Results.NoContent(); });
    }
    public async Task<object> SeedAsync(CoverBehaviorSeed input)
    {
        if (input.Kind is not ("selection" or "lifetime" or "focus" or "fit" or "conversion") || input.ShotCount is < 0 or > 3)
            throw new ArgumentException("Unknown cover fixture.");
        if (input.Art is not ("plugin" or "unavailable" or "user" or "igdb" or "steam")) throw new ArgumentException("Unknown selection fixture.");
        using (var connection = database.Open())
        {
            if (Ready || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0) throw new InvalidOperationException("Seed a fresh fixture only.");
            Kind = input.Kind; Art = input.Art; source.Wide = Kind == "fit";
            if (Kind == "selection")
            {
                var cover = Art switch
                {
                    "user" => UserArtRef.Format("fixtureuser"),
                    "igdb" or "steam" => "https://images.igdb.com/igdb/image/upload/t_cover_big/co42.jpg",
                    _ => PluginArtRef.Reference(Plugin),
                };
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name,cover_url) VALUES(1,'Game','Game',@cover),(2,'Game edition','Game edition',NULL);
                    INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Game','windows'),(2,2,'Game edition','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'epic',0),(2,2,'epic',0);
                    INSERT INTO merge_candidates(left_release_id,right_release_id,score,status) VALUES(1,2,0.95,'pending');
                    """, new { cover });
                if (Art is "steam" or "user") connection.Execute("INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','42')");
            }
            else
            {
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(1,'Fixture','Fixture'),(2,'Fixture edition','Fixture edition');
                    INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Fixture','windows'),(2,2,'Fixture edition','windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'epic',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','620'),(2,'steam','621');
                    INSERT INTO merge_candidates(left_release_id,right_release_id,score,status) VALUES(1,2,0.95,'pending');
                    """);
            }
            if (input.Mode is not null) connection.Execute("INSERT OR REPLACE INTO settings(key,value) VALUES('display.cover_art_mode',@mode)", new { mode = input.Mode });
            connection.Execute("""
                INSERT OR REPLACE INTO settings(key,value) VALUES('fullscreen.reduced-motion','true');
                CREATE TABLE fixture_cover_mode_writes(value TEXT);
                CREATE TRIGGER fixture_cover_mode_insert AFTER INSERT ON settings WHEN NEW.key='display.cover_art_mode'
                  BEGIN INSERT INTO fixture_cover_mode_writes(value) VALUES(NEW.value); END;
                CREATE TRIGGER fixture_cover_mode_update AFTER UPDATE ON settings WHEN NEW.key='display.cover_art_mode'
                  BEGIN INSERT INTO fixture_cover_mode_writes(value) VALUES(NEW.value); END;
                """);
        }
        preferences.ConfigureSources(Art == "unavailable" ? [] : [new("plugin:fixture", "Fixture")]);
        if (Art == "user")
        {
            Directory.CreateDirectory(userArt.Root);
            await File.WriteAllBytesAsync(userArt.PathFor("fixtureuser"), CoverBehaviorSource.Image(false));
        }
        if (Kind is "lifetime" or "fit") await images.UpsertAsync(new()
        {
            WorkId = 1, Source = "igdb", Kind = "screenshot",
            ImageIds = string.Join(',', new[] { "aa11", "bb22", "cc33" }.Take(input.ShotCount)), ObservedAt = Now,
        });
        if (Kind == "conversion") disk.WriteSource(CoverKey.Steam("43"), CoverBehaviorSource.Image(false));
        Ready = true;
        await PublishAsync();
        return State();
    }
    public Task PublishAsync() => publisher.PublishAsync(default);
    public object State()
    {
        using var connection = database.Open();
        using var bitmap = new SKBitmap(1, 1);
        bitmap.Erase(SKColors.SlateBlue);
        using var floor = CoverImaging.ApplyFloor(bitmap, (float)DormancyStyle.SaturationFloor, (float)DormancyStyle.BrightnessFloor, (float)DormancyStyle.HueDegrees);
        var color = floor.GetPixel(0, 0);
        return new { Kind, Art, Ready, Now, ProcessId = Environment.ProcessId, Calls = controls.Calls, SourceCalls = source.Calls,
            StoredMode = connection.QuerySingleOrDefault<string>("SELECT value FROM settings WHERE key='display.cover_art_mode'"),
            ModeWrites = Ready ? connection.Query<string>("SELECT value FROM fixture_cover_mode_writes ORDER BY rowid").ToArray() : [],
            FloorRgba = new byte[] { color.Red, color.Green, color.Blue, color.Alpha },
            Floor = new { DormancyStyle.BrightnessFloor, DormancyStyle.SaturationFloor, DormancyStyle.HueDegrees },
            PluginKey = Plugin, ExpectedCover = Kind != "selection" ? CoverKey.Steam("620") : Art switch
            {
                "unavailable" => (CoverKey?)null, "user" => CoverKey.User("fixtureuser"),
                "igdb" => CoverKey.Igdb("co42"), "steam" => CoverKey.Steam("42"), _ => Plugin,
            } };
    }
}

internal sealed class CoverBehaviorSource : ICoverSource
{
    private readonly Lock _sync = new();
    private readonly List<Plan> _plans = [];
    private readonly ConcurrentDictionary<string, Plan> _held = new();
    private readonly ConcurrentQueue<Call> _calls = new();
    public IReadOnlyList<Call> Calls => _calls.ToArray();
    public bool Wide { get; set; }
    public string Name => "cover-behavior-source";
    public bool CanHandle(CoverKey key) => key.Provider is "steam" or "steam-hero" or "steam-hero-standard" or "igdb" or "igdb-shot" or "igdb-backdrop" or "plugin-fixture";
    public object Arm(CoverBehaviorArm input)
    {
        var plan = new Plan(Guid.NewGuid().ToString("N"), input);
        lock (_sync) _plans.Add(plan);
        return new { GateId = plan.Id };
    }
    public void Release(string? id = null)
    { foreach (var plan in _held.Values.Where(p => id is null || p.Id == id)) plan.Done.TrySetResult(); }
    public async Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    {
        Plan? plan;
        lock (_sync)
        {
            plan = _plans.FirstOrDefault(p => (p.Input.Provider is null || p.Input.Provider == key.Provider) && (p.Input.Id is null || p.Input.Id == key.Id));
            if (plan is not null) { _plans.Remove(plan); _held[plan.Id] = plan; }
        }
        var call = new Call(key.Provider, key.Id, plan?.Id, ct);
        // Register the deliberate failing callback after the cancellable wait, so
        // wait completion cannot dispose it before this source failure is observed.
        var waiting = plan is null ? Task.CompletedTask : plan.Input.IgnoreCancellation ? plan.Done.Task : plan.Done.Task.WaitAsync(ct);
        using var registration = plan?.Input.ThrowOnCancel == true ? ct.Register(() =>
        { Interlocked.Increment(ref call.ThrowingCallbacks); throw new InvalidOperationException("Injected source cancellation failure."); }) : default;
        _calls.Enqueue(call);
        try
        {
            await waiting;
            return Image(Wide);
        }
        finally { call.Completed = true; }
    }
    private sealed record Plan(string Id, CoverBehaviorArm Input)
    { public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    internal sealed class Call(string provider, string id, string? gateId, CancellationToken token)
    {
        public string Provider { get; } = provider;
        public string Id { get; } = id;
        public string? GateId { get; } = gateId;
        public bool Canceled => token.IsCancellationRequested;
        public bool Completed { get; set; }
        public int ThrowingCallbacks;
        public int CancellationCallbackFailures => ThrowingCallbacks;
    }
    public static byte[] Image(bool wide)
    {
        using var bitmap = new SKBitmap(wide ? 400 : 160, wide ? 300 : 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SlateBlue);
        if (wide)
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 100, 300, paint);
            paint.Color = SKColors.Green; canvas.DrawRect(300, 0, 100, 300, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}

internal sealed class CoverBehaviorEngine(CoverBehaviorFixture fixture) : IRecommendationEngine
{
    public Task<RecommendationFeed> GetFeedAsync(RecommendationRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ShelfFeed> GetShelvesAsync(RecommendationRequest request, CancellationToken ct = default) => Task.FromResult(new ShelfFeed
    {
        CandidateCount = fixture.Ready ? 1 : 0, Tier = DataTier.ColdStart,
        Shelves = !fixture.Ready ? [] : [new RecommendationShelf
        {
            Id = "fixture", Title = "A shelf", Blurb = "", Items = [new Recommendation
            {
                OwnershipId = 1, ReleaseId = 1, WorkId = 1, Title = fixture.Kind == "selection" ? "Game" : "Fixture",
                Store = fixture.Kind == "selection" ? "epic" : "steam", Bucket = "never_played", Score = 1, Reason = "A forgotten game",
                Explanation = new() { Primary = ReasonSignal.None, Evidence = new() { ReleaseId = 1, Title = "Fixture" } }, Signals = [],
            }],
        }],
    });
}
