using Winnow.Ingest.Steam;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// End-to-end tests for <see cref="SteamLibrarySource"/> over a synthetic
/// Steam root built in a temp directory: real fixture manifests and
/// localconfig, a generated libraryfolders.vdf pointing at temp paths, and a
/// second account to exercise the multi-account playtime strategy.
///
/// <para>The fixture is deliberately lopsided the way a real install is: three
/// appids have manifests (1244090, 2686630, 1203620) while the fixture
/// localconfig also carries playtime for three appids that are NOT installed
/// (10, 60, 4588700). The scan emits the union of the two.</para>
/// </summary>
public sealed class SteamLibrarySourceTests : IDisposable
{
    private readonly string _root;
    private readonly string _secondLibrary;

    public SteamLibrarySourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"winnow-steam-{Guid.NewGuid():N}");
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
                                "228980"
                                {
                                    "LastPlayed"		"1787400000"
                                    "Playtime"		"9"
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

        // 3 installed manifests + 3 played-but-uninstalled appids.
        Assert.Equal(6, candidates.Count);

        // 228980 has BOTH a manifest and a localconfig playtime record here:
        // the deny-list must cover both sources, or it leaks back in.
        Assert.DoesNotContain(candidates, c => c.ProviderId == "228980");

        Assert.All(candidates, c =>
        {
            Assert.Equal("steam", c.Provider);
            Assert.Equal("steam_local", c.Source);
        });

        // Second-library app got its install path under that library root.
        var eldenRing = Assert.Single(candidates, c => c.ProviderId == "1203620");
        Assert.True(eldenRing.Installed);
        Assert.Equal("Elden Ring", eldenRing.Title);
        Assert.Equal(
            Path.Combine(_secondLibrary, "steamapps", "common", "ELDEN RING"),
            eldenRing.InstallPath);
        Assert.Equal(817, eldenRing.PlaytimeMinutes);
        Assert.Equal("12345678", eldenRing.AccountRef);
    }

    [Fact]
    public void Scan_emits_provisional_candidates_for_played_but_uninstalled_apps()
    {
        var candidates = new SteamLibrarySource().Scan(_root);

        // appid 10 is in the fixture localconfig with 358 minutes and has no
        // appmanifest anywhere — exactly the "bounced off, then uninstalled"
        // pile the product exists to surface.
        var uninstalled = Assert.Single(candidates, c => c.ProviderId == "10");
        Assert.Null(uninstalled.Title);           // no local title source exists
        Assert.False(uninstalled.Installed);
        Assert.Null(uninstalled.InstallPath);
        Assert.Equal(358, uninstalled.PlaytimeMinutes);
        Assert.Equal(SteamFixtures.Epoch(1527216883), uninstalled.LastPlayedAt);
        Assert.Equal("12345678", uninstalled.AccountRef);

        // appid 60 carries the 86400 "unknown" sentinel: playtime yes, date no.
        var sentinel = Assert.Single(candidates, c => c.ProviderId == "60");
        Assert.Equal(3, sentinel.PlaytimeMinutes);
        Assert.Null(sentinel.LastPlayedAt);
        Assert.Null(sentinel.Title);
    }

    [Fact]
    public void Scan_union_deduplicates_and_manifests_stay_authoritative()
    {
        var candidates = new SteamLibrarySource().Scan(_root);

        // No appid emitted twice, even though 1203620/2686630 appear in both
        // the manifest set and the playtime map.
        var appIds = candidates.Select(c => c.ProviderId).ToArray();
        Assert.Equal(appIds.Length, appIds.Distinct(StringComparer.Ordinal).Count());

        // In-both appid: title and install state come from the manifest,
        // playtime from localconfig. Neither source is allowed to win the other's field.
        var voyagers = Assert.Single(candidates, c => c.ProviderId == "2686630");
        Assert.Equal("Voyagers of Nera", voyagers.Title);
        Assert.True(voyagers.Installed);
        Assert.NotNull(voyagers.InstallPath);
        Assert.Equal(500, voyagers.PlaytimeMinutes);

        // Deterministic appid ordering across the whole union, not per-source.
        var ordered = candidates.Select(c => long.Parse(c.ProviderId)).ToArray();
        Assert.Equal(ordered.OrderBy(id => id).ToArray(), ordered);
    }

    [Fact]
    public void Playtime_only_candidates_use_the_same_multi_account_winner_rule()
    {
        // A third account with more time on the uninstalled appid 10 than the
        // fixture account's 358 minutes: it must win minutes, date and attribution.
        var account3Config = Path.Combine(_root, "userdata", "99999999", "config");
        Directory.CreateDirectory(account3Config);
        File.WriteAllText(
            Path.Combine(account3Config, "localconfig.vdf"),
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
                                "10"
                                {
                                    "LastPlayed"		"1787500000"
                                    "Playtime"		"900"
                                }
                            }
                        }
                    }
                }
            }
            """);

        var uninstalled = Assert.Single(
            new SteamLibrarySource().Scan(_root), c => c.ProviderId == "10");

        Assert.Equal(900, uninstalled.PlaytimeMinutes);
        Assert.Equal(SteamFixtures.Epoch(1787500000), uninstalled.LastPlayedAt);
        Assert.Equal("99999999", uninstalled.AccountRef);
        Assert.Null(uninstalled.Title);
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
        var missingRoot = Path.Combine(Path.GetTempPath(), $"winnow-no-steam-{Guid.NewGuid():N}");

        Assert.Empty(new SteamLibrarySource().Scan(missingRoot));
    }

    /// <summary>
    /// The appmanifest LastPlayed belongs to the MACHINE. Using it to fill the
    /// gap when the winning account's date is the 86400 "unknown" sentinel
    /// attributes another account's session to this one — and can make a
    /// genuinely dormant title look recently played, suppressing exactly what
    /// the staleness buckets exist to surface.
    /// </summary>
    [Fact]
    public void Winning_account_with_an_unknown_date_does_not_borrow_the_manifest_date()
    {
        // appid 60 in the fixture localconfig: 3 minutes, LastPlayed 86400.
        // Give it a manifest carrying a real machine-level date.
        File.WriteAllText(
            Path.Combine(_root, "steamapps", "appmanifest_60.acf"),
            """
            "AppState"
            {
                "appid"		"60"
                "name"		"Ricochet"
                "StateFlags"		"4"
                "installdir"		"Ricochet"
                "LastPlayed"		"1787400000"
            }
            """);

        var ricochet = Assert.Single(new SteamLibrarySource().Scan(_root), c => c.ProviderId == "60");

        Assert.Equal("Ricochet", ricochet.Title);
        Assert.Equal(3, ricochet.PlaytimeMinutes);
        Assert.Equal("12345678", ricochet.AccountRef);

        // The account owns this record and its date is genuinely unknown.
        Assert.Null(ricochet.LastPlayedAt);
    }

    /// <summary>
    /// Manifests but no readable <c>userdata/</c>: the manifest date is the only
    /// play evidence on the machine, so it is kept — unattributed, because no
    /// account can be named — and it must reach the play record rather than
    /// being discarded for want of minutes.
    /// </summary>
    [Fact]
    public void Manifest_only_machine_keeps_its_dates_but_attributes_them_to_no_account()
    {
        var manifestOnlyRoot = Path.Combine(_root, "ManifestOnlyMachine");
        Directory.CreateDirectory(Path.Combine(manifestOnlyRoot, "steamapps"));
        File.WriteAllText(
            Path.Combine(manifestOnlyRoot, "steamapps", "appmanifest_1203620.acf"),
            """
            "AppState"
            {
                "appid"		"1203620"
                "name"		"Elden Ring"
                "StateFlags"		"4"
                "installdir"		"ELDEN RING"
                "LastPlayed"		"1786924990"
            }
            """);

        var candidate = Assert.Single(new SteamLibrarySource().Scan(manifestOnlyRoot));

        Assert.Equal("1203620", candidate.ProviderId);
        Assert.Equal(SteamFixtures.Epoch(1786924990), candidate.LastPlayedAt);
        Assert.Null(candidate.PlaytimeMinutes);

        // Machine-level, so no account is named — attributing it would be a guess.
        Assert.Null(candidate.AccountRef);
    }

    [Fact]
    public void Files_with_a_longer_extension_are_not_read_as_appmanifests()
    {
        // On Windows the 8.3 short-name rule makes a "*.acf" glob also match
        // ".acfx" and friends, and Steam leaves backup files in this directory.
        File.WriteAllText(
            Path.Combine(_root, "steamapps", "appmanifest_999999.acfx"),
            """
            "AppState"
            {
                "appid"		"999999"
                "name"		"Not A Real Install"
                "StateFlags"		"4"
                "installdir"		"Nope"
            }
            """);

        Assert.DoesNotContain(new SteamLibrarySource().Scan(_root), c => c.ProviderId == "999999");
    }

    [Fact]
    public void Manifest_with_a_blank_name_emits_a_titleless_candidate()
    {
        // A blank name is "unnamed", not a name: the resolver must see null and
        // mint a repairable provisional work, not a permanently blank one.
        File.WriteAllText(
            Path.Combine(_root, "steamapps", "appmanifest_620.acf"),
            """
            "AppState"
            {
                "appid"		"620"
                "name"		""
                "StateFlags"		"4"
                "installdir"		"Portal 2"
            }
            """);

        var candidate = Assert.Single(new SteamLibrarySource().Scan(_root), c => c.ProviderId == "620");

        Assert.Null(candidate.Title);
        Assert.True(candidate.Installed);
        Assert.Equal(Path.Combine(_root, "steamapps", "common", "Portal 2"), candidate.InstallPath);
    }
}
