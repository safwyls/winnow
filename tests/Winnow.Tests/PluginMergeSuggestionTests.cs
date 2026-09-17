using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Winnow.App;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Identity;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Enrich.Igdb.Storage;
using Winnow.PluginSdk;
using Winnow.Plugins;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class PluginMergeSuggestionTests
{
    [Theory]
    [InlineData("psn", "steam")]
    [InlineData("psn", "gog")]
    [InlineData("psn", "epic")]
    [InlineData("xbox", "steam")]
    [InlineData("xbox", "gog")]
    [InlineData("xbox", "epic")]
    public async Task Imported_console_entry_gets_one_suggestion_without_linking_or_replacing_source_records(
        string pluginId, string store)
    {
        await using var fixture = new ImportFixture();
        var native = await fixture.ImportNativeAsync(store);
        var console = await fixture.ImportPluginAsync(pluginId);
        Assert.NotEqual(native.WorkId, console.WorkId);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
        var ownerships = await fixture.Get<IOwnershipRepository>().GetAllAsync();
        var actions = await fixture.ReadActionsAsync(pluginId, console.Id);

        var refresh = fixture.Get<IMergeSuggestionRefresh>();
        using var scope = fixture.Provider.CreateScope();
        Assert.Same(refresh, scope.ServiceProvider.GetRequiredService<IMergeSuggestionRefresh>());
        var first = await refresh.RefreshAsync();
        var second = await refresh.RefreshAsync();

        Assert.Equal(1, first.Outcome.Queued);
        Assert.Equal(0, first.Outcome.AutoMerged);
        Assert.Equal(0, second.Outcome.Queued);
        Assert.Equal(1, second.Outcome.AlreadyPending);
        Assert.Equal(2, refresh.Revision);
        var candidate = Assert.Single(await fixture.Candidates.GetPendingAsync());
        Assert.Equal(native.Id, candidate.LeftReleaseId);
        Assert.Equal(console.Id, candidate.RightReleaseId);
        Assert.Equal(MergeCandidateStatuses.Pending, candidate.Status);
        Assert.Single(await fixture.Candidates.GetAllAsync());
        Assert.Empty(await fixture.Links.GetHistoryAsync());
        Assert.Equal(native, await fixture.Get<IReleaseRepository>().GetAsync(native.Id));
        Assert.Equal(console, await fixture.Get<IReleaseRepository>().GetAsync(console.Id));
        Assert.Equal(ownerships, await fixture.Get<IOwnershipRepository>().GetAllAsync());
        Assert.Equal(actions, await fixture.ReadActionsAsync(pluginId, console.Id));
        await fixture.AssertPluginFactsAsync(pluginId, console);
    }

    [Theory]
    [InlineData("psn", true)]
    [InlineData("psn", false)]
    [InlineData("xbox", true)]
    [InlineData("xbox", false)]
    public async Task Reimport_and_refresh_preserve_the_users_answer_and_source_entries(string pluginId, bool confirmed)
    {
        await using var fixture = new ImportFixture();
        var native = await fixture.ImportNativeAsync("steam");
        var console = await fixture.ImportPluginAsync(pluginId);
        var refresh = fixture.Get<IMergeSuggestionRefresh>();
        await refresh.RefreshAsync();
        var candidate = Assert.Single(await fixture.Candidates.GetPendingAsync());
        if (confirmed)
        {
            await fixture.Links.LinkAsync(new IdentityLinkRequest
            {
                ParentWorkId = native.WorkId,
                ChildWorkIds = [console.WorkId],
            });
        }
        else
        {
            await fixture.Candidates.SetStatusAsync(candidate.Id, MergeCandidateStatuses.Rejected);
        }
        var history = await fixture.Links.GetHistoryAsync();

        Assert.Equal(console, await fixture.ImportPluginAsync(pluginId));
        var report = await refresh.RefreshAsync();

        Assert.Equal(0, report.Outcome.Queued);
        Assert.Equal(0, report.Outcome.AutoMerged);
        Assert.Empty(await fixture.Candidates.GetPendingAsync());
        if (confirmed)
        {
            Assert.Empty(await fixture.Candidates.GetAllAsync());
            Assert.Equal(native.WorkId, (await fixture.Links.GetResolutionAsync()).SameGame.Resolve(console.WorkId));
        }
        else
        {
            var retained = Assert.Single(await fixture.Candidates.GetAllAsync());
            Assert.Equal(candidate.Id, retained.Id);
            Assert.Equal(MergeCandidateStatuses.Rejected, retained.Status);
        }
        Assert.Equal(history, await fixture.Links.GetHistoryAsync());
        Assert.Equal(2, (await fixture.Get<IReleaseRepository>().GetIdentitiesAsync()).Count);
        Assert.Equal(2, (await fixture.Get<IOwnershipRepository>().GetAllAsync()).Count);
        await fixture.AssertPluginFactsAsync(pluginId, console);
    }

    [Theory]
    [InlineData("psn")]
    [InlineData("xbox")]
    public async Task Account_changes_and_repeated_imports_do_not_duplicate_releases_or_suggestions(string pluginId)
    {
        await using var fixture = new ImportFixture();
        await fixture.ImportNativeAsync("gog");
        var console = await fixture.ImportPluginAsync(pluginId, "fixture-account-a", 120);
        await fixture.Get<IMergeSuggestionRefresh>().RefreshAsync();
        var candidate = Assert.Single(await fixture.Candidates.GetPendingAsync());

        foreach (var account in new[] { "fixture-account-b", "fixture-account-a", "fixture-account-b" })
        {
            Assert.Equal(console, await fixture.ImportPluginAsync(pluginId, account, account.EndsWith('a') ? 120 : 30));
            await fixture.Get<IMergeSuggestionRefresh>().RefreshAsync();
        }

        Assert.Equal(candidate.Id, Assert.Single(await fixture.Candidates.GetAllAsync()).Id);
        Assert.Single(await fixture.Candidates.GetPendingAsync());
        Assert.Empty(await fixture.Links.GetHistoryAsync());
        Assert.Equal(2, (await fixture.Get<IReleaseRepository>().GetIdentitiesAsync()).Count);
        var ownership = Assert.Single(await fixture.Get<IOwnershipRepository>().GetByReleaseAsync(console.Id));
        var accounts = await fixture.Get<IOwnershipAccountRepository>().GetByOwnershipAsync(ownership.Id);
        Assert.Collection(accounts,
            account => { Assert.Equal("fixture-account-a", account.AccountRef); Assert.Equal(120, account.PlaytimeMinutes); },
            account => { Assert.Equal("fixture-account-b", account.AccountRef); Assert.Equal(30, account.PlaytimeMinutes); });
        Assert.All(accounts, account => Assert.Equal("plugin:" + pluginId, account.Source));
    }

    private sealed class ImportFixture : IAsyncDisposable
    {
        private readonly TempDatabase _database = new();
        private static readonly DateTime PlayedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        private const string GameId = "fixture-hollow-knight";

        public ImportFixture()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            Program.ConfigureServices(services,
                new(_database.DatabasePath + "-plugin-data", _database.DatabasePath, DataMigrationOutcome.None));
            services.AddSingleton<ISqliteConnectionFactory>(_database.Factory);
            Provider = services.BuildServiceProvider();
        }

        public ServiceProvider Provider { get; }
        public IMergeCandidateRepository Candidates => Get<IMergeCandidateRepository>();
        public IIdentityLinkRepository Links => Get<IIdentityLinkRepository>();
        public T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        public async Task<Release> ImportNativeAsync(string store)
        {
            await Get<ExternalIdResolver>().ResolveAsync(
                [new CandidateOwnership(store, "367520", "Hollow Knight", "fixture-native-account",
                    null, false, 0, null, null, store + "_fixture", PlayedAt)]);
            return Assert.IsType<Release>(await Get<IReleaseRepository>().FindByExternalIdAsync(store, "367520"));
        }

        public async Task<Release> ImportPluginAsync(string pluginId, string account = "fixture-account-a", long minutes = 120)
        {
            await Get<PluginSyncService>().ImportAsync(new PluginDescriptor
            {
                Manifest = new() { Id = pluginId, Name = pluginId, Capabilities = [PluginCapabilities.Library] },
                DirectoryPath = _database.DatabasePath + "-plugin-data",
            }, [new PluginLibraryGame(GameId, "Hollow Knight")
            {
                AccountRef = account,
                PlaytimeMinutes = minutes,
                LastPlayedAt = new DateTimeOffset(PlayedAt),
                LibrarySourceLabel = "Played history",
                Actions = [PluginGameActionKind.OpenStore],
            }], CancellationToken.None);
            return Assert.IsType<Release>(await Get<IReleaseRepository>().FindByExternalIdAsync("plugin:" + pluginId, GameId));
        }

        public async Task<string> ReadActionsAsync(string pluginId, long releaseId)
        {
            var cached = await Get<IMetadataCache>().GetAsync("plugin-library-actions",
                pluginId + ":" + releaseId.ToString(CultureInfo.InvariantCulture));
            return Assert.IsType<string>(cached?.PayloadJson);
        }

        public async Task AssertPluginFactsAsync(string pluginId, Release release)
        {
            var ownership = Assert.Single(await Get<IOwnershipRepository>().GetByReleaseAsync(release.Id));
            Assert.Equal("plugin:" + pluginId, ownership.Store);
            var play = Assert.IsType<PlayRecord>(await Get<IPlayRecordRepository>().GetLatestAsync(ownership.Id));
            Assert.Equal(120, play.PlaytimeMinutes);
            Assert.Equal(PlayedAt, play.LastPlayedAt);
            Assert.Equal("plugin:" + pluginId, play.Source);
            var externalId = Assert.Single(await Get<IReleaseRepository>().GetExternalIdsAsync(release.Id));
            Assert.Equal("plugin:" + pluginId, externalId.Provider);
            Assert.Equal(GameId, externalId.ProviderId);
            var actions = JsonSerializer.Deserialize<PluginGameActionService.Observation>(await ReadActionsAsync(pluginId, release.Id));
            Assert.NotNull(actions);
            Assert.Equal("Played history", actions.SourceLabel);
            Assert.Equal([PluginGameActionKind.OpenStore], actions.Actions);
        }

        public async ValueTask DisposeAsync()
        {
            await Provider.DisposeAsync();
            _database.Dispose();
        }
    }
}
