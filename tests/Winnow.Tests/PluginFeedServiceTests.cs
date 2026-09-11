using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Lifecycle;
using Winnow.Core.Queries;
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
            ReleaseId = 5, Source = "igdb", ObservedAt = Now,
            Signals = new() { IgdbStatus = "cancelled" },
        });
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
        Assert.Equal(["1", "8", "9", "10", "12"], supplied.Select(game => game.Id));
        var linked = Assert.Single(supplied, game => game.Id == "10");
        Assert.Equal(130, linked.PlaytimeMinutes);
        Assert.True(linked.Installed);
        Assert.Equal(["Adventure"], linked.Genres);
        Assert.Equal(["Exploration"], linked.Tags);
        Assert.Equal("10", linked.ExternalIds["steam"]);
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
        var settings = new SettingsRepository(host.Database.Factory);
        await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "11111");
        await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
        Assert.Single((await host.Service.GetShelvesAsync(Now)).Shelves);
        Assert.Equal("1", Assert.Single(host.SuppliedGames()).Id);
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
        var both = await service.GetShelvesAsync();
        Assert.Equal(["builtin", "plugin:fixture-feed"], both.Shelves.Select(shelf => shelf.Id));
        Assert.False(both.Failed);
        host.Values["behavior"] = "throw";
        var builtIn = await service.GetShelvesAsync();
        Assert.Equal("builtin", Assert.Single(builtIn.Shelves).Id);
        Assert.False(builtIn.Failed);
        Assert.DoesNotContain("private-key", Assert.Single(host.Catalog.Plugins).Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Plugin_feed_does_not_record_impressions_until_the_shared_feedback_command_runs()
    {
        await using var host = await Host.CreateAsync(2);
        var service = new FeedService(new Engine(), host.Feedback, plugins: host.Service);
        var snapshot = await service.GetShelvesAsync();
        Assert.Empty(await host.Feedback.GetSurfacedSinceAsync(DateOnly.FromDateTime(Now.AddDays(-1))));
        var shelf = snapshot.Shelves[1];
        var card = shelf.Items[0];
        await service.RecordSurfacedAsync(card.ReleaseId, shelf.Id);
        var surfaced = Assert.Single(await host.Feedback.GetSurfacedSinceAsync(DateOnly.FromDateTime(Now.AddDays(-1))));
        Assert.Equal("plugin:fixture-feed", surfaced.ShelfId);
        await service.RecordVerdictAsync(card.ReleaseId, FeedVerdictKind.NotInterested);
        var refreshed = await service.GetShelvesAsync();
        Assert.DoesNotContain(refreshed.Shelves[1].Items, item => item.ReleaseId == card.ReleaseId);
    }

    private sealed class Host : IAsyncDisposable, IPluginStateStore, IPluginContextFactory, IPluginContext, IPluginSettings
    {
        public TempDatabase Database { get; } = new();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "winnow-feed-plugin-" + Guid.NewGuid().ToString("N"));
        public PluginCatalog Catalog { get; private set; } = null!;
        public PluginFeedService Service { get; private set; } = null!;
        public FeedFeedbackRepository Feedback { get; private set; } = null!;
        public FacetRepository Facets { get; private set; } = null!;
        public Dictionary<string, string?> Values { get; } = [];
        public string PluginId => "fixture-feed";
        public IPluginSettings Settings => this;
        public IPluginSecrets Secrets => throw new NotSupportedException();
        public IPluginCache Cache => throw new NotSupportedException();
        public IPluginHttp Http => throw new NotSupportedException();

        public static async Task<Host> CreateAsync(int count)
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
            await host.Catalog.DiscoverAsync(host.Root, Path.Combine(host.Root, "absent"));
            Assert.True(Assert.Single(host.Catalog.Plugins).Loaded);
            return host;
        }

        public Task<long> VerdictAsync(long releaseId, string kind, DateTime? expiresAt = null)
            => Feedback.RecordVerdictAsync(new FeedVerdict { ReleaseId = releaseId, Kind = kind, CreatedAt = Now.AddDays(-2), ExpiresAt = expiresAt });

        public PluginGame[] SuppliedGames() => JsonSerializer.Deserialize<PluginGame[]>(Values["supplied-games"]!, JsonOptions)!;
        public ValueTask<bool?> GetEnabledAsync(string pluginId, CancellationToken cancellationToken = default) => ValueTask.FromResult<bool?>(true);
        public ValueTask SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public IPluginContext Create(PluginManifest manifest) => this;
        public ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default) => ValueTask.FromResult(Values.GetValueOrDefault(key));
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
