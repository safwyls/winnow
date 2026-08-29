using System.Globalization;
using System.Numerics;
using Winnow.Core.Matching;

namespace Winnow.Resolve.Matching;

/// <summary>
/// Pure, stateless soft matcher (§5.3 step 2). Given two <see cref="MatchSubject"/>s,
/// returns a confidence score in [0,1] with an itemised signal breakdown.
/// Cannot merge anything -- holds no repository or write path. Vetoes on
/// sequel ordinal mismatch, rebuild edition mismatch, or title below floor.
/// Symmetric: <c>Score(a, b)</c> equals <c>Score(b, a)</c>.
/// </summary>
public sealed class SoftMatcher
{
    private readonly SoftMatchThresholds _thresholds;

    public SoftMatcher(SoftMatchThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? SoftMatchThresholds.Default;
        _thresholds.Validate();
    }

    public SoftMatchThresholds Thresholds => _thresholds;

    /// <summary>Scores one candidate pair. Missing metadata is a signal that does not fire, not an error.</summary>
    public SoftMatchScore Score(MatchSubject left, MatchSubject right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        var leftTitle = TitleNormalizer.Normalize(left.Title);
        var rightTitle = TitleNormalizer.Normalize(right.Title);

        var titleSimilarity = Similarity(leftTitle, rightTitle);

        var leftYear = left.ReleaseYear ?? leftTitle.ParsedYear;
        var rightYear = right.ReleaseYear ?? rightTitle.ParsedYear;
        int? yearDelta = leftYear is not null && rightYear is not null
            ? Math.Abs(leftYear.Value - rightYear.Value)
            : null;

        var leftPublisher = TitleNormalizer.NormalizePublisher(left.Publisher);
        var rightPublisher = TitleNormalizer.NormalizePublisher(right.Publisher);
        bool? publisherMatch =
            !string.IsNullOrEmpty(leftPublisher) && !string.IsNullOrEmpty(rightPublisher)
                ? string.Equals(leftPublisher, rightPublisher, StringComparison.Ordinal)
                : null;

        int? coverDistance = left.CoverPerceptualHash is not null && right.CoverPerceptualHash is not null
            ? BitOperations.PopCount(left.CoverPerceptualHash.Value ^ right.CoverPerceptualHash.Value)
            : null;

        // Signals are built even for vetoed pairs, for diagnostics.
        var signals = new List<SoftMatchSignal>(5)
        {
            TitleSignal(titleSimilarity, leftTitle, rightTitle),
            YearSignal(leftYear, rightYear, yearDelta),
            PublisherSignal(leftPublisher, rightPublisher, publisherMatch),
            CoverSignal(coverDistance),
            BundleEditionSignal(leftTitle, rightTitle),
        };

        var veto = FindVeto(left, right, leftTitle, rightTitle, titleSimilarity);

        double score;
        if (veto is not null)
        {
            score = 0.0;
        }
        else
        {
            score = 0.0;
            foreach (var signal in signals)
            {
                score += signal.Contribution;
            }

            score = Math.Clamp(score, 0.0, 1.0);
        }

        var band = veto is not null || score < _thresholds.QueueFloor
            ? SoftMatchBand.Discarded
            : score >= _thresholds.PriorityThreshold
                ? SoftMatchBand.Priority
                : SoftMatchBand.Review;

        return new SoftMatchScore
        {
            Left = left,
            Right = right,
            LeftTitle = leftTitle,
            RightTitle = rightTitle,
            Score = score,
            Band = band,
            VetoReason = veto,
            Signals = signals,
            TitleSimilarity = titleSimilarity,
            YearDelta = yearDelta,
            PublisherMatch = publisherMatch,
            CoverHashDistance = coverDistance,
        };
    }

    /// <summary>
    /// Scores <paramref name="subject"/> against every possibility, returned
    /// best-first (score descending, release id ascending). Includes discarded
    /// scores for diagnostics.
    /// </summary>
    public IReadOnlyList<RankedMatch> Rank(MatchSubject subject, IEnumerable<MatchSubject> possibilities)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(possibilities);

