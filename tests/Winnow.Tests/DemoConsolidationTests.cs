using Winnow.Core.Queries;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The demo classifier as the pure function it is. Like
/// <see cref="SoftMatcherTests"/>, the centre of gravity is the NEGATIVE cases:
/// consolidation hides a row the user owns, so a wrong bind is a lie about
/// their library while a missed one is only a stray tile (§5.3, precision over
/// recall).
/// </summary>
public class DemoConsolidationTests
{
    private static long _nextId;

    private static DemoConsolidationEntry Owned(
        string title, int? year = null, bool provisional = false, string? type = null)
        => new()
        {
            ReleaseId = Interlocked.Increment(ref _nextId),
            Title = title,
            FirstReleaseYear = year,
            NameIsProvisional = provisional,
            SteamAppType = type,
        };

    // ── Marker detection ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Bastion Demo")]
    [InlineData("Magicka Demo")]
    [InlineData("Tales of Arise Demo")]
    [InlineData("Batman: Arkham Asylum Demo")]
    [InlineData("Call of Juarez: Bound in Blood Demo")]
    [InlineData("Cronos: The New Dawn - Demo")]
    [InlineData("Sid Meier's Civilization® V: Demo")]
    [InlineData("Portal (Demo)")]
    [InlineData("DRAGON QUEST BUILDERS 2 JUMBO DEMO")]
    public void Trailing_demo_marker_is_recognised(string title)
        => Assert.True(DemoConsolidation.IsDemoTitle(title));

    /// <summary>
    /// The failure this feature must not have: a real game swallowed because
    /// its name contains the letters "demo". Tokenising before matching is what
    /// makes these safe — none of them yields a bare <c>demo</c> token, and
    /// <c>Demo Disc</c>-style compilations are standalone releases whose name
    /// does not END in the marker.
    /// </summary>
    [Theory]
    [InlineData("Demonologist")]
    [InlineData("Demon's Souls")]
    [InlineData("Demolition Derby")]
    [InlineData("Democracy 3")]
    [InlineData("Demon Turf")]
    [InlineData("Haunted PS1 Demo Disc 2021")]
    [InlineData("Demo Disc: Spectral Mall")]
    [InlineData("Demo")]
    [InlineData("")]
    public void Real_titles_are_not_demos(string title)
        => Assert.False(DemoConsolidation.IsDemoTitle(title));

    // ── Binding ───────────────────────────────────────────────────────────

    [Fact]
    public void Demo_binds_to_the_base_game_when_both_are_owned()
    {
        var full = Owned("Bastion");
        var demo = Owned("Bastion Demo");

        var map = DemoConsolidation.Consolidate([full, demo]);

        Assert.Single(map);
        Assert.Equal(full.ReleaseId, map[demo.ReleaseId]);
    }

    [Fact]
    public void A_solitary_demo_binds_to_nothing()
    {
        var demo = Owned("Hellpoint Demo");
        var unrelated = Owned("Portal 2");

        Assert.Empty(DemoConsolidation.Consolidate([demo, unrelated]));
    }

    /// <summary>
    /// The canonical near-miss. <c>Portal</c> and <c>Portal 2</c> are 0.86
    /// similar on any string metric, which is exactly how a fuzzy matcher talks
    /// itself into a wrong merge. Nothing here is fuzzy: the sequel ordinal
    /// makes the two keys different strings.
    /// </summary>
    [Fact]
    public void Portal_Demo_does_not_bind_to_Portal_2()
    {
        var demo = Owned("Portal Demo");
        var sequel = Owned("Portal 2");

        Assert.Empty(DemoConsolidation.Consolidate([demo, sequel]));
    }

