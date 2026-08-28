namespace Hoard.App.ViewModels;

/// <summary>
/// One stretch of a reason sentence, and whether it is prose or data.
/// </summary>
/// <param name="Text">The characters, in order. Concatenating every run reproduces the sentence exactly.</param>
/// <param name="IsData">
/// True when this run is a number — set in IBM Plex Mono with tabular figures
/// by the card, like every other number in the app (§3).
/// </param>
public readonly record struct ReasonRun(string Text, bool IsData);

/// <summary>
/// Splits a reason sentence into prose runs and data runs, so a card can set
/// its numbers in the data face without touching the engine's words.
///
/// <para><b>Why this exists at all.</b> §3's rule is that every number in the
/// app renders in Plex Mono with tabular figures, and the feed is the surface
/// where the app's numbers stopped living in columns and moved into sentences —
/// "You put 2.8 hours into this in 2021 and it has had an update since". Setting
/// the whole sentence in the body face would be the first place in Hoard where a
/// playtime and a year are not in the data face; setting the whole thing in the
/// data face would turn a sentence into a readout. So the sentence keeps its
/// voice and the numbers keep theirs.</para>
///
/// <para><b>The rule is per WORD, not per digit</b>, and that is deliberate. A
/// digit-level split cuts "7.9.1b" into "7.9.1" and "b", and "v0.2.6428.27798"
/// into four pieces — version strings are data all the way through, and the
/// patch titles the engine quotes are full of them. Any whitespace-delimited
/// word containing a digit is data; everything else is prose. One rule, and it
/// is explainable out loud, which is the same standard the reasons themselves
/// are held to.</para>
///
/// <para>Punctuation the sentence owns is peeled back off the edges — the
/// quotation marks around a patch title and the full stop that ends a clause
/// belong to the prose, not to the number inside them.</para>
/// </summary>
public static class ReasonText
{
    /// <summary>Characters that belong to the sentence even when they touch a number.</summary>
    private const string EdgePunctuation = "\"'“”‘’(),.;:!?—–-";

    /// <summary>
    /// Splits <paramref name="reason"/> into alternating prose and data runs.
    /// Adjacent runs of the same kind are merged, so the result is the shortest
    /// sequence that says the same thing.
    /// </summary>
    public static IReadOnlyList<ReasonRun> Split(string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return [];
        }

        var runs = new List<ReasonRun>();
        var prose = new System.Text.StringBuilder();

        void FlushProse()
        {
            if (prose.Length > 0)
            {
                runs.Add(new ReasonRun(prose.ToString(), IsData: false));
                prose.Clear();
            }
        }

        var index = 0;
        while (index < reason.Length)
        {
            if (char.IsWhiteSpace(reason[index]))
            {
                prose.Append(reason[index++]);
                continue;
            }

            var wordEnd = index;
            while (wordEnd < reason.Length && !char.IsWhiteSpace(reason[wordEnd]))
            {
                wordEnd++;
            }

            var word = reason.AsSpan(index, wordEnd - index);
            if (!ContainsDigit(word))
            {
                prose.Append(word);
                index = wordEnd;
                continue;
            }

            // Peel the sentence's own punctuation off both ends: the quotation
            // marks around a patch title are the sentence speaking, not part of
            // the version number inside them.
            var start = 0;
            while (start < word.Length && EdgePunctuation.Contains(word[start]))
            {
                start++;
            }

            var end = word.Length;
            while (end > start && EdgePunctuation.Contains(word[end - 1]))
            {
                end--;
            }

            prose.Append(word[..start]);
            FlushProse();

            Append(runs, new ReasonRun(word[start..end].ToString(), IsData: true));
            prose.Append(word[end..]);
            index = wordEnd;
        }

        FlushProse();
        return runs;
    }

    /// <summary>Merges into the previous run when it is the same kind, so nothing splits twice.</summary>
    private static void Append(List<ReasonRun> runs, ReasonRun run)
    {
        if (runs.Count > 0 && runs[^1].IsData == run.IsData)
        {
            runs[^1] = runs[^1] with { Text = runs[^1].Text + run.Text };
            return;
        }

        runs.Add(run);
    }

    private static bool ContainsDigit(ReadOnlySpan<char> word)
    {
        foreach (var character in word)
        {
            if (char.IsAsciiDigit(character))
            {
                return true;
            }
        }

        return false;
    }
}
