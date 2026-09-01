using System.Text.RegularExpressions;
using Xunit;

namespace Winnow.Recommend.Tests;

/// <summary>
/// The explanation contract (F37) and the sameness the user actually
/// complained about. One sentence, inside a budget, for every combination of
/// signals the model can produce — and two cards in one session that do not
/// read as siblings.
/// </summary>
public class ReasonContractTests : IDisposable
{
    private static readonly RecommendationTuning Tuning = RecommendationTuning.Default;
    private static readonly DateTime AsOf = RecommendHarness.AsOf;

    /// <summary>Primaries the selector can choose. Nothing else ever opens a sentence.</summary>
    private static readonly ReasonSignal[] Primaries =
    [
        ReasonSignal.PatchedSinceYouLeft,
        ReasonSignal.Bounced,
        ReasonSignal.Sampled,
        ReasonSignal.NeverOpened,
        ReasonSignal.LaunchedUnmeasured,
        ReasonSignal.ProbablyDone,
    ];

    /// <summary>Secondaries the selector can choose, plus the no-secondary case.</summary>
    private static readonly ReasonSignal[] Secondaries =
    [
        ReasonSignal.None,
        ReasonSignal.TriedToLikeIt,
        ReasonSignal.TasteMatch,
        ReasonSignal.BoughtTwice,
        ReasonSignal.Installed,
        ReasonSignal.Dormant,
        ReasonSignal.UndatedDormancy,
        ReasonSignal.OnlineOnlyMismatch,
        ReasonSignal.SoloOnlyMismatch,
        ReasonSignal.PlayedRecently,
        ReasonSignal.ShownRecently,
    ];

    private readonly RecommendHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    // ── The contract ───────────────────────────────────────────────────────

    [Fact]
    public void Every_producible_combination_renders_exactly_one_sentence_inside_the_budget()
    {
        var checkedCount = 0;

        foreach (var primary in Primaries)
        {
            foreach (var secondary in Secondaries)
            {
                // Several evidence shapes per pair: the same signals on a game
                // that knows everything about itself and on one that knows
                // almost nothing must both render.
                foreach (var evidence in EvidenceShapes())
                {
                    var reason = ReasonBuilder.Build(
                        new RecommendationReason
                        {
                            Primary = primary,
                            Secondary = secondary,
                            Evidence = evidence,
                        },
                        Tuning);

                    var label = $"{primary}/{secondary}: {reason}";

                    Assert.False(string.IsNullOrWhiteSpace(reason), label);
                    Assert.EndsWith(".", reason, StringComparison.Ordinal);
                    Assert.Equal(1, SentenceCount(reason));
                    Assert.True(reason.Length <= Tuning.ReasonCharacterBudget,
                        $"{reason.Length} chars over a {Tuning.ReasonCharacterBudget} budget — {label}");
                    Assert.DoesNotContain("{", reason, StringComparison.Ordinal);
                    checkedCount++;
                }
            }
        }

        Assert.True(checkedCount >= Primaries.Length * Secondaries.Length, "the sweep must be exhaustive");
    }

    [Fact]
    public void Every_phrasing_is_one_clause_and_every_list_has_a_fallback()
    {
        foreach (var (signal, clause) in Primaries.Select(s => (s, ReasonClause.Primary))
            .Concat(Secondaries.Where(s => s != ReasonSignal.None).Select(s => (s, ReasonClause.Secondary))))
        {
            var variants = ReasonPhrasebook.Variants(signal, clause);
            Assert.NotEmpty(variants);

            // A variant whose tokens a given game cannot fill is skipped, so
            // every list needs one that asks for nothing.
            Assert.Contains(variants, v => v.IndexOf('{') < 0);

            foreach (var variant in variants)
            {
                Assert.DoesNotContain('\n', variant);
                Assert.Equal(0, SentenceCount(variant));

                if (clause == ReasonClause.Primary)
                {
                    // A primary may open on a token — the builder capitalises
                    // after filling — but never on a joiner.
                    Assert.False(variant[0] is ',' or ';' or '—' or ' ',
                        $"a primary clause opens the sentence: {variant}");
                }
                else
                {
                    Assert.True(variant[0] is ',' or ' ' or '—',
                        $"a secondary clause carries its own joiner: {variant}");
                }
            }
        }
    }

    [Fact]
    public void A_patched_card_always_names_what_landed()
    {
        // The one fact no storefront can compute. Whatever phrasing a game's
        // id selects, the update must survive into the sentence.
        for (var releaseId = 1; releaseId <= 40; releaseId++)
        {
            var reason = ReasonBuilder.Build(
                new RecommendationReason
                {
                    Primary = ReasonSignal.PatchedSinceYouLeft,
                    Secondary = ReasonSignal.Installed,
                    Evidence = Rich(releaseId),
                },
                Tuning);

            Assert.True(
                reason.Contains("Deep Water Update", StringComparison.Ordinal)
                    || reason.Contains("3 updates", StringComparison.Ordinal)
                    || reason.Contains(" 3 ", StringComparison.Ordinal),
                reason);
        }
    }

    // ── Anti-sameness ──────────────────────────────────────────────────────