    [Theory]
    // Sequel ordinals, in every notation the normaliser folds.
    [InlineData("Dark Souls Demo", "Dark Souls III")]
    [InlineData("Half-Life 2: Episode One Demo", "Half-Life 2: Episode Two")]
    // A rebuild is a different Release with a different achievement set
    // (§9 pitfall 5): owning the remaster does not supersede the original's demo.
    [InlineData("Bastion Demo", "Bastion Remastered")]
    [InlineData("Skyrim Demo", "Skyrim Special Edition")]
    // …and the reverse: a demo OF the special edition is not answered by the
    // plain game.
    [InlineData("Skyrim Special Edition Demo", "Skyrim")]
    // Different game entirely.
    [InlineData("Magicka Demo", "Magicka: Wizard Wars")]
    public void Mismatched_titles_do_not_bind(string demoTitle, string otherTitle)
    {
        var demo = Owned(demoTitle);
        var other = Owned(otherTitle);

        Assert.Empty(DemoConsolidation.Consolidate([demo, other]));
    }

    /// <summary>
    /// A bundle marker is the same build plus content — owning the GOTY edition
    /// IS owning the game, which is the real shape of the author's library
    /// ("Batman: Arkham Asylum Demo" beside "Batman: Arkham Asylum - Game of
    /// the Year Edition").
    /// </summary>
    [Fact]
    public void A_bundle_edition_still_counts_as_owning_the_full_game()
    {
        var goty = Owned("Batman: Arkham Asylum - Game of the Year Edition");
        var demo = Owned("Batman: Arkham Asylum Demo");

        var map = DemoConsolidation.Consolidate([goty, demo]);

        Assert.Equal(goty.ReleaseId, map[demo.ReleaseId]);
    }

    [Fact]
    public void A_demo_never_supersedes_another_demo()
    {
        var first = Owned("Cloudheim Demo");
        var second = Owned("Cloudheim Demo");

        Assert.Empty(DemoConsolidation.Consolidate([first, second]));
    }

    /// <summary>
    /// Prey (2006) / Prey (2017), §5.3's named example. Two KNOWN years far
    /// apart are evidence of two different games.
    /// </summary>
    [Fact]
    public void Known_years_that_disagree_veto_the_bind()
    {
        var demo = Owned("Prey Demo", year: 2006);
        var remake = Owned("Prey", year: 2017);

        Assert.Empty(DemoConsolidation.Consolidate([demo, remake]));
    }

    [Fact]
    public void A_year_in_the_title_is_read_too()
    {
        var demo = Owned("Prey (2006) Demo");
        var remake = Owned("Prey (2017)");

        Assert.Empty(DemoConsolidation.Consolidate([demo, remake]));
    }

    /// <summary>
    /// The normal case: IGDB carries no Steam demo appids, so a demo work is
    /// never enriched and has no year at all. Absent evidence must not veto —
    /// otherwise the feature would fire on nothing in a real library.
    /// </summary>
    [Fact]
    public void An_unknown_year_does_not_veto()
    {
        var full = Owned("Tales of Arise", year: 2021);
        var demo = Owned("Tales of Arise Demo");

        Assert.Equal(full.ReleaseId, DemoConsolidation.Consolidate([full, demo])[demo.ReleaseId]);
    }

    [Fact]
    public void Years_within_a_year_of_each_other_still_bind()
    {
        var full = Owned("Voxelgram", year: 2019);
        var demo = Owned("Voxelgram Demo", year: 2020);

        Assert.Equal(full.ReleaseId, DemoConsolidation.Consolidate([full, demo])[demo.ReleaseId]);
    }

    /// <summary>
    /// A placeholder name is derived from an appid, so comparing two of them
    /// compares two appids. Excluded from both sides, exactly as the soft-match
    /// sweep excludes them.
    /// </summary>
    [Fact]
    public void Provisional_names_take_no_part()
    {
        var provisionalBase = Owned("App 107100", provisional: true);
        var demo = Owned("App 107100 Demo", provisional: true);
        var realDemo = Owned("Bastion Demo");

        Assert.Empty(DemoConsolidation.Consolidate([provisionalBase, demo, realDemo]));
    }

    [Fact]
    public void Two_demos_of_one_base_both_bind_to_it()
    {
        var full = Owned("Bastion");
        var first = Owned("Bastion Demo");
        var second = Owned("Bastion (Demo)");

        var map = DemoConsolidation.Consolidate([full, first, second]);

        Assert.Equal(2, map.Count);
        Assert.Equal(full.ReleaseId, map[first.ReleaseId]);
        Assert.Equal(full.ReleaseId, map[second.ReleaseId]);
    }

