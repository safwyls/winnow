using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Merging;
using Winnow.Resolve.Matching;

namespace Winnow.App.ViewModels;

/// <summary>
/// One edge of a group: the matcher's evidence that two of its members are the
/// same game. Everything textual is decoded from the row's frozen
/// <c>signals_json</c>, never re-scored, so the explanation cannot drift away
/// from the score being asked about.
///
/// <para>Two presentations of the same evidence. <see cref="Signals"/> is the
/// full breakdown the pair card has always shown. <see cref="SummaryText"/> is
/// the same values on one line, which is what a roster of six members can
/// afford before the card turns into a table.</para>
/// </summary>
public sealed class MergeEdgeViewModel
{
    private MergeEdgeViewModel(
        MergeGroupEdge edge,
        IReadOnlyList<MergeSignalViewModel> signals,
        SoftMatchSignalsPayload? payload)
    {
        Edge = edge;
        Signals = signals;
        TitleSimilarity = payload?.TitleSimilarity;
        YearDelta = payload?.YearDelta;
        PublisherMatch = payload?.PublisherMatch;
    }

    /// <summary>The resolved edge this evidence belongs to.</summary>
    public MergeGroupEdge Edge { get; }

    /// <summary>The <c>merge_candidates.id</c> an answer writes to.</summary>
    public long CandidateId => Edge.CandidateId;

    /// <summary>Confidence in [0,1]. Never an auto-merge trigger.</summary>
    public double Score => Edge.Score;

    /// <summary>The matcher put this pair in its top band.</summary>
    public bool IsPriority => Edge.IsPriority;

    /// <summary>The full breakdown, one row per signal.</summary>
    public IReadOnlyList<MergeSignalViewModel> Signals { get; }

    /// <summary>Normalised title similarity in [0,1], or null when no payload was recorded.</summary>
    public double? TitleSimilarity { get; }

    /// <summary>Year difference between the two sides, or null when unknown.</summary>
    public int? YearDelta { get; }

    /// <summary>Whether the two publishers agree, or null when unknown.</summary>
    public bool? PublisherMatch { get; }

    /// <summary>True when the row carried a recorded breakdown.</summary>
    public bool HasSignals => Signals.Count > 0;

    /// <summary>Confidence as the card prints it: Plex Mono, two decimals, tabular.</summary>
    public string ScoreText => Score.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>1 - similarity, the title distance. Em dash without a payload.</summary>
    public string TitleDistanceText => TitleSimilarity is { } similarity
        ? (1.0 - similarity).ToString("0.00", CultureInfo.InvariantCulture)
        : "—";

    /// <summary>Year delta as the card prints it.</summary>
    public string YearDeltaText => YearDelta is { } delta
        ? string.Create(CultureInfo.InvariantCulture, $"Δ{delta}")
        : "—";

    /// <summary>Publisher verdict as the card prints it.</summary>
    public string PublisherMatchText => PublisherMatch switch
    {
        true => MergeCopy.PublisherSame,
        false => MergeCopy.PublisherDifferent,
        null => "—",
    };

    /// <summary>
    /// The whole diff on one line, in the data face: title distance, year delta,
    /// publisher verdict. What a roster row shows in place of the full grid.
    /// </summary>
    public string SummaryText => string.Format(
        CultureInfo.CurrentCulture,
        MergeCopy.EdgeSummaryFormat,
        TitleDistanceText,
        YearDeltaText,
        PublisherMatchText);

    /// <summary>Shown in place of the breakdown when the row carries no recorded evidence.</summary>
    public string NoSignalsMessage => MergeCopy.NoSignals;

    /// <summary>
    /// Decodes a stored row's frozen breakdown. A malformed payload is an edge
    /// without a breakdown, not a crash that takes the whole queue down.
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
    /// Whether the matcher put a decoded payload in its top band. Used only to
    /// decide what may arrive already checked; never a merge recommendation.
    /// </summary>
    public static bool IsPriorityBand(SoftMatchSignalsPayload? payload)
        => string.Equals(payload?.Band, nameof(SoftMatchBand.Priority), StringComparison.Ordinal);

    /// <summary>Builds one edge's evidence from its already-decoded payload.</summary>
    public static MergeEdgeViewModel Create(
        MergeGroupEdge edge, SoftMatchSignalsPayload? payload)
    {
        ArgumentNullException.ThrowIfNull(edge);

        var signals = new List<MergeSignalViewModel>();
        if (payload is not null)
        {
            foreach (var signal in payload.Signals)
            {
                signals.Add(MergeSignalViewModel.FromSignal(signal, payload));
            }
        }

        return new MergeEdgeViewModel(edge, signals, payload);
    }
}
