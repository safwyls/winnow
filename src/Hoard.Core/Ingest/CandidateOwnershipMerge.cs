namespace Hoard.Core.Ingest;

/// <summary>
/// Collapses the candidates of ONE ingest pass down to one candidate per
/// ownership, so a pass that saw a game through two sources records one
/// observation of it rather than two.
///
/// <para><b>The problem this exists to kill.</b> Steam is visible through two
/// readers that overlap: the local files (§4.1) and <c>GetOwnedGames</c> (§4.2).
/// The union of the two is deliberate — each sees games the other cannot — but
/// for the appids in the overlap it produces two candidates that land on the
/// same <c>(release, store)</c> ownership. The resolver's change detection
/// compares each candidate against the newest stored play record, and two
/// candidates for one ownership are not a time series: they are two views of the
/// same instant. When the two views disagreed at all — Steam's own two playtime
/// figures differ by a minute on Arma 2 (2 vs 3), Operation Arrowhead (153 vs
/// 154) and Portal (279 vs 280) — each one "changed" relative to the other, so
/// both appended a row, and every later sync appended two more. At the snapshot
/// scheduler's 15-minute cadence that grows without bound, and the detail view
/// shows whichever of the pair happened to be newest.</para>
///
/// <para><b>Why merging, and not source ranking.</b> Suppressing the Web API for
/// appids the local files also see would settle the row and lose nothing on
/// those appids — but it would put "which source wins" back in charge, and that
/// reasoning is what emptied the install filter. The rule here is instead about
/// what each field IS. Playtime and last-played are both monotonic counters for
/// the same account's same game, so <c>max</c> is safe from either direction: a
/// stale source cannot pull a number backwards, and there is no order in which
/// the answers differ. §4.1's "local is primary" then falls out as a consequence
/// rather than as a policy — the local files are ahead of the Web API's cache far
/// more often than behind it, so they usually supply the max.</para>
///
/// <para><b>Only within one pass.</b> This never looks at the database. Two
/// observations taken at different times are still a series and both still belong
/// in <c>play_records</c>; it is only simultaneous views that collapse.</para>
/// </summary>
public static class CandidateOwnershipMerge
{
    /// <summary>
    /// Merges candidates that address the same ownership, preserving first-seen
    /// order. The key is <c>(Provider, ProviderId)</c> — exactly the hard join
    /// the resolver uses to find the release, and the resolver keys ownership on
    /// <c>(release, store)</c> with store = provider, so this is the same
    /// grouping the database will apply.
    ///
    /// <para><c>AccountRef</c> is deliberately NOT part of the key, matching the
    /// ownership upsert: keying on it would split one ownership's observations
    /// back into two whenever the winning account changed.</para>
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
    /// Merges two views of one ownership, field by field. Both arguments must
    /// address the same <c>(Provider, ProviderId)</c>.
    ///
    /// <list type="bullet">
    /// <item><b>PlaytimeMinutes, LastPlayedAt</b> — the larger of the two, each
    /// independently, ignoring nulls. Both are monotonic for a given account and
    /// game, so the larger is the less stale answer and neither source can move
    /// the other backwards. They merge independently rather than as a tuple
    /// because both candidates describe the SAME account: the "never mix fields
    /// across accounts" rule <c>SteamLibrarySource</c> enforces is about two
    /// different people's records, a different situation that is already settled
    /// before candidates reach here.</item>
    /// <item><b>Installed, InstallPath</b> — from the first candidate with a
    /// non-null <c>Installed</c>; the two always move together (see
    /// <see cref="CandidateOwnership.Installed"/>). Only a source that reads the
    /// local disk has an opinion at all, so at most one candidate does and there
    /// is nothing to tie-break.</item>
    /// <item><b>Title, AccountRef, AcquiredAt</b> — first real answer wins;
    /// blank or null is not an answer. That is exactly what the resolver already
    /// did across two candidates (a real name is never overwritten, and the
    /// ownership upsert COALESCEs attribution), so merging changes nothing about
    /// which of two differing titles a work ends up with.</item>
    /// <item><b>Source</b> — whichever candidate supplied the winning playtime,
    /// so <c>play_records.source</c> names the reader the stored figure actually
    /// came from. Falls back to the first when neither has minutes.</item>
    /// <item><b>ObservedAt</b> — the later of the two. One of the pair may be
    /// served from a cache (§4.2 caches aggressively) and be hours old; the
    /// merged row is an observation as of the freshest input that fed it, which
    /// also keeps <c>play_records.observed_at</c> monotonic across syncs.</item>
    /// </list>
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

        // Only a strictly better playtime figure moves provenance; equal figures
        // keep the first candidate's source, so the merge stays deterministic.
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

    /// <summary>Blank is "no title" / "no account", never an answer — see <see cref="CandidateOwnership.Title"/>.</summary>
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
