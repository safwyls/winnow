using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Enrich.SteamWeb;
using Winnow.Enrich.SteamWeb.Credentials;
using Winnow.Enrich.SteamWeb.Model;
using Winnow.Ingest.Epic;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

public sealed class EpicSyncInstallOrderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_queued_pass_rereads_Epic_after_the_gate_and_cannot_undo_completion(bool remote)
    {
        using var tree = EpicFixtureTree.Create([EpicFixtureTree.FezManifest], includeThirdParty: false);
        using var db = new TempDatabase();
        var path = Path.Combine(tree.DataRoot, "Manifests", EpicFixtureTree.FezManifest);
        var complete = File.ReadAllText(path);
        File.WriteAllText(path, complete.Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": true"));
        var resolver = new ExternalIdResolver(new WorkRepository(db.Factory), new ReleaseRepository(db.Factory),
            new OwnershipRepository(db.Factory), new PlayRecordRepository(db.Factory),
            new PlaytimeSnapshotRepository(db.Factory), db.Factory, new OwnershipAccountRepository(db.Factory));
        var gate = new LibrarySyncGate();
        var local = new LocalLibrarySyncService(new SteamLibrarySource(steamRoot: Path.Combine(tree.DataRoot, "NoSteam")),
            new EpicLibrarySource(dataRoot: tree.DataRoot), SilentStores.Gog(), resolver, gate,
            NullLogger<LocalLibrarySyncService>.Instance);
        var backfill = new RemoteOwnershipSyncService(local, resolver, gate,
            NullLogger<RemoteOwnershipSyncService>.Instance, new OwnershipInventoryRepository(db.Factory), new ConfiguredSteam());
        var stale = local.Scan();
        Assert.False(Assert.Single(stale.Epic, c => c.ProviderId == "7a70b499513441c792b541d53505e0b2").Installed);

        Task<LibrarySyncReport> pending;
        using (await gate.EnterAsync(CancellationToken.None))
        {
            pending = remote ? backfill.SyncAsync(stale) : local.SyncAsync();
            Assert.False(pending.IsCompleted);
            File.WriteAllText(path, complete);
        }
        await pending;
        using var connection = db.Factory.Open();
        const string query = "SELECT installed FROM ownerships o JOIN external_ids e ON e.release_id=o.release_id WHERE e.provider='epic' AND e.provider_id='7a70b499513441c792b541d53505e0b2'";
        Assert.True(connection.QuerySingle<bool>(query));

        // The remote pass may reuse the original scan long after local completion.
        await backfill.SyncAsync(stale);
        Assert.True(connection.QuerySingle<bool>(query));
        Directory.Move(Path.Combine(tree.DataRoot, "Manifests"), Path.Combine(tree.DataRoot, "Unavailable"));
        await backfill.SyncAsync(stale);
        Assert.True(connection.QuerySingle<bool>(query));
    }

    private sealed class ConfiguredSteam : ISteamWebApiClient
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default) => ValueTask.FromResult(true);
        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, TimeSpan? cacheTtl = null,
            CancellationToken ct = default) => throw new InvalidOperationException("No Steam accounts in this fixture.");
        public Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(SteamId steamId,
            SteamCredentialPurpose purpose = SteamCredentialPurpose.Unattended, TimeSpan? cacheTtl = null,
            CancellationToken ct = default) => throw new InvalidOperationException("No Steam accounts in this fixture.");
    }
}
