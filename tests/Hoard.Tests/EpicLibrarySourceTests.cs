using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Hoard.Ingest.Epic;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The composed Epic scan over a launcher tree built from the sanitized fixtures.
/// </summary>
public class EpicLibrarySourceTests
{
    private const string FezId = "7a70b499513441c792b541d53505e0b2";
    private const string CelesteId = "38c07a09dc174b69b756aa51890c3dd4";
    private const string WatchDogsId = "6dc445f656de4e029834b2d32b6a2f77";
    private const string BountyOfBloodId = "0854f1cf60fd48d4a29178b211d2f133";

    private static EpicLibrarySource SourceOver(
        EpicFixtureTree tree, EpicInstallState thirdPartyAnswer)
        => new(
            installProbe: new FakeEpicThirdPartyInstallProbe(thirdPartyAnswer),
            dataRoot: tree.DataRoot);

    [Fact]
    public void Installed_and_catalog_only_titles_both_appear_with_the_right_install_state()
    {
        using var tree = EpicFixtureTree.Create(manifests: [EpicFixtureTree.FezManifest]);
        var candidates = SourceOver(tree, EpicInstallState.NotInstalled).Scan();

        // Fez has a complete manifest.
        var fez = Assert.Single(candidates, c => c.ProviderId == FezId);
        Assert.Equal("Fez", fez.Title);
        Assert.True(fez.Installed);
        Assert.Equal(@"C:\Program Files\Epic Games\Fez", fez.InstallPath);

        // Watch Dogs is in the catalog (owned) with no .item manifest.
        var watchDogs = Assert.Single(candidates, c => c.ProviderId == WatchDogsId);
        Assert.Equal("Watch Dogs", watchDogs.Title);
        Assert.False(watchDogs.Installed);
        Assert.Null(watchDogs.InstallPath);
    }

    [Fact]
    public void Owned_but_not_installed_comes_from_the_catalog_with_a_real_false()
    {
        // The catalog is the entitlement list; the manifests directory was read
        // and had no record of this title, so false is an observation and is what
        // makes an uninstall show. Only a source that cannot see the disk emits
        // null (CandidateOwnership.Installed).
        using var tree = EpicFixtureTree.Create(
            manifests: [], includeThirdParty: false);
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        var fez = Assert.Single(candidates, c => c.ProviderId == FezId);
        Assert.False(fez.Installed);
        Assert.Null(fez.InstallPath);

        // Every title EXCEPT the third-party-managed one, which the manifests
        // directory has nothing to say about even in principle — deleting the
        // ThirParty file does not change that, because the catalog entry itself
        // carries ThirdPartyManagedProvider.
        Assert.All(
            candidates.Where(c => c.ProviderId != WatchDogsId),
            c =>
            {
                Assert.False(c.Installed);
                Assert.Null(c.InstallPath);
            });
    }

