using Winnow.Core.Domain;

namespace Winnow.Core.Queries;

/// <summary>Reading state is derived from correlated pushes, effective play and release watermarks.</summary>
public static class UpdateReading
{
    public static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
        : value.ToUniversalTime();

    /// <summary>A missing date with measured play means unknown, old play; no play evidence means never played.</summary>
    public static bool SincePlay(DateTime occurredAt, DateTime? lastPlayedAt, long playtimeMinutes)
        => lastPlayedAt is { } played ? AsUtc(occurredAt) > AsUtc(played) : playtimeMinutes > 0;

    public static bool AfterWatermark(DateTime occurredAt, DateTime? acknowledgedThrough)
        => acknowledgedThrough is not { } through || AsUtc(occurredAt) > AsUtc(through);

    public static bool Correlates(DateTime pushAt, DateTime announcementAt, int windowDays)
        => Math.Abs((AsUtc(announcementAt) - AsUtc(pushAt)).TotalDays) <= windowDays;

    public static IReadOnlyList<UpdateEvent> CorrelatedPushes(IReadOnlyList<UpdateEvent> events, int windowDays)
    {
        var news = events.Where(e => e.Kind == UpdateEventKinds.Announcement)
            .ToLookup(e => e.ReleaseId);
        return events.Where(push => push.Kind == UpdateEventKinds.BuildPush
            && news[push.ReleaseId].Any(item => Correlates(push.OccurredAt, item.OccurredAt, windowDays))).ToArray();
    }
}
