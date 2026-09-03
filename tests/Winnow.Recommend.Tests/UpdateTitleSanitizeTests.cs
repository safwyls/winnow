using System.Text.RegularExpressions;
using Xunit;

// Regression tests for ReasonTokens.Sanitize, covering the version-number
// period bug fixed 2026-09-02 (TASK-74). The original terminator rule
// replaced every sentence-ending character unconditionally, destroying
// version numbers inside update titles. Three real titles were observed
// rendering with periods stripped on the running app's feed shelf.
namespace Winnow.Recommend.Tests;

public class UpdateTitleSanitizeTests
{
    private static readonly RecommendationTuning Tuning = RecommendationTuning.Default;

    // The three titles observed in the running app on 2026-09-02, verbatim.
    // Each must survive Sanitize unchanged. AC 1.
    [Theory]
    [InlineData("Dune: Awakening - 1.4.10.5 Hotfix Patch Notes", "Dune: Awakening - 1.4.10.5 Hotfix Patch Notes")]
    [InlineData("Game Update 7.9.1b Patch Notes", "Game Update 7.9.1b Patch Notes")]
    [InlineData("Patch Notes 2.03.a", "Patch Notes 2.03.a")]
    public void A_version_number_keeps_its_periods(string stored, string expected)
        => Assert.Equal(expected, ReasonTokens.Sanitize(stored));

    // A version-only title, a title ending in a period, and titles that mix
    // a version number with a second sentence. AC 3.
    [Theory]
    [InlineData("1.4.10.5", "1.4.10.5")]
    [InlineData("2.0", "2.0")]
    [InlineData("Patch Notes 2.03.a.", "Patch Notes 2.03.a")]
    [InlineData("Winter Update.", "Winter Update")]
    [InlineData("Patch 2.0. Read on!", "Patch 2.0 Read on")]
    [InlineData("Update 7.9.1b. It is a big one. Really!", "Update 7.9.1b It is a big one Really")]
    public void Version_only_trailing_period_and_mixed_titles(string stored, string expected)
        => Assert.Equal(expected, ReasonTokens.Sanitize(stored));

    // Sentence terminators followed by whitespace or end-of-string are still
    // removed, including quote stripping and the doubled "!!" run. AC 2.
    [Theory]
    [InlineData("Read on! Then play", "Read on Then play")]
    [InlineData("Who goes there? Find out", "Who goes there Find out")]
    [InlineData("Fixed a crash; also balance", "Fixed a crash also balance")]
    [InlineData("Hotfix!!", "Hotfix")]
    [InlineData("Notes \"quoted\" here", "Notes quoted here")]
    public void A_sentence_terminator_is_still_removed(string stored, string expected)
        => Assert.Equal(expected, ReasonTokens.Sanitize(stored));

    // Renders each title through ReasonBuilder across twelve release ids and
    // asserts one sentence inside the character budget. The release-id sweep
    // matters because the phrasing is picked by hashing the release id, so a
    // single id would only exercise one variant.
    [Theory]
    [InlineData("Dune: Awakening - 1.4.10.5 Hotfix Patch Notes")]
    [InlineData("Game Update 7.9.1b Patch Notes")]
    [InlineData("Patch Notes 2.03.a")]
    [InlineData("Patch 2.0. Read on!")]
    [InlineData("Update 7.9.1b. It is a big one. Really!")]
    public void A_quoted_title_never_breaks_the_one_sentence_contract(string stored)
    {
        for (var releaseId = 1L; releaseId <= 12; releaseId++)
        {
            var reason = ReasonBuilder.Build(
                new RecommendationReason
                {
                    Primary = ReasonSignal.PatchedSinceYouLeft,
                    Secondary = ReasonSignal.Installed,
                    Evidence = new ReasonEvidence
                    {
                        ReleaseId = releaseId,
                        Title = "Dune: Awakening",
                        PlaytimeMinutes = 200,
                        UpdatesSinceLastPlayed = 3,
                        LatestUpdateTitle = stored,
                    },
                },
                Tuning);

            Assert.Equal(1, SentenceCount(reason));
            Assert.True(reason.Length <= Tuning.ReasonCharacterBudget, reason);
        }
    }

    /// <summary>
    /// Counts sentence terminators (<c>[.!?]</c> followed by whitespace or
    /// end-of-string) outside quoted spans. Restated from
    /// ReasonContractTests so this file states the contract it is checking.
    /// </summary>
    private static int SentenceCount(string text)
    {
        var unquoted = Regex.Replace(text, "\"[^\"]*\"", "Q");
        return Regex.Matches(unquoted, @"[.!?](\s|$)").Count;
    }

    // Proves the version number reaches the rendered sentence, not just the
    // Sanitize helper. Every phrasebook variant that quotes an update title
    // must preserve the version digits.
    [Fact]
    public void The_version_survives_onto_the_card()
    {
        var quoting = ReasonPhrasebook.Variants(ReasonSignal.PatchedSinceYouLeft, ReasonClause.Primary)
            .Where(v => v.Contains("{updateTitle}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(quoting);

        foreach (var variant in quoting)
        {
            var filled = variant.Replace(
                "{updateTitle}",
                ReasonTokens.Sanitize("Game Update 7.9.1b Patch Notes"),
                StringComparison.Ordinal);

            Assert.Contains("7.9.1b", filled, StringComparison.Ordinal);
        }
    }
}
