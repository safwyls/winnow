using Hoard.Core.Domain;
using Hoard.Ingest.Gog;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// The composed GOG scan: Galaxy's database where it exists, GOG's install
/// registry plus <c>goggame-&lt;id&gt;.info</c> where it does not.
/// </summary>
public class GogLibrarySourceTests
{
    private static GogLibrarySource SourceOver(
        GalaxyFixtureTree tree, IGogInstalledGameRegistry? registry = null)
        => new(registry: registry ?? FakeGogInstalledGameRegistry.Empty, galaxyRoot: tree.GalaxyRoot);

    [Fact]
    public void The_steam_release_in_galaxys_library_never_becomes_a_candidate()
    {
        using var tree = GalaxyFixtureTree.Create();
        var candidates = SourceOver(tree).Scan();

        Assert.DoesNotContain(candidates, c => c.ProviderId == "1091500");
        Assert.DoesNotContain(candidates, c => c.Title == "Cyberpunk 2077");
        Assert.All(candidates, c => Assert.Equal(ExternalIdProviders.Gog, c.Provider));
    }

    [Fact]
    public void Owned_base_games_appear_and_dlc_does_not()
    {
        using var tree = GalaxyFixtureTree.Create();
        var candidates = SourceOver(tree).Scan();

        Assert.Equal(3, candidates.Count);
        Assert.Contains(candidates, c => c.ProviderId == GogFixtures.GwentProductId);
        Assert.Contains(candidates, c => c.ProviderId == GogFixtures.Witcher3ProductId);
        Assert.Contains(candidates, c => c.ProviderId == GogFixtures.TyrianProductId);
        Assert.DoesNotContain(candidates, c => c.ProviderId == GogFixtures.NewGamePlusProductId);
    }

    [Fact]
    public void Identity_is_the_bare_gog_product_id_not_the_release_key()
    {
        // The bare product id is what IGDB's external_games source 5 stores, and
        // what the registry and goggame-<id>.info both carry. No transformation.
        using var tree = GalaxyFixtureTree.Create();
        var gwent = Assert.Single(SourceOver(tree).Scan(), c => c.Title == "GWENT: The Witcher Card Game");

        Assert.Equal(GogFixtures.GwentProductId, gwent.ProviderId);
        Assert.Equal(GogLibrarySource.SourceName, gwent.Source);
    }

    [Fact]
    public void Playtime_last_played_and_purchase_date_come_through_in_the_right_units()
    {
        using var tree = GalaxyFixtureTree.Create();
        var candidates = SourceOver(tree).Scan();

        var gwent = Assert.Single(candidates, c => c.ProviderId == GogFixtures.GwentProductId);
        Assert.Equal(54, gwent.PlaytimeMinutes);
        Assert.Equal(new DateTime(2017, 7, 1, 3, 32, 16, DateTimeKind.Utc), gwent.LastPlayedAt);
        Assert.Equal(new DateTime(2016, 11, 28, 17, 40, 14, DateTimeKind.Utc), gwent.AcquiredAt);
        Assert.True(gwent.Installed);
        Assert.Equal(
            @"C:\Program Files\GOG Galaxy\Games\GWENT The Witcher Card Game", gwent.InstallPath);

        // Uninstalled, and its playtime is untouched by that.
        var witcher3 = Assert.Single(candidates, c => c.ProviderId == GogFixtures.Witcher3ProductId);
        Assert.False(witcher3.Installed);
        Assert.Null(witcher3.InstallPath);
        Assert.Equal(50, witcher3.PlaytimeMinutes);
        Assert.Equal(new DateTime(2018, 11, 20, 14, 18, 42, DateTimeKind.Utc), witcher3.LastPlayedAt);

        // A real zero from a real row, which is a different statement from null.
        var tyrian = Assert.Single(candidates, c => c.ProviderId == GogFixtures.TyrianProductId);
        Assert.Equal(0, tyrian.PlaytimeMinutes);
        Assert.Null(tyrian.LastPlayedAt);
    }

    [Fact]
    public void The_galaxy_title_wins_over_every_installer_locale_local_one()
    {
        // Both sources describe the same install. Galaxy's is English; every
        // local one is Polish. Never let a local title reach the fuzzy matcher.
        using var tree = GalaxyFixtureTree.Create();
        var registry = new FakeGogInstalledGameRegistry(
            RegFile.InstalledGames(GogFixtures.PathOf("gog-games.reg")).ToArray());

        var gwent = Assert.Single(
            SourceOver(tree, registry).Scan(), c => c.ProviderId == GogFixtures.GwentProductId);

        Assert.Equal("GWENT: The Witcher Card Game", gwent.Title);
    }

    [Fact]
    public void The_config_files_storagePath_is_honoured_rather_than_the_path_being_guessed()
    {
        using var tree = GalaxyFixtureTree.Create(storageDirectoryName: "moved-storage");

        Assert.Equal(tree.StoragePath, GogPaths.ReadStoragePath(
            Path.Combine(tree.GalaxyRoot, GogPaths.ConfigFileName)));
        Assert.NotEmpty(SourceOver(tree).Scan());
    }

