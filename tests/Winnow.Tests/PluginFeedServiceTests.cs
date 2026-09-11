using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Data.Repositories;
using Winnow.PluginFixture;
using Winnow.Plugins;
using Winnow.PluginSdk;
using Winnow.Recommend;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginFeedServiceTests
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    [Fact]
    public async Task Loaded_feed_sees_only_eligible_games_and_collapsed_game_facts()
    {
        await using var host = await Host.CreateAsync(12);
        using (var connection = host.Database.Factory.Open())
        {
            await connection.ExecuteAsync("""
                UPDATE works SET name_is_provisional=1 WHERE id=2;
                UPDATE works SET steam_app_type='tool' WHERE id=3;
                UPDATE ownerships SET installed=1 WHERE id=11;
                INSERT INTO play_records(ownership_id,playtime_minutes,source,observed_at) VALUES
                    (4,12000,'fixture',@now),(10,60,'fixture',@now),(11,70,'fixture',@now);
                """, new { now = Now });
        }
        await new LifecycleRepository(host.Database.Factory).AppendAsync(new LifecycleObservation
        {
            ReleaseId = 5, Source = "igdb", SourceId = "5", ObservedAt = Now,
            Signals = new() { IgdbStatus = "cancelled" },
        });
        using (var connection = host.Database.Factory.Open())
            await connection.ExecuteAsync("UPDATE works SET igdb_id=5 WHERE id=5;");
        await host.VerdictAsync(6, FeedVerdictKinds.NotInterested);
        await host.VerdictAsync(7, FeedVerdictKinds.Snoozed, Now.AddDays(2));
        await host.VerdictAsync(8, FeedVerdictKinds.NotInterested);
        await host.Feedback.RevokeVerdictsAsync(8, FeedVerdictKinds.NotInterested, Now);
        await host.VerdictAsync(9, FeedVerdictKinds.Snoozed, Now.AddDays(-1));
        await new IdentityLinkRepository(host.Database.Factory).LinkAsync(new IdentityLinkRequest { ParentWorkId = 10, ChildWorkIds = [11] });
        await host.Facets.SetWorkFacetsAsync(10, [new(FacetKinds.Genre, "Adventure")]);
        await host.Facets.SetReleaseFacetsAsync(11, [new(FacetKinds.Tag, "Exploration")]);

        var result = await host.Service.GetShelvesAsync(Now);
        Assert.Equal(5, result.CandidateCount);
        var supplied = host.SuppliedGames();
        Assert.Equal(["1", "8", "9", "11", "12"], supplied.Select(game => game.Id));
        var linked = Assert.Single(supplied, game => game.Id == "11");
        Assert.Equal(130, linked.PlaytimeMinutes);
        Assert.True(linked.Installed);
        Assert.Equal(["Adventure"], linked.Genres);
        Assert.Equal(["Exploration"], linked.Tags);
        Assert.Equal("11", linked.ExternalIds["steam"]);
        var shelf = Assert.Single(result.Shelves);
        Assert.Equal("plugin:fixture-feed", shelf.Id);
        Assert.Equal("Fixture feed", shelf.Title);
        Assert.Equal(5, shelf.Items.Count);
    }

    [Theory]
    [InlineData(FeedVerdictKinds.NotInterested)]
    [InlineData(FeedVerdictKinds.Snoozed)]
    public async Task Feedback_on_a_linked_child_suppresses_the_whole_game_until_unlinked(string kind)
    {
        await using var host = await Host.CreateAsync(3);
        var links = new IdentityLinkRepository(host.Database.Factory);
        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = 1, ChildWorkIds = [2] });
        await host.VerdictAsync(2, kind, kind == FeedVerdictKinds.Snoozed ? Now.AddDays(1) : null);
        var linked = Assert.Single((await host.Service.GetShelvesAsync(Now)).Shelves);
        Assert.Equal(3, Assert.Single(linked.Items).OwnershipId);

        await links.RetractLinkAsync(2);
        var unlinked = Assert.Single((await host.Service.GetShelvesAsync(Now)).Shelves);
        Assert.Equal([1L, 3L], unlinked.Items.Select(item => item.OwnershipId));
    }

    [Fact]
    public async Task Account_visibility_applies_before_any_library_data_reaches_the_plugin()
    {
        await using var host = await Host.CreateAsync(2);
        var accounts = new OwnershipAccountRepository(host.Database.Factory);
        await accounts.UpsertAsync(new OwnershipAccountUpsert(1, "11111", 0, null, "steam_web", Now));
        await accounts.UpsertAsync(new OwnershipAccountUpsert(2, "22222", 0, null, "steam_web", Now));
        var inventories = new OwnershipInventoryRepository(host.Database.Factory);
        var attempt = await inventories.BeginAttemptAsync("steam", "11111", OwnershipInventorySources.SteamOwnedGames);
        await inventories.CompleteAsync(attempt, Now, 1);
        var settings = new SettingsRepository(host.Database.Factory);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        Assert.Single((await host.Service.GetShelvesAsync(Now)).Shelves);
        Assert.Equal("1", Assert.Single(host.SuppliedGames()).Id);
    }

    [Fact]
    public async Task Hidden_child_verdict_uses_complete_identity_before_plugin_disclosure()
    {
        await using var host = await Host.CreateAsync(3);
        var links = new IdentityLinkRepository(host.Database.Factory);
        await links.LinkAsync(new IdentityLinkRequest { ParentWorkId = 1, ChildWorkIds = [2] });
        using (var connection = host.Database.Factory.Open())
            await connection.ExecuteAsync("UPDATE ownerships SET store='epic' WHERE id=1;");
        var accounts = new OwnershipAccountRepository(host.Database.Factory);
        await accounts.UpsertAsync(new(2, "22222", 0, null, "steam_web", Now));
        var inventories = new OwnershipInventoryRepository(host.Database.Factory);
        var attempt = await inventories.BeginAttemptAsync("steam", "11111", OwnershipInventorySources.SteamOwnedGames);
        await inventories.CompleteAsync(attempt, Now, 0);
        var settings = new SettingsRepository(host.Database.Factory);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        await host.VerdictAsync(2, FeedVerdictKinds.NotInterested);
        await host.Service.GetShelvesAsync(Now);
        Assert.DoesNotContain(host.SuppliedGames(), game => game.Id is "1" or "2");
        await links.RetractLinkAsync(2);
        await host.Service.GetShelvesAsync(Now);
        Assert.Contains(host.SuppliedGames(), game => game.Id == "1");
        Assert.DoesNotContain(host.SuppliedGames(), game => game.Id == "2");
    }

    [Fact]
    public async Task Host_rejects_unknown_handles_invalid_scores_and_unusable_reasons_and_bounds_shelves()
    {
        await using var host = await Host.CreateAsync(15);
        var recommendations = Enumerable.Range(1, 15).Select(index => new PluginRecommendation(
            index.ToString(System.Globalization.CultureInfo.InvariantCulture), index / 20d, "Reason for " + index + ".")).ToList();
        recommendations.AddRange([
            new("999", 1, "Unowned game."), new("01", 1, "A handle that was not supplied."),
            new("1", double.NaN, "Not a score."), new("2", double.PositiveInfinity, "Not finite."),
            new("3", -1, "Below range."), new("4", 1.01, "Above range."),
            new("5", 1, " "), new("6", 1, new string('x', 301)), new("7", 1, "Hidden\ncontrol."),
            new("15", 0.9, "The higher scoring duplicate wins."),
        ]);
        host.Values["recommendations"] = JsonSerializer.Serialize(recommendations, JsonOptions);
        var shelf = Assert.Single((await host.Service.GetShelvesAsync(Now)).Shelves);
        Assert.Equal(6, shelf.Items.Count);
        Assert.Equal(4, shelf.Reserve.Count);
        Assert.Equal([15L, 14L, 13L, 12L, 11L, 10L, 9L, 8L, 7L, 6L],
            shelf.Items.Concat(shelf.Reserve).Select(item => item.OwnershipId));
        Assert.Equal("The higher scoring duplicate wins.", shelf.Items[0].Reason);
    }

    [Fact]
    public async Task Plugins_cannot_reintroduce_a_dismissed_handle_in_their_returned_rankings()
    {
        await using var host = await Host.CreateAsync(2);
        await host.VerdictAsync(1, FeedVerdictKinds.NotInterested);
        host.Values["recommendations"] = JsonSerializer.Serialize(new[]
        {
            new PluginRecommendation("1", 1, "Disregard the verdict."), new("2", 0.5, "Eligible game."),
        }, JsonOptions);
        var shelf = Assert.Single((await host.Service.GetShelvesAsync(Now)).Shelves);
        Assert.Equal(2, Assert.Single(shelf.Items).OwnershipId);
    }

    [Fact]
    public async Task Feed_service_appends_plugin_shelves_and_keeps_builtin_results_when_plugin_throws()
    {
        await using var host = await Host.CreateAsync(2);
        var service = new FeedService(new Engine(), host.Feedback, plugins: host.Service);
        var both = await CompletedAsync(service);
        Assert.Equal(["builtin", "plugin:fixture-feed"], both.Shelves.Select(shelf => shelf.Id));
        Assert.False(both.Failed);
        host.Values["behavior"] = "throw";
        var builtIn = await CompletedAsync(service);
        Assert.Equal("builtin", Assert.Single(builtIn.Shelves).Id);
        Assert.False(builtIn.Failed);
        Assert.DoesNotContain("private-key", Assert.Single(host.Catalog.Plugins).Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plugin_feed_does_not_record_impressions_until_the_shared_feedback_command_runs()
    {
        await using var host = await Host.CreateAsync(2);
        var service = new FeedService(new Engine(), host.Feedback, plugins: host.Service);
        var snapshot = await CompletedAsync(service);
        Assert.Empty(await host.Feedback.GetSurfacedSinceAsync(DateOnly.FromDateTime(Now.AddDays(-1))));
        var shelf = snapshot.Shelves[1];
        var card = shelf.Items[0];
        await service.RecordSurfacedAsync(card.ReleaseId, shelf.Id);
        var surfaced = Assert.Single(await host.Feedback.GetSurfacedSinceAsync(DateOnly.FromDateTime(Now.AddDays(-1))));
        Assert.Equal("plugin:fixture-feed", surfaced.ShelfId);
        await service.RecordVerdictAsync(card.ReleaseId, FeedVerdictKind.NotInterested);
        var refreshed = await CompletedAsync(service);
        Assert.DoesNotContain(refreshed.Shelves[1].Items, item => item.ReleaseId == card.ReleaseId);
    }

    private static async Task<FeedSnapshot> CompletedAsync(IFeedService service)
    {
        var baseline = await service.GetShelvesAsync();
        var addition = await baseline.AdditionalShelves!;
        return baseline with { Shelves = baseline.Shelves.Concat(addition.Shelves).ToArray() };
    }

    [Theory]
    [InlineData("wait")]
    [InlineData("wait-ignore-cancellation")]
    public async Task Builtin_feed_is_available_before_optional_provider_and_host_wait_is_bounded(string behavior)
    {
        await using var host = await Host.CreateAsync(2);
        host.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Values["behavior"] = behavior;
        var reads = await FeedReads.CaptureAsync(host);
        var clock = new ManualDeadline();
        host.Service = new(host.Catalog, reads, reads, reads, clock);
        var service = new FeedService(new Engine(), host.Feedback, plugins: host.Service);
        try
        {
            var baseline = await service.GetShelvesAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("builtin", Assert.Single(baseline.Shelves).Id);
            await host.GateEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(host.Gate.Task.IsCompleted);
            clock.Expire();
            var extra = await baseline.AdditionalShelves!.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(extra.Shelves);
            Assert.Equal("builtin", Assert.Single(baseline.Shelves).Id);
        }
        finally { host.Gate.TrySetResult(null); }
    }

    [Fact]
    public async Task Fast_provider_survives_another_provider_exhausting_the_aggregate_budget()
    {
        await using var host = await Host.CreateAsync(2, includeImmediate: true);
        host.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        host.Values["behavior"] = "wait";
        var reads = await FeedReads.CaptureAsync(host);
        var clock = new ManualDeadline();
        host.Service = new(host.Catalog, reads, reads, reads, clock);
        try
        {
            var pending = host.Service.GetShelvesAsync(Now);
            await host.GateEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // Cached input reads complete synchronously, so both invocations have entered
            // their gates before GetShelvesAsync yields. A marker behind the fast one
            // proves it completed before expiry, independently of thread-pool scheduling.
            var immediate = Assert.Single(host.Catalog.Plugins, item => item.Manifest.Id == "fixture-immediate");
            await host.Catalog.InvokeAsync<string>(immediate, (_, _) => Task.FromResult<string?>(null))
                .WaitAsync(TimeSpan.FromSeconds(3));
            clock.Expire();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("plugin:fixture-immediate", Assert.Single(result.Shelves).Id);
        }
        finally { host.Gate.TrySetResult(null); }
    }

    [Fact]
    public async Task Aggregate_budget_includes_waiting_behind_an_existing_provider_invocation()
    {
        await using var host = await Host.CreateAsync(2);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupying = host.Catalog.InvokeAsync<string>(Assert.Single(host.Catalog.Plugins), (_, _) =>
        {
            entered.SetResult();
            return release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var reads = await FeedReads.CaptureAsync(host);
        var clock = new ManualDeadline();
        var service = new PluginFeedService(host.Catalog, reads, reads, reads, clock);
        try
        {
            var pending = service.GetShelvesAsync(Now);
            Assert.False(pending.IsCompleted);
            clock.Expire();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(result.Shelves);
            Assert.Equal(2, result.CandidateCount);
            Assert.False(occupying.IsCompleted);
            Assert.Null(host.Values.GetValueOrDefault("supplied-games"));
        }
        finally { release.TrySetResult(null); await occupying; }
    }

    [Theory]
    [InlineData("library", false)]
    [InlineData("feedback", false)]
    [InlineData("facets", false)]
    [InlineData("library", true)]
    [InlineData("feedback", true)]
    [InlineData("facets", true)]
    public async Task Shared_read_expiry_is_an_empty_supplement_but_caller_cancellation_propagates(string stage, bool callerCancels)
    {
        await using var host = await Host.CreateAsync(2);
        var reads = await FeedReads.CaptureAsync(host);
        reads.BlockAt = stage;
        var clock = new ManualDeadline();
        var service = new PluginFeedService(host.Catalog, reads, reads, reads, clock);
        using var caller = new CancellationTokenSource();
        var pending = service.GetShelvesAsync(Now, caller.Token);
        await reads.BlockEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(pending.IsCompleted);
        if (callerCancels) caller.Cancel();
        else clock.Expire();
        if (callerCancels)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        else
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(result.Shelves);
            Assert.Equal(0, result.CandidateCount);
        }
        Assert.Null(host.Values.GetValueOrDefault("supplied-games"));
    }

    private sealed class ManualDeadline : TimeProvider
    {
        private ControlledTimer? _timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(5), dueTime);
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            return _timer = new ControlledTimer(callback, state);
        }
        public void Expire() => (_timer ?? throw new InvalidOperationException("No deadline was created.")).Fire();

        private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed;
            public void Fire() { if (!_disposed) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class FeedReads(LibrarySnapshot library, FacetSnapshot facets, IReadOnlyList<FeedVerdict> verdicts)
        : ILibraryQueryRepository, IFeedFeedbackRepository, IFacetRepository
    {
        public string? BlockAt { get; set; }
        public TaskCompletionSource BlockEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static async Task<FeedReads> CaptureAsync(Host host) => new(
            await new LibraryQueryRepository(host.Database.Factory).GetSnapshotAsync(BucketThresholds.Default),
            await host.Facets.GetSnapshotAsync(), await host.Feedback.GetActiveVerdictsAsync(Now));
        private Task BeforeRead(string stage, CancellationToken ct)
        {
            if (BlockAt != stage) return Task.CompletedTask;
            BlockEntered.TrySetResult();
            return Task.Delay(Timeout.Infinite, ct);
        }
        public async Task<LibrarySnapshot> GetSnapshotAsync(BucketThresholds thresholds, CancellationToken ct = default)
        { await BeforeRead("library", ct); return library; }
        public async Task<FacetSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        { await BeforeRead("facets", ct); return facets; }
        public async Task<IReadOnlyList<FeedVerdict>> GetActiveVerdictsAsync(DateTime asOfUtc, CancellationToken ct = default)
        { await BeforeRead("feedback", ct); return verdicts; }
        public Task<IReadOnlyList<OwnershipBucket>> GetOwnershipBucketsAsync(BucketThresholds thresholds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountHiddenByAccountScopeAsync(BucketThresholds thresholds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountHiddenByExplicitFilterAsync(BucketThresholds thresholds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountHiddenByRatingCapAsync(BucketThresholds thresholds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FacetTarget>> GetFacetTargetsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Facet>> GetVocabularyAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SetWorkFacetsAsync(long workId, IReadOnlyList<FacetAssignment> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SetReleaseFacetsAsync(long releaseId, IReadOnlyList<FacetAssignment> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> RecordVerdictAsync(FeedVerdict verdict, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> RevokeVerdictsAsync(long releaseId, string kind, DateTime revokedAtUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeedVerdict>> GetAllVerdictsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordSurfacedAsync(IReadOnlyList<FeedSurfacing> surfacings, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeedSurfacing>> GetSurfacedSinceAsync(DateOnly since, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeedEndorsement>> GetEndorsementsAsync(int windowDays, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Host : IAsyncDisposable, IPluginStateStore, IPluginContextFactory, IPluginContext, IPluginSettings
    {
        public TempDatabase Database { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-feed-plugin-" + Guid.NewGuid().ToString("N"));
        public PluginCatalog Catalog { get; private set; } = null!;
        public PluginFeedService Service { get; set; } = null!;
        public FeedFeedbackRepository Feedback { get; private set; } = null!;
        public FacetRepository Facets { get; private set; } = null!;
        public System.Collections.Concurrent.ConcurrentDictionary<string, string?> Values { get; } = [];
        public TaskCompletionSource<string?>? Gate { get; set; }
        public TaskCompletionSource GateEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string PluginId => "fixture-feed";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => throw new NotSupportedException();
        public IPluginCache Cache => throw new NotSupportedException();
        public IPluginHttp Http => throw new NotSupportedException();

        public static async Task<Host> CreateAsync(int count, bool includeImmediate = false)
        {
            var host = new Host();
            LibraryReadFixtures.Seed(host.Database, count);
            host.Feedback = new(host.Database.Factory);
            host.Facets = new(host.Database.Factory);
            host.Catalog = new(host, host);
            host.Service = new(host.Catalog, new LibraryQueryRepository(host.Database.Factory), host.Feedback, host.Facets);
            var directory = Path.Combine(host.Root, "fixture-feed");
            Directory.CreateDirectory(directory);
            File.Copy(typeof(ConfigurableFeedPlugin).Assembly.Location, Path.Combine(directory, "Winnow.PluginFixture.dll"));
            var manifest = new PluginManifest
            {
                Id = "fixture-feed", Name = "Fixture feed", Version = "1.0.0",
                EntryAssembly = "Winnow.PluginFixture.dll", EntryType = typeof(ConfigurableFeedPlugin).FullName!,
                Capabilities = [PluginCapabilities.Recommendations],
                Settings = [new() { Key = "behavior", Label = "Behavior" }, new() { Key = "recommendations", Label = "Recommendations" },
                    new() { Key = "supplied-games", Label = "Supplied games" }],
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "plugin.json"), JsonSerializer.Serialize(manifest, JsonOptions));
            if (includeImmediate)
            {
                var immediateDirectory = Path.Combine(host.Root, "fixture-immediate");
                Directory.CreateDirectory(immediateDirectory);
                File.Copy(typeof(ImmediateFeedPlugin).Assembly.Location, Path.Combine(immediateDirectory, "Winnow.PluginFixture.dll"));
                var immediate = new PluginManifest
                {
                    Id = "fixture-immediate", Name = "Immediate feed", Version = "1.0.0",
                    EntryAssembly = "Winnow.PluginFixture.dll", EntryType = typeof(ImmediateFeedPlugin).FullName!,
                    Capabilities = [PluginCapabilities.Recommendations],
                };
                await File.WriteAllTextAsync(Path.Combine(immediateDirectory, "plugin.json"), JsonSerializer.Serialize(immediate, JsonOptions));
            }
            await host.Catalog.DiscoverAsync(host.Root, Path.Combine(host.Root, "absent"));
            Assert.Equal(includeImmediate ? 2 : 1, host.Catalog.Plugins.Count);
            Assert.All(host.Catalog.Plugins, plugin => Assert.True(plugin.Loaded));
            return host;
        }

        public Task<long> VerdictAsync(long releaseId, string kind, DateTime? expiresAt = null)
            => Feedback.RecordVerdictAsync(new FeedVerdict { ReleaseId = releaseId, Kind = kind, CreatedAt = Now.AddDays(-2), ExpiresAt = expiresAt });

        public PluginGame[] SuppliedGames() => JsonSerializer.Deserialize<PluginGame[]>(Values["supplied-games"]!, JsonOptions)!;
        public ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken cancellationToken = default) => ValueTask.FromResult<bool?>(true);
        public ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public IPluginContext Create(PluginManifest manifest) => this;
        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            if (key == "gate" && Gate is { } gate)
            {
                GateEntered.TrySetResult();
                return new(gate.Task.WaitAsync(cancellationToken));
            }
            return ValueTask.FromResult(Values.GetValueOrDefault(key));
        }
        public ValueTask SetAsync(string key, string? value, CancellationToken cancellationToken = default)
        {
            Values[key] = value;
            return ValueTask.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            await Catalog.DisposeAsync();
            Database.Dispose();
            // Collectible assembly unloading completes after GC; an async test frame can
            // retain a type briefly after the catalog has released its last reference.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var absoluteRoot = Path.GetFullPath(Root);
            if (!absoluteRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The fixture directory escaped the temporary directory.");
            try { Directory.Delete(absoluteRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class Engine : IRecommendationEngine
    {
        public Task<RecommendationFeed> GetFeedAsync(RecommendationRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ShelfFeed> GetShelvesAsync(RecommendationRequest request, CancellationToken ct = default) => Task.FromResult(new ShelfFeed
        {
            CandidateCount = 2, Tier = DataTier.Established,
            Shelves = [new() { Id = "builtin", Title = "Built-in", Blurb = "Built-in fixture.", Items = [] }],
        });
    }
}
