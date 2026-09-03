using Winnow.Core.Domain;
using Winnow.Resolve.Matching;

namespace Winnow.App.ViewModels;

/// <summary>
/// Decodes a proposal's frozen <c>signals_json</c>. Everything the card says
/// about a match is read from this payload, never re-scored, so the reason
/// sentence cannot drift away from the score the row was queued with.
/// </summary>
public static class MergeEdgeViewModel
{
    /// <summary>
    /// Decodes a stored row's frozen breakdown. A malformed payload is a
    /// proposal without a breakdown, not a crash that takes the whole queue
    /// down.
    /// </summary>
    public static SoftMatchSignalsPayload? Parse(MergeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        try
        {
            return SoftMatchSignalsJson.Deserialize(candidate.SignalsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the matcher put a decoded payload in its top band. Feeds the
    /// confidence word and the sort; never a merge recommendation.
    /// </summary>
    public static bool IsPriorityBand(SoftMatchSignalsPayload? payload)
        => string.Equals(payload?.Band, nameof(SoftMatchBand.Priority), StringComparison.Ordinal);
}