    /// <summary>
    /// Two owned releases normalise alike (the game and its GOTY edition):
    /// either proves the user owns it, so the pick only has to be STABLE.
    /// Lowest release id, regardless of the order rows arrive in.
    /// </summary>
    [Fact]
    public void An_ambiguous_base_is_chosen_deterministically()
    {
        var goty = Owned("Bastion Game of the Year Edition");
        var plain = Owned("Bastion");
        var demo = Owned("Bastion Demo");
        var expected = Math.Min(goty.ReleaseId, plain.ReleaseId);

        Assert.Equal(expected, DemoConsolidation.Consolidate([goty, plain, demo])[demo.ReleaseId]);
        Assert.Equal(expected, DemoConsolidation.Consolidate([demo, plain, goty])[demo.ReleaseId]);
    }

    // ── Betas, playtests and the rest of the handouts ─────────────────────────

    /// <summary>
    /// The entry that prompted this. Monster Hunter Wilds is owned; the beta
    /// test is a separate appid that answers <c>_missing_token</c> and has no
    /// type at all, so the title gate is the whole of gate one.
    /// </summary>
    [Fact]
    public void A_beta_test_binds_to_the_owned_base_game()
    {
        var full = Owned("Monster Hunter Wilds");
        var beta = Owned("Monster Hunter Wilds Beta test");

        Assert.Equal(full.ReleaseId, DemoConsolidation.Consolidate([full, beta])[beta.ReleaseId]);
    }

    /// <summary>
    /// The other entry the user named — and the one that must stay visible. Only
    /// the playtest is in the library; the game itself is not. Consolidation
    /// hides a handout because the GAME is there to show instead, so with no
    /// base there is nothing to hide behind and the rule is unchanged.
    /// </summary>
    [Fact]
    public void A_solitary_playtest_stays_visible()
    {
        var playtest = Owned("BitCraft Online Playtest");
        var unrelated = Owned("Portal 2");

        Assert.Empty(DemoConsolidation.Consolidate([playtest, unrelated]));
    }

    [Theory]
    // Read off real Steam entries, one per marker shape.
    [InlineData("Monster Hunter Wilds Beta test", "Monster Hunter Wilds")]
    [InlineData("BitCraft Online Playtest", "BitCraft Online")]
    [InlineData("Deep Rock Galactic Open Beta", "Deep Rock Galactic")]
    [InlineData("Foxhole Closed Beta", "Foxhole")]
    [InlineData("Gatewalkers (Alpha)", "Gatewalkers")]
    [InlineData("New World Public Test Realm", "New World")]
    [InlineData("Dune Awakening Test Server", "Dune Awakening")]
    [InlineData("Monster Hunter Wilds Network Test", "Monster Hunter Wilds")]
    [InlineData("Wild Terra 2 Free Weekend", "Wild Terra 2")]
    [InlineData("Final Fantasy XIV Online Free Trial", "Final Fantasy XIV Online")]
    public void Prerelease_markers_bind_to_their_owned_base(string variantTitle, string baseTitle)
    {
        var full = Owned(baseTitle);
        var variant = Owned(variantTitle);

        var map = DemoConsolidation.Consolidate([full, variant]);

        Assert.Equal(full.ReleaseId, map[variant.ReleaseId]);
    }

    /// <summary>
    /// The longest marker run has to win, or the residue keeps a marker word and
    /// binds to nothing: "New World Public Test Realm" would go looking for a
    /// base game called "New World Public".
    /// </summary>
    [Fact]
    public void The_longest_marker_run_wins()
    {
        var full = Owned("New World");
        var ptr = Owned("New World Public Test Realm");
        var wrong = Owned("New World Public");

        var map = DemoConsolidation.Consolidate([full, ptr, wrong]);

        Assert.Equal(full.ReleaseId, map[ptr.ReleaseId]);
    }

