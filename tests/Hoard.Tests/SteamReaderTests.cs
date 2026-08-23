using Hoard.Ingest.Steam;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// Reader tests against the sanitized real fixtures in tests/fixtures/steam/.
/// The asserted values are the ones the fixture README and
/// docs/spikes/steam-local-files.md document as deliberately preserved quirks.
/// </summary>
public class LibraryFoldersReaderTests
{
    [Fact]
    public void Parses_both_library_roots_with_labels_and_app_maps()
    {
        var folders = new LibraryFoldersReader().Read(SteamFixtures.PathOf("libraryfolders.vdf"));

        Assert.Equal(2, folders.Count);

        var primary = folders[0];
        Assert.Equal(@"C:\Program Files (x86)\Steam", primary.Path);
        Assert.Equal(string.Empty, primary.Label);
        Assert.Equal(@"C:\Program Files (x86)\Steam\steamapps", primary.SteamAppsPath);
        Assert.Equal(4, primary.Apps.Count);
        Assert.Equal(157818239, primary.Apps["228980"]);
        Assert.Equal(4879401530, primary.Apps["1244090"]);

        var secondary = folders[1];
        Assert.Equal(@"D:\SteamLibrary", secondary.Path);
        Assert.Equal("big drive", secondary.Label);
        Assert.Equal(2, secondary.Apps.Count);
        Assert.Equal(44998366792, secondary.Apps["1203620"]);
    }

    [Fact]
    public void App_with_zero_size_is_still_listed_not_treated_as_uninstalled()
    {
        // The spike: size "0" was observed for an app pending install/update;
        // the appmanifest, not this map, is the authority on install state.
        var folders = new LibraryFoldersReader().Read(SteamFixtures.PathOf("libraryfolders.vdf"));

        Assert.True(folders[0].Apps.TryGetValue("3321460", out var size));
        Assert.Equal(0, size);
    }

    [Fact]
    public void Missing_file_returns_empty_without_throwing()
    {
        var folders = new LibraryFoldersReader().Read(
            Path.Combine(Path.GetTempPath(), "hoard-does-not-exist", "libraryfolders.vdf"));

        Assert.Empty(folders);
    }
}

public class AppManifestReaderTests
{
    [Fact]
    public void Parses_never_played_manifest_including_lowercase_lastupdated()
    {
        var manifest = new AppManifestReader().Read(SteamFixtures.PathOf("appmanifest_1244090.acf"));

        Assert.NotNull(manifest);
        Assert.Equal("1244090", manifest.AppId);
        Assert.Equal("Sea of Stars: Sunset Edition", manifest.Name);
        Assert.Equal("Sea of Stars", manifest.InstallDir);
        Assert.Equal("23623172", manifest.BuildId);
        Assert.Equal(4, manifest.StateFlags);
        Assert.True(manifest.IsFullyInstalled);

        // On-disk key is all-lowercase "lastupdated" — must parse case-insensitively.
        Assert.Equal(SteamFixtures.Epoch(1787359073), manifest.LastUpdatedUtc);

        // LastPlayed "0" = never launched → null, not 1970-01-01.
        Assert.Null(manifest.LastPlayedUtc);
    }

    [Fact]
    public void Parses_recently_played_manifest_with_epoch_last_played()
    {
        var manifest = new AppManifestReader().Read(SteamFixtures.PathOf("appmanifest_2686630.acf"));

        Assert.NotNull(manifest);
        Assert.Equal("2686630", manifest.AppId);
        Assert.Equal("Voyagers of Nera", manifest.Name);
        Assert.Equal("Voyagers of Nera", manifest.InstallDir);
        Assert.Equal("24498984", manifest.BuildId);
        Assert.True(manifest.IsFullyInstalled);
        Assert.Equal(SteamFixtures.Epoch(1787334029), manifest.LastUpdatedUtc);
        Assert.Equal(SteamFixtures.Epoch(1787336129), manifest.LastPlayedUtc);
    }

    [Fact]
    public void Missing_file_returns_null_without_throwing()
    {
        var manifest = new AppManifestReader().Read(
            Path.Combine(Path.GetTempPath(), "hoard-does-not-exist", "appmanifest_1.acf"));

        Assert.Null(manifest);
    }

    /// <summary>
    /// The spike's headline rule: KV1 key casing is inconsistent even within a
    /// single file (<c>appid</c> vs <c>StateFlags</c> vs <c>lastupdated</c>), and
    /// Valve's own KeyValues is case-insensitive. Every key here is cased
    /// differently from the lookup that finds it, so this fails outright if
    /// KeyValues1.Child ever becomes an ordinal comparison.
    /// </summary>
    [Fact]
    public void Keys_are_matched_case_insensitively_whatever_the_file_uses()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hoard-kv-{Guid.NewGuid():N}.acf");
        File.WriteAllText(path, """
            "appstate"
            {
                "APPID"		"1203620"
                "NaMe"		"Elden Ring"
                "InstallDir"		"ELDEN RING"
                "BUILDID"		"20240001"
                "stateflags"		"4"
                "LastUpdated"		"1786900000"
                "lastplayed"		"1786924990"
            }
            """);

