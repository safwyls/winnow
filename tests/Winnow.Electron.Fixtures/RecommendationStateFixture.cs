using System.Collections.Concurrent;
using Dapper;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App.Services;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Recommend;

namespace Winnow.Electron.Fixtures;

internal sealed record RecommendationSeed(string Kind, bool InstalledSibling = false, bool Supplemental = false,
    bool Publish = true, bool HoldBuiltin = false);
internal sealed record RecommendationArm(string Operation = "builtin", string Behavior = "hold", bool IgnoreCancellation = true);
internal sealed record RecommendationRelease(string? GateId = null);
internal sealed record RecommendationNext(string Kind);
internal sealed record RecommendationPublish(int Count = 1);

internal sealed class RecommendationStateFixture(ISqliteConnectionFactory database, LibraryChangePublisher publisher,
    RecommendationControls controls)
{
    private readonly Lock _sync = new();
    private FeedSnapshot _snapshot = new([], 0, FeedConfidence.EarlyDays, false);
    private FeedSupplement _supplement = new([], 0);
    public string Kind { get; private set; } = "startup";
    public DateTime Now { get; private set; } = new(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc);
    public bool Ready { get; private set; }
    public static void Register(IServiceCollection services, bool noFeedback = false)
    {
        services.AddSingleton<RecommendationStateFixture>();
        services.AddSingleton<RecommendationControls>();
        services.AddSingleton<TimeProvider, RecommendationClock>();
        services.AddSingleton<RecommendationEngine>();
        services.AddSingleton<IRecommendationEngine, RecommendationFixtureEngine>();
        services.AddSingleton(provider => new FeedService(provider.GetRequiredService<IRecommendationEngine>(),
            noFeedback ? null : provider.GetRequiredService<IFeedFeedbackRepository>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IFeedService>(provider => provider.GetRequiredService<FeedService>());
    }

    public static void Map(WebApplication app)
    {
        // The source supplies an independent AdditionalShelves task. This test-only seam
        // preserves that injection boundary; it does not simulate PluginFeedService execution.
        // BackendApplication's earlier authority/token middleware still protects this path.
        app.Use(async (context, next) =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/api/v1/feed/supplement")
                await Results.Ok(await context.RequestServices.GetRequiredService<RecommendationStateFixture>()
                    .ReadSupplementAsync(context.RequestAborted)).ExecuteAsync(context);
            else await next(context);
        });
        const string prefix = "/__fixture/recommendation-state";
        app.MapPost(prefix + "/seed", (RecommendationSeed input, RecommendationStateFixture fixture) => fixture.SeedAsync(input));
        app.MapGet(prefix + "/state", (RecommendationStateFixture fixture) => fixture.State());
        app.MapPost(prefix + "/arm", (RecommendationArm input, RecommendationControls state) => state.Arm(input));
        app.MapPost(prefix + "/release", (RecommendationRelease input, RecommendationControls state) =>
        { state.Release(input.GateId); return Results.NoContent(); });
        app.MapPost(prefix + "/next", (RecommendationNext input, RecommendationStateFixture fixture) =>
        { fixture.Next(input.Kind); return Results.NoContent(); });
        app.MapPost(prefix + "/publish", async (RecommendationPublish input, RecommendationStateFixture fixture) =>
        { await fixture.PublishAsync(input.Count); return Results.NoContent(); });
    }

    public async Task<object> SeedAsync(RecommendationSeed input)
    {
        if (input.Kind is not ("composition" or "viewport13" or "reserve" or "excess8" or "primary" or "recent"
            or "optional" or "optional-stale" or "five" or "history" or "throwing" or "invalidation" or "reserve-invalidation")) throw new ArgumentException("Unknown feed fixture.");
        using var connection = database.Open();
        if (Ready || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM works") != 0)
            throw new InvalidOperationException("Recommendation fixture requires a new empty database.");
        controls.Reset();
        Kind = input.Kind;
        if (Kind == "viewport13") Now = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        if (Kind == "composition")
            connection.Execute("""
                INSERT INTO works(id,name,sort_name) VALUES(1,'Kept title','Kept title');
                INSERT INTO releases(id,work_id,name,platform) VALUES(1,1,'Kept title','windows'),(2,1,'Epic copy','windows');
                INSERT INTO ownerships(id,release_id,store,installed) VALUES(1,1,'steam',0),(2,2,'epic',@installed);
                """, new { installed = input.InstalledSibling ? 1 : 0 });
        else
        {
            var snapshot = Build(input.Kind, input.Supplemental);
            var tiles = snapshot.Shelves.Concat(_supplement.Shelves).SelectMany(shelf => shelf.Items.Concat(shelf.Reserve)).ToList();
            // Source FakeTileSource adds these lookup rows before the next pass without
            // TilesChanged. Preloading them preserves the Electron library cache during backfill.
            if (Kind == "reserve") tiles.AddRange(ReserveSnapshot(next: true).Shelves.SelectMany(shelf => shelf.Items.Concat(shelf.Reserve)));
            if (Kind is "five" or "invalidation") tiles.Add(FinalItem());
            if (Kind is "primary" or "recent") tiles.Add(new(2, 2, "Second game", ""));
            foreach (var item in tiles.DistinctBy(item => item.ReleaseId))
            {
                connection.Execute("""
                    INSERT INTO works(id,name,sort_name) VALUES(@id,@title,@title);
                    INSERT INTO releases(id,work_id,name,platform) VALUES(@id,@id,@title,'windows');
                    INSERT INTO ownerships(id,release_id,store,installed) VALUES(@id,@id,'steam',0);
                    INSERT INTO external_ids(release_id,provider,provider_id) VALUES(@id,'steam',@external);
                    """, new { id = item.ReleaseId, title = item.Title, external = item.ReleaseId.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                if (Kind is "reserve" or "reserve-invalidation" or "five" or "invalidation" or "excess8" or "history" or "throwing" or "optional-stale")
                    connection.Execute("""
                        INSERT INTO play_records(ownership_id,playtime_minutes,last_played_at,source,observed_at)
                        VALUES(@id,168,'2021-06-01 00:00:00','steam_web_api',@now);
                        INSERT INTO update_events(release_id,kind,occurred_at,title,build_id)
                        VALUES(@id,'build_push','2025-06-01 00:00:00','A major update','fixture');
                        INSERT INTO update_events(release_id,kind,occurred_at,title)
                        VALUES(@id,'announcement','2025-06-01 00:00:00','A major update');
                        """, new { id = item.ReleaseId, now = Now });
            }
            lock (_sync) _snapshot = snapshot;
        }
        if (input.HoldBuiltin) controls.Arm(new());
        if (Kind is "optional" or "optional-stale") controls.Arm(new("supplement"));
        lock (_sync) Ready = true;
        if (input.Publish) await PublishAsync(1);
        return State();
    }

    public void Next(string kind)
    {
        lock (_sync)
        {
            if (kind == "builtin") _supplement = new([], 0);
            _snapshot = kind switch
            {
                "reserve-next" => ReserveSnapshot(next: true),
                "final" => new([new("ready_to_play", "Installed and waiting", "Already on your disk, nothing sunk.", [FinalItem()])], 997, FeedConfidence.Settling, false),
                "builtin" => Kind == "optional-stale"
                    ? new([new("builtin", "Built in", "", [new(1, 1, "Deep Rock Galactic 1", "Baseline")])], 997, FeedConfidence.Settling, false)
                    : new([new("builtin", "Built in", "", [new(1, 1, "First game", "Baseline reason")])], 2, FeedConfidence.EarlyDays, false),
                _ => throw new ArgumentException("Unknown next feed snapshot."),
            };
        }
    }
    public async Task PublishAsync(int count)
    {
        if (count is < 1 or > 3) throw new ArgumentException("Publish between one and three changes.");
        for (var i = 0; i < count; i++) await publisher.PublishAsync(default);
    }
    public (bool Ready, string Kind, FeedSnapshot Snapshot) Capture()
    {
        lock (_sync) return (Ready, Ready ? Kind : "startup", _snapshot);
    }
    public async Task<FeedSupplement> ReadSupplementAsync(CancellationToken ct)
    {
        FeedSupplement snapshot;
        bool ready;
        string kind;
        lock (_sync) { snapshot = _supplement; ready = Ready; kind = Ready ? Kind : "startup"; }
        var call = await controls.EnterAsync("supplement", ready, kind, snapshot.Shelves, null, ct);
        try { return snapshot; }
        finally { call.Completed = true; }
    }
    public object State()
    {
        using var connection = database.Open();
        return new
        {
            Kind, Now, Ready, ProcessId = Environment.ProcessId, Calls = controls.Calls,
            Surfacings = connection.Query<Surfacing>("SELECT release_id ReleaseId,shelf_id ShelfId,surfaced_on SurfacedOn FROM feed_surfacings ORDER BY release_id").ToArray(),
            Verdicts = connection.Query<Verdict>("SELECT release_id ReleaseId,kind Kind,created_at CreatedAt,expires_at ExpiresAt,revoked_at RevokedAt FROM feed_verdicts ORDER BY id").ToArray(),
            LookupRows = connection.Query<Lookup>("SELECT w.id WorkId,w.name Title,r.id ReleaseId,o.id OwnershipId,o.store Store,o.installed Installed FROM works w JOIN releases r ON r.work_id=w.id JOIN ownerships o ON o.release_id=r.id ORDER BY r.id").ToArray(),
        };
    }
    internal sealed record Surfacing(long ReleaseId, string ShelfId, string SurfacedOn);
    internal sealed record Verdict(long ReleaseId, string Kind, DateTime CreatedAt, DateTime? ExpiresAt, DateTime? RevokedAt);
    internal sealed record Lookup(long WorkId, string Title, long ReleaseId, long OwnershipId, string Store, long Installed);

    private FeedSnapshot Build(string kind, bool supplemental)
    {
        _supplement = new([], 0);
        if (kind == "reserve") return ReserveSnapshot(false);
        if (kind == "reserve-invalidation") return new([new("ready_to_play", "Installed and waiting", "Already on your disk, nothing sunk.",
            Enumerable.Range(1, 2).Select(id => new FeedItem(id, id, $"Shown {id}", $"Never opened since it joined your library. (Shown {id})")).ToArray())], 997, FeedConfidence.Settling, false);
        if (kind == "viewport13") return new(Enumerable.Range(0, 2).Select(shelf => new FeedShelf($"shelf-{shelf}", "A shelf", "Games to revisit",
            Enumerable.Range(shelf * 6 + 1, 6).Select(id => new FeedItem(id, id, $"Fixture {id}", "You last played this game a long time ago.")).ToArray())
            { Reserve = shelf == 1 ? [new(13, 13, "Fixture 13", "A new reason for a held card.")] : [] }).ToArray(), 12, FeedConfidence.Established, false);
        if (kind == "excess8")
        {
            var items = Enumerable.Range(1, 8).Select(id => new FeedItem(id, id, $"Deep Rock Galactic {id}", $"Reason {id}")).ToArray();
            var shelf = new FeedShelf(supplemental ? "plugin:extra" : "patched", "Shelf", "", items.Take(6).ToArray()) { Reserve = items.Skip(6).ToArray() };
            if (supplemental) { _supplement = new([shelf], 8); return new([], 0, FeedConfidence.Settling, false); }
            return new([shelf], 997, FeedConfidence.Settling, false);
        }
        if (kind == "optional-stale")
        {
            _supplement = new([new("plugin:extra", "Extra", "", [new(2, 2, "Deep Rock Galactic 2", "Optional")])], 2);
            return new([new("builtin", "Built in", "", [new(1, 1, "Deep Rock Galactic 1", "Baseline")])], 997, FeedConfidence.Settling, false);
        }
        if (kind == "optional")
        {
            _supplement = new([new("plugin:extra", "Optional", "", [new(2, 2, "Second game", "Optional reason")])], 2);
            return new([new("builtin", "Built in", "", [new(1, 1, "First game", "Baseline reason")])], 2, FeedConfidence.EarlyDays, false);
        }
        if (kind is "primary" or "recent")
        {
            var recommended = new FeedShelf("recommended", "Recommended", "", [new(1, 1, "First game", kind == "primary" ? "An update arrived after your last session." : "Last played today.")]);
            return new(kind == "primary" ? [recommended] : [new("recently_played", "Recently played", "Your latest games", recommended.Items) { SupportsFeedback = false }, recommended], 1, FeedConfidence.Established, false);
        }
        if (kind == "throwing") return new([], 0, FeedConfidence.EarlyDays, false);
        if (kind == "history") return new([new("patched_while_away", "Patched while you were away", "Pitch.",
            [new(1, 1, "Deep Rock Galactic 1", "You put 2.8 hours into this in 2021 and it has had an update since.")])], 997, FeedConfidence.Settling, false);
        return new(FiveShelves, 997, FeedConfidence.Settling, false);
    }
    internal static FeedSnapshot ReserveSnapshot(bool next)
    {
        var items = Enumerable.Range(next ? 200 : 1, 5).Select(id => new FeedItem(id, id, $"Shown {id}", $"Never opened since it joined your library. (Shown {id})")).ToArray();
        var held = next ? 300 : 101;
        return new([new("ready_to_play", "Installed and waiting", "Already on your disk, nothing sunk.", items)
        { Reserve = [new(held, held, $"Held {held}", $"Never opened since it joined your library. (Held {held})")] }], 997, FeedConfidence.Settling, false);
    }
    private static FeedItem FinalItem() => new(99, 99, "Deep Rock Galactic 99", "The final library state.");
    internal static IReadOnlyList<FeedShelf> FiveShelves { get; } = new[]
    {
        ("patched_while_away", "Patched while you were away", "Major updates landed after you stopped playing.", "You put 2.8 hours into this in 2021 and it has had an update since, most recently \"PATCH NOTES - S06.05.02\". This matches your taste in Survival games."),
        ("worth_another_look", "Worth another look", "You committed real hours past the refund line, then drifted off mid-story.", "You put 2.5 hours in — past the refund line — then let it go — that was 2022."),
        ("ready_to_play", "Installed and waiting", "Already on your disk with nothing sunk.", "Never opened since it joined your library. It's installed and ready to launch."),
        ("barely_touched", "Barely gave it a chance", "Under 2 hours in — you opened the door and never walked through.", "You tried it for 104 minutes and never went back — that was 2017."),
        ("on_your_taste", "Never opened, right up your alley", "Sitting sealed in your library, and it matches where your hours actually go.", "Never opened since it joined your library. This matches your taste in Sandbox games."),
    }.Select((row, index) => new FeedShelf(row.Item1, row.Item2, row.Item3, [new(index + 1, index + 1, $"Deep Rock Galactic {index + 1}", row.Item4)])).ToArray();
}

internal sealed class RecommendationClock(RecommendationStateFixture fixture) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(fixture.Now);
}
internal sealed class RecommendationFixtureEngine(RecommendationStateFixture fixture, RecommendationControls controls,
    RecommendationEngine actual) : IRecommendationEngine
{
    public Task<RecommendationFeed> GetFeedAsync(RecommendationRequest request, CancellationToken ct = default)
        => actual.GetFeedAsync(request, ct);
    public async Task<ShelfFeed> GetShelvesAsync(RecommendationRequest request, CancellationToken ct = default)
    {
        var captured = fixture.Capture();
        var snapshot = captured.Snapshot;
        var call = await controls.EnterAsync("builtin", captured.Ready, captured.Kind, snapshot.Shelves, request, ct);
        try
        {
            if (call.Kind == "throwing") throw new InvalidOperationException("the database went away mid-pass");
            if (call.Kind == "composition")
            {
                var answer = await actual.GetShelvesAsync(request, ct);
                call.ReleaseIds = answer.Shelves.SelectMany(shelf => shelf.Items).Select(item => item.ReleaseId).ToArray();
                return answer;
            }
            return new()
            {
                CandidateCount = snapshot.CandidateCount, Tier = (DataTier)snapshot.Confidence,
                Shelves = snapshot.Shelves.Select(shelf => new RecommendationShelf
                {
                    Id = shelf.Id, Title = shelf.Title, Blurb = shelf.Blurb, SupportsFeedback = shelf.SupportsFeedback,
                    Items = shelf.Items.Concat(shelf.Reserve).Select(item => new Recommendation
                    {
                        OwnershipId = item.OwnershipId, ReleaseId = item.ReleaseId, WorkId = item.ReleaseId,
                        Title = item.Title, Store = "steam", Bucket = "stale_but_patched", Score = 1, Reason = item.Reason,
                        Explanation = new() { Primary = ReasonSignal.None, Evidence = new() { ReleaseId = item.ReleaseId, Title = item.Title } }, Signals = [],
                    }).ToArray(),
                }).ToArray(),
            };
        }
        catch { call.Failed = true; throw; }
        finally { call.Completed = true; }
    }
}

internal sealed class RecommendationControls
{
    private readonly Lock _sync = new();
    private readonly List<Plan> _plans = [];
    private readonly ConcurrentDictionary<string, Plan> _held = new();
    private readonly ConcurrentQueue<Call> _calls = new();
    private int _id;
    public IReadOnlyList<Call> Calls => _calls.ToArray();
    public void Reset()
    {
        if (_held.Values.Any(plan => !plan.Done.Task.IsCompleted)) throw new InvalidOperationException("Release active gates before seeding.");
        lock (_sync) _plans.Clear();
        _held.Clear(); _calls.Clear(); _id = 0;
    }
    public object Arm(RecommendationArm input)
    {
        if (input.Operation is not ("builtin" or "supplement") || input.Behavior is not ("hold" or "fail"))
            throw new ArgumentException("Unknown recommendation plan.");
        var plan = new Plan(Guid.NewGuid().ToString("N"), input);
        lock (_sync) _plans.Add(plan);
        return new { GateId = plan.Id };
    }
    public void Release(string? id = null)
    {
        foreach (var plan in _held.Values.Where(plan => id is null || id == plan.Id)) plan.Done.TrySetResult();
    }
    public async Task<Call> EnterAsync(string operation, bool ready, string kind, IReadOnlyList<FeedShelf> shelves,
        RecommendationRequest? request, CancellationToken ct)
    {
        Plan? plan = null;
        if (ready) lock (_sync)
        {
            plan = _plans.FirstOrDefault(plan => plan.Input.Operation == operation);
            if (plan is not null) _plans.Remove(plan);
        }
        var call = new Call(Interlocked.Increment(ref _id), operation, ready ? kind : "startup", plan?.Id, request,
            shelves.SelectMany(shelf => shelf.Items.Concat(shelf.Reserve)).Select(item => item.ReleaseId).ToArray(), ct);
        if (plan is not null) _held[plan.Id] = plan;
        // Publication follows registration so an observed call is always releasable.
        _calls.Enqueue(call);
        try
        {
            if (plan?.Input.Behavior == "fail") throw new InvalidOperationException("Source feed reader refusal.");
            if (plan?.Input.Behavior == "hold")
            {
                if (plan.Input.IgnoreCancellation) await plan.Done.Task;
                else await plan.Done.Task.WaitAsync(ct);
            }
            return call;
        }
        catch { call.Failed = true; call.Completed = true; throw; }
    }
    private sealed record Plan(string Id, RecommendationArm Input)
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    internal sealed class Call(int callId, string operation, string kind, string? gateId, RecommendationRequest? request,
        IReadOnlyList<long> releaseIds, CancellationToken token)
    {
        public int CallId { get; } = callId;
        public string Operation { get; } = operation;
        public string Kind { get; } = kind;
        public string? GateId { get; } = gateId;
        public RecommendationRequest? Request { get; } = request;
        public IReadOnlyList<long> ReleaseIds { get; set; } = releaseIds;
        public bool Canceled => token.IsCancellationRequested;
        public bool Completed { get; set; }
        public bool Failed { get; set; }
    }
}
