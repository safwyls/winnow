using Winnow.App.Services;
using Winnow.App.ViewModels;
using Winnow.Core.Domain;
using Winnow.Core.Queries;
using Winnow.Covers;

namespace Winnow.Tests;

/// <summary>
/// Builds tiles the way <c>LibraryViewModel</c> builds them, for tests that
/// need one without a database. Since TASK-70.6 a tile is one game and takes
/// its store entries and its folded figures, so this is where the old
/// one-ownership shape is assembled into the new one.
/// </summary>
internal static class TileFixture
{
    /// <summary>One store entry, which is what almost every fixture wants.</summary>
    public static GameTileViewModel Tile(
        DateTime nowUtc,
        long ownershipId = 1,
        long releaseId = 1,
        long workId = 1,
        string title = "Fixture",
        string store = "steam",
        string bucket = LibraryBuckets.NeverPlayed,
        long playtimeMinutes = 0,
        DateTime? lastPlayedUtc = null,
        DateTime? majorUpdateAt = null,
        CoverKey? coverKey = null,
        ICoverLeases? covers = null,
        Work? work = null,
        Ownership? ownership = null,
        DormancyRamp? ramp = null,
        string? steamAppId = null,
        string? gogProductId = null,
        EpicLaunchKey? epicLaunchKey = null,
        string? bucketLabel = null)
    {
        var entry = TileEntry.For(
            ownershipId: ownershipId,
            releaseId: releaseId,
            workId: workId,
            store: store,
            playtimeMinutes: playtimeMinutes,
            lastPlayedAt: lastPlayedUtc,
            ownership: ownership,
            steamAppId: steamAppId,
            gogProductId: gogProductId,
            epicLaunchKey: epicLaunchKey);

        return Tile(nowUtc, [entry], workId, bucket, majorUpdateAt,
            title, coverKey, covers, work, ramp, bucketLabel);
    }

    /// <summary>
    /// A game made of several store entries. The caller states the bucket it
    /// expects the folded figures to fall in; the thresholds are chosen so the
    /// shared rules actually produce that bucket, so a fixture that names a
    /// bucket its own playtime contradicts fails here rather than passing
    /// quietly.
    /// </summary>
    public static GameTileViewModel Tile(
        DateTime nowUtc,
        IReadOnlyList<TileEntry> entries,
        long resolvedWorkId,
        string bucket,
        DateTime? majorUpdateAt = null,
        string title = "Fixture",
        CoverKey? coverKey = null,
        ICoverLeases? covers = null,
        Work? work = null,
        DormancyRamp? ramp = null,
        string? bucketLabel = null)
    {
        // Thresholds tuned so the caller's stated bucket is the one the shared
        // rules produce for these entries: the fixture never invents a bucket
        // the read model would not have given it.
        var game = GameGrouping.Of(
            resolvedWorkId, entries, majorUpdateAt, ThresholdsFor(bucket, entries, majorUpdateAt));

        return new GameTileViewModel(
            entries: entries,
            game: game,
            title: title,
            nowUtc: nowUtc,
            coverKey: coverKey,
            covers: covers,
            work: work,
            ramp: ramp,
            bucketLabel: bucketLabel);
    }

    /// <summary>
    /// The thresholds under which these entries land in the requested bucket.
    /// Returns the defaults when they already agree; otherwise adjusts the
    /// floor so the bucket a tile carries is always one the rules actually
    /// derived.
    /// </summary>
    private static BucketThresholds ThresholdsFor(
        string bucket, IReadOnlyList<TileEntry> entries, DateTime? majorUpdateAt)
    {
        var minutes = entries.Sum(e => e.PlaytimeMinutes);
        var defaults = BucketThresholds.Default;

        if (LibraryBucketRules.Classify(
                minutes,
                entries.Max(e => e.LastPlayedAt),
                majorUpdateAt,
                defaults) == bucket)
        {
            return defaults;
        }

        return bucket switch
        {
            LibraryBuckets.Retired => defaults with { RetiredFloorMinutes = Math.Max(1, minutes) },
            LibraryBuckets.StaleButPatched => defaults with
            {
                RetiredFloorMinutes = long.MaxValue,
                StaleWindowMonths = 0,
            },
            LibraryBuckets.Bounced => defaults with
            {
                BouncedFloorMinutes = Math.Max(1, minutes),
                RetiredFloorMinutes = long.MaxValue,
            },
            LibraryBuckets.Active => defaults with
            {
                BouncedFloorMinutes = long.MaxValue,
                RetiredFloorMinutes = long.MaxValue,
                StaleWindowMonths = 1_200,
            },
            _ => defaults,
        };
    }
}
