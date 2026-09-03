using Winnow.Core.Identity;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The expansion detector, which exists because the soft
/// matcher cannot do this job. The matcher scores title DISTANCE, and
/// "Civilization IV" is a long way from "Civilization IV: Beyond the Sword",
/// so no threshold on it ever proposes the pair the user asked about.
///
/// <para>Most of these tests are about a proposal NOT being made. A false
/// positive here groups two unrelated games under one title, so every guard
/// is posed as its own case with the reason it refuses named, rather than as
/// a score that happened to fall below a line.</para>
/// </summary>
public sealed class ExpansionDetectorTests
{
    private static ExpansionSubject Subject(
        long workId, string title, int? year = 2005, string? publisher = "2K Games")
        => new()
        {
            WorkId = workId,
            Title = title,
            ReleaseYear = year,
            Publisher = publisher,
        };

    // ── What it must find ───────────────────────────────────────────────────

    [Fact]
    public void The_case_the_user_asked_about_is_proposed()
    {
        var civ = Subject(1, "Sid Meier's Civilization IV");
        var bts = Subject(2, "Sid Meier's Civilization IV: Beyond the Sword", year: 2007);

        Assert.True(ExpansionDetector.TryPropose(civ, bts, null, out var proposal, out var reason));
        Assert.Equal(ExpansionRefusalReason.None, reason);
        Assert.NotNull(proposal);
        Assert.Equal(1, proposal.BaseWorkId);
        Assert.Equal(2, proposal.ChildWorkId);
        Assert.Equal("beyond sword", proposal.Evidence.Suffix);
        Assert.Equal(2, proposal.Evidence.YearDelta);
        Assert.True(proposal.Evidence.PublisherAgrees);
        Assert.True(proposal.Evidence.HasSeparatorBoundary);
    }

    /// <summary>
    /// The whole point of the feature: six packs proposed under one base, as
    /// one card, rather than six pairwise questions each invalidating the
    /// next.
    /// </summary>
    [Fact]
    public void A_base_game_and_six_expansions_arrive_as_one_group()
    {
        var subjects = new List<ExpansionSubject>
        {
            Subject(1, "Sid Meier's Civilization IV"),
            Subject(2, "Sid Meier's Civilization IV: Warlords", year: 2006),
            Subject(3, "Sid Meier's Civilization IV: Beyond the Sword", year: 2007),
            Subject(4, "Sid Meier's Civilization IV: Colonization", year: 2008),
            Subject(5, "The Elder Scrolls V: Skyrim", year: 2011, publisher: "Bethesda"),
            Subject(6, "Hades", year: 2020, publisher: "Supergiant"),
        };

        var proposals = ExpansionDetector.Detect(subjects);

        Assert.Equal(3, proposals.Count);
        Assert.All(proposals, p => Assert.Equal(1, p.BaseWorkId));
        Assert.Equal([2, 3, 4], proposals.Select(p => p.ChildWorkId));
    }

    /// <summary>
    /// A pack joined by a dash rather than a colon, where the base's own title
    /// contains a hyphen. Every separator position is tried, not just the
    /// first, or the hyphen inside "Half-Life 2" would decide the question and
    /// the pair would lose its strongest corroboration.
    /// </summary>
    [Fact]
    public void A_hyphen_inside_the_base_title_does_not_hide_the_separator()
    {
        var half = Subject(1, "Half-Life 2", year: 2004, publisher: "Valve");
        var episode = Subject(2, "Half-Life 2 - Episode One", year: 2006, publisher: "Valve");

        Assert.True(ExpansionDetector.TryPropose(half, episode, null, out var proposal, out _));
        Assert.True(proposal!.Evidence.HasSeparatorBoundary);
    }

    /// <summary>
    /// With both "Civilization" and "Civilization IV" owned, a Civilization IV
    /// pack files under the nearer of the two. A child takes at most one base,
    /// so the queue never asks the same question twice with different answers.
    /// </summary>
    [Fact]
    public void A_pack_takes_the_longest_matching_base()
    {
        var proposals = ExpansionDetector.Detect(
        [
            Subject(1, "Sid Meier's Civilization", year: 1991),
            Subject(2, "Sid Meier's Civilization IV", year: 2005),
            Subject(3, "Sid Meier's Civilization IV: Warlords", year: 2006),
        ]);

        var pack = Assert.Single(proposals, p => p.ChildWorkId == 3);
        Assert.Equal(2, pack.BaseWorkId);
    }

