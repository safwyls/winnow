namespace Winnow.App.ViewModels;

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
/// Splits a reason sentence into prose and data runs so numbers render in Plex
/// Mono with tabular figures (§3). Any whitespace-delimited word containing a
/// digit is data; sentence-owned punctuation at the edges stays prose.
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
