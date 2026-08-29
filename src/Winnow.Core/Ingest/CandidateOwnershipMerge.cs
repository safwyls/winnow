namespace Winnow.Core.Ingest;

/// <summary>
/// Collapses candidates within one ingest pass to one per ownership, so
/// overlapping sources (e.g. local files and GetOwnedGames) record one
/// observation rather than two. Monotonic fields (playtime, last-played) take
/// the max; simultaneous views only -- never touches the database.
/// </summary>
public static class CandidateOwnershipMerge
{
    /// <summary>
    /// Merges candidates sharing the same <c>(Provider, ProviderId)</c>, preserving
    /// first-seen order. AccountRef is not part of the key.
    /// </summary>
    public static IReadOnlyList<CandidateOwnership> Coalesce(
        IEnumerable<CandidateOwnership> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var byOwnership = new Dictionary<(string Provider, string ProviderId), int>();
        var merged = new List<CandidateOwnership>();

        foreach (var candidate in candidates)
        {
            var key = (candidate.Provider, candidate.ProviderId);
            if (byOwnership.TryGetValue(key, out var index))
            {
                merged[index] = Merge(merged[index], candidate);
            }
            else
            {
                byOwnership[key] = merged.Count;
                merged.Add(candidate);
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges two views of one ownership. Playtime/LastPlayed: max independently.
    /// Installed: first non-null. Title/AccountRef/AcquiredAt: first real answer.
    /// Source: follows winning playtime. ObservedAt: the later.
    /// </summary>
    public static CandidateOwnership Merge(CandidateOwnership first, CandidateOwnership second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var playtime = Max(first.PlaytimeMinutes, second.PlaytimeMinutes);

        // The install answer and its path are one answer, never split.
        var installState = first.Installed is not null ? first
            : second.Installed is not null ? second
            : null;

        // Only a strictly better playtime moves provenance; equal keeps first.
        var secondWonPlaytime = playtime is not null
            && second.PlaytimeMinutes == playtime
            && first.PlaytimeMinutes != playtime;

        return first with
        {
            Title = FirstReal(first.Title, second.Title),
            AccountRef = FirstReal(first.AccountRef, second.AccountRef),
            InstallPath = installState?.InstallPath,
            Installed = installState?.Installed,
            PlaytimeMinutes = playtime,
            LastPlayedAt = Later(first.LastPlayedAt, second.LastPlayedAt),
            AcquiredAt = first.AcquiredAt ?? second.AcquiredAt,
            Source = secondWonPlaytime ? second.Source : first.Source,
            ObservedAt = first.ObservedAt >= second.ObservedAt ? first.ObservedAt : second.ObservedAt,
        };
    }

    /// <summary>First non-blank value wins; blank is "no answer".</summary>
    private static string? FirstReal(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first
            : !string.IsNullOrWhiteSpace(second) ? second
            : null;

    private static long? Max(long? first, long? second)
        => first is null ? second
            : second is null ? first
            : Math.Max(first.Value, second.Value);

    private static DateTime? Later(DateTime? first, DateTime? second)
        => first is null ? second
            : second is null ? first
            : first.Value >= second.Value ? first
            : second;
}