        var ranked = new List<RankedMatch>();
        foreach (var possibility in possibilities)
        {
            ranked.Add(new RankedMatch(possibility, Score(subject, possibility)));
        }

        ranked.Sort(static (a, b) =>
        {
            var byScore = b.Score.Score.CompareTo(a.Score.Score);
            return byScore != 0 ? byScore : a.Possibility.ReleaseId.CompareTo(b.Possibility.ReleaseId);
        });

        return ranked;
    }

    // ── Vetoes ───────────────────────────────────────────────────────────────

    private string? FindVeto(
        MatchSubject left,
        MatchSubject right,
        NormalizedTitle leftTitle,
        NormalizedTitle rightTitle,
        double titleSimilarity)
    {
        if (left.ReleaseId == right.ReleaseId)
        {
            return SoftMatchVetoReasons.SameRelease;
        }

        if (leftTitle.IsEmpty || rightTitle.IsEmpty)
        {
            return SoftMatchVetoReasons.EmptyTitle;
        }

        // Exact, ordered comparison. "Left 4 Dead" [4] is not "Left 4 Dead 2"
        // [4,2]; "Portal" [] is not "Portal 2" [2].
        if (!leftTitle.Ordinals.SequenceEqual(rightTitle.Ordinals))
        {
            return SoftMatchVetoReasons.SequelOrdinal;
        }

        if (!leftTitle.RebuildEditions.SequenceEqual(rightTitle.RebuildEditions, StringComparer.Ordinal))
        {
            return SoftMatchVetoReasons.RebuildEdition;
        }

        return titleSimilarity < _thresholds.TitleSimilarityFloor
            ? SoftMatchVetoReasons.TitleBelowFloor
            : null;
    }

    // ── Signals ──────────────────────────────────────────────────────────────

    private SoftMatchSignal TitleSignal(double similarity, NormalizedTitle left, NormalizedTitle right)
        => new(
            SoftMatchSignalNames.Title,
            Fired: true,
            Agreement: similarity,
            Contribution: similarity * _thresholds.TitleWeight,
            Detail: string.Create(
                CultureInfo.InvariantCulture,
                $"\"{left.Core}\" vs \"{right.Core}\" ({similarity:P0} similar)"));

    private SoftMatchSignal YearSignal(int? leftYear, int? rightYear, int? delta)
    {
        if (delta is null)
        {
            return new SoftMatchSignal(
                SoftMatchSignalNames.ReleaseYear,
                Fired: false,
                Agreement: null,
                Contribution: 0.0,
                Detail: leftYear is null && rightYear is null
                    ? "no release year on either side"
                    : $"release year known on one side only ({leftYear?.ToString(CultureInfo.InvariantCulture) ?? "?"} vs {rightYear?.ToString(CultureInfo.InvariantCulture) ?? "?"})");
        }

        var d = delta.Value;
        var (contribution, agreement) = d switch
        {
            0 => (_thresholds.YearExactBonus, 1.0),
            1 => (_thresholds.YearAdjacentBonus, 0.75),
            _ when d <= _thresholds.YearNearMaxDelta => (_thresholds.YearNearPenalty, 0.25),
            _ => (_thresholds.YearFarPenalty, 0.0),
        };

        return new SoftMatchSignal(
            SoftMatchSignalNames.ReleaseYear,
            Fired: true,
            Agreement: agreement,
            Contribution: contribution,
            Detail: $"{leftYear} vs {rightYear} (Δ{d})");
    }

    private SoftMatchSignal PublisherSignal(string leftPublisher, string rightPublisher, bool? match)
    {
        if (match is null)
        {
            return new SoftMatchSignal(
                SoftMatchSignalNames.Publisher,
                Fired: false,
                Agreement: null,
                Contribution: 0.0,
                Detail: "publisher unknown on at least one side");
        }

        return new SoftMatchSignal(
            SoftMatchSignalNames.Publisher,
            Fired: true,
            Agreement: match.Value ? 1.0 : 0.0,
            Contribution: match.Value ? _thresholds.PublisherMatchBonus : _thresholds.PublisherMismatchPenalty,
            Detail: match.Value
                ? $"both \"{leftPublisher}\""
                : $"\"{leftPublisher}\" vs \"{rightPublisher}\"");
    }

    private SoftMatchSignal CoverSignal(int? distance)
    {
        if (distance is null)
        {
            return new SoftMatchSignal(
                SoftMatchSignalNames.CoverHash,
                Fired: false,
                Agreement: null,
                Contribution: 0.0,
                Detail: "no cover hash on at least one side");
        }

        var d = distance.Value;
        var contribution =
            d <= _thresholds.CoverStrongMaxDistance ? _thresholds.CoverStrongBonus
            : d <= _thresholds.CoverWeakMaxDistance ? _thresholds.CoverWeakBonus
            : d >= _thresholds.CoverMismatchMinDistance ? _thresholds.CoverMismatchPenalty
            : 0.0;

        return new SoftMatchSignal(
            SoftMatchSignalNames.CoverHash,
            Fired: true,
            // Agreement over the useful 0-32 half of the 64-bit range.
            Agreement: Math.Clamp(1.0 - (d / 32.0), 0.0, 1.0),
            Contribution: contribution,
            Detail: $"Hamming distance {d}/64");
    }

    private SoftMatchSignal BundleEditionSignal(NormalizedTitle left, NormalizedTitle right)
    {
        var same = left.BundleEditions.SequenceEqual(right.BundleEditions, StringComparer.Ordinal);
        if (same)
        {
            return new SoftMatchSignal(
                SoftMatchSignalNames.BundleEdition,
                Fired: left.BundleEditions.Count > 0,
                Agreement: 1.0,
                Contribution: 0.0,
                Detail: left.BundleEditions.Count == 0
                    ? "neither side is an edition bundle"
                    : $"both \"{string.Join(", ", left.BundleEditions)}\"");
        }

        return new SoftMatchSignal(
            SoftMatchSignalNames.BundleEdition,
            Fired: true,
            Agreement: 0.0,
            Contribution: _thresholds.BundleEditionMismatchPenalty,
            Detail: $"\"{Describe(left.BundleEditions)}\" vs \"{Describe(right.BundleEditions)}\"");

        static string Describe(IReadOnlyList<string> editions)
            => editions.Count == 0 ? "(none)" : string.Join(", ", editions);
    }

    // ── String similarity ────────────────────────────────────────────────────

    /// <summary>Average of normalised edit distance and token Dice coefficient.</summary>
    internal static double Similarity(NormalizedTitle left, NormalizedTitle right)
    {
        if (left.IsEmpty || right.IsEmpty)
        {
            return 0.0;
        }

        return (0.5 * NormalizedEditDistance(left.Core, right.Core))
            + (0.5 * TokenDice(left.Tokens, right.Tokens));
    }

    private static double NormalizedEditDistance(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 1.0 : 1.0 - ((double)Levenshtein(a, b) / longest);
    }

    /// <summary>Two-row Levenshtein. Symmetric by construction, so A→B and B→A agree exactly.</summary>
    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Dice coefficient over the token multiset: 2·|A ∩ B| / (|A| + |B|).</summary>
    private static double TokenDice(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0.0;
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in a)
        {
            counts[token] = counts.TryGetValue(token, out var n) ? n + 1 : 1;
        }

        var shared = 0;
        foreach (var token in b)
        {
            if (counts.TryGetValue(token, out var n) && n > 0)
            {
                counts[token] = n - 1;
                shared++;
            }
        }

        return 2.0 * shared / (a.Count + b.Count);
    }
}