    [Fact]
    public void Playtime_is_null_and_never_zero_because_epic_writes_none_to_disk()
    {
        // Epic has no per-game playtime and no last-played anywhere on disk. A
        // zero would be a claim the user has never played the game; null is the
        // truth, which is "this source cannot know". This is the same distinction
        // that the Web API's Installed: false and its 86400 sentinel got wrong.
        using var tree = EpicFixtureTree.Create();
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Null(c.PlaytimeMinutes));
        Assert.All(candidates, c => Assert.Null(c.LastPlayedAt));
        Assert.DoesNotContain(candidates, c => c.PlaytimeMinutes == 0);
    }

    [Fact]
    public void Acquisition_date_is_null_because_dateAdded_is_the_store_release_date()
    {
        using var tree = EpicFixtureTree.Create();
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        Assert.All(candidates, c => Assert.Null(c.AcquiredAt));
    }

    [Fact]
    public void Engine_tools_and_cosmetic_entitlements_are_filtered_out()
    {
        using var tree = EpicFixtureTree.Create();
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        // Twinmotion (software, applications) and the "audience" cosmetic
        // entitlement are both in the catalog and neither is a game.
        Assert.DoesNotContain(candidates, c => c.Title?.StartsWith("Twinmotion", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(candidates, c => c.ProviderId == "7888a49fe08b4d5583b5646bf9fecc7a");
        Assert.DoesNotContain(candidates, c => c.ProviderId == "cd4cceaa96da49bb81aeedfb110f49c5");
    }

    [Fact]
    public void Dlc_is_excluded_even_when_it_has_its_own_installed_manifest()
    {
        using var tree = EpicFixtureTree.Create(
            manifests: [EpicFixtureTree.FezManifest, EpicFixtureTree.DlcManifest]);
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        Assert.DoesNotContain(candidates, c => c.ProviderId == BountyOfBloodId);
        // ...and the LEGO Fortnite child, which looks like a base game by category.
        Assert.DoesNotContain(candidates, c => c.ProviderId == "8f33cce63b3f4a46aca59ff8c85ff1cd");
    }

    [Fact]
    public void A_part_downloaded_install_is_reported_as_not_installed()
    {
        using var tree = EpicFixtureTree.Create(
            manifests: [EpicFixtureTree.FezManifest, EpicFixtureTree.IncompleteManifest]);
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        var celeste = Assert.Single(candidates, c => c.ProviderId == CelesteId);
        Assert.Equal("Celeste", celeste.Title);
        Assert.False(celeste.Installed);
        Assert.Null(celeste.InstallPath);
    }

    [Fact]
    public void Ubisoft_delivered_titles_appear_and_are_probed_rather_than_assumed_uninstalled()
    {
        // These never get a .item manifest, so "no manifest" says nothing about
        // them. The source must ask the delivering launcher instead.
        using var tree = EpicFixtureTree.Create(manifests: []);
        var probe = new FakeEpicThirdPartyInstallProbe(
            EpicInstallState.At(@"D:\Ubisoft\Watch Dogs"));
        var candidates = new EpicLibrarySource(installProbe: probe, dataRoot: tree.DataRoot).Scan();

        var watchDogs = Assert.Single(candidates, c => c.ProviderId == WatchDogsId);
        Assert.Equal("Watch Dogs", watchDogs.Title);
        Assert.True(watchDogs.Installed);
        Assert.Equal(@"D:\Ubisoft\Watch Dogs", watchDogs.InstallPath);

        Assert.Contains(
            probe.Calls,
            call => call.Path == @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\274"
                && call.Value == "InstallDir");
    }

    [Fact]
    public void A_third_party_title_the_probe_cannot_answer_for_carries_a_null_install_state()
    {
        using var tree = EpicFixtureTree.Create(manifests: []);
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        var watchDogs = Assert.Single(candidates, c => c.ProviderId == WatchDogsId);
        Assert.Null(watchDogs.Installed);
        Assert.Null(watchDogs.InstallPath);
    }

    [Fact]
    public void Identity_is_the_catalog_item_id_and_the_provider_is_epic()
    {
        using var tree = EpicFixtureTree.Create();
        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        Assert.All(candidates, c => Assert.Equal(ExternalIdProviders.Epic, c.Provider));
        Assert.All(candidates, c => Assert.Equal(EpicLibrarySource.SourceName, c.Source));

        var fez = Assert.Single(candidates, c => c.Title == "Fez");
        Assert.Equal(FezId, fez.ProviderId);
        // Never the codename.
        Assert.NotEqual("Bluebird", fez.ProviderId);
    }

    [Fact]
    public void An_absent_launcher_returns_empty_rather_than_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "hoard-no-epic-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(new EpicLibrarySource(dataRoot: missing).Scan());
        Assert.Empty(new EpicLibrarySource().Scan(missing));
    }

    [Fact]
    public void An_unreadable_manifests_directory_yields_a_null_install_state_not_a_false()
    {
        // The one case where Epic genuinely cannot answer. Reporting false here
        // would clear the stored install flag for the whole library on a machine
        // whose manifests directory moved.
        using var tree = EpicFixtureTree.Create();
        Directory.Delete(Path.Combine(tree.DataRoot, EpicPaths.ManifestsDirectoryName), recursive: true);

        var candidates = SourceOver(tree, EpicInstallState.Unknown).Scan();

        var fez = Assert.Single(candidates, c => c.ProviderId == FezId);
        Assert.Null(fez.Installed);
        Assert.Null(fez.InstallPath);
    }

    [Fact]
    public void The_scan_writes_nothing_into_the_launcher_tree()
    {
        using var tree = EpicFixtureTree.Create();
        var before = SnapshotOf(tree.DataRoot);

        SourceOver(tree, EpicInstallState.Unknown).Scan();

        Assert.Equal(before, SnapshotOf(tree.DataRoot));
    }

    [Fact]
    public void Candidates_coalesce_cleanly_with_another_sources_view_of_the_same_ownership()
    {
        // Epic's nulls must not overwrite a real answer from a source that knows,
        // and must not be turned into zeros on the way through the merge.
        using var tree = EpicFixtureTree.Create(manifests: [EpicFixtureTree.FezManifest]);
        var epic = Assert.Single(SourceOver(tree, EpicInstallState.Unknown).Scan(), c => c.ProviderId == FezId);

        var fromElsewhere = epic with
        {
            Installed = null,
            InstallPath = null,
            PlaytimeMinutes = 120,
            LastPlayedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Source = "process_monitor",
        };

        var merged = Assert.Single(CandidateOwnershipMerge.Coalesce([epic, fromElsewhere]));

        Assert.Equal(120, merged.PlaytimeMinutes);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), merged.LastPlayedAt);
        Assert.True(merged.Installed);
        Assert.Equal(@"C:\Program Files\Epic Games\Fez", merged.InstallPath);
    }

    private static List<(string Path, long Length, DateTime Written)> SnapshotOf(string root)
        => Directory
            .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? (p, new FileInfo(p).Length, File.GetLastWriteTimeUtc(p))
                : (p, -1L, Directory.GetLastWriteTimeUtc(p)))
            .ToList();
}
