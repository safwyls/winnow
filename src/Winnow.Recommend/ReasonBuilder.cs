using System.Text;

namespace Winnow.Recommend;

/// <summary>
/// Renders a <see cref="RecommendationReason"/> as ONE sentence inside a
/// character budget. One sentence is the card's contract, and the old builder
/// broke it: it concatenated a lead, a secondary reason, a probably-done
/// statement and a mode-mismatch clause into as many as four. That
/// concatenation was also the mechanical cause of the cookie-cutter feel, since
/// every card was assembled from the same fragments in the same order.
///
/// <para>The builder now renders exactly two clauses and picks the phrasing of
/// each from <see cref="ReasonPhrasebook"/> using the game's own release id, so
/// a reload renders the identical sentence while two cards in one session do
/// not read as siblings. The supporting fact is chosen before the opening is
/// written, because which fact it lands on decides what the opening may not
/// repeat: the opening may not spend <c>{year}</c> or <c>{age}</c> when
/// the supporting clause is already telling the time story.</para>
/// </summary>
internal static class ReasonBuilder
{
    /// <summary>Tokens a primary clause may not use when the secondary is already telling the time story.</summary>
    private static readonly string[] TimeTokens = ["year", "age"];

    private static readonly string[] NoTokens = [];

    public static string Build(
        RecommendationReason reason,
        RecommendationTuning tuning,
        ShelfReasonLedger? ledger = null)
    {
        var budget = Math.Max(40, tuning.ReasonCharacterBudget);
        var evidence = reason.Evidence;

        // The supporting clause is chosen before the opening is written,
        // because which fact it lands on decides what the opening may not
        // repeat.
        var (supporting, secondary) = ChooseSupporting(reason, tuning, ledger);

        // If the supporting clause is about how long it has been, the opening
        // must not also date it: "you put 5 hours in back in 2019, untouched
        // for seven years" is one fact told twice.
        var forbidden = supporting is ReasonSignal.Dormant or ReasonSignal.UndatedDormancy
            or ReasonSignal.PlayedRecently
            ? TimeTokens
            : NoTokens;

        // Capitalised after filling, not before: a template is free to open on
        // a token ("{updates} landed here since…"), which is one more sentence
        // shape available to the copy.
        var primary = Capitalise(
            Render(reason.Primary, ReasonClause.Primary, evidence, tuning, forbidden, ledger)
                ?? Render(reason.Primary, ReasonClause.Primary, evidence, tuning, NoTokens, ledger)
                ?? ReasonPhrasebook.Fallback);

        if (secondary is not null)
        {
            var joined = Terminate(primary + secondary);
            if (joined.Length <= budget)
            {
                // Counted only once the card has actually said it, so a clause
                // the budget dropped cannot silence the next card.
                ledger?.Cite(supporting, evidence);
                return joined;
            }
        }

        var alone = Terminate(primary);
        return alone.Length <= budget ? alone : Elide(alone, budget);
    }

    // Walks the card's supporting facts in the scorer's strongest-first
    // order, skipping any fact the surface has already spent (ledger.CanCite),
    // and renders the first one admitted. When none is left the return is
    // (None, null), which means no supporting clause at all: the card says
    // less rather than repeating. A Secondary of None means the card has no
    // supporting clause at all, and the early return honours that: it is not
    // a signal to go looking for a substitute in SupportingSignals. A caller
    // that deliberately clears Secondary gets silence rather than a fallback.
    private static (ReasonSignal Signal, string? Text) ChooseSupporting(
        RecommendationReason reason,
        RecommendationTuning tuning,
        ShelfReasonLedger? ledger)
    {
        if (reason.Secondary == ReasonSignal.None)
        {
            return (ReasonSignal.None, null);
        }

        foreach (var signal in Supporting(reason))
        {
            if (ledger is not null && !ledger.CanCite(signal, reason.Evidence))
            {
                continue;
            }

            var text = Render(
                signal, ReasonClause.Secondary, reason.Evidence, tuning, NoTokens, ledger);
            if (text is not null)
            {
                return (signal, text);
            }
        }

        return (ReasonSignal.None, null);
    }

    /// <summary>
    /// Yields <see cref="RecommendationReason.Secondary"/> first, then every
    /// other entry in <see cref="RecommendationReason.SupportingSignals"/>
    /// that the head did not already cover, in the scorer's strongest-first
    /// order. Each one is a fact the scorer proved about this card.
    /// </summary>
    private static IEnumerable<ReasonSignal> Supporting(RecommendationReason reason)
    {
        yield return reason.Secondary;

        foreach (var signal in reason.SupportingSignals)
        {
            if (signal != reason.Secondary && signal != ReasonSignal.None)
            {
                yield return signal;
            }
        }
    }

