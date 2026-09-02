using Winnow.Core.Identity;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The four mechanisms by which the title-prefix detector
/// produced 21 wrong proposals out of 31 on a real 1,033-work library, each
/// refused here by the example the diagnosis names, plus the rule that replaced
/// the detector's job: it is a GAP-FILLER now, and proposes only where every
/// storefront is silent.
///
/// <para>Every subject in this file is built from facts the storefronts really
/// returned for these titles. No network, no database.</para>
/// </summary>
public sealed class ExpansionMetadataGuardTests
{
    private static ExpansionSubject Subject(
        long workId,
        string title,
        int? year = 2005,
        string? publisher = "2K Games",
        StorefrontFacts? facts = null,
        long? claimedParent = null)
    {
        var claim = StorefrontRelation.Read(facts);
        return new ExpansionSubject
        {
            WorkId = workId,
            Title = title,
            ReleaseYear = year,
            Publisher = publisher,
            Claim = claim,
            ClaimedParentWorkId = claim is null ? null : claimedParent,
        };
    }

    private static StorefrontFacts MainGame => new() { IgdbGameType = "main_game" };

    // ── Mechanism one: the word-suffixed sequel ─────────────────────────────

    /// <summary>
    /// A guard that tests whether the suffix opens on a NUMBER cannot refuse a
    /// sequel whose suffix is a WORD. The ordinal guard is not broken and roman
    /// numerals are not the leak: "DOOM Eternal" simply cannot be seen by it.
    /// IGDB types DOOM Eternal a main game with no parent, and that one fact
    /// refutes the pair outright.
    /// </summary>
    [Theory]
    [InlineData("DOOM", "DOOM Eternal")]
    [InlineData("BioShock", "BioShock Infinite")]
    [InlineData("Magicka", "Magicka: Wizard Wars")]
    [InlineData("Duke Nukem", "Duke Nukem: Manhattan Project")]
    [InlineData("Liftoff", "Liftoff: Micro Drones")]
    [InlineData("Worlds Adrift", "Worlds Adrift Island Creator")]
    [InlineData("Sid Meier's Ace Patrol", "Sid Meier's Ace Patrol: Pacific Skies")]
    [InlineData("Borderlands", "Borderlands: The Pre-Sequel")]
    public void A_word_suffixed_sequel_is_refused_by_the_storefront(string baseTitle, string childTitle)
    {
        var baseGame = Subject(1, baseTitle, publisher: null);
        var child = Subject(2, childTitle, year: 2013, publisher: null, facts: MainGame);

        Assert.False(ExpansionDetector.TryPropose(baseGame, child, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.MetadataContradicts, reason);
    }

    /// <summary>
    /// The same shape with nothing behind it. Without the storefront these pairs
    /// reach the title guards, which is precisely why the heuristic on its own
    /// could not be trusted with them — and why it now proposes only where
    /// every source is silent.
    /// </summary>
    [Fact]
    public void Without_a_storefront_the_ordinal_guard_still_cannot_see_a_word_suffix()
    {
        var doom = Subject(1, "DOOM", year: 2016, publisher: "Bethesda");
        var eternal = Subject(2, "DOOM Eternal", year: 2020, publisher: "Bethesda");

        // Proposed, wrongly, on titles alone. Stated as a test rather than left
        // implicit: this is the limit the metadata exists to cover.
        Assert.True(ExpansionDetector.TryPropose(doom, eternal, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.None, reason);
    }

    // ── Mechanism two: the corroboration guard that was a no-op ─────────────

    /// <summary>
    /// The pair the old guard let through. Two completely unrelated games where
    /// one title opens with the other, refused now with no storefront involved
    /// at all — because two known years are not evidence of anything, and the
    /// old rule was satisfied by exactly that.
    /// </summary>
    [Fact]
    public void Two_known_years_no_longer_corroborate_a_shared_first_word()
    {
        var inside = Subject(1, "INSIDE", year: 2016, publisher: null);
        var backrooms = Subject(2, "Inside the Backrooms", year: 2022, publisher: null);

        Assert.False(ExpansionDetector.TryPropose(inside, backrooms, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.NoCorroboration, reason);
    }

    /// <summary>
    /// The guard fires on the shape production actually has: both years known.
    /// 947 of the author's 1,033 works carry a first_release_year, so a guard
    /// that only fired on an unknown year fired for 8.3% of pairs.
    /// </summary>
    [Fact]
    public void The_corroboration_guard_fires_on_the_shape_the_library_actually_has()
    {
        var rush = Subject(1, "Rush", year: 2010, publisher: null);
        var bros = Subject(2, "Rush Bros", year: 2013, publisher: null);

        Assert.False(ExpansionDetector.TryPropose(rush, bros, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.NoCorroboration, reason);
    }

    /// <summary>
    /// A publisher on its own is not corroboration either, and neither is a
    /// year pair. Together, with the child not shipping first, they are.
    /// </summary>
    [Fact]
    public void A_publisher_alone_does_not_corroborate_but_a_publisher_and_a_year_gap_do()
    {
        var withoutYears = ExpansionDetector.TryPropose(
            Subject(1, "Bridge Constructor", year: null, publisher: "Headup"),
            Subject(2, "Bridge Constructor Stunts", year: null, publisher: "Headup"),
            null,
            out _,
            out var lonely);

        Assert.False(withoutYears);
        Assert.Equal(ExpansionRefusalReason.NoCorroboration, lonely);

        var withYears = ExpansionDetector.TryPropose(
            Subject(1, "Bridge Constructor", year: 2013, publisher: "Headup"),
            Subject(2, "Bridge Constructor Stunts", year: 2015, publisher: "Headup"),
            null,
            out _,
            out var supported);

        Assert.True(withYears);
        Assert.Equal(ExpansionRefusalReason.None, supported);
    }

    // ── Mechanism three: the rebuild guard bypassed by "edition" ────────────

    /// <summary>
    /// TitleNormalizer lifts a trailing bare "edition" as a BUNDLE marker
    /// rather than a REBUILD marker, so the rebuild guard never sees these and
    /// the pair proposes as an expansion. IGDB types the first a Remaster and
    /// the second a Port, and both name a version_parent — so the storefront
    /// makes the proposal, with the true word on it, and the heuristic stands
    /// down.
    /// </summary>
    [Theory]
    [InlineData("The Outer Worlds", "The Outer Worlds: Spacer's Choice Edition", "remaster", RelationLabels.Remaster)]
    [InlineData("Hellblade: Senua's Sacrifice", "Hellblade: Senua's Sacrifice VR Edition", "port", RelationLabels.Port)]
    public void A_generic_edition_phrase_is_typed_by_the_storefront_not_guessed(
        string baseTitle, string childTitle, string gameType, string expectedLabel)
    {
        var baseGame = Subject(1, baseTitle, year: 2019, facts: new StorefrontFacts
        {
            IgdbGameType = "main_game",
        });

        var edition = Subject(
            2,
            childTitle,
            year: 2023,
            facts: new StorefrontFacts { IgdbGameType = gameType, IgdbVersionParentId = 42 },
            claimedParent: 1);

        // The title heuristic does not get a vote on a pair a storefront has
        // typed.
        Assert.False(ExpansionDetector.TryPropose(baseGame, edition, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.MetadataSpeaks, reason);

        // And the storefront proposes it itself, with its own word.
        var proposal = Assert.Single(ExpansionDetector.Detect([baseGame, edition]));
        Assert.Equal(1, proposal.BaseWorkId);
        Assert.Equal(2, proposal.ChildWorkId);
        Assert.Equal(IdentityLinkKinds.ExpansionOf, proposal.Kind);
        Assert.Equal(expectedLabel, proposal.RelationLabel);
        Assert.True(proposal.FromMetadata);
    }

    // ── Mechanism four: the wrong parent ────────────────────────────────────

    /// <summary>
    /// Longest-owned-prefix-wins is not the same question as who the parent is.
    /// Death of the Outsider is a standalone expansion of Dishonored 2;
    /// Deleted Scenes belongs to Condition Zero; DayZ Mod to Operation
    /// Arrowhead; Colonization is a remake of Sid Meier's Colonization and not
    /// an expansion of Civilization IV at all. In every case the pair is real
    /// and the base is wrong, and a known parent pointing elsewhere refutes it.
    /// </summary>
    [Theory]
    [InlineData("Dishonored", "Dishonored: Death of the Outsider", "standalone_expansion")]
    [InlineData("Counter-Strike", "Counter-Strike: Condition Zero Deleted Scenes", "expansion")]
    [InlineData("Arma 2", "Arma 2: DayZ Mod", "mod")]
    [InlineData("Sid Meier's Civilization IV", "Sid Meier's Civilization IV: Colonization", "remake")]
    public void A_known_parent_that_is_a_different_work_refutes_the_pair(
        string baseTitle, string childTitle, string gameType)
    {
        var wrongBase = Subject(1, baseTitle, year: 2005);
        var child = Subject(
            2,
            childTitle,
            year: 2008,
            // Work 3 is the real parent, and it is not the base being proposed.
            facts: new StorefrontFacts { IgdbGameType = gameType, IgdbParentId = 99 },
            claimedParent: 3);

        Assert.False(ExpansionDetector.TryPropose(wrongBase, child, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.MetadataContradicts, reason);
    }

    // ── The gap-filler rule itself ──────────────────────────────────────────

    /// <summary>
    /// Where a storefront speaks about EITHER member of the pair, the title
    /// heuristic stands down. Silence is its whole territory.
    /// </summary>
    [Fact]
    public void The_heuristic_proposes_nothing_where_metadata_speaks()
    {
        var silentBase = Subject(1, "Sid Meier's Civilization IV", year: 2005);
        var silentChild = Subject(2, "Sid Meier's Civilization IV: Warlords", year: 2006);

        // Silent on both sides: proposed, which is the population the heuristic
        // still exists for.
        Assert.True(ExpansionDetector.TryPropose(silentBase, silentChild, null, out _, out var open));
        Assert.Equal(ExpansionRefusalReason.None, open);

        // A typed BASE is enough on its own, even though the claim says nothing
        // about the child.
        var typedBase = silentBase with
        {
            Claim = StorefrontRelation.Read(new StorefrontFacts { IgdbGameType = "bundle" }),
        };

        Assert.False(ExpansionDetector.TryPropose(typedBase, silentChild, null, out _, out var closed));
        Assert.Equal(ExpansionRefusalReason.MetadataSpeaks, closed);
    }

    /// <summary>
    /// A plain Steam type 0 with no parent is NOT speech. Steam is
    /// documented-silent on expansions — every genuine standalone expansion in
    /// the measured library is type 0 with no parent appid — so reading it as
    /// an opinion would mute the heuristic over the whole Steam library.
    /// </summary>
    [Fact]
    public void A_plain_steam_game_type_is_silence_not_speech()
    {
        var facts = new StorefrontFacts { SteamStoreType = StorefrontRelation.SteamTypeGame };
        Assert.Null(StorefrontRelation.Read(facts));

        var baseGame = Subject(1, "Sid Meier's Civilization IV", year: 2005, facts: facts);
        var child = Subject(2, "Sid Meier's Civilization IV: Warlords", year: 2006, facts: facts);

        Assert.True(ExpansionDetector.TryPropose(baseGame, child, null, out _, out var reason));
        Assert.Equal(ExpansionRefusalReason.None, reason);
    }

    /// <summary>
    /// Eleven of the author's 38 proposals were demos, betas, playtests and
    /// staging branches offered under the word "expansion". Even with every
    /// storefront silent — which for a delisted staging branch they all are —
    /// the fallback now names them for what they are.
    /// </summary>
    [Theory]
    [InlineData("Sid Meier's Civilization V", "Sid Meier's Civilization V: Demo", RelationLabels.Demo)]
    [InlineData("Midnight Ghost Hunt", "Midnight Ghost Hunt - Beta Test", RelationLabels.Beta)]
    [InlineData("Barony", "Barony (Beta)", RelationLabels.Beta)]
    [InlineData("Rainbow Six Siege", "Rainbow Six Siege - Test Server", RelationLabels.Beta)]
    public void A_variant_marker_makes_the_fallback_propose_variant_of(
        string baseTitle, string childTitle, string expectedLabel)
    {
        var baseGame = Subject(1, baseTitle, year: 2010, publisher: "Firaxis");
        var variant = Subject(2, childTitle, year: 2010, publisher: "Firaxis");

        Assert.True(ExpansionDetector.TryPropose(baseGame, variant, null, out var proposal, out _));
        Assert.NotNull(proposal);
        Assert.Equal(IdentityLinkKinds.VariantOf, proposal.Kind);
        Assert.Equal(expectedLabel, proposal.RelationLabel);
        Assert.False(proposal.FromMetadata);
    }

    /// <summary>
    /// A genuine expansion with no storefront opinion keeps the kind it always
    /// had, and carries no label, because nothing named it.
    /// </summary>
    [Fact]
    public void A_silent_expansion_stays_expansion_of_with_no_label()
    {
        var civ = Subject(1, "Sid Meier's Civilization IV", year: 2005);
        var bts = Subject(2, "Sid Meier's Civilization IV: Beyond the Sword", year: 2007);

        Assert.True(ExpansionDetector.TryPropose(civ, bts, null, out var proposal, out _));
        Assert.NotNull(proposal);
        Assert.Equal(IdentityLinkKinds.ExpansionOf, proposal.Kind);
        Assert.Null(proposal.RelationLabel);
    }

    // ── The IGDB vocabulary ────────────────────────────────────────────────

    /// <summary>
    /// game_type maps to a kind and a label. Products that depend on a base take
    /// expansion_of; editions take the same NUMBERS and their own WORD, which is
    /// the reason kinds and labels are separate columns; mods, bundles and
    /// updates keep a label and claim no kind at all.
    /// </summary>
    [Theory]
    [InlineData("dlc_addon", IdentityLinkKinds.ExpansionOf, RelationLabels.Dlc)]
    [InlineData("expansion", IdentityLinkKinds.ExpansionOf, RelationLabels.Expansion)]
    [InlineData("standalone_expansion", IdentityLinkKinds.ExpansionOf, RelationLabels.StandaloneExpansion)]
    [InlineData("episode", IdentityLinkKinds.ExpansionOf, RelationLabels.Episode)]
    [InlineData("season", IdentityLinkKinds.ExpansionOf, RelationLabels.Season)]
    [InlineData("pack", IdentityLinkKinds.ExpansionOf, RelationLabels.Pack)]
    [InlineData("expanded_game", IdentityLinkKinds.ExpansionOf, RelationLabels.ExpandedGame)]
    [InlineData("remaster", IdentityLinkKinds.ExpansionOf, RelationLabels.Remaster)]
    [InlineData("remake", IdentityLinkKinds.ExpansionOf, RelationLabels.Remake)]
    [InlineData("port", IdentityLinkKinds.ExpansionOf, RelationLabels.Port)]
    [InlineData("fork", IdentityLinkKinds.ExpansionOf, RelationLabels.Fork)]
    [InlineData("mod", null, RelationLabels.Mod)]
    [InlineData("bundle", null, RelationLabels.Bundle)]
    [InlineData("update", null, RelationLabels.Update)]
    public void An_igdb_game_type_maps_to_a_kind_and_a_label(
        string gameType, string? expectedKind, string expectedLabel)
    {
        var claim = StorefrontRelation.Read(new StorefrontFacts
        {
            IgdbGameType = gameType,
            IgdbParentId = 1234,
        });

        Assert.NotNull(claim);
        Assert.Equal(expectedKind, claim.Kind);
        Assert.Equal(expectedLabel, claim.Label);
        Assert.Equal(1234, claim.IgdbParentId);
        Assert.False(claim.RefutesExtension);
    }

    /// <summary>
    /// main_game with no parent is the refutation, and main_game WITH a parent
    /// is not: a main game can still be part of a bundle, and refusing on the
    /// type alone would throw that away.
    /// </summary>
    [Fact]
    public void Main_game_refutes_only_when_it_names_no_parent()
    {
        var orphan = StorefrontRelation.Read(new StorefrontFacts { IgdbGameType = "main_game" });
        Assert.NotNull(orphan);
        Assert.True(orphan.RefutesExtension);
        Assert.Null(orphan.Kind);
        Assert.Equal(RelationLabels.MainGame, orphan.Label);

        var parented = StorefrontRelation.Read(
            new StorefrontFacts { IgdbGameType = "main_game", IgdbParentId = 7 });
        Assert.NotNull(parented);
        Assert.False(parented.RefutesExtension);
    }

    /// <summary>
    /// An IGDB type this build has never seen is recorded verbatim and claims no
    /// kind. IGDB has fifteen type names today and will add more; guessing which
    /// numbers a new one changes is exactly the mistake the label column avoids.
    /// </summary>
    [Fact]
    public void An_unknown_igdb_type_is_recorded_and_claims_nothing()
    {
        var claim = StorefrontRelation.Read(
            new StorefrontFacts { IgdbGameType = "anthology", IgdbParentId = 5 });

        Assert.NotNull(claim);
        Assert.Equal("anthology", claim.Label);
        Assert.Null(claim.Kind);
        Assert.False(claim.RefutesExtension);
    }

    /// <summary>
    /// Steam owns the variant line and IGDB owns everything above it. A demo
    /// that also carries an IGDB main_game type is still a demo: IGDB does not
    /// model demos, betas or playtests at all, so it cannot be contradicting
    /// anything here.
    /// </summary>
    [Fact]
    public void A_steam_variant_type_outranks_what_igdb_says()
    {
        var claim = StorefrontRelation.Read(new StorefrontFacts
        {
            SteamStoreType = StorefrontRelation.SteamTypeDemo,
            SteamParentAppId = "8930",
            IgdbGameType = "main_game",
        });

        Assert.NotNull(claim);
        Assert.Equal(IdentityLinkKinds.VariantOf, claim.Kind);
        Assert.Equal(RelationLabels.Demo, claim.Label);
        Assert.Equal(StorefrontRelationSources.SteamStore, claim.Source);
    }

    /// <summary>
    /// The PICS mirror is the second Steam source, and it still answers for an
    /// app the store has delisted. Migration 0006 recorded that "Valve has no
    /// beta/playtest type"; the author's database now holds works typed
    /// literally Beta, so that note is superseded and this is the correction.
    /// </summary>
    [Fact]
    public void The_pics_type_supplies_the_variant_when_the_store_is_silent()
    {
        var beta = StorefrontRelation.Read(
            new StorefrontFacts { SteamAppType = "Beta", SteamParentAppId = "915810" });

        Assert.NotNull(beta);
        Assert.Equal(IdentityLinkKinds.VariantOf, beta.Kind);
        Assert.Equal(RelationLabels.Beta, beta.Label);
        Assert.Equal(StorefrontRelationSources.SteamPics, beta.Source);
        Assert.Equal("915810", beta.SteamParentAppId);
    }
}