        try
        {
            var manifest = new AppManifestReader().Read(path);

            Assert.NotNull(manifest);
            Assert.Equal("1203620", manifest.AppId);
            Assert.Equal("Elden Ring", manifest.Name);
            Assert.Equal("ELDEN RING", manifest.InstallDir);
            Assert.Equal("20240001", manifest.BuildId);
            Assert.Equal(4, manifest.StateFlags);
            Assert.True(manifest.IsFullyInstalled);
            Assert.Equal(SteamFixtures.Epoch(1786900000), manifest.LastUpdatedUtc);
            Assert.Equal(SteamFixtures.Epoch(1786924990), manifest.LastPlayedUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Blank_name_is_read_as_absent_not_as_an_empty_title()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hoard-kv-{Guid.NewGuid():N}.acf");
        File.WriteAllText(path, """
            "AppState"
            {
                "appid"		"620"
                "name"		""
                "StateFlags"		"4"
                "installdir"		"Portal 2"
            }
            """);

        try
        {
            var manifest = new AppManifestReader().Read(path);

            Assert.NotNull(manifest);
            Assert.Equal("620", manifest.AppId);
            Assert.Null(manifest.Name);
            Assert.Equal("Portal 2", manifest.InstallDir);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class LocalConfigReaderTests
{
    private readonly IReadOnlyDictionary<string, SteamAppPlaytime> _apps =
        new LocalConfigReader().Read(SteamFixtures.PathOf("localconfig.vdf"));

    [Fact]
    public void Reads_exactly_the_app_blocks_that_carry_playtime()
    {
        // 7 and 760 are cloud-only blocks with no Playtime key — skipped, not zeros.
        Assert.Equal(5, _apps.Count);
        Assert.False(_apps.ContainsKey("7"));
        Assert.False(_apps.ContainsKey("760"));
    }

    [Fact]
    public void Apptickets_map_does_not_contaminate_playtime()
    {
        // 1244090 appears ONLY under UserLocalConfigStore/apptickets (also an
        // appid-keyed map). A reader that grabs the first appid-keyed node
        // would emit it; navigating Software/Valve/Steam/apps must not.
        Assert.False(_apps.ContainsKey("1244090"));

        // 2686630 is in both apptickets and apps — its values must be the
        // playtime ones, not ticket hex.
        var app = _apps["2686630"];
        Assert.Equal(244, app.PlaytimeMinutes);
        Assert.Equal(34, app.Playtime2WeeksMinutes);
        Assert.Equal(SteamFixtures.Epoch(1787336130), app.LastPlayedUtc);
    }

    [Fact]
    public void Sentinel_86400_last_played_maps_to_null()
    {
        var app = _apps["60"];
        Assert.Equal(3, app.PlaytimeMinutes);
        Assert.Null(app.LastPlayedUtc);
        Assert.Null(app.Playtime2WeeksMinutes);
    }

    [Fact]
    public void Real_last_played_epoch_round_trips()
    {
        var app = _apps["10"];
        Assert.Equal(358, app.PlaytimeMinutes);
        Assert.Equal(SteamFixtures.Epoch(1527216883), app.LastPlayedUtc);
    }

    [Fact]
    public void Noise_keys_are_ignored()
    {
        // 1203620 carries _eula_, cloud, autocloud and BadgeData noise around
        // its playtime keys.
        var app = _apps["1203620"];
        Assert.Equal(817, app.PlaytimeMinutes);
        Assert.Equal(473, app.Playtime2WeeksMinutes);
        Assert.Equal(SteamFixtures.Epoch(1786924992), app.LastPlayedUtc);
    }

    [Fact]
    public void Key_order_inside_an_app_block_does_not_matter()
    {
        // 4588700 has the alternate ordering (LastPlayed, BadgeData,
        // Playtime2wks, Playtime).
        var app = _apps["4588700"];
        Assert.Equal(1, app.PlaytimeMinutes);
        Assert.Equal(1, app.Playtime2WeeksMinutes);
        Assert.Equal(SteamFixtures.Epoch(1787343570), app.LastPlayedUtc);
    }

    [Fact]
    public void Missing_file_returns_empty_without_throwing()
    {
        var apps = new LocalConfigReader().Read(
            Path.Combine(Path.GetTempPath(), "hoard-does-not-exist", "localconfig.vdf"));

        Assert.Empty(apps);
    }
}
