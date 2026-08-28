using Winnow.Core.Matching;
using Winnow.Resolve.Matching;
using Xunit;

namespace Winnow.Tests;

/// <summary>
/// The arithmetic behind the default thresholds, pinned so that tuning one
/// number cannot quietly move a landmark case across a band boundary.
///
/// <para>The model in one line: <b>title similarity is the gate and the base;
/// every other signal is a signed adjustment, and absent evidence adds
/// nothing.</b> That last clause is the load-bearing one — if missing signals
/// were renormalised away, a library with no metadata would read as a library
/// of certain matches, and §5.3's non-negotiable would be one refactor away
/// from being violated.</para>
/// </summary>
public sealed class SoftMatchCalibrationTests
{
    private static readonly SoftMatcher Matcher = new();
    private static readonly SoftMatchThresholds T = SoftMatchThresholds.Default;

    private static MatchSubject Subject(
        long id, string title, int? year = null, string? publisher = null, ulong? cover = null)
        => new()
        {
            ReleaseId = id,
            Title = title,
            ReleaseYear = year,
            Publisher = publisher,
            CoverPerceptualHash = cover,
        };

    [Fact]
    public void DefaultThresholdsAreOrderedAndInRange()
    {
        T.Validate();

        Assert.Equal(0.70, T.TitleSimilarityFloor);
        Assert.Equal(0.45, T.QueueFloor);
        Assert.Equal(0.85, T.PriorityThreshold);
        Assert.Equal(0.65, T.TitleWeight);

        // An identical title on its own must be queueable but never top-band:
        // identical titles are precisely the Prey trap.
        Assert.True(T.TitleWeight > T.QueueFloor);
        Assert.True(T.TitleWeight < T.PriorityThreshold);
    }

    /// <summary>
    /// Two identical titles with no corroborating metadata at all: 0.65.
    /// Comfortably queued, nowhere near priority.
    /// </summary>
    [Fact]
    public void IdenticalTitleAlone_ScoresTheTitleWeight()
    {
        var score = Matcher.Score(Subject(1, "Celeste"), Subject(2, "Celeste"));

        Assert.Equal(0.65, score.Score, 6);
        Assert.Equal(SoftMatchBand.Review, score.Band);
    }

    /// <summary>
    /// The far-year penalty is sized against the title weight so that a perfect
    /// title match plus a four-year-or-worse gap lands at 0.35 — under the 0.45
    /// floor. That is the arithmetic that keeps Prey (2006) / Prey (2017) out of
    /// the queue rather than merely low in it.
    /// </summary>
    [Fact]
    public void IdenticalTitlePlusFarYearGap_FallsUnderTheQueueFloor()
    {
        var score = Matcher.Score(Subject(1, "Prey", 2006), Subject(2, "Prey", 2017));

        Assert.Equal(0.35, score.Score, 6);
        Assert.True(score.Score < T.QueueFloor);
        Assert.Equal(SoftMatchBand.Discarded, score.Band);
    }

    [Fact]
    public void IdenticalTitlePlusExactYearAndPublisher_ReachesPriority()
    {
        var score = Matcher.Score(
            Subject(1, "Hollow Knight", 2017, "Team Cherry"),
            Subject(2, "Hollow Knight", 2017, "Team Cherry"));

        // 0.65 title + 0.20 year + 0.12 publisher.
        Assert.Equal(0.97, score.Score, 6);
        Assert.Equal(SoftMatchBand.Priority, score.Band);
        Assert.False(score.AutoMergeAllowed);
    }

    /// <summary>±1 year is agreement per §5.3, just weaker than an exact match.</summary>
    [Fact]
    public void AdjacentYearIsStillAgreement()
    {
        var score = Matcher.Score(
            Subject(1, "Hollow Knight", 2017, "Team Cherry"),
            Subject(2, "Hollow Knight", 2018, "Team Cherry"));

        // 0.65 + 0.15 + 0.12.
        Assert.Equal(0.92, score.Score, 6);
        Assert.Equal(SoftMatchBand.Priority, score.Band);
    }