    /// <summary>
    /// Real games whose titles brush against the new markers. Bare
    /// <c>test</c>, <c>trial</c> and <c>prologue</c> are deliberately NOT
    /// markers for exactly this reason, and a leading marker word is a name.
    /// </summary>
    [Theory]
    [InlineData("The Turing Test")]
    [InlineData("Beta Decay")]
    [InlineData("Alpha Protocol")]
    [InlineData("Trials Rising")]
    [InlineData("Chernobylite: Prologue")]
    [InlineData("Playtest")]
    [InlineData("Beta")]
    [InlineData("Test Drive Unlimited")]
    public void Real_titles_are_not_variants(string title)
        => Assert.False(DemoConsolidation.IsVariantTitle(title));

    /// <summary>
    /// A game with a marker-shaped name is still only hidden if the library also
    /// holds the exact base — which it does not here, so nothing moves. Both
    /// gates, always.
    /// </summary>
    [Fact]
    public void A_marker_shaped_title_with_no_owned_base_is_untouched()
    {
        var turing = Owned("The Turing Test");
        var other = Owned("Portal 2");

        Assert.Empty(DemoConsolidation.Consolidate([turing, other]));
    }

    [Fact]
    public void A_variant_never_supersedes_another_variant()
    {
        var beta = Owned("Cloudheim Beta");
        var demo = Owned("Cloudheim Demo");

        // Neither is a base for the other: a beta is not the game.
        Assert.Empty(DemoConsolidation.Consolidate([beta, demo]));
    }

    // ── common.type as gate one ──────────────────────────────────────────────

    /// <summary>
    /// Valve's classification catches what the title cannot. "FINAL FANTASY XIV
    /// Online Free Trial" is typed <c>Demo</c> live; so is "Wild Terra 2: New
    /// Lands - Free Weekend". A demo whose name is simply the game's name has no
    /// marker at all, and only the type can see it.
    /// </summary>
    [Fact]
    public void A_type_of_demo_consolidates_even_with_no_marker_in_the_title()
    {
        var full = Owned("Enshrouded");
        var demo = Owned("Enshrouded", type: "Demo");

        Assert.Equal(full.ReleaseId, DemoConsolidation.Consolidate([full, demo])[demo.ReleaseId]);
    }

    /// <summary>
    /// …and it is still only half the decision. A typed demo with no owned base
    /// stays visible like any other.
    /// </summary>
    [Fact]
    public void A_solitary_typed_demo_stays_visible()
    {
        var demo = Owned("Everwind Demo", type: "Demo");
        var unrelated = Owned("Portal 2");

        Assert.Empty(DemoConsolidation.Consolidate([demo, unrelated]));
    }

    /// <summary>
    /// The service's casing is not stable — Bastion answers <c>game</c>, Monster
    /// Hunter Wilds answers <c>Game</c> — so every comparison is
    /// case-insensitive. Reading these as unknown values would silently disable
    /// the type signal for whichever half of the library got the other casing.
    /// </summary>
    [Theory]
    [InlineData("Demo")]
    [InlineData("demo")]
    [InlineData("DEMO")]
    [InlineData("  Demo  ")]
    public void The_type_comparison_ignores_case_and_padding(string type)
    {
        var full = Owned("Enshrouded");
        var demo = Owned("Enshrouded", type: type);

        Assert.Single(DemoConsolidation.Consolidate([full, demo]));
    }

    /// <summary>
    /// The disagreement, resolved in favour of the type. Valve types demos
    /// <c>Demo</c>; an appid it calls <c>Game</c> whose title happens to end in
    /// the word is a real game with an awkward name, and hiding it is the exact
    /// failure §5.3 forbids.
    /// </summary>
    [Fact]
    public void A_game_type_overrules_a_demo_token_in_the_title()
    {
        var full = Owned("Cloudheim");
        var lookalike = Owned("Cloudheim Demo", type: "Game");

        Assert.Empty(DemoConsolidation.Consolidate([full, lookalike]));
    }

