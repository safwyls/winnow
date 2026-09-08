using Microsoft.Extensions.Logging.Abstractions;
using Winnow.App.Services;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Winnow.Data.Repositories;
using Winnow.Ingest.Steam;
using Winnow.Resolve;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// TASK-152.5. Four triggers could start a local scan within a second of a
/// launch and every one of them did, over byte-identical files: the startup
/// pipeline's local sync, the remote backfill's install-state re-read, and the
/// first stable manifest read of each install watcher. These tests hold the
/// coalescing that removed three of the four without removing any trigger — a
/// manifest that genuinely moves must still reach the library.
/// </summary>
public sealed class LibraryScanCoalescingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"winnow-scan-coalesce-{Guid.NewGuid():N}");
    private readonly TempDatabase _db = new();

    private string ManifestPath => Path.Combine(_root, "steamapps", "appmanifest_620.acf");

    public LibraryScanCoalescingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        File.WriteAllText(Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"0\" { \"path\" \"" + _root.Replace("\\", "\\\\") + "\" } }");
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, true);
    }

    private void WriteManifest(int state, string name = "Portal 2") => File.WriteAllText(ManifestPath,
        $$"""
        "AppState" { "appid" "620" "name" "{{name}}" "StateFlags" "{{state}}" "installdir" "{{name}}" }
        """);

    private LocalLibrarySyncService Sync(SteamLibrarySource source, LibraryScanBaseline? baseline)
    {
        var resolver = new ExternalIdResolver(new WorkRepository(_db.Factory), new ReleaseRepository(_db.Factory),
            new OwnershipRepository(_db.Factory), new PlayRecordRepository(_db.Factory),
            new PlaytimeSnapshotRepository(_db.Factory), _db.Factory, new OwnershipAccountRepository(_db.Factory));
        return new LocalLibrarySyncService(source, SilentStores.Epic(), SilentStores.Gog(), resolver,
            new LibrarySyncGate(), NullLogger<LocalLibrarySyncService>.Instance,
            steamInstallState: new SteamInstallStateRepository(_db.Factory),
            baseline: baseline);
    }

    // ── The watcher's first stable read ──────────────────────────────────────

    [Fact]
    public async Task A_first_stable_read_a_completed_pass_already_covered_starts_no_scan()
    {
        WriteManifest(4);
        var source = new SteamLibrarySource(steamRoot: _root);
        var baseline = new LibraryScanBaseline(source.ReadInstallFingerprint);
        var syncs = 0;
        var refreshes = 0;

        // What the startup pipeline's pass did: read the fingerprints before
        // scanning, then publish them once the pass completed.
        baseline.Publish(baseline.Read());

        using var watcher = new SteamInstallRefreshService(
            source.ReadInstallFingerprint,
            _ => { syncs++; return Task.CompletedTask; },
            _ => { refreshes++; return Task.CompletedTask; },
            NullLogger<SteamInstallRefreshService>.Instance,
            baseline: baseline);

        // Two polls: the first only establishes the pending fingerprint, and
        // the second is the one that used to publish.
        await watcher.PollAsync();
        await watcher.PollAsync();

        Assert.Equal(0, syncs);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task A_manifest_that_moved_since_the_covered_pass_still_publishes()
    {
        WriteManifest(4);
        var source = new SteamLibrarySource(steamRoot: _root);
        var baseline = new LibraryScanBaseline(source.ReadInstallFingerprint);
        baseline.Publish(baseline.Read());

        var syncs = 0;
        using var watcher = new SteamInstallRefreshService(
            source.ReadInstallFingerprint,
            _ => { syncs++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            NullLogger<SteamInstallRefreshService>.Instance,
            baseline: baseline);

        // The install directory changed after the pass read it. This is the
        // case the coalescing must not swallow: the watcher exists for it.
        WriteManifest(4, "Portal 2 Beta");

        await watcher.PollAsync();
        await watcher.PollAsync();

        Assert.Equal(1, syncs);
    }

    [Fact]
    public async Task A_watcher_holds_its_first_read_while_a_pass_is_still_expected()
    {
        WriteManifest(4);
        var source = new SteamLibrarySource(steamRoot: _root);
        var baseline = new LibraryScanBaseline(source.ReadInstallFingerprint);
        var syncs = 0;

        using var watcher = new SteamInstallRefreshService(
            source.ReadInstallFingerprint,
            _ => { syncs++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            NullLogger<SteamInstallRefreshService>.Instance,
            baseline: baseline);

        var pass = baseline.Expect();
        await watcher.PollAsync();
        await watcher.PollAsync();
        await watcher.PollAsync();

        // The pass is mid-scan. Scanning now would be the duplicate, and
        // publishing now would be a change nobody made.
        Assert.Equal(0, syncs);

        baseline.Publish(baseline.Read());
        pass.Dispose();

        await watcher.PollAsync();
        Assert.Equal(0, syncs);
    }

    [Fact]
    public async Task A_pass_that_failed_before_publishing_leaves_the_watcher_to_scan()
    {
        WriteManifest(4);
        var source = new SteamLibrarySource(steamRoot: _root);
        var baseline = new LibraryScanBaseline(source.ReadInstallFingerprint);
        var syncs = 0;

        using var watcher = new SteamInstallRefreshService(
            source.ReadInstallFingerprint,
            _ => { syncs++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            NullLogger<SteamInstallRefreshService>.Instance,
            baseline: baseline);

        // The expectation is released without a publish, which is what the
        // startup task's finally block does when the pass throws. Waiting for
        // ever on a pipeline that failed would cost the user install state.
        baseline.Expect().Dispose();

        await watcher.PollAsync();
        await watcher.PollAsync();

        Assert.Equal(1, syncs);
    }

    [Fact]
    public async Task A_change_after_the_launch_read_is_settled_publishes_normally()
    {
        WriteManifest(4);
        var source = new SteamLibrarySource(steamRoot: _root);
        var baseline = new LibraryScanBaseline(source.ReadInstallFingerprint);
        baseline.Publish(baseline.Read());

        var syncs = 0;
        using var watcher = new SteamInstallRefreshService(
            source.ReadInstallFingerprint,
            _ => { syncs++; return Task.CompletedTask; },
            _ => Task.CompletedTask,
            NullLogger<SteamInstallRefreshService>.Instance,
            baseline: baseline);

        await watcher.PollAsync();
        await watcher.PollAsync();
        Assert.Equal(0, syncs);

        // The watcher's ordinary working life, after the launch question is
        // answered: a real uninstall, two stable reads, one publish.
        WriteManifest(0);
        await watcher.PollAsync();
        await watcher.PollAsync();

        Assert.Equal(1, syncs);
    }

    // ── The baseline itself ──────────────────────────────────────────────────

    [Fact]
    public void A_null_fingerprint_is_never_covered()
    {
        // The readers answer null for "no complete inventory to compare", which
        // is not the same as "nothing there and nothing changed".
        var baseline = new LibraryScanBaseline();
        baseline.Publish(baseline.Read());

        Assert.False(baseline.Covered(LibraryScanSource.Steam, null));
        Assert.False(baseline.Covered(LibraryScanSource.Epic, null));
    }

    [Fact]
    public void Nothing_is_covered_before_a_pass_publishes()
    {
        var baseline = new LibraryScanBaseline(() => "STEAM", () => "EPIC");

        Assert.False(baseline.Covered(LibraryScanSource.Steam, "STEAM"));

        baseline.Publish(baseline.Read());

        Assert.True(baseline.Covered(LibraryScanSource.Steam, "STEAM"));
        Assert.True(baseline.Covered(LibraryScanSource.Epic, "EPIC"));

        // Each source answers for itself; a watcher must not adopt the other's.
        Assert.False(baseline.Covered(LibraryScanSource.Steam, "EPIC"));
    }

    [Fact]
    public void Expectations_nest_and_dispose_idempotently()
    {
        var baseline = new LibraryScanBaseline();
        Assert.False(baseline.PassExpected);

        var first = baseline.Expect();
        var second = baseline.Expect();
        Assert.True(baseline.PassExpected);

        first.Dispose();
        first.Dispose();
        Assert.True(baseline.PassExpected);

        second.Dispose();
        Assert.False(baseline.PassExpected);
    }

    // ── The scan the backfill was handed ─────────────────────────────────────

    [Fact]
    public async Task An_unmoved_launcher_state_is_not_re_read_for_the_backfill()
    {
        WriteManifest(4);
        var source = new SteamLibrarySource(steamRoot: _root);
        var baseline = new LibraryScanBaseline(source.ReadInstallFingerprint);
        var sync = Sync(source, baseline);

        // The scan the startup pass paid for, carrying the state it covered.
        var scanned = await sync.RefreshInstallStateAsync(
            new LocalLibraryScan([], [], []), CancellationToken.None);
        Assert.Single(scanned.Steam);
        Assert.NotNull(scanned.Covered);

        // The manifest is removed while the backfill waits on HTTP, but its
        // fingerprint is re-read on the way back in: an unchanged fingerprint
        // means the candidates it already holds are still the answer, so the
        // sentinel below survives.
        var reusable = scanned with { Steam = [.. scanned.Steam, Sentinel()] };
        var again = await sync.RefreshInstallStateAsync(reusable, CancellationToken.None);
        Assert.Equal(2, again.Steam.Count);

        // And a manifest that did move is re-read, sentinel and all.
        WriteManifest(0);
        var moved = await sync.RefreshInstallStateAsync(reusable, CancellationToken.None);
        Assert.Single(moved.Steam);
        Assert.False(Assert.Single(moved.Steam).Installed);
    }

    /// <summary>A candidate no reader would produce, so its survival says the scan was reused.</summary>
    private static CandidateOwnership Sentinel() => new(
        ExternalIdProviders.Steam, "999999", "Sentinel", null, null, null, null, null, null,
        "test", DateTime.UtcNow);
}
