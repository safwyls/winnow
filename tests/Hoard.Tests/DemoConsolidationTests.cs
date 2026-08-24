using Hoard.Core.Queries;
using Xunit;

namespace Hoard.Tests;

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

    private static DemoConsolidationEntry Owned(string title, int? year = null, bool provisional = false)
        => new()
        {
            ReleaseId = Interlocked.Increment(ref _nextId),
            Title = title,
            FirstReleaseYear = year,
            NameIsProvisional = provisional,
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
