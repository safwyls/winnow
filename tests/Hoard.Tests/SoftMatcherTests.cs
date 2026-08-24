using Hoard.Core.Matching;
using Hoard.Resolve.Matching;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// §5.3 step 2 — the soft matcher, tested as the pure function it is.
///
/// <para>The centre of gravity here is the NEGATIVE cases. §5.3's one
/// non-negotiable is that fuzzy matching must never auto-merge, because it will
/// confidently merge Prey (2006) with Prey (2017); §9 ranks that as the second
/// most likely way this project fails. So most of what follows asserts that
/// things which look similar are kept apart, and that the reason is legible in
/// the signal breakdown rather than buried in a scalar.</para>
/// </summary>
public sealed class SoftMatcherTests
{
    private static readonly SoftMatcher Matcher = new();
    private static readonly SoftMatchThresholds Thresholds = SoftMatchThresholds.Default;

    private static MatchSubject Subject(
        long id,
        string title,
        int? year = null,
        string? publisher = null,
        ulong? coverHash = null)
        => new()
        {
            ReleaseId = id,
            Title = title,
            ReleaseYear = year,
            Publisher = publisher,
            CoverPerceptualHash = coverHash,
        };

    private static SoftMatchSignal SignalNamed(SoftMatchScore score, string name)
        => Assert.Single(score.Signals, s => s.Name == name);

    // ── The Prey trap (§5.3's worked example of why this is dangerous) ───────

    /// <summary>
    /// Identical titles, eleven years apart, two entirely unrelated games.
    /// The title signal fires at 100% — a title-only matcher merges them — and
    /// the year signal is the only thing standing between the user and a silent
    /// merge that absorbs one game's playtime into the other.
    /// </summary>
    [Fact]
    public void PreyTrap_IdenticalTitlesElevenYearsApart_IsNeverAutoMergedAndNeverQueued()
    {
        var prey2006 = Subject(1, "Prey", year: 2006, publisher: "2K Games");
        var prey2017 = Subject(2, "Prey", year: 2017, publisher: "Bethesda Softworks");

        var score = Matcher.Score(prey2006, prey2017);

        // The non-negotiable, asserted directly.
        Assert.False(score.AutoMergeAllowed);
        Assert.NotEqual(SoftMatchBand.Priority, score.Band);

        // And it does not even reach the queue: an 11-year gap plus a different
        // publisher is not a "maybe" for a human to adjudicate, it is a no.
        Assert.Equal(SoftMatchBand.Discarded, score.Band);
        Assert.False(score.ShouldQueue);
        Assert.True(score.Score < Thresholds.QueueFloor);
    }

    /// <summary>
    /// The breakdown must let the merge-confirm UI say WHY (design-system §6:
    /// "signal diff between them: title distance, year delta, publisher").
    /// A pair whose title agrees perfectly and whose year does not must be
    /// distinguishable from a pair where both agree — not merely lower-scoring.
    /// </summary>
    [Fact]
    public void PreyTrap_SignalBreakdownDistinguishesTheTwoGames()
    {
        var score = Matcher.Score(
            Subject(1, "Prey", year: 2006, publisher: "2K Games"),
            Subject(2, "Prey", year: 2017, publisher: "Bethesda Softworks"));

        Assert.Equal(1.0, score.TitleSimilarity, 6);
        Assert.Equal(11, score.YearDelta);
        Assert.False(score.PublisherMatch);

        var title = SignalNamed(score, SoftMatchSignalNames.Title);
        Assert.True(title.Fired);
        Assert.True(title.Contribution > 0);

        var year = SignalNamed(score, SoftMatchSignalNames.ReleaseYear);
        Assert.True(year.Fired);
        Assert.True(year.Contribution < 0);
        Assert.Contains("11", year.Detail, StringComparison.Ordinal);

        var publisher = SignalNamed(score, SoftMatchSignalNames.Publisher);
        Assert.True(publisher.Fired);
        Assert.True(publisher.Contribution < 0);
    }