    [Fact]
    public async Task Genuinely_different_histories_produce_genuinely_different_sentences()
    {
        var asOf = RecommendHarness.AsOf;

        // Ten games with ten different stories, not ten rows with different
        // numbers in them.
        var patched = await _harness.SeedGameAsync("Signal Lost", minutes: 40, lastPlayed: asOf.AddYears(-3));
        await _harness.SeedMajorUpdateAsync(patched, asOf.AddMonths(-2), "Deep Water Update");

        var persistent = await _harness.SeedGameAsync("Six Evenings", minutes: 260, lastPlayed: asOf.AddYears(-2));
        for (var i = 0; i < 6; i++)
        {
            await _harness.SeedSnapshotAsync(persistent, minutes: 280 + i * 20, observedAt: asOf.AddDays(-400 + i));
        }

        await _harness.SeedGameAsync("One Long Sitting", minutes: 900, lastPlayed: asOf.AddYears(-6));
        await _harness.SeedGameAsync("Barely Started", minutes: 22, lastPlayed: asOf.AddYears(-4));
        await _harness.SeedGameAsync("Installed And Sealed", installed: true);
        await _harness.SeedGameAsync("Sealed Forever");
        await _harness.SeedGameAsync("Pre Timestamp Relic", minutes: 700, lastPlayed: null);
        await _harness.SeedGameAsync("Opened But Unmeasured", minutes: 0, lastPlayed: asOf.AddYears(-2));

        var twice = await _harness.SeedGameAsync("Paid For Twice", minutes: 400, lastPlayed: asOf.AddYears(-5));
        await _harness.SeedSecondStoreAsync(twice, "gog");

        var done = await _harness.SeedGameAsync("Forty Hours And Out", minutes: 2_600, lastPlayed: asOf.AddYears(-7));
        await _harness.SeedUpdateCoverageAsync(done, asOf.AddYears(-8));

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        var reasons = feed.Items.Select(i => i.Reason).ToList();

        Assert.Equal(10, reasons.Count);

        // Not the same sentence twice.
        Assert.Equal(reasons.Count, reasons.Distinct(StringComparer.Ordinal).Count());

        // And — the actual complaint — not the same FRAME with the nouns
        // swapped. Masking every number, quoted title and proper noun must
        // still leave sentences that differ structurally.
        var skeletons = reasons.Select(Skeleton).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(skeletons.Count >= 8,
            $"only {skeletons.Count} distinct frames across 10 different histories:\n"
                + string.Join('\n', reasons));
    }

    [Fact]
    public async Task Even_identical_shelfware_does_not_render_as_one_repeated_line()
    {
        // The hardest case in the app and the most numerous: never-opened
        // games have no facts to tell them apart, so the phrasing itself has to
        // vary or a screenful of them is one sentence twelve times.
        for (var i = 0; i < 12; i++)
        {
            await _harness.SeedGameAsync($"Sealed {i:00}");
        }

        var feed = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());
        var distinct = feed.Items.Select(i => i.Reason).Distinct(StringComparer.Ordinal).Count();

        Assert.True(distinct >= 3, $"only {distinct} distinct never-opened phrasings across 12 cards");
    }

    [Fact]
    public async Task The_same_game_reads_the_same_way_on_every_reload()
    {
        var asOf = RecommendHarness.AsOf;
        await _harness.SeedGameAsync("Stable Prose", minutes: 300, lastPlayed: asOf.AddYears(-3));
        await _harness.SeedGameAsync("Also Stable", minutes: 40, lastPlayed: asOf.AddYears(-2));

        var first = await _harness.Engine.GetFeedAsync(RecommendHarness.Request());

        // A different day deals a different hand, but it must not reword the
        // cards: the variant is chosen from the game, never from the seed.
        var tomorrow = await _harness.Engine.GetFeedAsync(
            RecommendHarness.Request() with { ShuffleSeed = 99 });

        foreach (var item in first.Items)
        {
            Assert.Equal(item.Reason, tomorrow.Items.Single(i => i.ReleaseId == item.ReleaseId).Reason);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Sentence-ending punctuation outside quoted spans. The one-sentence contract, made countable.</summary>
    private static int SentenceCount(string text)
    {
        var unquoted = Regex.Replace(text, "\"[^\"]*\"", "Q");
        return Regex.Matches(unquoted, @"[.!?](\s|$)").Count;
    }

    /// <summary>The sentence with every game-specific noun and number masked, leaving only its shape.</summary>
    private static string Skeleton(string reason)
    {
        var masked = Regex.Replace(reason, "\"[^\"]*\"", "\"T\"");
        masked = Regex.Replace(masked, @"\d+(\.\d+)?", "#");
        masked = Regex.Replace(masked, @"\b[A-Z][a-z]+\b", "N");
        return masked.ToLowerInvariant();
    }

    private static IEnumerable<ReasonEvidence> EvidenceShapes()
    {
        yield return Rich(1);
        yield return Rich(2) with
        {
            LastPlayedYear = null,
            DormancyDays = null,
            UpdatesSinceLastPlayed = null,
            LatestUpdateTitle = null,
            ReturnEpisodes = null,
            StoreCount = 1,
            TasteFacetName = null,
        };
        yield return Rich(3) with { PlaytimeMinutes = 0, LatestUpdateTitle = null };
        yield return Rich(4) with
        {
            // The worst case for the budget: a store-authored headline at the
            // quoting cap, on a game that also has everything else to say.
            LatestUpdateTitle = "The Very Long Anniversary Overhaul Update, Part Two: Electric Boogaloo",
            PlaytimeMinutes = 12_345,
            UpdatesSinceLastPlayed = 17,
            ReturnEpisodes = 23,
            TasteFacetName = "Immersive Sim",
        };
        yield return Rich(5) with { Title = "A Game With A Really Rather Long Name Indeed" };
    }

    private static ReasonEvidence Rich(long releaseId) => new()
    {
        ReleaseId = releaseId,
        Title = "Subnautica",
        Store = "steam",
        PlaytimeMinutes = 40,
        LastPlayedYear = 2023,
        DormancyDays = 1_100,
        UpdatesSinceLastPlayed = 3,
        LatestUpdateTitle = "Deep Water Update",
        ReturnEpisodes = 6,
        StoreCount = 2,
        TasteFacetName = "Survival",
    };
}
