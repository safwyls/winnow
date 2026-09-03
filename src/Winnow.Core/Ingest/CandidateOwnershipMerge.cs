namespace Winnow.Core.Ingest;

/// <summary>
/// Collapses candidates within one ingest pass to one per ownership, so
/// overlapping sources (e.g. local files and GetOwnedGames) record one
/// observation rather than two. Play fields — playtime, last-played, source
/// and observed-at — are taken from one winning candidate as a coherent tuple
/// (see <see cref="PlayWinnerIsSecond"/>), never blended across candidates.
/// Non-play fields (title, install state, acquired date) fill from either
/// side. Never touches the database.
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
    /// Merges two views of one ownership around ONE winning play tuple.
    /// Playtime, last-played, source and observation time all come from the same
    /// candidate — the one that wins on the discipline in
    /// <see cref="PlayWinnerIsSecond"/> — so the merged record is a fact some
    /// source actually reported rather than a blend of several. Install state is
    /// merged on its own axis; title and acquisition date, which are not part of
    /// the play fact, fill in from either side.
    /// </summary>
    public static CandidateOwnership Merge(CandidateOwnership first, CandidateOwnership second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        // Neither side attests to a play fact, so nothing here is a play tuple:
        // source and observation time are only saying "this ownership was seen",
        // and the later look is the better answer to that.
        if (!HasPlayFact(first) && !HasPlayFact(second))
        {
            return Fill(first, second, first with
            {
                ObservedAt = first.ObservedAt >= second.ObservedAt ? first.ObservedAt : second.ObservedAt,
            });
        }

        var secondWon = PlayWinnerIsSecond(first, second);
        var winner = secondWon ? second : first;
        var loser = secondWon ? first : second;

        // The winner carries PlaytimeMinutes, LastPlayedAt, Source and ObservedAt
        // untouched; only fields outside the play tuple are filled from the loser.
        return Fill(first, second, winner with
        {
            // Attribution belongs to the tuple: these are the winner's minutes,
            // so they are the winner's account. A loser's account is used only
            // when the winner names none, where it answers "who owns this"
            // rather than "who played it".
            AccountRef = FirstReal(winner.AccountRef, loser.AccountRef),
        });
    }

    /// <summary>
    /// Unions two per-account lists by account reference, in first-seen order.
    ///
    /// <para><b>Merging happens WITHIN one account and never across two.</b>
    /// The play tuple above picks a single winner because the household figure
    /// has to be one source's coherent answer; this list is the opposite kind of
    /// fact — one row per account, each holding only what that account did — so
    /// there is nothing to pick between. Two readers describing the SAME account
    /// (localconfig.vdf and GetOwnedGames, both looking at account 12345) are
    /// two partial views of one figure, and the higher minutes and the later
    /// date are the better of them. Two readers describing DIFFERENT accounts
    /// are two separate facts, and combining them would recreate exactly the
    /// household collapse this list exists to undo.</para>
    ///
    /// <para>Minutes take the max rather than the newer reading for the reason
    /// <c>PlaytimeView.LowerBound</c> exists: every source sees a floor of a
    /// cumulative counter, so the larger figure is the one closer to the truth.
    /// This is the same "higher wins" the play tuple applies one level up, and
    /// it is deliberately NOT the err-low band: that band is about a stored
    /// figure disagreeing with an incoming one, and is applied where the row is
    /// written (<c>IOwnershipAccountRepository.UpsertAsync</c>). Erring low here
    /// would make a first write land a minute below the ownership's first write
    /// — introducing the very divergence the band removes.
    /// Minutes and date are merged on their own axes here, unlike the play
    /// tuple — within a single account there is no risk of attributing one
    /// person's hours to another, which is the only thing the tuple discipline
    /// protects against.</para>
    /// </summary>
    private static IReadOnlyList<CandidateAccount> UnionAccounts(
        IReadOnlyList<CandidateAccount> first, IReadOnlyList<CandidateAccount> second)
    {
        if (second.Count == 0)
        {
            return first;
        }

        if (first.Count == 0)
        {
            return second;
        }

        var byRef = new Dictionary<string, int>(StringComparer.Ordinal);
        var merged = new List<CandidateAccount>(first.Count + second.Count);

        foreach (var account in first.Concat(second))
        {
            if (string.IsNullOrWhiteSpace(account.AccountRef))
            {
                // An unnamed account is not an account. Keeping it would put a
                // blank key in the membership table that no filter could ever
                // match and that would count as evidence against the user.
                continue;
            }

            if (byRef.TryGetValue(account.AccountRef, out var index))
            {
                var stored = merged[index];
                merged[index] = stored with
                {
                    PlaytimeMinutes = Larger(stored.PlaytimeMinutes, account.PlaytimeMinutes),
                    LastPlayedAt = Later(stored.LastPlayedAt, account.LastPlayedAt),
                };
            }
            else
            {
                byRef[account.AccountRef] = merged.Count;
                merged.Add(account);
            }
        }

        return merged;
    }

    /// <summary>Larger of two figures; null is "no answer", never zero.</summary>
    private static long? Larger(long? first, long? second)
        => first is null ? second
            : second is null ? first
            : Math.Max(first.Value, second.Value);

    /// <summary>Later of two dates; null is "no answer", never "earlier".</summary>
    private static DateTime? Later(DateTime? first, DateTime? second)
        => first is null ? second
            : second is null ? first
            : first.Value >= second.Value ? first
            : second;

    /// <summary>
    /// Whether <paramref name="second"/> holds the better play tuple. The same
    /// discipline as <c>SteamLibrarySource.ResolvePlaytimeWinner</c>: higher
    /// cumulative minutes wins, an equal figure is broken by the later
    /// last-played date, and a remaining tie keeps the first-seen candidate.
    /// A candidate with no play fact never wins over one that has one.
    /// </summary>
    private static bool PlayWinnerIsSecond(CandidateOwnership first, CandidateOwnership second)
    {
        if (!HasPlayFact(second))
        {
            return false;
        }

        if (!HasPlayFact(first))
        {
            return true;
        }

        // Null minutes with a date is "played, total unknown" — a real play fact,
        // and one any actual figure outranks.
        var firstMinutes = first.PlaytimeMinutes ?? -1;
        var secondMinutes = second.PlaytimeMinutes ?? -1;

        return secondMinutes != firstMinutes
            ? secondMinutes > firstMinutes
            : Nullable.Compare(second.LastPlayedAt, first.LastPlayedAt) > 0;
    }

    /// <summary>
    /// Fields that are not part of the play tuple, in first-seen order: a title
    /// or an acquisition date is the same fact whichever reader saw it, and
    /// install state is one answer that only a reader which can see the disk is
    /// allowed to give.
    ///
    /// <para>The per-account list is filled here rather than carried by the
    /// winner, and that is the point of it: the play tuple keeps ONE account's
    /// coherent answer, so a winner-only list would drop every account that
    /// lost — which is precisely the population the account filter has to be
    /// able to see. Both sides' entries survive (<see cref="UnionAccounts"/>).</para>
    /// </summary>
    private static CandidateOwnership Fill(
        CandidateOwnership first, CandidateOwnership second, CandidateOwnership merged)
    {
        // The install answer and its path are one answer, never split.
        var installState = first.Installed is not null ? first
            : second.Installed is not null ? second
            : null;

        return merged with
        {
            Title = FirstReal(first.Title, second.Title),
            AcquiredAt = first.AcquiredAt ?? second.AcquiredAt,
            InstallPath = installState?.InstallPath,
            Installed = installState?.Installed,
            Accounts = UnionAccounts(first.Accounts, second.Accounts),
        };
    }

    /// <summary>
    /// Whether this candidate says anything about play at all. A date with no
    /// minutes counts: an appmanifest LastPlayed with no readable userdata is an
    /// observation, not a blank.
    /// </summary>
    private static bool HasPlayFact(CandidateOwnership candidate)
        => candidate.PlaytimeMinutes is not null || candidate.LastPlayedAt is not null;

    /// <summary>First non-blank value wins; blank is "no answer".</summary>
    private static string? FirstReal(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first
            : !string.IsNullOrWhiteSpace(second) ? second
            : null;
}
