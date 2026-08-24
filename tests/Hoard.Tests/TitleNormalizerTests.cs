using Hoard.Core.Matching;
using Xunit;

namespace Hoard.Tests;

/// <summary>
/// Normalisation is where the soft matcher's precision is actually won or lost
/// (§5.3 step 2, "normalised title"). These tests pin the two structural
/// decisions the scoring model leans on: the sequel ordinal and the edition
/// marker are pulled OUT of the comparable core rather than folded into it, so
/// they can be compared exactly instead of fuzzily.
/// </summary>
public sealed class TitleNormalizerTests
{
    [Theory]
    [InlineData("The Witcher 3: Wild Hunt", "witcher 3 wild hunt")]
    [InlineData("The Witcher III: Wild Hunt", "witcher 3 wild hunt")]
    [InlineData("Assassin's Creed® IV Black Flag™", "assassins creed 4 black flag")]
    [InlineData("S.T.A.L.K.E.R.: Shadow of Chernobyl", "stalker shadow of chernobyl")]
    [InlineData("Pokémon Trading Card Game Live", "pokemon trading card game live")]
    [InlineData("Command & Conquer: Red Alert 2", "command and conquer red alert 2")]
    [InlineData("Half-Life", "half life")]
    [InlineData("Counter-Strike: Global Offensive", "counter strike global offensive")]
    [InlineData("A Way Out", "way out")]
    [InlineData("Grand Theft Auto V", "grand theft auto 5")]
    [InlineData("Deus Ex: Human Revolution — Director's Cut", "deus ex human revolution")]
    public void CoreNormalisation(string title, string expectedCore)
        => Assert.Equal(expectedCore, TitleNormalizer.Normalize(title).Core);

    /// <summary>
    /// The sequel number lives in its own field. This is the whole reason
    /// "Portal" and "Portal 2" can be told apart — as strings they are 86%
    /// alike, which no threshold can safely separate from a real near-match.
    /// </summary>
    [Theory]
    [InlineData("Portal", new int[0])]
    [InlineData("Portal 2", new[] { 2 })]
    [InlineData("Dark Souls III", new[] { 3 })]
    [InlineData("Left 4 Dead 2", new[] { 4, 2 })]
    [InlineData("Civilization VI", new[] { 6 })]
    [InlineData("Grand Theft Auto V", new[] { 5 })]
    // Spelled-out cardinals fold too, so the ordinal veto is consistent with
    // itself: "Episode II" vs "Episode III" was always caught, and now
    // "Episode One" vs "Episode Two" is as well.
    [InlineData("Half-Life 2: Episode One", new[] { 2, 1 })]
    [InlineData("Half-Life 2: Episode Two", new[] { 2, 2 })]
    [InlineData("The Walking Dead: Season Two", new[] { 2 })]
    public void OrdinalsAreExtracted(string title, int[] expected)
        => Assert.Equal(expected, TitleNormalizer.Normalize(title).Ordinals);

    /// <summary>
    /// A bare "X" is a name at least as often as it is a ten. Folding it made
    /// <c>Mega Man X</c> and <c>Mega Man 10</c> the same normalised string —
    /// similarity 1.00, with nothing distinguishable left for a veto to catch —
    /// so it is left alone. The cost is that <c>Final Fantasy X</c> no longer
    /// matches <c>Final Fantasy 10</c>: §5.3 is precision over recall, always.
    /// </summary>
    [Theory]
    [InlineData("Mega Man X", "mega man x", new int[0])]
    [InlineData("Mega Man 10", "mega man 10", new[] { 10 })]
    [InlineData("Final Fantasy X", "final fantasy x", new int[0])]
    [InlineData("XCOM 2", "xcom 2", new[] { 2 })]
    public void ABareXIsALetterNotATen(string title, string expectedCore, int[] expectedOrdinals)
    {
        var normalized = TitleNormalizer.Normalize(title);

        Assert.Equal(expectedCore, normalized.Core);
        Assert.Equal(expectedOrdinals, normalized.Ordinals);
    }

    /// <summary>
    /// A one-letter numeral at the FRONT of a title is the title. The guard
    /// used to cover only "I"; "V Rising" and "X Rebirth" are the same mistake
    /// with different letters. A number word in front is the same case again —
    /// "Five Nights at Freddy's" is not the fifth Nights.
    /// </summary>
    [Theory]
    [InlineData("I Am Setsuna", "i am setsuna")]
    [InlineData("V Rising", "v rising")]
    [InlineData("X Rebirth", "x rebirth")]
    [InlineData("Five Nights at Freddy's", "five nights at freddys")]
    [InlineData("Two Point Hospital", "two point hospital")]
    [InlineData("One Piece Odyssey", "one piece odyssey")]
    public void ALeadingNumeralIsTheTitleNotASequelNumber(string title, string expectedCore)
    {
        var normalized = TitleNormalizer.Normalize(title);

        Assert.Equal(expectedCore, normalized.Core);
        Assert.Empty(normalized.Ordinals);
    }

