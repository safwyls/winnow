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

internal sealed record PreviewSeed(string Kind, bool Publish = true);
internal sealed record PreviewArm(string Operation, long? WorkId = null, string? Slot = null,
    string? Provider = null, string? Id = null, bool IgnoreCancellation = true, int? Width = null);
internal sealed record PreviewRelease(string? GateId = null);
internal sealed record PreviewChange(string Kind, bool Publish = true);

internal sealed class RecommendationPreviewFixture(ISqliteConnectionFactory database,
    IWorkRatingRepository ratings, IWorkImageRepository images, LibraryChangePublisher publisher,
    PreviewResponseControls controls)
{
    public static readonly string[] Titles = ["Disco Elysium", "Hollow Knight", "Outer Wilds", "Hades", "Subnautica"];
    public static readonly string[] SteamIds = ["632470", "367520", "753640", "1145360", "264710"];
    public static readonly string[] Reasons = ["2 hours played, then left untouched for 8 months.",
        "An unfinished journey through Hallownest.",
        "You played 48 minutes last winter. There is still a whole solar system to unravel.",
        "One more escape attempt.", "Your last dive was a year ago."];
    public DateTime Now { get; } = DateTime.UtcNow;
    public string Kind { get; private set; } = "startup";
    public bool Ready { get; private set; }
    public FeedSnapshot Feed { get; private set; } = new([], 0, FeedConfidence.EarlyDays, false);

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<RecommendationPreviewFixture>();
        services.AddSingleton<PreviewResponseControls>();
        services.AddSingleton<IRecommendationEngine, PreviewRecommendationEngine>();
        services.RemoveAll<ICoverSource>();
        services.AddSingleton<ICoverSource, PreviewCoverSource>();
    }

    public static void Map(WebApplication app)
    {
        // Delay the immutable result after the real endpoint finishes its repository scope.
        // This is the source's controlled metadata task, not a replacement DTO or long SQLite lease.
        app.Use(async (context, next) =>
        {
            var fixture = context.RequestServices.GetRequiredService<RecommendationPreviewFixture>();
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
                call.Json = context.Response.ContentType?.Contains("json", StringComparison.Ordinal) == true
                    ? Encoding.UTF8.GetString(bytes) : null;
                call.ResponseReady = true;
                await state.WaitAsync(call, context.RequestAborted);
                context.Response.Body = body;
                await body.WriteAsync(bytes, context.RequestAborted);
                call.Delivered = true;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally { context.Response.Body = body; call.Completed = true; }
        });
        const string prefix = "/__fixture/recommendation-preview";
        app.MapPost(prefix + "/seed", (PreviewSeed input, RecommendationPreviewFixture fixture) => fixture.SeedAsync(input));
        app.MapGet(prefix + "/state", (RecommendationPreviewFixture fixture) => fixture.State());
        app.MapPost(prefix + "/arm", (PreviewArm input, PreviewResponseControls state) => state.Arm(input));
        app.MapPost(prefix + "/release", (PreviewRelease input, PreviewResponseControls state) =>
        { state.Release(input.GateId); return Results.NoContent(); });
        app.MapPost(prefix + "/change", (PreviewChange input, RecommendationPreviewFixture fixture) => fixture.ChangeAsync(input));
    }

    public async Task<object> SeedAsync(PreviewSeed input)
    {
        if (input.Kind is not ("ratings" or "library" or "missing" or "shelf" or "backdrop"))
            throw new ArgumentException("Unknown preview fixture.");
        using (var connection = database.Open())
        {
            if (Ready || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
                throw new InvalidOperationException("Preview fixture requires an empty test database.");
            Kind = input.Kind;
            var titles = Kind == "shelf" ? Titles : ["Hades"];
            for (var index = 0; index < titles.Length; index++)
            {
                var id = index + 1;
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name,summary) VALUES(@id,@title,@title,@summary);
                    INSERT INTO releases(id,work_id,name,platform) VALUES(@id,@id,@title,'windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(@id,@id,'steam',@installed);
                    """, new { id, title = titles[index], installed = Kind == "shelf" ? 1 : 0,
                    summary = Kind == "shelf" && index == 3 ? "Defy the god of the dead as you battle out of the Underworld." : null });
                if (Kind != "backdrop") connection.Execute(
                    "INSERT INTO external_ids(release_id,provider,provider_id) VALUES(@id,'steam',@external)",
                    new { id, external = Kind == "shelf" ? SteamIds[index] : "123" });
            }
        }
        if (Kind is "ratings" or "library")
        {
            await ratings.UpsertAsync(new() { WorkId = 1, Source = "steam", Score = 93, RatingCount = 1500, Label = "Very Positive", ObservedAt = Now });
            await ratings.UpsertAsync(new() { WorkId = 1, Source = "igdb_critics", Score = 88, RatingCount = 12, ObservedAt = Now });
            if (Kind == "ratings") await ratings.UpsertAsync(new() { WorkId = 1, Source = "igdb_users", Score = 81, RatingCount = 100, ObservedAt = Now });
        }
        if (Kind == "backdrop") await SetArtAsync("staleart");
        var shelves = Kind == "shelf" ? Enumerable.Range(0, 2).Select(shelf => new FeedShelf($"visual-{shelf}",
            shelf == 0 ? "Worth another look" : "Still waiting for their first session",
            shelf == 0 ? "Games you started, with a reason to come back." : "A fresh start, already in your library.",
            Titles.Select((title, i) => new FeedItem(i + 1, i + 1, title, Reasons[i])).ToArray())).ToArray()
            : [new FeedShelf("preview", "A shelf", "", [new(1, 1, "Hades", "Reason")])];
        // The visual source appends cards without loading its feed: the count stays zero.
        Feed = new(shelves, Kind == "shelf" ? 0 : 1, FeedConfidence.EarlyDays, false);
        Ready = true;
        if (input.Publish) await publisher.PublishAsync(default);
        return State();
    }

    public async Task<object> ChangeAsync(PreviewChange input)
    {
        if (!Ready) throw new InvalidOperationException("Seed first.");
        if (input.Kind == "replacement")
        {
            using var connection = database.Open();
            connection.Execute("UPDATE works SET name='Replacement',sort_name='Replacement' WHERE id=1; UPDATE releases SET name='Replacement' WHERE id=1");
        }
        else if (input.Kind == "currentart") await SetArtAsync("currentart");
        else throw new ArgumentException("Unknown preview change.");
        if (input.Publish) await publisher.PublishAsync(default);
        return State();
    }
    private Task SetArtAsync(string id) => images.UpsertAsync(new WorkImages
        { WorkId = 1, Source = "igdb", Kind = "artwork", ImageIds = id, ObservedAt = Now });
    public object State() => new { Kind, Now, Ready, ProcessId = Environment.ProcessId,
        WorkIds = Kind == "shelf" ? new long[] { 1, 2, 3, 4, 5 } : [1], Feed, Calls = controls.Calls };
}

internal sealed class PreviewRecommendationEngine(RecommendationPreviewFixture fixture) : IRecommendationEngine
{
    public Task<RecommendationFeed> GetFeedAsync(RecommendationRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("The preview fixture only supplies source shelves.");
    public Task<ShelfFeed> GetShelvesAsync(RecommendationRequest request, CancellationToken ct = default)
        => Task.FromResult(new ShelfFeed
        {
            CandidateCount = fixture.Feed.CandidateCount, Tier = (DataTier)fixture.Feed.Confidence,
            Shelves = fixture.Feed.Shelves.Select(shelf => new RecommendationShelf
            {
                Id = shelf.Id, Title = shelf.Title, Blurb = shelf.Blurb,
                Items = shelf.Items.Select(item => new Recommendation
                {
                    OwnershipId = item.OwnershipId, ReleaseId = item.ReleaseId, WorkId = item.ReleaseId,
                    Title = item.Title, Store = "steam", Bucket = "untouched", Score = 1, Reason = item.Reason,
                    Explanation = new() { Primary = ReasonSignal.None, Evidence = new() { ReleaseId = item.ReleaseId, Title = item.Title } }, Signals = [],
                }).ToArray(),
            }).ToArray(),
        });
}

internal sealed class PreviewResponseControls
{
    private readonly Lock _sync = new();
    private readonly List<Plan> _plans = [];
    private readonly ConcurrentDictionary<string, Plan> _held = new();
    private readonly ConcurrentQueue<Call> _calls = new();
    private int _id;
    public IReadOnlyList<Call> Calls => _calls.ToArray();
    public object Arm(PreviewArm input)
    {
        if (input.Operation is not ("details" or "backdrop" or "artworkState" or "image")) throw new ArgumentException("Unknown preview gate.");
        var plan = new Plan(Guid.NewGuid().ToString("N"), input);
        lock (_sync) _plans.Add(plan);
        return new { GateId = plan.Id };
    }
    public Call? Begin(HttpContext context, bool ready)
    {
        if (!ready || context.Request.Method != "GET") return null;
        var path = context.Request.Path.Value!;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string operation;
        long? workId = null;
        string? slot = null;
        if (parts.Length == 5 && parts[2] == "games" && parts[4] == "details" && long.TryParse(parts[3], out var game))
        { operation = "details"; workId = game; }
        else if (parts.Length == 5 && parts[2] == "works" && parts[4] == "backdrop" && long.TryParse(parts[3], out game))
        { operation = "backdrop"; workId = game; }
        else if (parts.Length == 6 && parts[2] == "works" && parts[4] == "artwork" && long.TryParse(parts[3], out game))
        { operation = "artworkState"; workId = game; slot = parts[5]; }
        else if (path == "/api/v1/artwork/image") operation = "image";
        else return null;
        var provider = context.Request.Query["provider"].FirstOrDefault();
        var id = context.Request.Query["id"].FirstOrDefault();
        int? requestedWidth = int.TryParse(context.Request.Query["width"], out var width) ? width : null;
        Plan? plan;
        lock (_sync)
        {
            plan = _plans.FirstOrDefault(p => p.Input.Operation == operation && (p.Input.WorkId is null || p.Input.WorkId == workId)
                && (p.Input.Slot is null || string.Equals(p.Input.Slot, slot, StringComparison.OrdinalIgnoreCase))
                && (p.Input.Provider is null || p.Input.Provider == provider) && (p.Input.Id is null || p.Input.Id == id)
                && (p.Input.Width is null || p.Input.Width == requestedWidth));
            if (plan is not null) { _plans.Remove(plan); _held[plan.Id] = plan; }
        }
        var call = new Call(Interlocked.Increment(ref _id), operation, workId, slot, provider, id, path, plan?.Id, context.RequestAborted);
        call.Width = requestedWidth;
        _calls.Enqueue(call);
        return call;
    }
    public async Task WaitAsync(Call call, CancellationToken ct)
    {
        if (call.GateId is not { } id || !_held.TryGetValue(id, out var plan)) return;
        if (plan.Input.IgnoreCancellation) await plan.Done.Task;
        else await plan.Done.Task.WaitAsync(ct);
    }
    public void Release(string? id = null)
    {
        foreach (var plan in _held.Values.Where(p => id is null || p.Id == id)) plan.Done.TrySetResult();
    }
    private sealed record Plan(string Id, PreviewArm Input)
    { public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    internal sealed class Call(int callId, string operation, long? workId, string? slot, string? provider, string? id,
        string path, string? gateId, CancellationToken token)
    {
        public int CallId { get; } = callId;
        public string Operation { get; } = operation;
        public long? WorkId { get; } = workId;
        public string? Slot { get; } = slot;
        public string? Provider { get; } = provider;
        public string? Id { get; } = id;
        public string Path { get; } = path;
        public string? GateId { get; } = gateId;
        public bool Canceled => token.IsCancellationRequested;
        public bool ResponseReady { get; set; }
        public bool Completed { get; set; }
        public bool Delivered { get; set; }
        public int StatusCode { get; set; }
        public int ResponseBytes { get; set; }
        public string? ResponseSha256 { get; set; }
        public string? Json { get; set; }
        public int? Width { get; set; }
    }
}

internal sealed class PreviewCoverSource : ICoverSource
{
    public string Name => "source-preview-artwork";
    public bool CanHandle(CoverKey key) => key.Provider is CoverProviders.Steam or CoverProviders.SteamHero
        or CoverProviders.SteamHeroStandard or CoverProviders.IgdbBackdrop;
    public Task<byte[]?> TryFetchAsync(CoverKey key, CancellationToken ct = default)
    { ct.ThrowIfCancellationRequested(); return Task.FromResult<byte[]?>(Image(key)); }
    public static byte[] Image(CoverKey key)
    {
        var portrait = key.Provider == CoverProviders.Steam;
        using var bitmap = new SKBitmap(portrait ? 400 : 800, portrait ? 600 : 450);
        using var canvas = new SKCanvas(bitmap);
        var index = Array.IndexOf(RecommendationPreviewFixture.SteamIds, key.Id);
        SKColor[] colors = [new(128, 61, 47), new(45, 92, 124), new(149, 111, 55), new(113, 45, 52), new(37, 98, 112)];
        canvas.Clear(key.Id == "staleart" ? SKColors.Red : key.Id == "currentart" ? SKColors.Green : colors[Math.Max(0, index)]);
        using var paint = new SKPaint { Color = new SKColor(239, 226, 180, 110), IsAntialias = true };
        canvas.DrawCircle(bitmap.Width * .67f, bitmap.Height * .28f, bitmap.Width * .22f, paint);
        paint.Color = new SKColor(12, 30, 32, 140);
        canvas.DrawRect(0, bitmap.Height * .72f, bitmap.Width, bitmap.Height * .28f, paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
}
