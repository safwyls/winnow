using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Core.Queries;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class RemoteOwnershipInventoryTests
{
    [Theory]
    [InlineData("complete", 1, true)]
    [InlineData("empty", 0, true)]
    [InlineData("partial", 2, false)]
    [InlineData("stale", 2, false)]
    [InlineData("unanswered", 1, false)]
    [InlineData("throw", 1, false)]
    public async Task Only_complete_resolved_inventories_enable_filtering_including_the_empty_library(string outcome, int visible, bool complete)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Client.Outcome = outcome;
        await fixture.Service.SyncAsync(new LocalLibraryScan([], [], []));
        Assert.Equal(["111"], fixture.Client.Accounts);
        using var check = fixture.Database.Factory.Open();
        Assert.Equal(complete, check.ExecuteScalar<bool>("SELECT is_complete FROM account_inventory_observations;"));
        Assert.Equal(visible, (await new LibraryQueryRepository(fixture.Database.Factory).GetOwnershipBucketsAsync(BucketThresholds.Default)).Count);
        if (complete)
        {
            Assert.Equal(outcome == "empty" ? 0 : 1, check.ExecuteScalar<int>("SELECT item_count FROM account_inventory_observations;"));
            Assert.Equal(fixture.Client.ObservedAt, check.ExecuteScalar<DateTime>("SELECT observed_at FROM account_inventory_observations;"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_or_resolution_failure_cannot_publish_completion(bool cancel)
    {
        using var fixture = new Fixture();
        await fixture.SeedAsync();
        using var cts = new CancellationTokenSource();
        if (cancel) fixture.Client.BeforeAnswer = () => cts.Cancel();
        else
        {
            using var inject = fixture.Database.Factory.Open();
            inject.Execute("CREATE TRIGGER reject_work BEFORE INSERT ON works BEGIN SELECT RAISE(ABORT,'injected resolver failure'); END;");
        }
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.SyncAsync(new LocalLibraryScan([], [], []), cts.Token));
        using var check = fixture.Database.Factory.Open();
        Assert.False(check.ExecuteScalar<bool>("SELECT is_complete FROM account_inventory_observations;"));
        Assert.Single(await new LibraryQueryRepository(fixture.Database.Factory).GetOwnershipBucketsAsync(BucketThresholds.Default));
    }

    private sealed class Fixture : IDisposable
    {
        public TempDatabase Database { get; } = new();
        public Client Client { get; } = new();
        public RemoteOwnershipSyncService Service { get; }
        public Fixture()
        {
            var factory = Database.Factory;
            var resolver = new ExternalIdResolver(new WorkRepository(factory), new ReleaseRepository(factory), new OwnershipRepository(factory),
                new PlayRecordRepository(factory), new PlaytimeSnapshotRepository(factory), factory, new OwnershipAccountRepository(factory));
            var gate = new LibrarySyncGate();
            var local = new LocalLibrarySyncService(new SteamLibrarySource(steamRoot: Path.Combine(Database.DatabasePath, "no-steam")),
                SilentStores.Epic(), SilentStores.Gog(), resolver, gate, NullLogger<LocalLibrarySyncService>.Instance);
            Service = new(local, resolver, gate, NullLogger<RemoteOwnershipSyncService>.Instance,
                new OwnershipInventoryRepository(factory), Client, settings: new SettingsRepository(factory));
        }
        public async Task SeedAsync()
        {
            using var connection = Database.Factory.Open();
            connection.Execute("""
                INSERT INTO works(id,name) VALUES(1,'Household');
                INSERT INTO releases(id,work_id,name) VALUES(1,1,'Household');
                INSERT INTO ownerships(id,release_id,store) VALUES(1,1,'steam');
                INSERT INTO external_ids(release_id,provider,provider_id) VALUES(1,'steam','2');
                INSERT INTO ownership_accounts(ownership_id,account_ref,source,first_seen_at,last_seen_at)
                    VALUES(1,'222','steam_local','2026-08-01','2026-08-01');
                """);
            var settings = new SettingsRepository(Database.Factory);
            await settings.SetAsync(AccountScope.SettingKey, AccountScope.Own);
            await settings.SetAsync(SteamOwnedAccount.RefSettingKey, "111");
        }
        public void Dispose() => Database.Dispose();
    }

    private sealed class Client : ISteamWebApiClient
    {
        public string Outcome { get; set; } = "complete";
        public List<string> Accounts { get; } = [];
        public Action? BeforeAnswer { get; set; }
        public DateTime ObservedAt { get; } = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(SteamId steamId, SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended,
            TimeSpan? cacheTtl = null, CancellationToken ct = default)
        {
            Accounts.Add(steamId.AccountRef);
            BeforeAnswer?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Outcome == "throw") throw new HttpRequestException("Unavailable");
            if (Outcome == "unanswered") return Task.FromResult(SteamOwnedLibrary.Unanswered(steamId, ObservedAt));
            return Task.FromResult(new SteamOwnedLibrary(steamId, true,
                Outcome == "empty" ? [] : [new("1", "Mine", 0, 0, null, null)], ObservedAt, Outcome == "stale")
                { IsComplete = Outcome is "complete" or "empty" });
        }
        public Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => throw new InvalidOperationException("Candidate-only results cannot prove inventory completeness.");
    }
}