    /// <summary>
    /// With no year data at all the pair is genuinely ambiguous, so it is queued
    /// for a human — but it still never reaches the priority band and still
    /// never auto-merges. An identical title on its own is deliberately worth
    /// less than the priority threshold: identical titles ARE the trap.
    /// </summary>
    [Fact]
    public void PreyTrap_WithNoYearData_IsQueuedForAHumanButNeverPrioritised()
    {
        var score = Matcher.Score(Subject(1, "Prey"), Subject(2, "Prey"));

        Assert.Equal(SoftMatchBand.Review, score.Band);
        Assert.False(score.AutoMergeAllowed);
        Assert.True(score.Score < Thresholds.PriorityThreshold);
        Assert.Null(score.YearDelta);
        Assert.False(SignalNamed(score, SoftMatchSignalNames.ReleaseYear).Fired);
    }

    /// <summary>
    /// No arrangement of signals produces an auto-merge, because there is no
    /// auto-merge to produce. Belt and braces against a future contributor
    /// deciding 0.99 is "basically certain".
    /// </summary>
    [Fact]
    public void AutoMergeIsNeverAllowed_EvenWhenEverySignalAgreesPerfectly()
    {
        var score = Matcher.Score(
            Subject(1, "Hollow Knight", 2017, "Team Cherry", 0xDEAD_BEEF_1234_5678),
            Subject(2, "Hollow Knight", 2017, "Team Cherry", 0xDEAD_BEEF_1234_5678));

        Assert.Equal(SoftMatchBand.Priority, score.Band);
        Assert.True(score.Score >= Thresholds.PriorityThreshold);
        Assert.False(score.AutoMergeAllowed);
    }

    // ── Editions are different Releases (§9 pitfall 5) ───────────────────────

    /// <summary>
    /// Skyrim, Skyrim Special Edition and Skyrim Anniversary Edition are three
    /// Releases of one Work: different executables, different achievement sets,
    /// incompatible mod ecosystems. Collapsing them is the pitfall §9 lists
    /// fifth, and it must not even be offered to the user as a question.
    /// </summary>
    [Theory]
    [InlineData("The Elder Scrolls V: Skyrim", "The Elder Scrolls V: Skyrim Special Edition")]
    [InlineData("The Elder Scrolls V: Skyrim", "The Elder Scrolls V: Skyrim Anniversary Edition")]
    [InlineData("The Elder Scrolls V: Skyrim Special Edition", "The Elder Scrolls V: Skyrim Anniversary Edition")]
    [InlineData("Dark Souls", "Dark Souls: Remastered")]
    [InlineData("Baldur's Gate", "Baldur's Gate: Enhanced Edition")]
    [InlineData("Age of Empires II", "Age of Empires II: Definitive Edition")]
    public void RebuildEditionsAreNeverCollapsed(string left, string right)
    {
        var score = Matcher.Score(Subject(1, left), Subject(2, right));

        Assert.Equal(SoftMatchVetoReasons.RebuildEdition, score.VetoReason);
        Assert.Equal(0.0, score.Score);
        Assert.Equal(SoftMatchBand.Discarded, score.Band);
        Assert.False(score.ShouldQueue);
    }

    /// <summary>
    /// Same three Skyrims, all mutually distinct — and the score is zero in
    /// every direction, so no ordering of a scan can smuggle one past.
    /// </summary>
    [Fact]
    public void SkyrimVariants_AreMutuallyDistinct()
    {
        var variants = new[]
        {
            Subject(1, "The Elder Scrolls V: Skyrim", 2011, "Bethesda Softworks"),
            Subject(2, "The Elder Scrolls V: Skyrim Special Edition", 2016, "Bethesda Softworks"),
            Subject(3, "The Elder Scrolls V: Skyrim Anniversary Edition", 2021, "Bethesda Softworks"),
        };

        for (var i = 0; i < variants.Length; i++)
        {
            for (var j = 0; j < variants.Length; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var score = Matcher.Score(variants[i], variants[j]);
                Assert.Equal(0.0, score.Score);
                Assert.False(score.ShouldQueue);
            }
        }
    }

