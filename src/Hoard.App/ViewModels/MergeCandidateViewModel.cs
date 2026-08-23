using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.Core.Domain;
using Hoard.Covers;
using Hoard.Resolve.Matching;

namespace Hoard.App.ViewModels;

/// <summary>
/// One pending pair in the merge confirm queue (§6): two covers side by side at
/// 200×300 with the signal diff between them, and two answers — `Same game` /
/// `Different games`.
///
/// <para>Everything textual is decoded from the row's frozen
/// <c>signals_json</c>; the repositories are consulted only for the cover key,
/// which the payload does not carry. When a row has no payload — hand-written,
/// or written by an older build — the card degrades to titles from the release
/// rows and says so, rather than inventing a breakdown.</para>
/// </summary>
public partial class MergeCandidateViewModel : ObservableObject
{
    public MergeCandidateViewModel(
        MergeCandidate candidate,
        MergeSideViewModel left,
        MergeSideViewModel right,
        IReadOnlyList<MergeSignalViewModel> signals,
        SoftMatchSignalsPayload? payload)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Id = candidate.Id;
        Score = candidate.Score;
        Left = left;
        Right = right;
        Signals = signals;

        TitleSimilarity = payload?.TitleSimilarity;
        YearDelta = payload?.YearDelta;
        PublisherMatch = payload?.PublisherMatch;
        IsPriority = string.Equals(payload?.Band, nameof(SoftMatchBand.Priority), StringComparison.Ordinal);
    }

    /// <summary>The <c>merge_candidates.id</c> a decision writes its status to.</summary>
    public long Id { get; }

    /// <summary>Confidence in [0,1]. <b>Never</b> an auto-merge trigger — §5.3 has no such threshold.</summary>
    public double Score { get; }

    public MergeSideViewModel Left { get; }

    public MergeSideViewModel Right { get; }

    public IReadOnlyList<MergeSignalViewModel> Signals { get; }

    /// <summary>Normalised title similarity in [0,1], or null when no payload was recorded.</summary>
    public double? TitleSimilarity { get; }

    public int? YearDelta { get; }

    public bool? PublisherMatch { get; }

    /// <summary>
    /// The matcher put this pair in its top band, which means "show the user
    /// this one first" and nothing else. It is not a merge recommendation and
    /// the card does not present it as one.
    /// </summary>
    public bool IsPriority { get; }

    /// <summary>Confidence as the card prints it — Plex Mono, two decimals, tabular.</summary>
    public string ScoreText => Score.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>1 − similarity, the "title distance" §6 names. Em dash without a payload.</summary>
    public string TitleDistanceText => TitleSimilarity is { } similarity
        ? (1.0 - similarity).ToString("0.00", CultureInfo.InvariantCulture)
        : "—";

    public string YearDeltaText => YearDelta is { } delta
        ? string.Create(CultureInfo.InvariantCulture, $"Δ{delta}")
        : "—";

    public string PublisherMatchText => PublisherMatch switch
    {
        true => "SAME",
        false => "DIFFERENT",
        null => "—",
    };

    public bool HasSignals => Signals.Count > 0;

    /// <summary>Shown in place of the breakdown when the row carries no recorded evidence.</summary>
    public string NoSignalsMessage =>
        "No breakdown was recorded for this pair. Decide from the covers and titles, or leave it — nothing merges on its own.";

    /// <summary>Keyboard/pointer selection: 2px Volt edge, matching the grid (§8).</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Latched the moment an answer is given, so a double-click cannot write two statuses.</summary>
    [ObservableProperty]
    public partial bool IsDecided { get; set; }

    /// <summary>
    /// Builds a card from a stored row. <paramref name="fallbackTitles"/> names
    /// each release from the database, used when the payload is missing or has a
    /// side the payload does not describe.
    /// </summary>
    public static MergeCandidateViewModel Create(
        MergeCandidate candidate,
        IReadOnlyDictionary<long, string> fallbackTitles,
        IReadOnlyDictionary<long, CoverKey> coverKeys,
        ICoverCache? covers = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(fallbackTitles);
        ArgumentNullException.ThrowIfNull(coverKeys);

        SoftMatchSignalsPayload? payload;
        try
        {
            payload = SoftMatchSignalsJson.Deserialize(candidate.SignalsJson);
        }
        catch (System.Text.Json.JsonException)
        {
            // A malformed payload is a card without a breakdown, not a crash
            // that takes the whole queue down with it.
            payload = null;
        }

        var signals = new List<MergeSignalViewModel>();
        if (payload is not null)
        {
            foreach (var signal in payload.Signals)
            {
                signals.Add(MergeSignalViewModel.FromSignal(signal, payload));
            }
        }

        // The resolver canonicalises a pair to (lower id, higher id) before
        // scoring, so the payload's sides normally line up with the row's
        // columns — but a row written by hand, or by a build that did not
        // canonicalise, can be mirrored. Orient by release id so the card never
        // shows one game's cover over the other game's facts.
        var (leftJson, rightJson) =
            payload is not null
            && payload.Left.ReleaseId == candidate.RightReleaseId
            && payload.Right.ReleaseId == candidate.LeftReleaseId
                ? (payload.Right, payload.Left)
                : (payload?.Left, payload?.Right);

        return new MergeCandidateViewModel(
            candidate,
            Side(candidate.LeftReleaseId, leftJson, fallbackTitles, coverKeys, covers),
            Side(candidate.RightReleaseId, rightJson, fallbackTitles, coverKeys, covers),
            signals,
            payload);
    }

    internal void RequestCovers(double displayWidthPixels)
    {
        Left.RequestCover(displayWidthPixels);
        Right.RequestCover(displayWidthPixels);
    }

    private static MergeSideViewModel Side(
        long releaseId,
        SoftMatchSideJson? side,
        IReadOnlyDictionary<long, string> fallbackTitles,
        IReadOnlyDictionary<long, CoverKey> coverKeys,
        ICoverCache? covers)
    {
        // The payload's side is keyed by release id, so a payload written for
        // the mirrored orientation still lines up with the row's columns.
        var recorded = side?.ReleaseId == releaseId ? side : null;

        return new MergeSideViewModel(
            releaseId,
            recorded?.Title ?? fallbackTitles.GetValueOrDefault(releaseId, $"Release {releaseId}"),
            recorded?.NormalizedTitle,
            recorded?.Year,
            recorded?.Publisher,
            coverKeys.TryGetValue(releaseId, out var key) ? key : null,
            covers);
    }
}
