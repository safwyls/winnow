using Hoard.App.Services;
using Hoard.Core.Ingest;
using Hoard.Data.Repositories;
using Hoard.Enrich.SteamWeb;
using Hoard.Enrich.SteamWeb.Model;
using Hoard.Ingest.Steam;
using Hoard.Resolve;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The regression test for the bug at the level it actually shipped at:
/// <see cref="SteamSyncService"/> unioning two sources, one that can see the
/// local disk (§4.1's appmanifests) and one that cannot (§4.2's
/// <c>GetOwnedGames</c>). On the live library this produced 946 ownerships with
/// zero installed, because the web candidates — resolved second purely because
/// of how the union was written — reported <c>Installed: false</c> for games
/// they had never looked for and cleared the flags the manifests had just set.
///
/// <para>Both halves are real: the real <see cref="SteamLibrarySource"/> over a
/// synthetic Steam root, the real <see cref="ExternalIdResolver"/> over a real
/// migrated database, and candidates from the real
/// <see cref="SteamOwnedGame.ToCandidate"/> projection. Only the HTTP call is
/// substituted, so none of the write rules are faked away.</para>
/// </summary>
public sealed class SteamSyncInstallStateTests : IDisposable
{
    private const string InstalledAppId = "1203620";
    private const string OwnedOnlyAppId = "1244090";

    private readonly TempDatabase _db = new();
    private readonly string _steamRoot;
    private readonly SteamSyncService _sync;
    private readonly ReleaseRepository _releases;
    private readonly OwnershipRepository _ownerships;

    public SteamSyncInstallStateTests()
    {
        _steamRoot = Path.Combine(Path.GetTempPath(), $"hoard-sync-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_steamRoot, "steamapps"));

        // VDF escapes backslashes, so the temp path goes in doubled.
        var escapedRoot = _steamRoot.Replace(@"\", @"\\");
        File.WriteAllText(
            Path.Combine(_steamRoot, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"{{escapedRoot}}"
                    "label"		""
                    "apps"
                    {
                        "1203620"		"44998366792"
                    }
                }
            }
            """);

        // One game genuinely on disk. StateFlags 4 = fully installed.
        File.WriteAllText(
            Path.Combine(_steamRoot, "steamapps", "appmanifest_1203620.acf"),
            """
            "AppState"
            {
                "appid"		"1203620"
                "name"		"Elden Ring"
                "StateFlags"		"4"
                "installdir"		"ELDEN RING"
            }
            """);

        // One account, so the local candidates carry an account_ref the sync
        // service can turn into a SteamID64 for the owned-library lookup.
        var config = Path.Combine(_steamRoot, "userdata", "12345678", "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "localconfig.vdf"),
            """
            "UserLocalConfigStore"
            {
                "Software"
                {
                    "Valve"
                    {
                        "Steam"
                        {
                            "apps"
                            {
                                "1203620"
                                {
                                    "LastPlayed"		"1787400000"
                                    "Playtime"		"817"
                                }
                            }
                        }
                    }
                }
            }
            """);

        _releases = new ReleaseRepository(_db.Factory);
        _ownerships = new OwnershipRepository(_db.Factory);

        _sync = new SteamSyncService(
            new SteamLibrarySource(steamRoot: _steamRoot),
            new ExternalIdResolver(
                new WorkRepository(_db.Factory),
                _releases,
                _ownerships,
                new PlayRecordRepository(_db.Factory),
                new PlaytimeSnapshotRepository(_db.Factory),
                _db.Factory),
            NullLogger<SteamSyncService>.Instance,
            new OwnedLibraryStub());
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_steamRoot, recursive: true);
    }

    [Fact]
    public async Task A_sync_that_also_reads_the_owned_library_still_records_what_is_on_disk()
    {
        var report = await _sync.SyncAsync();

        // Both sources contributed: the installed game (known to both) and the
        // owned-but-never-installed one (known only to the Web API).
        Assert.Equal(3, report.Candidates);
        Assert.NotNull(report.Result);

        var installed = await OwnershipForAsync(InstalledAppId);
        Assert.True(installed.Installed);
        Assert.Equal(
            Path.Combine(_steamRoot, "steamapps", "common", "ELDEN RING"),
            installed.InstallPath);

        // The web-only title is not installed and claims no path — a first
        // sighting from a source with no opinion still has to produce a row.
        var ownedOnly = await OwnershipForAsync(OwnedOnlyAppId);
        Assert.False(ownedOnly.Installed);
        Assert.Null(ownedOnly.InstallPath);
    }

    /// <summary>
    /// The exact shape of the live failure: the first sync looked right,
    /// because the conflicting write only happens once a row exists. Re-syncing
    /// has to converge, not erode.
    /// </summary>
    [Fact]
    public async Task Re_syncing_does_not_erode_install_state()
    {
        await _sync.SyncAsync();
        await _sync.SyncAsync();
        await _sync.SyncAsync();

        var installed = await OwnershipForAsync(InstalledAppId);
        Assert.True(installed.Installed);
        Assert.NotNull(installed.InstallPath);

        var all = await _ownerships.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Single(all, o => o.Installed);
    }

    private async Task<Core.Domain.Ownership> OwnershipForAsync(string appId)
    {
        var release = await _releases.FindByExternalIdAsync("steam", appId);
        Assert.NotNull(release);
        return Assert.Single(await _ownerships.GetByReleaseAsync(release.Id));
    }

    /// <summary>
    /// Stands in for the HTTP call and nothing else. It returns candidates
    /// through the real <see cref="SteamOwnedGame.ToCandidate"/>, so whatever
    /// that projection says about install state is what this test sees —
    /// including if someone changes it back to <c>false</c>.
    /// </summary>
    private sealed class OwnedLibraryStub : ISteamWebApiClient
    {
        public ValueTask<bool> IsConfiguredAsync(CancellationToken ct = default)
            => ValueTask.FromResult(true);

        public Task<SteamOwnedLibrary> GetOwnedGamesAsync(
            SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => Task.FromResult(new SteamOwnedLibrary(
                steamId,
                Succeeded: true,
                Games:
                [
                    // Owned, played and installed here — the row the local scan
                    // also speaks for, and the one the bug used to clear.
                    new SteamOwnedGame(InstalledAppId, "Elden Ring", 817, 0, null, null),
                    // Owned and never launched: invisible to localconfig.vdf,
                    // which is why the union exists at all.
                    new SteamOwnedGame(OwnedOnlyAppId, "Sea of Stars", 0, 0, null, null),
                ],
                ObservedAt: new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc),
                FromCache: false));

        public async Task<IReadOnlyList<CandidateOwnership>> GetOwnershipCandidatesAsync(
            SteamId steamId, TimeSpan? cacheTtl = null, CancellationToken ct = default)
            => (await GetOwnedGamesAsync(steamId, cacheTtl, ct))
                .ToCandidates(SteamWebApiClient.SourceName);
    }
}
