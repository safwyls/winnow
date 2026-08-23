using Hoard.Ingest.Steam;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// End-to-end tests for <see cref="SteamLibrarySource"/> over a synthetic
/// Steam root built in a temp directory: real fixture manifests and
/// localconfig, a generated libraryfolders.vdf pointing at temp paths, and a
/// second account to exercise the multi-account playtime strategy.
/// </summary>
public sealed class SteamLibrarySourceTests : IDisposable
{
    private readonly string _root;
    private readonly string _secondLibrary;

    public SteamLibrarySourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"hoard-steam-{Guid.NewGuid():N}");
        _secondLibrary = Path.Combine(_root, "SecondLibrary");

        Directory.CreateDirectory(Path.Combine(_root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(_secondLibrary, "steamapps"));

        // libraryfolders.vdf referencing both temp roots (VDF escapes backslashes).
        File.WriteAllText(
            Path.Combine(_root, "steamapps", "libraryfolders.vdf"),
            $$"""
            "libraryfolders"
            {
                "0"
                {
                    "path"		"{{Escape(_root)}}"
                    "label"		""
                    "apps"
                    {
                        "1244090"		"4879401530"
                        "2686630"		"29799918863"
                        "228980"		"157818239"
                    }
                }
                "1"
                {
                    "path"		"{{Escape(_secondLibrary)}}"
                    "label"		"big drive"
                    "apps"
                    {
                        "1203620"		"44998366792"
                    }
                }
            }
            """);

        // Primary library: the two real fixture manifests plus a Steamworks
        // redistributables manifest that must be deny-listed.
        File.Copy(SteamFixtures.PathOf("appmanifest_1244090.acf"),
            Path.Combine(_root, "steamapps", "appmanifest_1244090.acf"));
        File.Copy(SteamFixtures.PathOf("appmanifest_2686630.acf"),
            Path.Combine(_root, "steamapps", "appmanifest_2686630.acf"));
        File.WriteAllText(
            Path.Combine(_root, "steamapps", "appmanifest_228980.acf"),
            """
            "AppState"
            {
                "appid"		"228980"
                "name"		"Steamworks Common Redistributables"
                "StateFlags"		"4"
                "installdir"		"Steamworks Shared"
            }
            """);

        // Second library: a manifest for Elden Ring (playtime lives in account 12345678's localconfig).
        File.WriteAllText(
            Path.Combine(_secondLibrary, "steamapps", "appmanifest_1203620.acf"),
            """
            "AppState"
            {
                "appid"		"1203620"
                "name"		"Elden Ring"
                "StateFlags"		"4"
                "installdir"		"ELDEN RING"
                "buildid"		"20240001"
                "lastupdated"		"1786900000"
                "LastPlayed"		"1786924990"
            }
            """);

        // Account 12345678: the real localconfig fixture (2686630 → 244 min).
        var account1Config = Path.Combine(_root, "userdata", "12345678", "config");
        Directory.CreateDirectory(account1Config);
        File.Copy(SteamFixtures.PathOf("localconfig.vdf"),
            Path.Combine(account1Config, "localconfig.vdf"));

        // Account 87654321: more playtime on 2686630 → must win the join.
        var account2Config = Path.Combine(_root, "userdata", "87654321", "config");
        Directory.CreateDirectory(account2Config);
        File.WriteAllText(
            Path.Combine(account2Config, "localconfig.vdf"),
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
                                "2686630"
                                {
                                    "LastPlayed"		"1787400000"
                                    "Playtime"		"500"
                                    "Playtime2wks"		"120"
                                }
                            }
                        }
                    }
                }
            }
            """);

        // Non-account noise under userdata that must be ignored.
        Directory.CreateDirectory(Path.Combine(_root, "userdata", "ac_cache"));
        Directory.CreateDirectory(Path.Combine(_root, "userdata", "0"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Escape(string path) => path.Replace(@"\", @"\\");

    [Fact]
    public void Account_enumerator_returns_only_numeric_account_folders()
    {
        var accounts = new SteamAccountEnumerator().Enumerate(_root);

        Assert.Equal(2, accounts.Count);
        Assert.Equal("12345678", accounts[0].Steam3Id);
        Assert.Equal("87654321", accounts[1].Steam3Id);
        Assert.EndsWith(
            Path.Combine("userdata", "12345678", "config", "localconfig.vdf"),
            accounts[0].LocalConfigPath);
    }

    [Fact]
    public void Scan_emits_candidates_from_all_libraries_and_skips_tool_apps()
    {
        var candidates = new SteamLibrarySource().Scan(_root);

        Assert.Equal(3, candidates.Count);
        Assert.DoesNotContain(candidates, c => c.ProviderId == "228980");
        Assert.All(candidates, c =>
        {
            Assert.Equal("steam", c.Provider);
            Assert.Equal("steam_local", c.Source);
            Assert.True(c.Installed);
        });

        // Second-library app got its install path under that library root.
        var eldenRing = Assert.Single(candidates, c => c.ProviderId == "1203620");
        Assert.Equal("Elden Ring", eldenRing.Title);
        Assert.Equal(
            Path.Combine(_secondLibrary, "steamapps", "common", "ELDEN RING"),
            eldenRing.InstallPath);
        Assert.Equal(817, eldenRing.PlaytimeMinutes);
        Assert.Equal("12345678", eldenRing.AccountRef);
    }

    [Fact]
    public void Never_played_install_has_no_playtime_and_no_last_played()
    {
        var candidates = new SteamLibrarySource().Scan(_root);

        var seaOfStars = Assert.Single(candidates, c => c.ProviderId == "1244090");
        Assert.Equal("Sea of Stars: Sunset Edition", seaOfStars.Title);
        Assert.Null(seaOfStars.PlaytimeMinutes);
        Assert.Null(seaOfStars.LastPlayedAt);
        Assert.Equal(
            Path.Combine(_root, "steamapps", "common", "Sea of Stars"),
            seaOfStars.InstallPath);
    }

    [Fact]
    public void Multi_account_playtime_takes_the_account_with_max_playtime()
    {
        var candidates = new SteamLibrarySource().Scan(_root);

        // 2686630 has 244 min on account 12345678 and 500 min on 87654321:
        // the larger total wins the whole record, attribution included.
        var voyagers = Assert.Single(candidates, c => c.ProviderId == "2686630");
        Assert.Equal(500, voyagers.PlaytimeMinutes);
        Assert.Equal("87654321", voyagers.AccountRef);
        Assert.Equal(SteamFixtures.Epoch(1787400000), voyagers.LastPlayedAt);
    }

    [Fact]
    public void Machine_without_steam_returns_empty_without_throwing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"hoard-no-steam-{Guid.NewGuid():N}");

        Assert.Empty(new SteamLibrarySource().Scan(missingRoot));
    }
}
