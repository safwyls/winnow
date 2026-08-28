using Hoard.Core.Queries;

namespace Hoard.Recommend;

/// <summary>
/// Composes the one-line <see cref="Recommendation.Reason"/> from the same
/// contributions that produced the score — the prose can cite nothing the
/// arithmetic didn't use, which is what keeps a reason honest under
/// interrogation.
///
/// <para>The lead sentence is the strongest story the facts support; the
/// charter's own example — "you put 40 minutes into this in 2023 and it has
/// had three major patches since" — fuses commitment, dormancy and the patch
/// signal into one sentence, so the builder does the same for the stale bucket
/// rather than stapling three explanations together.</para>
/// </summary>
internal static class ReasonBuilder
{
    public static string Build(
        CandidateFacts facts,
        IReadOnlyList<SignalContribution> contributions)
    {
        var parts = new List<string>(3) { Lead(facts, contributions) };

        // One supporting clause: the strongest contributor whose story the
        // lead didn't already tell. Friction, intent and taste qualify;
        // commitment/dormancy/patch are the lead's raw material, and jitter
        // explains rotation, not the game.
        var secondary = contributions
            .Where(c => c.Contribution > 0 && c.Signal is
                SignalNames.Installed or SignalNames.BoughtTwice or
                SignalNames.TasteAffinity or SignalNames.TriedToLikeIt)
            .OrderByDescending(c => c.Contribution)
            .FirstOrDefault();
        if (secondary is not null)
        {
            parts.Add(secondary.Explanation);
        }

        // The honesty clause. If probably-done fired, the feed is REQUIRED to
        // say "you were right to drop this" out loud — a demoted row with a
        // cheerful reason would be the model lying about its own arithmetic.
        var probablyDone = contributions.FirstOrDefault(
            c => c.Signal == SignalNames.ProbablyDone);
        if (probablyDone is not null)
        {
            parts.Add(probablyDone.Explanation);
        }

        // Same honesty rule for the mode mismatch: a row demoted for being
        // online-only in a single-player library must say so where it does
        // surface, or the demotion is arbitrary from the user's side.
        var modeMismatch = contributions.FirstOrDefault(
            c => c.Signal == SignalNames.ModeMismatch);
        if (modeMismatch is not null)
        {
            parts.Add(modeMismatch.Explanation);
        }

        return string.Join(" ", parts);
    }

    private static string Lead(
        CandidateFacts facts, IReadOnlyList<SignalContribution> contributions)
    {
        // Stale-but-patched: fuse played-when with changed-since.
        if (facts.Bucket == LibraryBuckets.StaleButPatched)
        {
            var played = facts.PlaytimeMinutes > 0
                ? $"You put {Phrases.Duration(facts.PlaytimeMinutes)} into this"
                : "You opened this";
            var when = facts.LastPlayedAt is { } lastPlayed ? $" in {lastPlayed.Year}" : " once";

            string changed;
            if (facts.UpdatesSinceLastPlayed is { } updates && updates > 0)
            {
                var latest = facts.LatestUpdateTitle is { Length: > 0 } title
                    ? $", most recently \"{title}\""
                    : string.Empty;
                changed = updates == 1
                    ? $"and it has had an update since{latest}."
                    : $"and it has had {updates} updates since{latest}.";
            }
            else
            {
                changed = "and it has had a major update since.";
            }

            return $"{played}{when} {changed}";
        }

        // Everything else leads with the commitment sentence, which the scorer
        // wrote from the same minutes: "never opened…", "you tried it for 40
        // minutes…", "you put 5.2 hours in…". Dormancy joins it when known —
        // "then let it go" reads differently at eight years than at eight
        // weeks.
        var commitment = contributions.FirstOrDefault(c => c.Signal == SignalNames.Commitment);
        var dormancy = contributions.FirstOrDefault(c => c.Signal == SignalNames.Dormancy);

        if (commitment is not null)
        {
            return dormancy is not null && facts.PlaytimeMinutes > 0 && facts.LastPlayedAt is { } lp
                ? $"{commitment.Explanation.TrimEnd('.')} — that was {lp.Year}."
                : commitment.Explanation;
        }

        // A recently-played row that somehow reaches rendering, or a future
        // shape this builder doesn't know: fall back to something true rather
        // than something empty. An empty reason is a contract violation.
        var top = contributions
            .Where(c => c.Contribution > 0 && c.Signal != SignalNames.Jitter)
            .OrderByDescending(c => c.Contribution)
            .FirstOrDefault();
        return top?.Explanation ?? "In your library, and today's rotation picked it.";
    }
}
