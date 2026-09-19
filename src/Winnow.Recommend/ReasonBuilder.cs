using System.Text;

namespace Winnow.Recommend;

/// <summary>Renders the primary evidence as one bounded sentence, with stable wording variety.</summary>
internal static class ReasonBuilder
{
    public static string Build(
        RecommendationReason reason,
        RecommendationTuning tuning,
        ShelfReasonLedger? ledger = null)
    {
        if (reason.Primary == ReasonSignal.LastPlayed && reason.Evidence.LastPlayedAt is { } lastPlayed)
        {
            return $"Last played on {lastPlayed.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}.";
        }

        if (reason.Primary == ReasonSignal.Lifecycle && reason.Evidence.Lifecycle is { } lifecycle)
        {
            return $"{lifecycle.Reason.TrimEnd('.', '!', '?')} ({lifecycle.Confidence.ToString("P0", System.Globalization.CultureInfo.InvariantCulture)} confidence).";
        }

        var budget = Math.Max(40, tuning.ReasonCharacterBudget);
        var evidence = reason.Evidence;

        var primary = Capitalise(Render(reason.Primary, evidence, tuning, ledger)
            ?? ReasonPhrasebook.Fallback);

        var alone = Terminate(primary);
        return alone.Length <= budget ? alone : Elide(alone, budget);
    }

    /// <summary>
    /// Picks one variant for a signal, deterministically from the release id,
    /// among those whose tokens this game can actually fill.
    /// </summary>
    private static string? Render(
        ReasonSignal signal,
        ReasonEvidence evidence,
        RecommendationTuning tuning,
        ShelfReasonLedger? ledger)
    {
        var variants = ReasonPhrasebook.Variants(signal);
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
            if (!CanFill(variant, evidence, tuning))
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

        // Deterministic per (release, signal): the same game renders
        // the same sentence on every reload, and neighbouring games in one
        // feed land on different phrasings.
        var seed = (int)(Hash(evidence.ReleaseId, (int)signal * 31)
            % (ulong)usable.Count);

        // Prefer fresh wording across a surface, even a generic variant,
        // before repeating a specific one.
        if (ledger is not null)
        {
            var chosen = ledger.ClaimVariant(signal, usable, seed)
                ?? (specific is not null && generic is not null
                    ? ledger.ClaimVariant(signal, generic, seed)
                    : null);

            if (chosen is not null)
            {
                return Fill(chosen, evidence, tuning);
            }
        }

        return Fill(usable[seed], evidence, tuning);
    }

    private static bool CanFill(
        string template, ReasonEvidence evidence, RecommendationTuning tuning)
    {
        foreach (var token in Tokens(template))
        {
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