    // ── Sequel ordinals: the case string distance structurally cannot solve ──

    /// <summary>
    /// "Portal" and "Portal 2" are one character apart. No weighting of an edit
    /// distance separates that from a genuine near-match, so the sequel number
    /// is lifted out during normalisation and compared exactly. Roman and arabic
    /// forms fold together first, which is why Dark Souls II / III is caught.
    /// </summary>
    [Theory]
    [InlineData("Portal", "Portal 2")]
    [InlineData("Half-Life", "Half-Life 2")]
    [InlineData("Dark Souls II", "Dark Souls III")]
    [InlineData("Dark Souls 2", "Dark Souls III")]
    [InlineData("The Witcher 2: Assassins of Kings", "The Witcher 3: Wild Hunt")]
    [InlineData("Left 4 Dead", "Left 4 Dead 2")]
    [InlineData("Civilization V", "Civilization VI")]
    [InlineData("Fallout 3", "Fallout 4")]
    // Word-form ordinals. The roman spelling was always caught; these were not,
    // so the veto was inconsistent with itself — "Episode One" and "Episode Two"
    // both reduced to the ordinal of Half-Life 2 and scored 0.84 on title
    // similarity alone.
    [InlineData("Half-Life 2: Episode One", "Half-Life 2: Episode Two")]
    [InlineData("Half-Life 2: Episode One", "Half-Life 2: Episode Three")]
    [InlineData("Half-Life 2: Episode One", "Half-Life 2: Episode 2")]
    [InlineData("The Walking Dead: Season One", "The Walking Dead: Season Two")]
    // A bare "X" is a letter, not a ten: Mega Man X is a different series from
    // Mega Man, and folding it made the two titles identical strings.
    [InlineData("Mega Man X", "Mega Man 10")]
    [InlineData("Mega Man X", "Mega Man 2")]
    public void SequelsAreNeverMatched(string left, string right)
    {
        var score = Matcher.Score(Subject(1, left), Subject(2, right));

        Assert.Equal(0.0, score.Score);
        Assert.Equal(SoftMatchBand.Discarded, score.Band);
        Assert.False(score.ShouldQueue);
    }

    [Fact]
    public void SequelMismatch_IsVetoedNotMerelyDownweighted()
    {
        var score = Matcher.Score(
            Subject(1, "Portal", 2007, "Valve"),
            Subject(2, "Portal 2", 2011, "Valve"));

        // Same publisher, adjacent-ish years, near-identical strings: a weighted
        // model would still put this well over the floor. The veto is the point.
        Assert.Equal(SoftMatchVetoReasons.SequelOrdinal, score.VetoReason);
        Assert.Equal(0.0, score.Score);
    }

    // ── Pairs that SHOULD score high ────────────────────────────────────────

