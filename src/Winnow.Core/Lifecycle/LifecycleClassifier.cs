namespace Winnow.Core.Lifecycle;

/// <summary>Conservative, recomputable rules over source observations, never persisted verdicts.</summary>
public static class LifecycleClassifier
{
    public static GameLifecycle Classify(IEnumerable<LifecycleObservation> observations, DateTime now, LifecycleTuning? tuning = null)
    {
        var rules = tuning ?? LifecycleTuning.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rules.EvidenceFreshDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rules.ActivityFreshDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rules.PlayerHistoryDays);
        ArgumentOutOfRangeException.ThrowIfNegative(rules.LowPlayerCeiling);
        ArgumentOutOfRangeException.ThrowIfNegative(rules.LowReviewCeiling);
        ArgumentOutOfRangeException.ThrowIfLessThan(rules.MinimumPlayerSamples, 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rules.MinimumPlayerSpanDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rules.DeadQuietDays);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rules.AbandonedQuietDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(rules.PlayerHistoryDays, rules.MinimumPlayerSpanDays);
        ArgumentOutOfRangeException.ThrowIfLessThan(rules.EvidenceFreshDays, rules.ActivityFreshDays);
        foreach (var confidence in new[] { rules.CancelledConfidence, rules.OfflineConfidence,
                     rules.DelistedConfidence, rules.AbandonedConfidence, rules.DeadConfidence,
                     rules.InactiveConfidence, rules.ActiveConfidence, rules.UnknownConfidence })
        {
            if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(tuning), "Lifecycle confidence must be finite and between zero and one.");
        }
        var history = observations.Where(o => o.ObservedAt <= now).ToArray();
        // A new complete answer supersedes the same source's old answer, including null fields.
        var latest = history.GroupBy(o => (o.Source, o.SourceId))
            .Select(g => g.OrderByDescending(o => o.ObservedAt).ThenByDescending(o => o.Id).First())
            .Where(o => now - o.ObservedAt <= TimeSpan.FromDays(rules.EvidenceFreshDays)).ToArray();
        var signals = latest.Select(o => o.Signals).ToArray();
        bool Status(string status) => signals.Any(s => string.Equals(s.IgdbStatus, status, StringComparison.OrdinalIgnoreCase));
        if (Status("cancelled")) return new(GameLifecycleStatus.Cancelled, rules.CancelledConfidence, "IGDB reports cancellation.");
        if (Status("offline")) return new(GameLifecycleStatus.Offline, rules.OfflineConfidence, "IGDB reports offline status.");
        if (signals.Any(s => s.OfficialShutdownAt is { } date && date <= now))
            return new(GameLifecycleStatus.Offline, rules.OfflineConfidence, "The source reports an official shutdown.");
        if (Status("delisted") || latest.Any(o => o.Signals.StoreListed == false
            && history.Any(p => p.Source == o.Source && p.SourceId == o.SourceId
                && p.ObservedAt < o.ObservedAt && p.Signals.StoreListed == true)))
            return new(GameLifecycleStatus.Delisted, rules.DelistedConfidence, "This release is reported delisted; existing copies may still run.");
        bool Old(DateTime? date, int days) => date is { } value && value <= now.AddDays(-days);
        bool Quiet(Func<LifecycleSignals, DateTime?> select, int days)
        {
            var dates = signals.Select(select).Where(d => d.HasValue).ToArray();
            return dates.Length > 0 && dates.All(d => Old(d, days));
        }
        if ((Status("early_access") || signals.Any(s => s.IsUnfinished == true))
            && !Status("released") && !signals.Any(s => s.IsUnfinished == false)
            && Quiet(s => s.LastDevelopmentAt, rules.AbandonedQuietDays)
            && Quiet(s => s.LastCommunicationAt, rules.AbandonedQuietDays)
            && !signals.Any(s => s.LastStoreChangeAt is { } changed && !Old(changed, rules.AbandonedQuietDays)))
            return new(GameLifecycleStatus.Abandoned, rules.AbandonedConfidence, $"Unfinished, with at least {rules.AbandonedQuietDays} days since the observed patch notes and announcements; development may continue elsewhere.");
        var activity = latest.Where(o => now - o.ObservedAt <= TimeSpan.FromDays(rules.ActivityFreshDays)).ToArray();
        var lowPlayers = activity.Any(o => o.Signals.CurrentPlayers >= 0 && o.Signals.CurrentPlayers <= rules.LowPlayerCeiling)
            && !activity.Any(o => o.Signals.CurrentPlayers > rules.LowPlayerCeiling);
        var lowReviews = activity.Any(o => o.Signals.RecentReviewCount >= 0 && o.Signals.RecentReviewCount <= rules.LowReviewCeiling)
            && !activity.Any(o => o.Signals.RecentReviewCount > rules.LowReviewCeiling);
        var samples = history.Where(o => o.ObservedAt >= now.AddDays(-rules.PlayerHistoryDays) && o.Signals.CurrentPlayers is not null).ToArray();
        var sustained = samples.Length >= rules.MinimumPlayerSamples && samples.All(o => o.Signals.CurrentPlayers >= 0 && o.Signals.CurrentPlayers <= rules.LowPlayerCeiling)
            && samples.Max(o => o.ObservedAt) - samples.Min(o => o.ObservedAt) >= TimeSpan.FromDays(rules.MinimumPlayerSpanDays);
        if (Status("released") && !Status("early_access") && !signals.Any(s => s.IsUnfinished == true)
            && signals.Any(s => s.IsMultiplayer == true) && signals.Any(s => s.HasSinglePlayer == false)
            && !signals.Any(s => s.HasSinglePlayer == true) && lowPlayers && lowReviews && sustained
            && Quiet(s => s.LastDevelopmentAt, rules.DeadQuietDays)
            && Quiet(s => s.LastCommunicationAt, rules.DeadQuietDays))
            return new(GameLifecycleStatus.Dead, rules.DeadConfidence, $"Multiplayer-only release has sustained low Steam activity and no observed patch notes or announcements for {rules.DeadQuietDays} days; other platforms may differ.");
        if (lowPlayers && lowReviews)
            return new(GameLifecycleStatus.Inactive, rules.InactiveConfidence, "Recent Steam player and review activity is low; this does not establish that the game is unplayable.");
        if (activity.Any(o => o.Signals.CurrentPlayers > rules.LowPlayerCeiling || o.Signals.RecentReviewCount > rules.LowReviewCeiling))
            return new(GameLifecycleStatus.Active, rules.ActiveConfidence, "Recent Steam activity was observed.");
        return new(GameLifecycleStatus.Unknown, rules.UnknownConfidence, "Insufficient current lifecycle evidence.");
    }
}