    /// <summary>A content bundle costs a nudge, not a veto: 0.65 - 0.05.</summary>
    [Fact]
    public void BundleEditionMismatchCostsExactlyTheDocumentedNudge()
    {
        var score = Matcher.Score(
            Subject(1, "The Witcher 3: Wild Hunt"),
            Subject(2, "The Witcher III: Wild Hunt — Game of the Year Edition"));

        Assert.Equal(0.60, score.Score, 6);
        Assert.Equal(SoftMatchBand.Review, score.Band);
    }

    /// <summary>
    /// A near-perfect cover hash is worth more than a publisher match and less
    /// than an exact year — covers are strong evidence, but the same publisher
    /// reuses key art across a franchise, and identical art on two store fronts
    /// is the norm rather than a coincidence.
    /// </summary>
    [Fact]
    public void CoverHashIsWeightedBetweenPublisherAndYear()
    {
        Assert.True(T.CoverStrongBonus > T.PublisherMatchBonus);
        Assert.True(T.CoverStrongBonus < T.YearExactBonus);

        var score = Matcher.Score(
            Subject(1, "Celeste", cover: 0xFFFF_FFFF_0000_0000),
            Subject(2, "Celeste", cover: 0xFFFF_FFFF_0000_0000));

        // 0.65 + 0.15.
        Assert.Equal(0.80, score.Score, 6);
        Assert.Equal(SoftMatchBand.Review, score.Band);
    }

    /// <summary>
    /// Thresholds are parameters, not constants baked into the arithmetic — a
    /// future tuning pass (or a per-source profile) changes behaviour without
    /// touching the scoring code.
    /// </summary>
    [Fact]
    public void ThresholdsAreParameterised()
    {
        var strict = new SoftMatcher(SoftMatchThresholds.Default with { QueueFloor = 0.70 });
        var pair = (Subject(1, "Celeste"), Subject(2, "Celeste"));

        Assert.Equal(SoftMatchBand.Review, Matcher.Score(pair.Item1, pair.Item2).Band);
        Assert.Equal(SoftMatchBand.Discarded, strict.Score(pair.Item1, pair.Item2).Band);
    }

    [Fact]
    public void NonsensicalThresholdsAreRejectedAtConstruction()
    {
        Assert.Throws<ArgumentException>(() =>
            new SoftMatcher(SoftMatchThresholds.Default with { QueueFloor = 0.9, PriorityThreshold = 0.5 }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SoftMatcher(SoftMatchThresholds.Default with { QueueFloor = 1.5 }));
    }

    /// <summary>
    /// Contributions must add up to the reported score. If they drift apart the
    /// merge-confirm UI shows the user an explanation that does not justify the
    /// number next to it, which is worse than showing no explanation.
    /// </summary>
    [Theory]
    [InlineData("Celeste", "Celeste")]
    [InlineData("The Witcher 3: Wild Hunt", "The Witcher III: Wild Hunt — Game of the Year Edition")]
    [InlineData("Fallout: New Vegas", "Fallout New Vegas Ultimate Edition")]
    public void SignalContributionsSumToTheScore(string left, string right)
    {
        var score = Matcher.Score(
            Subject(1, left, 2015, "Publisher", 0x0123_4567_89AB_CDEF),
            Subject(2, right, 2015, "Publisher", 0x0123_4567_89AB_CDEF));

        Assert.Null(score.VetoReason);
        Assert.Equal(score.Score, Math.Clamp(score.Signals.Sum(s => s.Contribution), 0.0, 1.0), 9);
    }

    /// <summary>A vetoed pair reports zero, and every signal is still explained.</summary>
    [Fact]
    public void AVetoedPairScoresZeroButStillCarriesItsBreakdown()
    {
        var score = Matcher.Score(
            Subject(1, "The Elder Scrolls V: Skyrim", 2011, "Bethesda Softworks"),
            Subject(2, "The Elder Scrolls V: Skyrim Special Edition", 2016, "Bethesda Softworks"));

        Assert.Equal(0.0, score.Score);
        Assert.Equal(SoftMatchVetoReasons.RebuildEdition, score.VetoReason);
        Assert.Equal(1.0, score.TitleSimilarity, 6);
        Assert.Equal(5, score.Signals.Count);
        Assert.True(score.Signals.Sum(s => s.Contribution) > 0, "the raw signals still describe a strong title/publisher match");
    }
}
