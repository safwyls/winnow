using System.Globalization;

namespace Winnow.App.ViewModels;

/// <summary>
/// The history log's own naming rule for members of one act. Narrower than
/// <see cref="MergeMemberLabels"/>: it carries at most a store name, never
/// year, publisher or position. A card is one question being answered and
/// can afford four qualifying facts; the log is a list being scanned and
/// must stay short. The divergence is deliberate and permanent.
/// </summary>
internal static class MergeHistoryLabels
{
    /// <summary>
    /// Returns a display label for each member, index-aligned with the
    /// inputs. Index 0 is the parent (drawn as the row's headline); the
    /// rest are children in work id order (drawn as subtext beneath it).
    /// Children are qualified first; the headline takes its store only when
    /// a child still renders identically to it, which keeps the plain game
    /// name on the headline in the ordinary case.
    /// </summary>
    /// <param name="titles">
    /// Game title of each member, index-aligned. Index 0 is the headline;
    /// the rest are children in work id order.
    /// </param>
    /// <param name="storeNames">
    /// Comma-joined store display names for each member, index-aligned with
    /// <paramref name="titles"/>. Empty when no ownership row names a store.
    /// </param>
    public static IReadOnlyList<string> For(
        IReadOnlyList<string> titles, IReadOnlyList<string> storeNames)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(storeNames);

        if (titles.Count != storeNames.Count)
        {
            throw new ArgumentException(
                "A store entry is required for every title.", nameof(storeNames));
        }

        var labels = new string[titles.Count];
        for (var i = 0; i < titles.Count; i++)
        {
            labels[i] = titles[i];
        }

        if (titles.Count < 2)
        {
            return labels;
        }

        // Children first. The headline takes its store only if, after the
        // children have had their turn, a child still renders a string
        // identical to it.
        for (var i = 1; i < titles.Count; i++)
        {
            if (StoreSeparates(titles, storeNames, i))
            {
                labels[i] = Qualify(titles[i], storeNames[i]);
            }
        }

        for (var i = 1; i < titles.Count; i++)
        {
            if (Same(labels[i], titles[0]) && StoreSeparates(titles, storeNames, 0))
            {
                labels[0] = Qualify(titles[0], storeNames[0]);
                break;
            }
        }

        return labels;
    }

    // A store that every same-titled member shares separates nothing and is
    // not printed. Printing it would add "(Steam)" to rows that already read
    // identically, making labels longer without making any member
    // distinguishable.
    private static bool StoreSeparates(
        IReadOnlyList<string> titles, IReadOnlyList<string> storeNames, int index)
    {
        if (storeNames[index].Length == 0)
        {
            return false;
        }

        for (var other = 0; other < titles.Count; other++)
        {
            if (other != index
                && Same(titles[other], titles[index])
                && !Same(storeNames[other], storeNames[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static string Qualify(string title, string storeNames)
        => string.Format(
            CultureInfo.CurrentCulture, MergeCopy.HistoryQualifierFormat, title, storeNames);

    private static bool Same(string left, string right)
        => string.Equals(left, right, StringComparison.CurrentCultureIgnoreCase);
}
