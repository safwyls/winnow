using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Ingest;
using Winnow.Core.Repositories;
using Winnow.Data;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Ingest.Gog;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class GogInstallReconciliationTests
{
    private static readonly GogRegistryGame Game = RegFile.InstalledGames(GogFixtures.PathOf("gog-games.reg"))[0]
        with { InstallPath = Path.Combine(Path.GetTempPath(), "winnow-no-gog-install") };

    [Theory]
    [InlineData("denied")]
    [InlineData("disappeared")]
    [InlineData("enumeration")]
    public void Failed_registry_reads_preserve_readable_positives_and_withhold_absence(string failure)
    {
        var scan = WindowsGogInstalledGameRegistry.ReadInventory(
            () => Names(), name => name == "good" ? Game : failure == "denied"
                ? throw new UnauthorizedAccessException("Synthetic inaccessible key") : null);
        Assert.False(scan.IsComplete);
        Assert.Equal(Game, Assert.Single(scan.Games));

        IEnumerable<string> Names()
        {
            yield return "good";
            if (failure == "enumeration") throw new IOException("Synthetic enumeration failure");
            yield return "failed";
        }
    }

    [Fact]
    public void An_empty_readable_inventory_is_complete()
        => Assert.True(WindowsGogInstalledGameRegistry.ReadInventory(() => [], _ => null).IsComplete);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registry_only_uninstall_survives_restart_and_an_old_remote_snapshot(bool incompleteFirst)
    {
        using var db = new TempDatabase();
        var registry = new MutableRegistry(new([Game], true));
        var gate = new LibrarySyncGate();
        var local = Local(db.Factory, registry, gate);
        await local.SyncAsync();
        var old = local.Scan();
        using var connection = db.Factory.Open();
        var before = Read(connection);
        Assert.True(before.Installed);
        // Independent observations are retained when only install facts change.
        await Resolver(db.Factory).ResolveAsync([old.Gog[0] with
        {
            PlaytimeMinutes = 123,
            AcquiredAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        }]);
        var historyBefore = connection.QuerySingle<long>("SELECT COUNT(*) FROM playtime_snapshots;");

        // Reopening both factory and repository proves this is persisted provenance.
        var reopened = new SqliteConnectionFactory(db.DatabasePath, pooling: false);
        local = Local(reopened, registry, gate);
        if (incompleteFirst)
        {
            registry.Result = new([], false);
            await local.SyncAsync();
            Assert.True(Read(connection).Installed);
            Assert.Equal(before.InstallPath, Read(connection).InstallPath);
        }
        registry.Result = new([], true);
        await local.SyncAsync();
        var removed = Read(connection);
        Assert.False(removed.Installed);
        Assert.Null(removed.InstallPath);
        Assert.Equal(before.Id, removed.Id);
        Assert.Equal(123, removed.PlaytimeMinutes);
        Assert.NotNull(removed.AcquiredAt);
        Assert.Equal(historyBefore, connection.QuerySingle<long>("SELECT COUNT(*) FROM playtime_snapshots;"));

        await new RemoteOwnershipSyncService(local, Resolver(reopened), gate,
            NullLogger<RemoteOwnershipSyncService>.Instance, new OwnershipInventoryRepository(reopened), new ConfiguredSteam()).SyncAsync(old);
        Assert.Equal(removed, Read(connection));
    }

    [Fact]
    public async Task A_positive_partial_inventory_updates_install_path_without_clearing_other_installs()
    {
        using var db = new TempDatabase();
        var other = Game with { GameId = "999000111", GameName = "Other" };
        var registry = new MutableRegistry(new([Game, other], true));
        var local = Local(db.Factory, registry, new LibrarySyncGate());
        await local.SyncAsync();
        registry.Result = new([Game with { InstallPath = "updated-path" }], false);
        await local.SyncAsync();
        using var connection = db.Factory.Open();
        Assert.Equal("updated-path", Read(connection).InstallPath);
        Assert.Equal(2, connection.QuerySingle<int>("SELECT COUNT(*) FROM ownerships WHERE installed=1;"));
    }

    [Fact]
    public async Task Legacy_installations_with_unknown_provenance_are_not_inferred_to_be_registry_owned()
    {
        using var db = new TempDatabase();
        await Resolver(db.Factory).ResolveAsync([new CandidateOwnership("gog", Game.GameId, Game.GameName,
            null, Game.InstallPath, true, 12, null, null, "gog_local", DateTime.UtcNow)]);
        await Local(db.Factory, new MutableRegistry(new([], true)), new LibrarySyncGate()).SyncAsync();
        using var connection = db.Factory.Open();
        Assert.True(Read(connection).Installed);
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM gog_registry_installations;"));
    }

    [Fact]
    public async Task Current_Galaxy_installs_survive_registry_absence_but_old_remote_Galaxy_cannot_restore_them()
    {
        using var db = new TempDatabase();
        var registry = new MutableRegistry(new([Game], true));
        var gate = new LibrarySyncGate();
        var local = Local(db.Factory, registry, gate);
        await local.SyncAsync();
        var old = local.Scan();
        old = old with { GogEvidence = old.GogEvidence! with { GalaxyInstalledProductIds = [Game.GameId] } };
        registry.Result = new([], true);
        using (await gate.EnterAsync(default))
        {
            var current = await local.RefreshInstallStateAsync(old, default);
            Assert.True(Assert.Single(current.Gog).Installed);
        }
        using var connection = db.Factory.Open();
        Assert.True(Read(connection).Installed);
        await local.SyncAsync();
        Assert.False(Read(connection).Installed);
        await new RemoteOwnershipSyncService(local, Resolver(db.Factory), gate,
            NullLogger<RemoteOwnershipSyncService>.Instance, new OwnershipInventoryRepository(db.Factory), new ConfiguredSteam()).SyncAsync(old);
        Assert.False(Read(connection).Installed);
        Assert.Null(Read(connection).InstallPath);
    }

    [Fact]
    public async Task Reconciliation_enlists_in_an_existing_transaction()
    {
        using var db = new TempDatabase();
        var registry = new MutableRegistry(new([Game], true));
        await Local(db.Factory, registry, new LibrarySyncGate()).SyncAsync();
        using (db.Factory.Begin())
        {
            await new GogInstallStateRepository(db.Factory).ReconcileAsync(["999"], [], true, []);
        }
        using var connection = db.Factory.Open();
        Assert.True(Read(connection).Installed);
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM gog_registry_installations;"));
    }

    [Fact]
    public async Task A_caught_batch_failure_rolls_back_provenance_before_the_caller_commits()
    {
        using var db = new TempDatabase();
        using (var scope = db.Factory.Begin())
        {
            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
                new GogInstallStateRepository(db.Factory).ReconcileAsync(["valid", " "], [], true, []));
            scope.Commit();
        }
        using var connection = db.Factory.Open();
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM gog_registry_installations;"));
    }

    private static LocalLibrarySyncService Local(
        SqliteConnectionFactory factory, IGogInstalledGameRegistry registry, LibrarySyncGate gate) => new(
        new SteamLibrarySource(steamRoot: Path.Combine(Path.GetTempPath(), "winnow-no-steam")),
        SilentStores.Epic(), new GogLibrarySource(registry: registry,
            galaxyRoot: Path.Combine(Path.GetTempPath(), "winnow-no-galaxy")),
        Resolver(factory), gate, NullLogger<LocalLibrarySyncService>.Instance,
        gogInstallState: new GogInstallStateRepository(factory));

    private static ExternalIdResolver Resolver(SqliteConnectionFactory factory) => new(
        new WorkRepository(factory), new ReleaseRepository(factory), new OwnershipRepository(factory),
        new PlayRecordRepository(factory), new PlaytimeSnapshotRepository(factory), factory,
        new OwnershipAccountRepository(factory));

    private static InstallObservation Read(Microsoft.Data.Sqlite.SqliteConnection connection)
        => connection.QuerySingle<InstallObservation>("""
            SELECT o.id AS Id, o.installed AS Installed, o.install_path AS InstallPath,
                   (SELECT MAX(playtime_minutes) FROM play_records WHERE ownership_id=o.id) AS PlaytimeMinutes,
                   o.acquired_at AS AcquiredAt
            FROM ownerships o JOIN external_ids e ON e.release_id=o.release_id
            WHERE e.provider='gog' AND e.provider_id=@id;
            """, new { id = Game.GameId });

    private sealed record InstallObservation
    {
        public long Id { get; init; }
        public bool Installed { get; init; }
        public string? InstallPath { get; init; }
        public long? PlaytimeMinutes { get; init; }
        public DateTime? AcquiredAt { get; init; }
    }
    private sealed class MutableRegistry(GogRegistryScan result) : IGogInstalledGameRegistry
    {
        public GogRegistryScan Result { get; set; } = result;
        public GogRegistryScan Scan() => Result;
    }

    private sealed class ConfiguredSteam : ISteamWebApiClient
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, TimeSpan? cacheTtl = null,
            CancellationToken ct = default) => throw new InvalidOperationException("Fixture has no Steam accounts.");
        public Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, TimeSpan? cacheTtl = null,
            CancellationToken ct = default) => throw new InvalidOperationException("Fixture has no Steam accounts.");
    }
}