    /// <summary>
    /// The normalisation cases that actually turn up in a Steam/GOG/Epic
    /// library: roman numerals, trademark symbols, subtitle separators,
    /// apostrophes, accents, ampersands, articles and content-bundle suffixes.
    /// All of these are the same game and must clear the queue floor.
    /// </summary>
    [Theory]
    [InlineData("The Witcher 3: Wild Hunt", "The Witcher III: Wild Hunt — Game of the Year Edition")]
    [InlineData("Assassin's Creed® IV Black Flag™", "Assassins Creed 4: Black Flag")]
    [InlineData("Grand Theft Auto V", "Grand Theft Auto 5")]
    [InlineData("Counter-Strike: Global Offensive", "Counter Strike Global Offensive")]
    [InlineData("Fallout: New Vegas", "Fallout New Vegas - Ultimate Edition")]
    [InlineData("S.T.A.L.K.E.R.: Shadow of Chernobyl", "STALKER Shadow of Chernobyl")]
    [InlineData("Pokémon Trading Card Game Live", "Pokemon Trading Card Game Live")]
    [InlineData("Command & Conquer: Red Alert 2", "Command and Conquer Red Alert 2")]
    [InlineData("Deus Ex: Human Revolution", "Deus Ex Human Revolution - Director's Cut")]
    [InlineData("The Last of Us Part II", "Last of Us Part 2")]
    // Word-form folding cuts both ways: it separates One from Two, and it joins
    // One to 1.
    [InlineData("Half-Life 2: Episode One", "Half-Life 2 Episode 1")]
    public void NormalisationEquivalentsClearTheQueueFloor(string left, string right)
    {
        var score = Matcher.Score(Subject(1, left), Subject(2, right));

        Assert.Null(score.VetoReason);
        Assert.True(
            score.Score >= Thresholds.QueueFloor,
            $"expected >= {Thresholds.QueueFloor} for \"{left}\" / \"{right}\", got {score.Score:F3} " +
            $"(cores \"{score.LeftTitle.Core}\" / \"{score.RightTitle.Core}\")");
        Assert.True(score.ShouldQueue);
    }

    /// <summary>
    /// Corroborating evidence is what lifts a pair into the priority band — and
    /// even the priority band is only "show the user this one first".
    /// </summary>
    [Fact]
    public void CorroboratingSignalsLiftAPairIntoThePriorityBand()
    {
        var bare = Matcher.Score(
            Subject(1, "The Witcher 3: Wild Hunt"),
            Subject(2, "The Witcher III: Wild Hunt"));

        var corroborated = Matcher.Score(
            Subject(1, "The Witcher 3: Wild Hunt", 2015, "CD Projekt Red", 0x0F0F_0F0F_0F0F_0F0F),
            Subject(2, "The Witcher III: Wild Hunt", 2015, "CD Projekt RED", 0x0F0F_0F0F_0F0F_0F0F));

        Assert.Equal(SoftMatchBand.Review, bare.Band);
        Assert.Equal(SoftMatchBand.Priority, corroborated.Band);
        Assert.True(corroborated.Score > bare.Score);
        Assert.False(corroborated.AutoMergeAllowed);
    }

    // ── Pairs that must NOT be queued ───────────────────────────────────────

    /// <summary>
    /// Different games whose titles share a lot of surface. These must be
    /// discarded, not queued: a review queue seeded with obviously-wrong pairs
    /// is a queue nobody clears, and an uncleared queue is indistinguishable
    /// from a broken one.
    /// </summary>
    [Theory]
    [InlineData("Portal", "Portal 2")]
    [InlineData("Half-Life", "Half-Life 2")]
    [InlineData("Dark Souls II", "Dark Souls III")]
    [InlineData("Doom", "Doom Eternal")]
    [InlineData("Batman: Arkham Asylum", "Batman: Arkham City")]
    [InlineData("Far Cry", "Far Cry Primal")]
    [InlineData("Mirror's Edge", "Mirror's Edge Catalyst")]
    [InlineData("Hitman", "Hitman: Blood Money")]
    [InlineData("Stardew Valley", "Story of Seasons")]
    [InlineData("Team Fortress 2", "Titanfall 2")]
    public void DifferentGamesAreDiscarded(string left, string right)
    {
        var score = Matcher.Score(Subject(1, left), Subject(2, right));

        Assert.True(
            score.Score < Thresholds.QueueFloor,
            $"expected < {Thresholds.QueueFloor} for \"{left}\" / \"{right}\", got {score.Score:F3} " +
            $"(cores \"{score.LeftTitle.Core}\" / \"{score.RightTitle.Core}\")");
        Assert.Equal(SoftMatchBand.Discarded, score.Band);
        Assert.False(score.ShouldQueue);
    }

