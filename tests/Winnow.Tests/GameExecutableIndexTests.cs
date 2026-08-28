using Winnow.Monitor;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The §5.2 executable→release map: what gets into the Tier 1 name set, and how
/// a resolved path is attributed back to an ownership.
///
/// <para>Every layout exercised here was observed in the developer's own
/// library, not invented — the Unreal shim-plus-shipping-binary pair from
/// <c>Palworld</c>, the six copies of <c>CrashReportClient.exe</c> scattered
/// through <c>steamapps/common</c>, GOG's <c>unins000.exe</c> beside
/// <c>UnityCrashHandler64.exe</c>, and Epic's <c>Prerequisites</c> folder full
/// of redistributable installers.</para>
/// </summary>
public sealed class GameExecutableIndexTests
{
    [Fact]
    public async Task Only_installed_games_reach_the_tier_1_name_set()
    {
        using var harness = new SessionWatcherHarness();
        await harness.AddGameAsync("Enshrouded", "enshrouded.exe");
        await harness.AddGameAsync("Sea of Stars", installed: false, "SeaOfStars.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Contains("enshrouded", index.ProcessNames);

        // An uninstalled game's install_path can be a stale directory that now
        // belongs to whatever was installed there next; indexing it would
        // attribute a stranger's runtime to a game the user removed.
        Assert.DoesNotContain("SeaOfStars", index.ProcessNames);
    }

    [Fact]
    public async Task Crash_reporters_and_prerequisite_installers_are_not_indexed()
    {
        using var harness = new SessionWatcherHarness();

        // Palworld's real layout, plus GOG's and Epic's usual companions.
        var game = await harness.AddGameAsync(
            "Palworld",
            "Palworld.exe",
            "Pal/Binaries/Win64/Palworld-Win64-Shipping.exe",
            "Engine/Binaries/Win64/CrashReportClient.exe",
            "Engine/Binaries/Win64/EpicWebHelper.exe",
            "unins000.exe",
            "UnityCrashHandler64.exe",
            "crashpad_handler.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Contains("Palworld", index.ProcessNames);
        Assert.Contains("Palworld-Win64-Shipping", index.ProcessNames);

        // The important one: Unreal starts CrashReportClient from inside the
        // game's own directory *after* a crash, so an indexed crash reporter
        // would be read as the game relaunching and would extend the session it
        // just ended.
        Assert.DoesNotContain("CrashReportClient", index.ProcessNames);
        Assert.DoesNotContain("EpicWebHelper", index.ProcessNames);
        Assert.DoesNotContain("unins000", index.ProcessNames);
        Assert.DoesNotContain("UnityCrashHandler64", index.ProcessNames);
        Assert.DoesNotContain("crashpad_handler", index.ProcessNames);

        Assert.Equal(2, index.ExecutableCount);
        Assert.Equal(game.OwnershipId, index.Match(game.Exe("Palworld-Win64-Shipping.exe"), "Palworld-Win64-Shipping"));
    }

    [Fact]
    public async Task Redistributable_subtrees_are_pruned_before_they_are_walked()
    {
        using var harness = new SessionWatcherHarness();
        await harness.AddGameAsync(
            "Fez",
            "FEZ.exe",
            // Epic's real layout. Both of these are also on the name deny-list,
            // but the directory prune is what stops the walk descending into
            // trees like this at all.
            "Prerequisites/dotNetFx40_Full_x86_x64.exe",
            "_CommonRedist/DirectX/June2010/DXSETUP.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Equal(1, index.ExecutableCount);
        Assert.Equal(["FEZ"], index.ProcessNames);
    }

    [Fact]
    public async Task The_scan_stops_at_the_depth_limit()
    {
        using var harness = new SessionWatcherHarness(o => o.ExecutableScanDepth = 2);
        await harness.AddGameAsync(
            "Deep",
            "shallow.exe",
            "a/b/inrange.exe",
            "a/b/c/toodeep.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Contains("shallow", index.ProcessNames);
        Assert.Contains("inrange", index.ProcessNames);
        Assert.DoesNotContain("toodeep", index.ProcessNames);
    }

    [Fact]
    public async Task Unreal_shipping_binaries_are_inside_the_default_depth()
    {
        using var harness = new SessionWatcherHarness();
        await harness.AddGameAsync(
            "RSDragonwilds",
            "RSDragonwilds.exe",
            "Rust/Binaries/Win64/RSDragonwilds-Win64-Shipping.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Contains("RSDragonwilds-Win64-Shipping", index.ProcessNames);
    }

    [Fact]
    public async Task A_path_outside_every_install_directory_never_matches()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Enshrouded", "enshrouded.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Equal(game.OwnershipId, index.Match(game.Exe("enshrouded.exe"), "enshrouded"));

        // The user's own build, or an unrelated tool that happens to share a
        // name with something under a game folder. A resolvable path that lies
        // outside every install root is a definite no — it must not fall through
        // to the weaker name rule.
        Assert.Null(index.Match(harness.ElsewherePath("enshrouded.exe"), "enshrouded"));
    }

    [Fact]
    public async Task An_unresolvable_path_matches_only_when_the_name_is_unambiguous()
    {
        using var harness = new SessionWatcherHarness();
        var solo = await harness.AddGameAsync("Solo", "Solo.exe");
        await harness.AddGameAsync("GameOne", "Launcher.exe");
        await harness.AddGameAsync("GameTwo", "Launcher.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        // An anti-cheat-protected or elevated process yields no path at all
        // (ITrackedProcess.ExecutablePath). A unique name is still evidence.
        Assert.Equal(solo.OwnershipId, index.Match(null, "Solo"));

        // Two games both ship a Launcher.exe. Guessing would attribute a real
        // session to the wrong game, which is indistinguishable from data once
        // it is written. Refusing costs one session and nothing else.
        Assert.Null(index.Match(null, "Launcher"));
    }

    [Fact]
    public async Task A_game_installed_inside_another_attributes_to_the_innermost()
    {
        using var harness = new SessionWatcherHarness();
        var outer = await harness.AddGameAsync("Outer", "outer.exe");

        // Galaxy can be pointed at a root that contains other installs, and a
        // Steam library folder can legitimately sit inside one. Longest prefix
        // wins, so the inner game keeps its own executables.
        var innerPath = Path.Combine(outer.InstallPath, "Inner");
        Directory.CreateDirectory(innerPath);
        var innerExe = Path.Combine(innerPath, "inner.exe");
        File.WriteAllBytes(innerExe, []);

        var index = new GameExecutableIndex(
            [new GameExecutable(innerExe, 2), new GameExecutable(outer.Exe("outer.exe"), outer.OwnershipId)],
            [(outer.InstallPath, outer.OwnershipId), (innerPath, 2)]);

        Assert.Equal(2, index.Match(innerExe, "inner"));
        Assert.Equal(outer.OwnershipId, index.Match(outer.Exe("outer.exe"), "outer"));
    }

    [Fact]
    public async Task An_install_directory_prefix_only_matches_on_a_separator_boundary()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Portal", "portal.exe");

        var index = await harness.IndexBuilder.BuildAsync();

        // "…/Portal 2/portal.exe" starts with "…/Portal" as a string but is a
        // different directory. A naive StartsWith would hand Portal 2's
        // sessions to Portal.
        var neighbour = game.InstallPath + " 2" + Path.DirectorySeparatorChar + "portal.exe";
        Assert.Null(index.Match(neighbour, "portal"));
    }

    [Fact]
    public async Task A_game_uninstalled_between_rebuilds_leaves_the_index()
    {
        using var harness = new SessionWatcherHarness();
        var game = await harness.AddGameAsync("Gone", "gone.exe");

        Assert.Contains("gone", (await harness.IndexBuilder.BuildAsync()).ProcessNames);

        Directory.Delete(game.InstallPath, recursive: true);

        var rebuilt = await harness.IndexBuilder.BuildAsync();
        Assert.DoesNotContain("gone", rebuilt.ProcessNames);
        Assert.Equal(0, rebuilt.InstallRootCount);
    }

    [Fact]
    public async Task A_library_with_nothing_installed_yields_an_index_that_matches_nothing()
    {
        using var harness = new SessionWatcherHarness();

        var index = await harness.IndexBuilder.BuildAsync();

        Assert.Empty(index.ProcessNames);
        Assert.Null(index.Match(@"C:\anything\at\all.exe", "all"));
    }
}