    /// <summary>
    /// Number-word folding runs AFTER edition extraction, so "Day One Edition"
    /// is lifted out whole instead of being shredded into "day 1 edition" —
    /// which would leave a stray ordinal on every such title and veto it
    /// against the plain edition it is a bundle of.
    /// </summary>
    [Fact]
    public void DayOneEditionSurvivesNumberWordFolding()
    {
        var normalized = TitleNormalizer.Normalize("Dying Light Day One Edition");

        Assert.Contains("day one edition", normalized.BundleEditions);
        Assert.Equal("dying light", normalized.Core);
        Assert.Empty(normalized.Ordinals);
    }

    /// <summary>
    /// Roman folding must not fire on ordinary words. "Mix" parses as M+IX
    /// (1009) under a naive reader, and a leading "I" is a pronoun far more
    /// often than it is a sequel number.
    /// </summary>
    [Theory]
    [InlineData("DJ Mix Tour", "dj mix tour")]
    [InlineData("I Am Setsuna", "i am setsuna")]
    [InlineData("Civil War", "civil war")]
    [InlineData("Lid Simulator", "lid simulator")]
    [InlineData("Dim Light", "dim light")]
    public void RomanFoldingDoesNotFireOnOrdinaryWords(string title, string expectedCore)
        => Assert.Equal(expectedCore, TitleNormalizer.Normalize(title).Core);

    /// <summary>
    /// Edition markers are split into two tiers because they mean two different
    /// things. A rebuild (Special Edition, Remastered, Anniversary) is a
    /// separate Release with its own achievement set — merging is a bug, §9
    /// pitfall 5. A bundle (GOTY, Complete, Deluxe) is the same build plus
    /// content, and whether to merge is the user's call.
    /// </summary>
    [Theory]
    [InlineData("The Elder Scrolls V: Skyrim Special Edition", "special edition")]
    [InlineData("The Elder Scrolls V: Skyrim Anniversary Edition", "anniversary edition")]
    [InlineData("Dark Souls: Remastered", "remastered")]
    [InlineData("Baldur's Gate: Enhanced Edition", "enhanced edition")]
    [InlineData("Age of Empires II: Definitive Edition", "definitive edition")]
    [InlineData("Halo: Combat Evolved Anniversary", "anniversary")]
    public void RebuildEditionsAreLiftedOut(string title, string expectedMarker)
    {
        var normalized = TitleNormalizer.Normalize(title);

        Assert.Contains(expectedMarker, normalized.RebuildEditions);
        Assert.Empty(normalized.BundleEditions);
        Assert.DoesNotContain("edition", normalized.Tokens);
    }

    [Theory]
    [InlineData("Borderlands 2: Game of the Year Edition", "game of the year edition")]
    [InlineData("Fallout: New Vegas Ultimate Edition", "ultimate edition")]
    [InlineData("The Witcher 2 GOTY", "goty")]
    [InlineData("Deus Ex: Human Revolution - Director's Cut", "directors cut")]
    [InlineData("Grand Theft Auto V: Premium Edition", "premium edition")]
    public void BundleEditionsAreLiftedOutSeparately(string title, string expectedMarker)
    {
        var normalized = TitleNormalizer.Normalize(title);

        Assert.Contains(expectedMarker, normalized.BundleEditions);
        Assert.Empty(normalized.RebuildEditions);
    }

    /// <summary>
    /// An edition word IS the title when nothing precedes it. Stripping
    /// "Classic" out of "Classic" would leave every such game matching every
    /// other one.
    /// </summary>
    [Fact]
    public void AnEditionWordAloneIsNotTreatedAsAMarker()
    {
        var normalized = TitleNormalizer.Normalize("Classic");

        Assert.Equal("classic", normalized.Core);
        Assert.Empty(normalized.RebuildEditions);
    }

    [Fact]
    public void ParenthesisedYearsAreLiftedOutButBareTrailingYearsAreNot()
    {
        var disambiguated = TitleNormalizer.Normalize("Prey (2006)");
        Assert.Equal(2006, disambiguated.ParsedYear);
        Assert.Equal("prey", disambiguated.Core);

        var annual = TitleNormalizer.Normalize("Madden NFL 2004");
        Assert.Null(annual.ParsedYear);
        Assert.Equal("madden nfl 2004", annual.Core);
        Assert.Equal([2004], annual.Ordinals);
    }

    [Theory]
    [InlineData("Bethesda Softworks LLC", "bethesda softworks")]
    [InlineData("bethesda softworks", "bethesda softworks")]
    [InlineData("CD PROJEKT RED", "cd projekt red")]
    [InlineData("Devolver Digital, Inc.", "devolver digital")]
    [InlineData(null, "")]
    public void PublisherNormalisationDropsLegalForm(string? publisher, string expected)
        => Assert.Equal(expected, TitleNormalizer.NormalizePublisher(publisher));

    [Fact]
    public void NormalisationIsTotal_NoInputThrows()
    {
        foreach (var input in new[] { null, "", "   ", "™®", "!!!", "…", "🎮" })
        {
            var normalized = TitleNormalizer.Normalize(input);
            Assert.NotNull(normalized.Core);
            Assert.NotNull(normalized.Tokens);
        }
    }
}