    /// <summary>
    /// Title agreement is a NECESSARY condition, not one weighted signal among
    /// four. Without this gate a same-publisher-same-year coincidence carries
    /// half-matching titles over the floor, and every annual sports franchise
    /// floods the queue.
    /// </summary>
    [Fact]
    public void SamePublisherAndYearCannotRescueAWeakTitleMatch()
    {
        var score = Matcher.Score(
            Subject(1, "Dishonored", 2012, "Bethesda Softworks"),
            Subject(2, "Doom", 2012, "Bethesda Softworks"));

        Assert.Equal(SoftMatchVetoReasons.TitleBelowFloor, score.VetoReason);
        Assert.Equal(0.0, score.Score);
    }

    // ── Individual signal behaviour ─────────────────────────────────────────

    [Fact]
    public void YearWithinOne_IsTreatedAsAgreement_PerSection53()
    {
        var exact = Matcher.Score(Subject(1, "Celeste", 2018), Subject(2, "Celeste", 2018));
        var adjacent = Matcher.Score(Subject(1, "Celeste", 2018), Subject(2, "Celeste", 2019));
        var near = Matcher.Score(Subject(1, "Celeste", 2018), Subject(2, "Celeste", 2021));

        Assert.True(SignalNamed(exact, SoftMatchSignalNames.ReleaseYear).Contribution > 0);
        Assert.True(SignalNamed(adjacent, SoftMatchSignalNames.ReleaseYear).Contribution > 0);
        Assert.True(SignalNamed(near, SoftMatchSignalNames.ReleaseYear).Contribution < 0);
        Assert.True(exact.Score > adjacent.Score);
        Assert.True(adjacent.Score > near.Score);
    }

    /// <summary>
    /// Absent evidence must never be renormalised into agreement: "we have no
    /// year" has to score strictly lower than "the years agree". Otherwise a
    /// library with no metadata at all reads as a library of certain matches.
    /// </summary>
    [Fact]
    public void MissingSignalsContributeNothing_TheyDoNotBecomeAgreement()
    {
        var unknown = Matcher.Score(Subject(1, "Braid"), Subject(2, "Braid"));
        var agreeing = Matcher.Score(Subject(1, "Braid", 2008), Subject(2, "Braid", 2008));

        Assert.False(SignalNamed(unknown, SoftMatchSignalNames.ReleaseYear).Fired);
        Assert.Equal(0.0, SignalNamed(unknown, SoftMatchSignalNames.ReleaseYear).Contribution);
        Assert.True(agreeing.Score > unknown.Score);
    }

    [Fact]
    public void ParenthesisedYearInTheTitleIsUsedWhenNoYearFieldIsSupplied()
    {
        var score = Matcher.Score(Subject(1, "Prey (2006)"), Subject(2, "Prey (2017)"));

        Assert.Equal(11, score.YearDelta);
        Assert.Equal("prey", score.LeftTitle.Core);
        Assert.Equal(2006, score.LeftTitle.ParsedYear);
        Assert.False(score.ShouldQueue);
    }

    /// <summary>
    /// A bare trailing year is part of the title, not a disambiguator: strip it
    /// and every annual sports release collapses into one game.
    /// </summary>
    [Fact]
    public void BareTrailingYearsStayInTheTitle()
    {
        var score = Matcher.Score(Subject(1, "Madden NFL 2004"), Subject(2, "Madden NFL 2005"));

        Assert.Null(score.LeftTitle.ParsedYear);
        Assert.False(score.ShouldQueue);
    }

