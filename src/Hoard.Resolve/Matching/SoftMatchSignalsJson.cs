using System.Text.Json;
using System.Text.Json.Serialization;
using Hoard.Core.Matching;

namespace Hoard.Resolve.Matching;

/// <summary>
/// The shape stored in <c>merge_candidates.signals_json</c>. It is a frozen
/// record of the evidence <i>as it stood when the pair was queued</i>: the
/// merge-confirm UI renders the diff from this, without re-scoring, so the
/// explanation the user is shown cannot drift from the score they are being
/// asked about after a threshold tune.
/// </summary>
public sealed record SoftMatchSignalsPayload
{
    /// <summary>Payload schema version, so a future breakdown change can be read back safely.</summary>
    public int Version { get; init; } = 1;

    public required double Score { get; init; }
    public required string Band { get; init; }
    public string? Veto { get; init; }

    /// <summary>Always false — recorded so the row itself carries §5.3's rule, not just the code.</summary>
    public bool AutoMergeAllowed { get; init; }

    public required SoftMatchSideJson Left { get; init; }
    public required SoftMatchSideJson Right { get; init; }

    public double TitleSimilarity { get; init; }
    public int? YearDelta { get; init; }
    public bool? PublisherMatch { get; init; }
    public int? CoverHashDistance { get; init; }

    public required IReadOnlyList<SoftMatchSignalJson> Signals { get; init; }
}

/// <summary>One side of the pair, as the UI needs to label it.</summary>
public sealed record SoftMatchSideJson
{
    public required long ReleaseId { get; init; }
    public required string Title { get; init; }
    public required string NormalizedTitle { get; init; }
    public IReadOnlyList<int> Ordinals { get; init; } = [];
    public IReadOnlyList<string> RebuildEditions { get; init; } = [];
    public IReadOnlyList<string> BundleEditions { get; init; } = [];
    public int? Year { get; init; }
    public string? Publisher { get; init; }
}

/// <summary>One signal row of the breakdown.</summary>
public sealed record SoftMatchSignalJson
{
    public required string Name { get; init; }
    public required bool Fired { get; init; }
    public double? Agreement { get; init; }
    public required double Contribution { get; init; }
    public required string Detail { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    WriteIndented = false)]
[JsonSerializable(typeof(SoftMatchSignalsPayload))]
internal sealed partial class SoftMatchJsonContext : JsonSerializerContext;

/// <summary>Builds and serialises the <c>signals_json</c> payload.</summary>
public static class SoftMatchSignalsJson
{
    public static SoftMatchSignalsPayload ToPayload(SoftMatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);

        var signals = new List<SoftMatchSignalJson>(score.Signals.Count);
        foreach (var signal in score.Signals)
        {
            signals.Add(new SoftMatchSignalJson
            {
                Name = signal.Name,
                Fired = signal.Fired,
                Agreement = signal.Agreement,
                Contribution = signal.Contribution,
                Detail = signal.Detail,
            });
        }

        return new SoftMatchSignalsPayload
        {
            Score = score.Score,
            Band = score.Band.ToString(),
            Veto = score.VetoReason,
            AutoMergeAllowed = score.AutoMergeAllowed,
            Left = Side(score.Left, score.LeftTitle),
            Right = Side(score.Right, score.RightTitle),
            TitleSimilarity = score.TitleSimilarity,
            YearDelta = score.YearDelta,
            PublisherMatch = score.PublisherMatch,
            CoverHashDistance = score.CoverHashDistance,
            Signals = signals,
        };
    }

    public static string Serialize(SoftMatchScore score)
        => JsonSerializer.Serialize(ToPayload(score), SoftMatchJsonContext.Default.SoftMatchSignalsPayload);

    public static SoftMatchSignalsPayload? Deserialize(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(json, SoftMatchJsonContext.Default.SoftMatchSignalsPayload);

    private static SoftMatchSideJson Side(MatchSubject subject, NormalizedTitle title)
        => new()
        {
            ReleaseId = subject.ReleaseId,
            Title = subject.Title,
            NormalizedTitle = title.Core,
            Ordinals = title.Ordinals,
            RebuildEditions = title.RebuildEditions,
            BundleEditions = title.BundleEditions,
            Year = subject.ReleaseYear ?? title.ParsedYear,
            Publisher = subject.Publisher,
        };
}