    // ── What it must refuse ─────────────────────────────────────────────────

    /// <summary>
    /// The single most dangerous false positive available to a prefix rule:
    /// "Portal" prefixes "Portal 2" exactly, and they are two games. A suffix
    /// that opens on a number names a different numbered entry in a series.
    /// </summary>
    [Fact]
    public void A_numbered_sequel_is_not_an_expansion()
    {
        var portal = Subject(1, "Portal", year: 2007, publisher: "Valve");
        var portal2 = Subject(2, "Portal 2", year: 2011, publisher: "Valve");

        Assert.False(ExpansionDetector.TryPropose(portal, portal2, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.SequelOrdinal, reason);
    }

    /// <summary>
    /// The same guard with a subtitle behind the number, which is the shape
    /// that would otherwise slip through: the suffix is not ONLY a number, but
    /// it still opens on one.
    /// </summary>
    [Fact]
    public void A_numbered_sequel_with_a_subtitle_is_not_an_expansion()
    {
        var witcher = Subject(1, "The Witcher", year: 2007, publisher: "CD Projekt");
        var wild = Subject(2, "The Witcher 3: Wild Hunt", year: 2015, publisher: "CD Projekt");

        Assert.False(ExpansionDetector.TryPropose(witcher, wild, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.SequelOrdinal, reason);
    }

    /// <summary>
    /// Two unrelated games where one title opens with the other. Nothing but
    /// the prefix agrees, so nothing is proposed — the corroboration rule is
    /// what stops the detector turning a coincidence into a grouping.
    /// </summary>
    [Fact]
    public void Two_unrelated_games_sharing_a_first_word_are_not_proposed()
    {
        // Both years KNOWN, which is the shape production actually has;
        // 947 of the author's 1,033 works carry a first_release_year. This
        // test used to pass year: null on both sides, the one shape where
        // the old guard fired, so the suite reported a guard production did
        // not have while "INSIDE" and "Inside the Backrooms" were being
        // proposed to a person. Two known years are not evidence of
        // anything; see ExpansionMetadataGuardTests for the rule that
        // replaced it.
        var rush = Subject(1, "Rush", year: 2010, publisher: null);
        var bros = Subject(2, "Rush Bros", year: 2013, publisher: null);

        Assert.False(ExpansionDetector.TryPropose(rush, bros, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.NoCorroboration, reason);
    }

    /// <summary>
    /// Different publishers is a hard veto, however well the titles line up.
    /// One studio's game is not another studio's expansion.
    /// </summary>
    [Fact]
    public void A_publisher_mismatch_vetoes()
    {
        var one = Subject(1, "Bridge Constructor", year: 2013, publisher: "Headup");
        var two = Subject(2, "Bridge Constructor: Portal", year: 2017, publisher: "Valve");

        Assert.False(ExpansionDetector.TryPropose(one, two, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.PublisherMismatch, reason);
    }

    /// <summary>
    /// A remaster is a separate BUILD of the same game, not an extension of it
    /// (§9 pitfall 5). It is refused twice over: the edition markers disagree,
    /// and the normaliser lifts them out of the core, so most such titles are
    /// not even a strict prefix extension.
    /// </summary>
    [Fact]
    public void A_rebuild_edition_is_not_an_expansion()
    {
        var skyrim = Subject(1, "Skyrim", year: 2011, publisher: "Bethesda");
        var special = Subject(
            2, "Skyrim Dragonborn Special Edition", year: 2016, publisher: "Bethesda");

        Assert.False(ExpansionDetector.TryPropose(skyrim, special, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.RebuildEdition, reason);
    }

    /// <summary>
    /// An edition bundle adds words to a title and adds no product. Both sides
    /// normalise to the same tokens, so there is no extension to propose and
    /// the pair never reaches a guard.
    /// </summary>
    [Fact]
    public void An_edition_bundle_is_not_an_extension_of_its_own_base()
    {
        var civ = Subject(1, "Sid Meier's Civilization IV");
        var complete = Subject(2, "Sid Meier's Civilization IV: Complete Edition", year: 2009);

        Assert.False(ExpansionDetector.TryPropose(civ, complete, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.NotAPrefix, reason);
    }

    /// <summary>An expansion does not ship before the thing it expands.</summary>
    [Fact]
    public void A_child_that_predates_its_base_is_refused()
    {
        var baseGame = Subject(1, "Fixture Game", year: 2015);
        var child = Subject(2, "Fixture Game: Prequel Pack", year: 2009);

        Assert.False(ExpansionDetector.TryPropose(baseGame, child, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.ChildPredatesBase, reason);
    }

    /// <summary>
    /// One year of slack, because the two rows are enriched from different
    /// sources and a regional date can disagree by a year.
    /// </summary>
    [Fact]
    public void A_year_of_slack_is_allowed_in_the_wrong_direction()
    {
        var baseGame = Subject(1, "Fixture Game", year: 2015);
        var child = Subject(2, "Fixture Game: Pack", year: 2014);

        Assert.True(ExpansionDetector.TryPropose(baseGame, child, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.None, reason);
    }

    /// <summary>A pack two decades after its base is a coincidence, not a pack.</summary>
    [Fact]
    public void A_year_gap_wider_than_the_ceiling_is_refused()
    {
        var baseGame = Subject(1, "Fixture Game", year: 1998);
        var child = Subject(2, "Fixture Game: Pack", year: 2024);

        Assert.False(ExpansionDetector.TryPropose(baseGame, child, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.YearGapTooWide, reason);
    }

    /// <summary>
    /// A two-letter title prefixes half a library. The floor is on the
    /// normalised core, so it is a floor on what was actually compared.
    /// </summary>
    [Fact]
    public void A_base_title_too_short_to_discriminate_is_refused()
    {
        var tiny = Subject(1, "Go", year: 2010);
        var other = Subject(2, "Go: Home Pack", year: 2012);

        Assert.False(ExpansionDetector.TryPropose(tiny, other, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.BaseTooShort, reason);
    }

    /// <summary>The relation is directional: a base is not an expansion of its own pack.</summary>
    [Fact]
    public void The_relation_is_directional()
    {
        var civ = Subject(1, "Sid Meier's Civilization IV");
        var bts = Subject(2, "Sid Meier's Civilization IV: Beyond the Sword", year: 2007);

        Assert.True(ExpansionDetector.TryPropose(civ, bts, null, out _, out _));
        Assert.False(ExpansionDetector.TryPropose(bts, civ, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.NotAPrefix, reason);
    }

    /// <summary>
    /// With no publisher and no year on either side, the separator in the
    /// child's own title is the corroboration that needs no enrichment. This
    /// is what makes the feature work on a library the metadata backfill has
    /// not reached.
    /// </summary>
    [Fact]
    public void A_separator_alone_corroborates_when_nothing_is_enriched()
    {
        var baseGame = Subject(1, "Fixture Game", year: null, publisher: null);
        var child = Subject(2, "Fixture Game: The Pack", year: null, publisher: null);

        Assert.True(ExpansionDetector.TryPropose(baseGame, child, null, out var proposal, out _));
        Assert.True(proposal!.Evidence.HasSeparatorBoundary);
        Assert.Null(proposal.Evidence.PublisherAgrees);
        Assert.Null(proposal.Evidence.YearDelta);
    }

    /// <summary>Nothing is proposed about a work and itself.</summary>
    [Fact]
    public void A_work_is_not_its_own_expansion()
    {
        var one = Subject(1, "Sid Meier's Civilization IV");
        Assert.False(ExpansionDetector.TryPropose(one, one, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.SameWork, reason);
    }

    /// <summary>The order the library is read in cannot change what is proposed.</summary>
    [Fact]
    public void The_order_of_the_subjects_does_not_change_the_result()
    {
        var subjects = new List<ExpansionSubject>
        {
            Subject(1, "Sid Meier's Civilization IV"),
            Subject(2, "Sid Meier's Civilization IV: Warlords", year: 2006),
            Subject(3, "Sid Meier's Civilization IV: Beyond the Sword", year: 2007),
        };

        var forward = ExpansionDetector.Detect(subjects);
        var backward = ExpansionDetector.Detect(Enumerable.Reverse(subjects).ToList());

        Assert.Equal(
            forward.Select(p => (p.BaseWorkId, p.ChildWorkId)),
            backward.Select(p => (p.BaseWorkId, p.ChildWorkId)));
    }
}