    [Fact]
    public void CoverHashDistanceIsScoredAndReported()
    {
        var identical = Matcher.Score(
            Subject(1, "Outer Wilds", coverHash: 0xFFFF_0000_FFFF_0000),
            Subject(2, "Outer Wilds", coverHash: 0xFFFF_0000_FFFF_0000));

        var unrelated = Matcher.Score(
            Subject(1, "Outer Wilds", coverHash: 0xFFFF_FFFF_FFFF_FFFF),
            Subject(2, "Outer Wilds", coverHash: 0x0000_0000_0000_0000));

        Assert.Equal(0, identical.CoverHashDistance);
        Assert.Equal(64, unrelated.CoverHashDistance);
        Assert.True(SignalNamed(identical, SoftMatchSignalNames.CoverHash).Contribution > 0);
        Assert.True(SignalNamed(unrelated, SoftMatchSignalNames.CoverHash).Contribution < 0);
        Assert.True(identical.Score > unrelated.Score);
    }

    [Fact]
    public void MissingCoverHashIsReportedAsNotFired_NotAsMismatch()
    {
        var score = Matcher.Score(
            Subject(1, "Outer Wilds", coverHash: 0xFFFF_0000_FFFF_0000),
            Subject(2, "Outer Wilds"));

        Assert.Null(score.CoverHashDistance);
        var cover = SignalNamed(score, SoftMatchSignalNames.CoverHash);
        Assert.False(cover.Fired);
        Assert.Equal(0.0, cover.Contribution);
    }

    [Fact]
    public void PublisherComparisonIgnoresLegalFormAndCase()
    {
        var score = Matcher.Score(
            Subject(1, "Fallout: New Vegas", 2010, "Bethesda Softworks LLC"),
            Subject(2, "Fallout New Vegas", 2010, "bethesda softworks"));

        Assert.True(score.PublisherMatch);
        Assert.True(SignalNamed(score, SoftMatchSignalNames.Publisher).Contribution > 0);
    }

    [Fact]
    public void BundleEditionMismatchIsAPenaltyNotAVeto()
    {
        var same = Matcher.Score(
            Subject(1, "Borderlands 2"),
            Subject(2, "Borderlands 2"));

        var bundled = Matcher.Score(
            Subject(1, "Borderlands 2"),
            Subject(2, "Borderlands 2: Game of the Year Edition"));

        Assert.Null(bundled.VetoReason);
        Assert.True(bundled.ShouldQueue);
        Assert.True(bundled.Score < same.Score);
        Assert.True(SignalNamed(bundled, SoftMatchSignalNames.BundleEdition).Contribution < 0);
    }

    // ── Determinism and order independence ──────────────────────────────────

    /// <summary>
    /// Scoring must be symmetric to the bit. Otherwise the review queue's
    /// contents depend on the order the scanner happened to walk the library,
    /// and "why did this appear/disappear?" becomes unanswerable.
    /// </summary>
    [Theory]
    [InlineData("Prey", "Prey")]
    [InlineData("Portal", "Portal 2")]
    [InlineData("The Witcher 3: Wild Hunt", "The Witcher III: Wild Hunt — Game of the Year Edition")]
    [InlineData("The Elder Scrolls V: Skyrim", "The Elder Scrolls V: Skyrim Special Edition")]
    [InlineData("Doom", "Doom Eternal")]
    [InlineData("Grand Theft Auto V", "Grand Theft Auto 5")]
    public void ScoringIsSymmetric(string left, string right)
    {
        var a = Subject(1, left, 2015, "Publisher A", 0x1234_5678_9ABC_DEF0);
        var b = Subject(2, right, 2017, "Publisher B", 0x1234_5678_9ABC_0000);

        var forward = Matcher.Score(a, b);
        var backward = Matcher.Score(b, a);

        Assert.Equal(forward.Score, backward.Score);
        Assert.Equal(forward.Band, backward.Band);
        Assert.Equal(forward.VetoReason, backward.VetoReason);
        Assert.Equal(forward.TitleSimilarity, backward.TitleSimilarity);
        Assert.Equal(forward.YearDelta, backward.YearDelta);
        Assert.Equal(forward.PublisherMatch, backward.PublisherMatch);
        Assert.Equal(forward.CoverHashDistance, backward.CoverHashDistance);
    }

