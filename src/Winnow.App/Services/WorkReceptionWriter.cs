using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb.Model;
using Winnow.Enrich.Steam.Model;

namespace Winnow.App.Services;

/// <summary>
/// Turns one <c>IgdbGame</c> or one <c>SteamStoreReviewSummary</c> into
/// <c>work_images</c> and <c>work_ratings</c> rows, and returns the number
/// of rows it actually changed. It reads what is stored and compares before
/// it writes, so a warm re-run writes nothing and reports zero. That count
/// is what lets the refetch control say "updated" or "nothing new" honestly
/// instead of claiming credit for an upsert that wrote the same bytes back.
/// </summary>
public sealed class WorkReceptionWriter
{
    private readonly IWorkImageRepository _images;
    private readonly IWorkRatingRepository _ratings;
    private readonly TimeProvider _clock;

    public WorkReceptionWriter(
        IWorkImageRepository images,
        IWorkRatingRepository ratings,
        TimeProvider? clock = null)
    {
        _images = images;
        _ratings = ratings;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<int> ApplyIgdbAsync(long workId, IgdbGame game, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        var observedAt = _clock.GetUtcNow().UtcDateTime;
        var stored = await _images.GetForWorkAsync(workId, ct);
        var changed = 0;

        if (await SetImagesAsync(
                workId, ImageKinds.Screenshot, game.ScreenshotImageIds, stored, observedAt, ct))
        {
            changed++;
        }

        if (await SetImagesAsync(
                workId, ImageKinds.Artwork, game.ArtworkImageIds, stored, observedAt, ct))
        {
            changed++;
        }

        var ratings = await _ratings.GetForWorkAsync(workId, ct);

        if (await SetRatingAsync(
                workId, RatingSources.IgdbUsers, game.UserRating, game.UserRatingCount,
                label: null, ratings, observedAt, ct))
        {
            changed++;
        }

        if (await SetRatingAsync(
                workId, RatingSources.IgdbCritics, game.CriticRating, game.CriticRatingCount,
                label: null, ratings, observedAt, ct))
        {
            changed++;
        }

        return changed;
    }

    public async Task<int> ApplySteamAsync(
        long workId, SteamStoreReviewSummary reviews, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reviews);

        var observedAt = _clock.GetUtcNow().UtcDateTime;
        var stored = await _ratings.GetForWorkAsync(workId, ct);

        var changed = await SetRatingAsync(
            workId,
            RatingSources.Steam,
            reviews.IsEmpty ? null : reviews.PercentPositive,
            reviews.IsEmpty ? null : reviews.ReviewCount,
            reviews.IsEmpty ? null : reviews.Label,
            stored,
            observedAt,
            ct);

        return changed ? 1 : 0;
    }

    private async Task<bool> SetImagesAsync(
        long workId,
        string kind,
        IReadOnlyList<string> imageIds,
        IReadOnlyList<WorkImages> stored,
        DateTime observedAt,
        CancellationToken ct)
    {
        var joined = ImageIdList.Join(imageIds);
        var existing = stored.FirstOrDefault(
            row => row.Source == ImageSources.Igdb && row.Kind == kind);

        if (joined is null)
        {
            return existing is not null
                   && await _images.DeleteAsync(workId, ImageSources.Igdb, kind, ct);
        }

        if (existing is not null && string.Equals(existing.ImageIds, joined, StringComparison.Ordinal))
        {
            return false;
        }

        await _images.UpsertAsync(
            new WorkImages
            {
                WorkId = workId,
                Source = ImageSources.Igdb,
                Kind = kind,
                ImageIds = joined,
                ObservedAt = observedAt,
            },
            ct);

        return true;
    }

    private async Task<bool> SetRatingAsync(
        long workId,
        string source,
        double? score,
        int? ratingCount,
        string? label,
        IReadOnlyList<WorkRating> stored,
        DateTime observedAt,
        CancellationToken ct)
    {
        var existing = stored.FirstOrDefault(row => row.Source == source);

        if (score is null || ratingCount is not > 0)
        {
            return existing is not null && await _ratings.DeleteAsync(workId, source, ct);
        }

        if (existing is not null
            && existing.Score == score
            && existing.RatingCount == ratingCount
            && string.Equals(existing.Label, label, StringComparison.Ordinal))
        {
            return false;
        }

        await _ratings.UpsertAsync(
            new WorkRating
            {
                WorkId = workId,
                Source = source,
                Score = score,
                RatingCount = ratingCount,
                Label = label,
                ObservedAt = observedAt,
            },
            ct);

        return true;
    }
}
