using Winnow.Core.Domain;

namespace Winnow.Core.Queries;

public static class SteamPlaytimeReconciler
{
    public static readonly TimeSpan SettlingDelay = TimeSpan.FromMinutes(30);

    public static IReadOnlyList<SteamReportedActivity> Reconcile(
        IReadOnlyList<SteamPlaytimeObservation> observations, IReadOnlyList<Session> sessions,
        DateTime asOfUtc, string? accountRef = null, IReadOnlySet<long>? ambiguousOwnershipIds = null)
    {
        var result = new List<SteamReportedActivity>();
        foreach (var ownership in observations.Where(o => o.ObservedAt <= asOfUtc)
                     .GroupBy(o => o.OwnershipId))
        {
            // Sessions do not carry Steam account provenance. Never spend their
            // duration against two accounts or silently guess the active account.
            var ambiguous = ambiguousOwnershipIds?.Contains(ownership.Key) == true
                || ownership.Select(o => o.AccountRef).Distinct(StringComparer.Ordinal).Skip(1).Any();
            var recorded = sessions.Where(s => s.OwnershipId == ownership.Key
                && (s.EndedAt is null || s.EndedAt <= asOfUtc)).ToArray();
            foreach (var account in ownership.GroupBy(o => o.AccountRef))
            {
                if (accountRef is not null && !StringComparer.Ordinal.Equals(accountRef, account.Key)) continue;
                long? highwater = null;
                DateTime baseline = default, lastAtHighwater = default;
                double creditedSeconds = 0;
                var sources = new HashSet<string>(StringComparer.Ordinal);
                foreach (var observation in account.OrderBy(o => o.ObservedAt).ThenBy(o => o.Id))
                {
                    if (observation.PlaytimeMinutes is not { } total || total < 0) continue;
                    var newSource = sources.Add(observation.Source);
                    if (highwater is null)
                    {
                        highwater = total;
                        baseline = lastAtHighwater = observation.ObservedAt;
                        continue;
                    }
                    // A newly available source can reveal old history. Its first
                    // reading raises the baseline without claiming recent play.
                    if (newSource)
                    {
                        if (total > highwater)
                        {
                            highwater = total;
                            lastAtHighwater = observation.ObservedAt;
                            baseline = observation.ObservedAt;
                            creditedSeconds = 0;
                        }
                        continue;
                    }
                    if (total < highwater) continue;
                    if (total == highwater)
                    {
                        lastAtHighwater = observation.ObservedAt;
                        continue;
                    }

                    var delta = total - highwater.Value;
                    highwater = total;
                    var start = lastAtHighwater;
                    lastAtHighwater = observation.ObservedAt;
                    // Cumulative credit permits a delayed Steam increment to use
                    // an earlier sitting, but each recorded second is spent once.
                    var availableSeconds = recorded.Sum(s => CoveredSeconds(s, baseline, observation.ObservedAt));
                    var matchedSeconds = Math.Min((double)delta * 60, Math.Max(0, availableSeconds - creditedSeconds));
                    creditedSeconds += matchedSeconds;
                    if (asOfUtc - observation.ObservedAt < SettlingDelay) continue;
                    var comparisonUnavailable = ambiguous || recorded.Any(s => s.EndedAt is null
                        && s.DetectionMethod == DetectionMethods.ProcessWatch && s.StartedAt <= observation.ObservedAt);
                    var unexplained = Math.Max(0, delta - matchedSeconds / 60);
                    if (unexplained <= PlaytimeTolerance.Minutes) unexplained = 0;

                    result.Add(new SteamReportedActivity
                    {
                        Id = observation.Id, OwnershipId = ownership.Key, AccountRef = account.Key,
                        WindowStartedAt = start, WindowEndedAt = observation.ObservedAt,
                        SteamDeltaMinutes = delta, ComparisonUnavailable = comparisonUnavailable,
                        MatchedRecordedMinutes = comparisonUnavailable ? null : matchedSeconds / 60,
                        UnexplainedMinutes = comparisonUnavailable ? null : unexplained,
                    });
                }
            }
        }
        return result.OrderByDescending(r => r.WindowEndedAt).ThenByDescending(r => r.Id).ToArray();
    }

    private static double CoveredSeconds(Session session, DateTime from, DateTime until)
    {
        if (session.EndedAt is not { } ended || session.DurationSeconds is not > 0 || ended <= session.StartedAt)
            return 0;
        var start = session.StartedAt > from ? session.StartedAt : from;
        var end = ended < until ? ended : until;
        if (start >= end) return 0;
        // Duration may omit pauses; prorate only the part inside the observed window.
        return Math.Min(session.DurationSeconds.Value, (ended - session.StartedAt).TotalSeconds)
            * (end - start).TotalSeconds / (ended - session.StartedAt).TotalSeconds;
    }
}
