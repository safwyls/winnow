using Xunit;

namespace Winnow.Recommend.Tests;

public class ShelfFactVarietyTests
{
    [Fact]
    public void Supporting_facts_do_not_change_primary_wording_or_consume_variants()
    {
        var supportedLedger = new ShelfReasonLedger();
        var primaryLedger = new ShelfReasonLedger();
        foreach (var signal in Enum.GetValues<ReasonSignal>())
        {
            var reason = new RecommendationReason
            {
                Primary = ReasonSignal.NeverOpened,
                Secondary = signal,
                SupportingSignals = [signal, ReasonSignal.Installed, ReasonSignal.TasteMatch],
                Evidence = new ReasonEvidence { ReleaseId = (long)signal + 1, Title = "Owned game", TasteFacetName = "Survival" },
            };
            Assert.Equal(
                ReasonBuilder.Build(reason with { Secondary = ReasonSignal.None, SupportingSignals = [] }, RecommendationTuning.Default, primaryLedger),
                ReasonBuilder.Build(reason, RecommendationTuning.Default, supportedLedger));
        }
    }
}
