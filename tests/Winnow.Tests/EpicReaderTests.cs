using Winnow.Ingest.Epic;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// Reader tests against the sanitized real fixtures in tests/fixtures/epic/.
/// Everything asserted here is a quirk the fixture README and
/// docs/spikes/epic-gog-local-files.md document as deliberately preserved.
/// </summary>
public class EpicManifestReaderTests
{
    [Fact]
    public void Parses_the_installed_base_game_manifest()
    {
        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.FezManifest));

        Assert.NotNull(manifest);
        Assert.Equal("Fez", manifest.DisplayName);
        Assert.Equal("7a70b499513441c792b541d53505e0b2", manifest.CatalogItemId);
        Assert.Equal("41f47fd0d3e248bc938a5815d6d64daa", manifest.CatalogNamespace);
        Assert.Equal(@"C:\Program Files\Epic Games\Fez", manifest.InstallLocation);
        Assert.Equal("FEZ.exe", manifest.LaunchExecutable);
        Assert.Equal(@"C:\Program Files\Epic Games\Fez\FEZ.exe", manifest.LaunchExecutablePath);
        Assert.Equal("1.12.0", manifest.AppVersionString);
        Assert.Equal("A47587CE819533CC1BDD688E306742B3", manifest.InstallationGuid);

        // InstallSize is a JSON *number*, not a string.
        Assert.Equal(450279934L, manifest.InstallSize);

        Assert.True(manifest.IsGame);
        Assert.False(manifest.IsDlc);
        Assert.True(manifest.IsFullyInstalled);
    }

    [Fact]
    public void AppName_is_a_codename_and_is_never_the_title()
    {
        // "Bluebird" is Fez. Rendering AppName ships gibberish; it is kept only
        // because it is the key GOG's gamesdb identity graph accepts for Epic.
        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.FezManifest));

        Assert.NotNull(manifest);
        Assert.Equal("Bluebird", manifest.AppName);
        Assert.NotEqual(manifest.AppName, manifest.DisplayName);
    }

    [Fact]
    public void MainGame_fields_are_empty_strings_on_a_base_game_not_missing_keys()
    {
        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.FezManifest));

        Assert.NotNull(manifest);
        Assert.Equal(string.Empty, manifest.MainGameCatalogItemId);
        Assert.Equal(string.Empty, manifest.MainGameAppName);
        Assert.False(manifest.IsDlc);
    }

    [Fact]
    public void Dlc_is_identified_by_MainGame_alone_because_categories_look_like_a_base_game()
    {
        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.DlcManifest));

        Assert.NotNull(manifest);
        Assert.True(manifest.IsDlc);
        Assert.Equal("5cf86732e2744fec98a1c8a077d9f3a8", manifest.MainGameCatalogItemId);

        // The trap: by category this DLC is indistinguishable from a base game.
        Assert.True(manifest.IsGame);
        Assert.Contains("games", manifest.AppCategories);
        Assert.Contains("applications", manifest.AppCategories);
    }

    [Fact]
    public void An_in_flight_download_has_a_manifest_and_is_not_installed()
    {
        // The .item file is written when the install is QUEUED, not when it
        // finishes — a reader that ignores bIsIncompleteInstall reports a
        // half-downloaded game as installed.
        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.IncompleteManifest));

        Assert.NotNull(manifest);
        Assert.True(manifest.IsIncompleteInstall);
        Assert.False(manifest.IsFullyInstalled);
        Assert.Equal(0L, manifest.InstallSize);
    }

    [Fact]
    public void Missing_directory_returns_empty_without_throwing()
    {
        var manifests = new EpicManifestReader().ReadDirectory(
            Path.Combine(Path.GetTempPath(), "winnow-does-not-exist", "Manifests"));

        Assert.Empty(manifests);
    }
}

public class EpicCatalogReaderTests
{
    private static IReadOnlyList<EpicCatalogEntry> Catalog()
        => new EpicCatalogReader().Read(EpicFixtureTree.PathOf(EpicPaths.CatalogCacheFileName));

    [Fact]
    public void Catcache_is_base64_of_plain_json_not_gzip()
    {
        // If this ever fails with an empty list, Epic changed the encoding —
        // section 22 of the spike says that is the trigger to revisit OAuth.
        var entries = Catalog();

        Assert.Equal(6, entries.Count);
        Assert.Contains(entries, e => e.Title == "Fez");
    }