    /// <summary>
    /// Picks one variant for a signal, deterministically from the release id,
    /// among those whose tokens this game can actually fill.
    /// </summary>
    private static string? Render(
        ReasonSignal signal,
        ReasonClause clause,
        ReasonEvidence evidence,
        RecommendationTuning tuning,
        string[] forbidden,
        ShelfReasonLedger? ledger)
    {
        var variants = ReasonPhrasebook.Variants(signal, clause);
        if (variants.Count == 0)
        {
            return null;
        }

        // Specific over generic. A variant that cites one of this game's own
        // numbers is always preferred to one that would be equally true of any
        // game — that preference is what stops a feed of "it's in your library"
        // cards, and it is why the token-free variant is a fallback rather than
        // an equal option.
        List<string>? specific = null;
        List<string>? generic = null;
        foreach (var variant in variants)
        {
            if (!CanFill(variant, evidence, tuning, forbidden))
            {
                continue;
            }

            if (variant.IndexOf('{') >= 0)
            {
                (specific ??= []).Add(variant);
            }
            else
            {
                (generic ??= []).Add(variant);
            }
        }

        var usable = specific ?? generic;
        if (usable is null)
        {
            return null;
        }

        // Deterministic per (release, signal, clause): the same game renders
        // the same sentence on every reload, and neighbouring games in one
        // feed land on different phrasings.
        var seed = (int)(Hash(evidence.ReleaseId, (int)signal * 31 + (int)clause)
            % (ulong)usable.Count);

        // The hash alone chooses per card with no knowledge of the shelf, so
        // two cards can land on the same variant; observed 2026-09-02 on the
        // patched shelf where Stationeers and PEAK drew the same sentence.
        // The ledger is the shelf's memory: the hash still chooses first and
        // a card only moves off its own pick when that pick is already spoken.
        //
        // A fresh generic beats a repeated specific, which is the one place
        // the specific-over-generic preference above yields. A signal can
        // easily have fewer token-bearing variants than a shelf has slots
        // (patched-since has four when no update carries a title), so holding
        // the preference absolutely would force the repetition the ledger
        // exists to prevent.
        if (ledger is not null)
        {
            var chosen = ledger.ClaimVariant(signal, clause, usable, seed)
                ?? (specific is not null && generic is not null
                    ? ledger.ClaimVariant(signal, clause, generic, seed)
                    : null);

            if (chosen is not null)
            {
                return Fill(chosen, evidence, tuning);
            }
        }

        return Fill(usable[seed], evidence, tuning);
    }

    private static bool CanFill(
        string template, ReasonEvidence evidence, RecommendationTuning tuning, string[] forbidden)
    {
        foreach (var token in Tokens(template))
        {
            if (Array.IndexOf(forbidden, token) >= 0)
            {
                return false;
            }

            if (ReasonTokens.Resolve(token, evidence, tuning) is null)
            {
                return false;
            }
        }

        return true;
    }

    private static string Fill(string template, ReasonEvidence evidence, RecommendationTuning tuning)
    {
        if (template.IndexOf('{') < 0)
        {
            return template;
        }

        var result = new StringBuilder(template.Length + 32);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                result.Append(template[i]);
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                result.Append(template[i]);
                continue;
            }

            var token = template[(i + 1)..close];
            result.Append(ReasonTokens.Resolve(token, evidence, tuning) ?? string.Empty);
            i = close;
        }

        return result.ToString();
    }

    private static IEnumerable<string> Tokens(string template)
    {
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                continue;
            }

            var close = template.IndexOf('}', i + 1);
            if (close < 0)
            {
                yield break;
            }

            yield return template[(i + 1)..close];
            i = close;
        }
    }

    /// <summary>First letter up, so a template may open on a token without the copy having to know.</summary>
    private static string Capitalise(string clause)
        => clause.Length > 0 && char.IsLower(clause[0])
            ? char.ToUpperInvariant(clause[0]) + clause[1..]
            : clause;

    /// <summary>Exactly one terminator, always, whatever the template ended with.</summary>
    private static string Terminate(string sentence)
    {
        var trimmed = sentence.TrimEnd();
        while (trimmed.Length > 0 && (trimmed[^1] is '.' or ',' or ';' or ' '))
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return trimmed.Length == 0 ? ReasonPhrasebook.Fallback + "." : trimmed + ".";
    }

    /// <summary>Last-resort cut at a word boundary. Should never fire on shipped copy; the budget test proves it.</summary>
    private static string Elide(string sentence, int budget)
    {
        var cut = sentence[..Math.Max(1, budget - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > budget / 2)
        {
            cut = cut[..space];
        }

        return cut.TrimEnd(' ', ',', ';', '.', '—', '-') + "…";
    }

    /// <summary>SplitMix64 over (releaseId, salt) — the same family as the scorer's jitter, and just as reproducible.</summary>
    private static ulong Hash(long releaseId, int salt)
    {
        unchecked
        {
            var x = (ulong)releaseId + 0x9E3779B97F4A7C15UL * (ulong)(uint)(salt + 1);
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