    [Fact]
    public void A_galaxy_less_machine_falls_back_to_the_registry_and_still_yields_candidates()
    {
        // Standalone GOG installers are a first-class product; a user who never
        // installs Galaxy still owns games, and the registry plus the .info file
        // in the install directory is all there is.
        using var install = new TempInstallDirectory("goggame-1971477531.info");
        var registry = new FakeGogInstalledGameRegistry(
            RegFile.InstalledGames(GogFixtures.PathOf("gog-games.reg"))
                .Select(g => g with { InstallPath = install.Path })
                .ToArray());

        var noGalaxy = Path.Combine(Path.GetTempPath(), "hoard-no-gog-" + Guid.NewGuid().ToString("N"));
        var candidate = Assert.Single(new GogLibrarySource(registry: registry).Scan(noGalaxy));

        Assert.Equal(ExternalIdProviders.Gog, candidate.Provider);
        Assert.Equal(GogFixtures.GwentProductId, candidate.ProviderId);
        Assert.True(candidate.Installed);
        Assert.Equal(install.Path, candidate.InstallPath);

        // The .info file's name beats the registry's, which additionally strips
        // diacritics. Both are installer-locale and neither is the store title —
        // the product id is what carries identity here.
        Assert.Equal("GWINT: Wiedźmińska Gra Karciana", candidate.Title);

        // No playtime source exists on this path at all. Null, never zero.
        Assert.Null(candidate.PlaytimeMinutes);
        Assert.Null(candidate.LastPlayedAt);

        // INSTALLDATE is local time and is an install date, not a purchase date.
        Assert.Null(candidate.AcquiredAt);
    }

    [Fact]
    public void The_registry_fallback_excludes_dlc_using_gameId_versus_rootGameId()
    {
        using var install = new TempInstallDirectory("goggame-1430742983.info");
        var registry = new FakeGogInstalledGameRegistry(
            new GogRegistryGame(
                GameId: GogFixtures.NewGamePlusProductId,
                GameName: "New Game +",
                InstallPath: install.Path,
                Executable: null,
                BuildId: null,
                Version: null,
                InstallDateLocal: null));

        var noGalaxy = Path.Combine(Path.GetTempPath(), "hoard-no-gog-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(new GogLibrarySource(registry: registry).Scan(noGalaxy));
    }

    [Fact]
    public void A_standalone_install_galaxy_has_not_indexed_is_still_ingested()
    {
        using var tree = GalaxyFixtureTree.Create();
        using var install = new TempInstallDirectory();
        var registry = new FakeGogInstalledGameRegistry(
            new GogRegistryGame(
                GameId: "1207659013",
                GameName: "Treasure Adventure Game",
                InstallPath: install.Path,
                Executable: null,
                BuildId: null,
                Version: null,
                InstallDateLocal: null));

        var candidates = SourceOver(tree, registry).Scan();

        Assert.Equal(4, candidates.Count);
        var extra = Assert.Single(candidates, c => c.ProviderId == "1207659013");
        Assert.True(extra.Installed);
        Assert.Null(extra.PlaytimeMinutes);
    }

    [Fact]
    public void An_absent_gog_installation_returns_empty_rather_than_throwing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "hoard-no-gog-" + Guid.NewGuid().ToString("N"));

        Assert.Empty(new GogLibrarySource(registry: FakeGogInstalledGameRegistry.Empty).Scan(missing));
        Assert.Empty(new GogLibrarySource(
            registry: FakeGogInstalledGameRegistry.Empty, galaxyRoot: missing).Scan());
    }

    [Fact]
    public void Galaxy_present_but_with_no_client_database_is_not_an_error()
    {
        using var tree = GalaxyFixtureTree.Create();
        File.Delete(tree.DatabasePath);

        Assert.Empty(SourceOver(tree).Scan());
    }

    [Fact]
    public void The_scan_writes_nothing_into_gogs_directory()
    {
        // Belt and braces around GalaxyDatabaseSnapshot's own test: the whole
        // Galaxy tree, not just the storage directory, must come out identical.
        using var tree = GalaxyFixtureTree.Create(walMode: true);
        var before = SnapshotOf(tree.GalaxyRoot);

        Assert.NotEmpty(SourceOver(tree).Scan());

        Assert.Equal(before, SnapshotOf(tree.GalaxyRoot));
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

/// <summary>
/// A throwaway game install directory holding whichever <c>goggame-*.info</c>
/// fixtures a test needs, so the Galaxy-less path reads a real captured file from
/// a path that exists on any machine.
/// </summary>
internal sealed class TempInstallDirectory : IDisposable
{
    internal TempInstallDirectory(params string[] infoFixtures)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "hoard-gog-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);

        foreach (var fixture in infoFixtures)
        {
            File.Copy(GogFixtures.PathOf(fixture), System.IO.Path.Combine(Path, fixture));
        }
    }

    internal string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must never fail a test run.
        }
    }
}
