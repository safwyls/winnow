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

public sealed class EpicScanCompletenessTests
{
    private const string FezId = "7a70b499513441c792b541d53505e0b2";

    [Fact]
    public void A_partial_scan_keeps_positive_facts_but_has_no_absence_authority()
    {
        using var tree = EpicFixtureTree.Create(includeThirdParty: false);
        File.WriteAllText(Path.Combine(tree.DataRoot, "Manifests", "broken.item"), "{");
        var scan = new EpicLibrarySource(dataRoot: tree.DataRoot,
            installProbe: new FakeEpicThirdPartyInstallProbe(EpicInstallState.Unknown)).ScanLibrary();
        Assert.False(scan.ManifestScanComplete);
        Assert.True(Assert.Single(scan.Candidates, c => c.ProviderId == FezId).Installed);
        Assert.All(scan.Candidates.Where(c => c.ProviderId != FezId), c => Assert.Null(c.Installed));
        Assert.Null(new EpicManifestStateReader(tree.DataRoot).ReadFingerprint());
    }

    [Fact]
    public void Missing_and_readable_empty_directories_have_different_authority()
    {
        using var tree = EpicFixtureTree.Create([], includeThirdParty: false);
        var reader = new EpicManifestReader();
        var directory = Path.Combine(tree.DataRoot, "Manifests");
        var empty = reader.ScanDirectory(directory);
        Assert.True(empty.IsComplete);
        Assert.NotNull(empty.Fingerprint);
        Assert.Empty(empty.Manifests);
        Directory.Move(directory, Path.Combine(tree.DataRoot, "Unavailable"));
        var missing = reader.ScanDirectory(directory);
        Assert.False(missing.IsComplete);
        Assert.Null(missing.Fingerprint);
    }

    [Theory]
    [InlineData("malformed", false)]
    [InlineData("locked", false)]
    [InlineData("oversized", false)]
    [InlineData("disappearing", false)]
    [InlineData("enumeration", false)]
    [InlineData("completion", false)]
    [InlineData("malformed", true)]
    [InlineData("locked", true)]
    [InlineData("oversized", true)]
    [InlineData("disappearing", true)]
    [InlineData("enumeration", true)]
    [InlineData("completion", true)]
    public async Task Incomplete_scans_preserve_a_prior_install_until_a_complete_scan_proves_absence(
        string failure, bool remote)
    {
        using var tree = EpicFixtureTree.Create(includeThirdParty: false);
        using var db = new TempDatabase();
        var path = Path.Combine(tree.DataRoot, "Manifests", EpicFixtureTree.FezManifest);
        var resolver = new ExternalIdResolver(new WorkRepository(db.Factory), new ReleaseRepository(db.Factory),
            new OwnershipRepository(db.Factory), new PlayRecordRepository(db.Factory),
            new PlaytimeSnapshotRepository(db.Factory), db.Factory, new OwnershipAccountRepository(db.Factory));
        var gate = new LibrarySyncGate();
        LocalLibrarySyncService Local(EpicManifestReader? reader = null) => new(
            new SteamLibrarySource(steamRoot: Path.Combine(tree.DataRoot, "NoSteam")),
            new EpicLibrarySource(dataRoot: tree.DataRoot, manifestReader: reader,
                installProbe: new FakeEpicThirdPartyInstallProbe(EpicInstallState.Unknown)), SilentStores.Gog(),
            resolver, gate, NullLogger<LocalLibrarySyncService>.Instance);
        var baseline = Local();
        await baseline.SyncAsync();
        var stale = baseline.Scan();
        using var connection = db.Factory.Open();
        const string query = "SELECT o.installed, o.install_path AS InstallPath FROM ownerships o JOIN external_ids e ON e.release_id=o.release_id WHERE e.provider='epic' AND e.provider_id='7a70b499513441c792b541d53505e0b2'";
        var before = connection.QuerySingle<InstallObservation>(query);
        Assert.True(before.Installed);

        FileStream? locked = null;
        var reader = new EpicManifestReader();
        switch (failure)
        {
            case "malformed": File.WriteAllText(path, "{"); break;
            case "locked": locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); break;
            case "oversized": reader = new EpicManifestReader(limits: new StorefrontParserLimits { MaxFileBytes = 16 }); break;
            case "disappearing": reader = new EpicManifestReader(enumerateFiles: _ => Disappearing(path)); break;
            case "enumeration": reader = new EpicManifestReader(enumerateFiles: _ => FailingEnumeration(path)); break;
            case "completion": File.WriteAllText(path, File.ReadAllText(path).Replace("\"bIsIncompleteInstall\": false", "\"bIsIncompleteInstall\": null")); break;
        }
        try
        {
            var source = new EpicLibrarySource(dataRoot: tree.DataRoot, manifestReader: reader,
                installProbe: new FakeEpicThirdPartyInstallProbe(EpicInstallState.Unknown));
            var scan = source.ScanLibrary();
            Assert.False(scan.ManifestScanComplete);
            Assert.Null(Assert.Single(scan.Candidates, c => c.ProviderId == FezId).Installed);
            var local = Local(reader);
            if (remote)
                await new RemoteOwnershipSyncService(local, resolver, gate,
                    NullLogger<RemoteOwnershipSyncService>.Instance, new ConfiguredSteam()).SyncAsync(stale);
            else
                await local.SyncAsync();
            Assert.Equal(before, connection.QuerySingle<InstallObservation>(query));
        }
        finally { locked?.Dispose(); }

        File.Delete(path);
        await Local().SyncAsync();
        var removed = connection.QuerySingle<InstallObservation>(query);
        Assert.False(removed.Installed);
        Assert.Null(removed.InstallPath);
    }

    private static IEnumerable<string> FailingEnumeration(string path)
    {
        yield return path;
        throw new IOException("Synthetic deferred enumeration failure.");
    }

    private static IEnumerable<string> Disappearing(string path)
    {
        yield return path;
        File.Delete(path);
    }

    private sealed record InstallObservation
    {
        public bool Installed { get; init; }
        public string? InstallPath { get; init; }
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
