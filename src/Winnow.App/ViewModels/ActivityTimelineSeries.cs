using System.Globalization;
using Winnow.Core.Domain;

namespace Winnow.App.ViewModels;

public sealed record ActivityTimelineBar(DateTime StartUtc, DateTime EndUtc, double Hours,
    bool IsTracked, string Label)
{
    public string RecordDate => Label.Split('·')[0].Trim();
    public string RecordedHours => Hours.ToString("0.##", CultureInfo.CurrentCulture) + "h";
}

public sealed record ActivityTimelineCoverage(DateTime StartUtc, DateTime EndUtc);

/// <summary>Read-only activity projection. Empty time is never inferred to be zero play.</summary>
public sealed record ActivityTimelineSeries
{
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public required IReadOnlyList<ActivityTimelineBar> Bars { get; init; }
    public required IReadOnlyList<ActivityTimelineCoverage> Coverage { get; init; }
    public double MaxHours => Bars.Count == 0 ? 0 : Bars.Max(b => b.Hours);
    public bool HasHistory => Bars.Count > 0;
    public required string Summary { get; init; }
    public required string CoverageNote { get; init; }
    public required string PeriodLabel { get; init; }
    public DateTime? LastPlayedUtc { get; init; }

    public static ActivityTimelineSeries Build(IReadOnlyList<PlaytimeSnapshot> snapshots,
        IReadOnlyList<Session> sessions, DateTime? acquiredUtc, DateTime? lastPlayedUtc,
        DateTime nowUtc, bool trackedSessions = false)
    {
        nowUtc = Utc(nowUtc);
        acquiredUtc = ValidDate(acquiredUtc, nowUtc);
        lastPlayedUtc = ValidDate(lastPlayedUtc, nowUtc);
        var validSessions = sessions
            .Where(s => s.StartedAt > DateTime.MinValue && s.EndedAt is { } end
                && Utc(end) <= nowUtc && Utc(end) > Utc(s.StartedAt)
                && s.DurationSeconds is > 0
                && s.DurationSeconds <= (Utc(end) - Utc(s.StartedAt)).TotalSeconds + 1)
            .DistinctBy(s => (s.OwnershipId, s.StartedAt, s.EndedAt, s.DurationSeconds))
            .OrderBy(s => s.StartedAt).ToArray();
        var monthly = new Dictionary<DateTime, ActivityTimelineBar>();
        var coverage = new List<ActivityTimelineCoverage>();
        string? baselineSummary = null;

        // Different ownerships have independent counters. Reject an accidental mixed
        // input rather than summing or comparing them as one history.
        var readings = snapshots.Where(s => s.ObservedAt > DateTime.MinValue && Utc(s.ObservedAt) <= nowUtc)
            .OrderBy(s => s.ObservedAt).ToArray();
        if (readings.Select(s => s.OwnershipId).Distinct().Count() == 1)
        {
            var endpoints = readings.GroupBy(s => Utc(s.ObservedAt))
                .Where(g => IsMonthEnd(g.Key))
                .Select(g => new { At = g.Key, Values = g.Select(s => s.PlaytimeMinutes).Distinct().ToArray() })
                .ToArray();
            if (endpoints.Length > 0 && endpoints.All(e => e.Values.Length == 1 && e.Values[0] >= 0))
                baselineSummary = $"{HoursText(endpoints[0].Values[0] / 60d)} recorded by {endpoints[0].At:MMM yyyy}";
            for (var i = 1; i < endpoints.Length; i++)
            {
                var previous = endpoints[i - 1];
                var current = endpoints[i];
                var month = MonthStart(current.At);
                if (MonthStart(previous.At).AddMonths(1) != month
                    || previous.Values.Length != 1 || current.Values.Length != 1
                    || previous.Values[0] < 0 || current.Values[0] < previous.Values[0])
                    continue;
                // An intervening reset invalidates even a plausible pair of endpoints.
                var between = readings.Where(s => Utc(s.ObservedAt) >= previous.At && Utc(s.ObservedAt) <= current.At).ToArray();
                if (between.Any(s => s.PlaytimeMinutes < previous.Values[0] || s.PlaytimeMinutes > current.Values[0])
                    || between.Zip(between.Skip(1)).Any(p => p.First.PlaytimeMinutes > p.Second.PlaytimeMinutes))
                    continue;
                var hours = (current.Values[0] - previous.Values[0]) / 60d;
                var end = month.AddMonths(1);
                monthly[month] = new(month, end, hours, false,
                    $"{month:MMM yyyy} · {HoursText(hours)} · monthly history");
                coverage.Add(new(month, end));
            }
        }

        var trackedMonths = new Dictionary<DateTime, double>();
        foreach (var session in validSessions)
        {
            var start = Utc(session.StartedAt);
            var end = Utc(session.EndedAt!.Value);
            var seconds = session.DurationSeconds!.Value;
            for (var cursor = start; cursor < end;)
            {
                var month = MonthStart(cursor);
                var next = month.AddMonths(1) < end ? month.AddMonths(1) : end;
                var hours = seconds / 3600d * (next - cursor).TotalSeconds / (end - start).TotalSeconds;
                trackedMonths[month] = trackedMonths.GetValueOrDefault(month) + hours;
                cursor = next;
            }
        }
        foreach (var (month, hours) in trackedMonths)
            monthly.TryAdd(month, new(month, month.AddMonths(1) < nowUtc ? month.AddMonths(1) : nowUtc,
                hours, true, $"{month:MMM yyyy} · {HoursText(hours)} · Winnow sessions"));

        var bars = trackedSessions
            ? validSessions.Select(s => new ActivityTimelineBar(Utc(s.StartedAt), Utc(s.EndedAt!.Value),
                s.DurationSeconds!.Value / 3600d, true,
                $"{Utc(s.StartedAt):d MMM yyyy HH:mm} UTC · {HoursText(s.DurationSeconds.Value / 3600d)} · Winnow session")).ToArray()
            : monthly.Values.OrderBy(b => b.StartUtc).ToArray();
        var evidence = new List<DateTime>();
        if (acquiredUtc is { } acquired) evidence.Add(acquired);
        if (lastPlayedUtc is { } last) evidence.Add(last);
        if (readings.Length > 0) evidence.Add(Utc(readings[0].ObservedAt));
        if (bars.Length > 0) evidence.Add(bars[0].StartUtc);
        var startUtc = evidence.Count == 0 ? nowUtc : evidence.Min();
        if (trackedSessions)
            startUtc = bars.Length == 0 ? nowUtc :
                (bars[0].StartUtc < nowUtc.AddDays(-30) ? bars[0].StartUtc : nowUtc.AddDays(-30));

        return new()
        {
            StartUtc = startUtc, EndUtc = nowUtc, Bars = bars,
            Coverage = trackedSessions ? [] : coverage,
            LastPlayedUtc = lastPlayedUtc,
            Summary = trackedSessions
                ? bars.Length == 0 ? "No completed sessions recorded yet"
                    : $"{bars.Length} observed {(bars.Length == 1 ? "session" : "sessions")} · {bars[0].StartUtc:d MMM yyyy}–{bars[^1].EndUtc:d MMM yyyy}"
                : string.Join(" · ", new[]
                {
                    acquiredUtc is { } date ? $"Acquired · {date:d MMM yyyy}" : "Acquisition date unavailable",
                    baselineSummary
                }.Where(s => s is not null)),
            CoverageNote = trackedSessions
                ? "Only sessions observed by Winnow are shown."
                : "Gaps may have no records. Winnow months include observed sessions only.",
            PeriodLabel = trackedSessions ? "Hours per session" : "Hours per month"
        };
    }

    private static string HoursText(double hours) => hours.ToString("0.##", CultureInfo.CurrentCulture) + "h";
    private static DateTime MonthStart(DateTime date) => new(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    private static bool IsMonthEnd(DateTime date) => date.Month < 12 || date.Year < 9999
        ? date == MonthStart(date).AddMonths(1).AddSeconds(-1) : false;
    private static DateTime Utc(DateTime date) => date.Kind == DateTimeKind.Local
        ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);
    private static DateTime? ValidDate(DateTime? date, DateTime now) => date is { } value
        && value > DateTime.MinValue && Utc(value) <= now ? Utc(value) : null;
}