    /// <summary>
    /// But <c>Game</c> is NOT a denial for a beta, because Valve has no beta or
    /// playtest type to have used instead. Verified live: "Call of Duty: WWII -
    /// PC Open Beta", "PUBG: Test Server", "New World Public Test Realm" and
    /// "Gatewalkers (Alpha)" are all typed <c>Game</c>. Letting that veto the
    /// title would switch this feature off for every beta in the library while
    /// looking like it worked.
    /// </summary>
    [Theory]
    [InlineData("Foxhole Open Beta")]
    [InlineData("Foxhole Playtest")]
    [InlineData("Foxhole Public Test Realm")]
    public void A_game_type_does_not_overrule_a_beta_marker(string variantTitle)
    {
        var full = Owned("Foxhole");
        var beta = Owned(variantTitle, type: "Game");

        Assert.Equal(full.ReleaseId, DemoConsolidation.Consolidate([full, beta])[beta.ReleaseId]);
    }

    /// <summary>
    /// The unreadable appids — <c>_missing_token</c>, no <c>common</c> block, no
    /// type — are the majority of what the user actually wants consolidated, so
    /// the title fallback is load-bearing rather than vestigial.
    /// </summary>
    [Fact]
    public void An_unknown_type_falls_back_to_the_title_gate()
    {
        var full = Owned("Monster Hunter Wilds");
        var beta = Owned("Monster Hunter Wilds Beta test", type: null);
        var demoBase = Owned("Bastion");
        var demo = Owned("Bastion Demo", type: null);

        var map = DemoConsolidation.Consolidate([full, beta, demoBase, demo]);

        Assert.Equal(2, map.Count);
        Assert.Equal(full.ReleaseId, map[beta.ReleaseId]);
        Assert.Equal(demoBase.ReleaseId, map[demo.ReleaseId]);
    }

    /// <summary>
    /// Tools, applications, config depots and soundtracks are not variants of a
    /// game the user owns, so they are never folded into one. Whether they
    /// should be visible AT ALL is a different product decision — a "non-game
    /// entries" filter — and this class does not make it unilaterally.
    /// </summary>
    [Theory]
    [InlineData("Tool")]
    [InlineData("Application")]
    [InlineData("Config")]
    [InlineData("Music")]
    [InlineData("DLC")]
    public void A_non_game_type_is_never_consolidated(string type)
    {
        var full = Owned("Eco");

        // Deliberately named so the title gate WOULD have bound it: only the
        // type keeps it visible.
        var tool = Owned("Eco Demo", type: type);

        Assert.Empty(DemoConsolidation.Consolidate([full, tool]));
    }

    /// <summary>
    /// A real game is never suppressed, whatever its type says, because gate two
    /// still has to find an owned base with exactly its key.
    /// </summary>
    [Fact]
    public void A_real_game_is_never_suppressed()
    {
        DemoConsolidationEntry[] library =
        [
            Owned("Demonologist", type: "Game"),
            Owned("Demon's Souls", type: "Game"),
            Owned("The Turing Test", type: "Game"),
            Owned("Alpha Protocol", type: "Game"),
            Owned("Portal 2", type: "Game"),
            Owned("Portal Demo", type: "Demo"),
        ];

        // Portal Demo is typed Demo and is still visible: the library holds
        // Portal 2, not Portal.
        Assert.Empty(DemoConsolidation.Consolidate(library));
    }

    /// <summary>
    /// Nothing is stored, so "run it again" is the same pure call. This is the
    /// whole idempotence argument for repeated syncs.
    /// </summary>
    [Fact]
    public void Repeated_passes_over_the_same_library_agree()
    {
        DemoConsolidationEntry[] library =
        [
            Owned("Bastion"),
            Owned("Bastion Demo"),
            Owned("Hellpoint Demo"),
            Owned("Portal 2"),
        ];

        var first = DemoConsolidation.Consolidate(library);
        var second = DemoConsolidation.Consolidate(library);

        Assert.Single(first);
        Assert.Equal(first.Count, second.Count);
        foreach (var (demoId, baseId) in first)
        {
            Assert.Equal(baseId, second[demoId]);
        }
    }
}