    [Fact]
    public void ScoringIsRepeatable()
    {
        var a = Subject(1, "Hades", 2020, "Supergiant Games");
        var b = Subject(2, "Hades", 2020, "Supergiant Games");

        var first = Matcher.Score(a, b).Score;
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(first, Matcher.Score(a, b).Score);
        }
    }

    [Fact]
    public void RankIsOrderedByScoreThenReleaseId_RegardlessOfInputOrder()
    {
        var subject = Subject(1, "Hollow Knight", 2017);
        var possibilities = new[]
        {
            Subject(40, "Hollow Knight", 2017),
            Subject(20, "Hollow Knight", 2017),
            Subject(30, "Hollow Knight: Silksong", 2019),
            Subject(10, "Hollow Knight", 2011),
        };

        var forward = Matcher.Rank(subject, possibilities);
        var reversed = Matcher.Rank(subject, possibilities.Reverse());

        Assert.Equal(
            forward.Select(r => r.Possibility.ReleaseId),
            reversed.Select(r => r.Possibility.ReleaseId));

        // Equal scores tie-break on release id ascending, so the order is total.
        Assert.Equal(20, forward[0].Possibility.ReleaseId);
        Assert.Equal(40, forward[1].Possibility.ReleaseId);
    }

    [Fact]
    public void AReleaseCannotMatchItself()
    {
        var subject = Subject(7, "Tunic", 2022);

        var score = Matcher.Score(subject, subject);

        Assert.Equal(SoftMatchVetoReasons.SameRelease, score.VetoReason);
        Assert.Equal(0.0, score.Score);
    }

    [Fact]
    public void ATitleThatNormalisesToNothingIsVetoed()
    {
        var score = Matcher.Score(Subject(1, "   "), Subject(2, "   "));

        Assert.Equal(SoftMatchVetoReasons.EmptyTitle, score.VetoReason);
        Assert.Equal(0.0, score.Score);
    }

    // ── signals_json round trip ─────────────────────────────────────────────

    /// <summary>
    /// The breakdown stored on the row must be enough to render the merge
    /// confirm UI without re-scoring, so the explanation the user sees cannot
    /// drift from the score they are being asked about after a threshold tune.
    /// </summary>
    [Fact]
    public void SignalsJsonRoundTripsTheWholeBreakdown()
    {
        var score = Matcher.Score(
            Subject(11, "The Witcher 3: Wild Hunt", 2015, "CD Projekt Red", 0x0F0F_0F0F_0F0F_0F0F),
            Subject(22, "The Witcher III: Wild Hunt — Game of the Year Edition", 2016, "CD Projekt RED", 0x0F0F_0F0F_0F0F_0F0E));

        var json = SoftMatchSignalsJson.Serialize(score);
        var payload = SoftMatchSignalsJson.Deserialize(json);

        Assert.NotNull(payload);
        Assert.Equal(score.Score, payload.Score);
        Assert.Equal(score.Band.ToString(), payload.Band);
        Assert.False(payload.AutoMergeAllowed);
        Assert.Equal(11, payload.Left.ReleaseId);
        Assert.Equal(22, payload.Right.ReleaseId);
        Assert.Equal("witcher 3 wild hunt", payload.Left.NormalizedTitle);
        Assert.Equal("witcher 3 wild hunt", payload.Right.NormalizedTitle);
        Assert.Equal(1, payload.YearDelta);
        Assert.True(payload.PublisherMatch);
        Assert.Equal(1, payload.CoverHashDistance);
        Assert.Equal(score.Signals.Count, payload.Signals.Count);
        Assert.Contains(payload.Signals, s => s.Name == SoftMatchSignalNames.ReleaseYear && s.Fired);
        Assert.Contains("game of the year edition", payload.Right.BundleEditions);
    }
}