    [Fact]
    public void Catalog_entry_joins_to_the_manifest_on_namespace_id_and_appname()
    {
        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.FezManifest));
        var fez = Assert.Single(Catalog(), e => e.CatalogItemId == "7a70b499513441c792b541d53505e0b2");

        Assert.NotNull(manifest);
        Assert.Equal(manifest.CatalogNamespace, fez.CatalogNamespace);
        Assert.Equal(manifest.DisplayName, fez.Title);
        // releaseInfo[0].appId is the manifest's AppName.
        Assert.Equal(manifest.AppName, fez.AppName);
        Assert.Equal("Polytron Corporation, Inc", fez.Developer);
    }

    [Fact]
    public void Non_games_are_rejected_by_category()
    {
        var entries = Catalog();

        // Twinmotion: software + applications, no "games".
        var twinmotion = Assert.Single(entries, e => e.Title.StartsWith("Twinmotion", StringComparison.Ordinal));
        Assert.False(twinmotion.IsGame);

        // A cosmetic entitlement: audience + public only. "audience" is the
        // single most common category on a real account (114 of 297) and is all
        // filler — leaving it in fills the library with junk.
        var audience = Assert.Single(entries, e => e.Categories.Contains("audience"));
        Assert.False(audience.IsGame);
    }

    [Fact]
    public void Dlc_is_identified_bottom_up_from_mainGameItem_never_from_dlcItemList()
    {
        var entries = Catalog();

        var bountyOfBlood = Assert.Single(entries, e => e.CatalogItemId == "0854f1cf60fd48d4a29178b211d2f133");
        Assert.True(bountyOfBlood.IsDlc);
        Assert.Equal("5cf86732e2744fec98a1c8a077d9f3a8", bountyOfBlood.MainGameCatalogItemId);

        // The edge case: LEGO Fortnite Odyssey carries games + applications AND
        // games/experience, and is still correctly a child of Fortnite.
        var legoFortnite = Assert.Single(entries, e => e.CatalogItemId == "8f33cce63b3f4a46aca59ff8c85ff1cd");
        Assert.True(legoFortnite.IsGame);
        Assert.True(legoFortnite.IsDlc);
    }

    [Fact]
    public void Third_party_managed_entries_expose_the_delivering_launchers_registry_pointer()
    {
        var watchDogs = Assert.Single(Catalog(), e => e.CatalogItemId == "6dc445f656de4e029834b2d32b6a2f77");

        Assert.True(watchDogs.IsThirdPartyManaged);
        Assert.Equal("UbisoftConnect", watchDogs.ThirdPartyManagedProvider);
        Assert.NotEqual(string.Empty, watchDogs.RegistryPath);
        Assert.NotEqual(string.Empty, watchDogs.RegistryKey);
    }

    [Fact]
    public void Missing_catalog_returns_empty_without_throwing()
    {
        var entries = new EpicCatalogReader().Read(
            Path.Combine(Path.GetTempPath(), "winnow-does-not-exist", "catcache.bin"));

        Assert.Empty(entries);
    }
}

public class EpicThirdPartyAppReaderTests
{
    [Fact]
    public void Reads_the_misspelled_directory_and_its_differently_cased_keys()
    {
        var apps = new EpicThirdPartyAppReader().ReadDirectory(
            EpicFixtureTree.PathOf(EpicPaths.ThirdPartyManagedAppsDirectoryName));

        var app = Assert.Single(apps);
        Assert.Equal("Watch Dogs", app.Title);
        // CatalogID here, CatalogItemId on a .item manifest.
        Assert.Equal("6dc445f656de4e029834b2d32b6a2f77", app.CatalogItemId);
        Assert.Equal("ecebf45065bc4993abfe0e84c40ff18e", app.CatalogNamespace);
        Assert.Equal("Jasper", app.AppName);
        Assert.Equal("UbisoftConnect", app.Provider);
        Assert.Equal(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\274", app.RegistryPath);
        Assert.Equal("InstallDir", app.RegistryKey);
        Assert.Equal("274", app.GameId);
    }

    [Fact]
    public void The_directory_name_epic_actually_uses_is_misspelled()
        => Assert.Equal("ThirPartyManagedApps", EpicPaths.ThirdPartyManagedAppsDirectoryName);

    [Fact]
    public void Missing_directory_returns_empty_without_throwing()
    {
        var apps = new EpicThirdPartyAppReader().ReadDirectory(
            Path.Combine(Path.GetTempPath(), "winnow-does-not-exist", "ThirPartyManagedApps"));

        Assert.Empty(apps);
    }
}

public class EpicDeadPathTests
{
    [Fact]
    public void LauncherInstalled_dat_reports_nothing_installed_while_a_game_is_installed()
    {
        // Regression fixture, not a reader test: this file is why the fixture
        // exists. It says the installation list is empty on a machine where Fez
        // is installed and playable, so no Epic reader may ever consult it. If
        // this assertion is ever "fixed" by pointing a reader at the file, the
        // install state of the entire Epic library goes to false.
        var text = File.ReadAllText(EpicFixtureTree.PathOf("LauncherInstalled.dat"));

        Assert.Contains("\"InstallationList\": []", text, StringComparison.Ordinal);

        var manifest = new EpicManifestReader().Read(EpicFixtureTree.PathOf(EpicFixtureTree.FezManifest));
        Assert.NotNull(manifest);
        Assert.True(manifest.IsFullyInstalled);
    }
}
