using Winnow.Core.Queries;

namespace Winnow.Core.Domain;

/// <summary>Source image metadata. Null attributes mean the source did not report them.</summary>
public sealed record GameImage
{
    public required string ImageId { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool? AlphaChannel { get; init; }
    public bool? Animated { get; init; }
    public string? ImageType { get; init; }
    /// <summary>Optional source asset URL when its image ID alone does not identify the CDN file.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// One source's screenshot or artwork list for a work. Projected from
/// <c>work_images</c> (migration 0028), one row per (work, source, kind),
/// the same arrangement <see cref="WorkMaturity"/> has with
/// <c>work_maturity</c>.
/// </summary>
public sealed record WorkImages
{
    /// <summary>The work these images belong to.</summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// Which source reported them (an <see cref="ImageSources"/> value).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Screenshot or artwork (an <see cref="ImageKinds"/> value). A game can
    /// have one and not the other.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Comma-joined source image IDs in source order. Optional CDN URLs live
    /// beside the matching IDs in <see cref="Images"/>.
    /// </summary>
    public required string ImageIds { get; init; }

    /// <summary>Optional metadata in source order; older observations have none.</summary>
    public IReadOnlyList<GameImage> Images { get; init; } = [];

    /// <summary>When this source's reading was taken (UTC).</summary>
    public required DateTime ObservedAt { get; init; }

    /// <summary>
    /// The stored list split into individual image ids on read, preserving
    /// the publisher's order.
    /// </summary>
    public IReadOnlyList<string> Ids => ImageIdList.Split(ImageIds);
}

/// <summary>
/// One source's rating figure for a work. Projected from <c>work_ratings</c>
/// (migration 0028), one row per (work, source), the same arrangement
/// <see cref="WorkMaturity"/> has with <c>work_maturity</c>.
/// </summary>
public sealed record WorkRating
{
    /// <summary>The work this rating describes.</summary>
    public required long WorkId { get; init; }

    /// <summary>
    /// Which source published the figure (a <see cref="RatingSources"/>
    /// value). One row per source, so IGDB's user body, IGDB's critics and
    /// Steam each keep their own reading.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// The score the source published. Not a derived value and not Winnow's
    /// own score. Null when the source has no figure.
    /// </summary>
    public double? Score { get; init; }

    /// <summary>
    /// How many people the score rests on. Travels with <see cref="Score"/>
    /// and is never optional in meaning: a 90 from four people and a 90
    /// from four thousand are different claims.
    /// </summary>
    public int? RatingCount { get; init; }

    /// <summary>
    /// Steam's own label (e.g. "Very Positive"), stored verbatim. IGDB
    /// publishes no label, so its rows leave this null.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>When this source's figure was observed (UTC).</summary>
    public required DateTime ObservedAt { get; init; }

    /// <summary>
    /// True only when a score and a positive count are both present. A
    /// source with no figure gets no row, but a deserialized record may
    /// still carry nulls before being tested.
    /// </summary>
    public bool HasFigure => Score is not null && RatingCount is > 0;
}
