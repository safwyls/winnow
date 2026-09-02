using System.Globalization;
using System.Text;

namespace Winnow.App.ViewModels;

/// <summary>
/// Builds a distinct screen-reader label for each member of one card.
///
/// <para>Labels are built from the facts already drawn on the row: title,
/// then stores, then year, then publisher. Each fact is added only while two
/// members would otherwise share a label, so the common card says "Prey" and
/// only the Prey-against-Prey card says "Prey (Steam, 2017)". A position is
/// the last resort, for two members a storefront describes identically.
/// Database ids are never used (§10.5).</para>
/// </summary>
internal static class MergeMemberLabels
{
    /// <summary>
    /// Returns a label for each member, index-aligned with
    /// <paramref name="sides"/>. Every label is distinct within the card.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyList<MergeSideViewModel> sides)
    {
        ArgumentNullException.ThrowIfNull(sides);

        for (var depth = 0; depth < 4; depth++)
        {
            var labels = new string[sides.Count];
            for (var i = 0; i < sides.Count; i++)
            {
                labels[i] = Label(sides[i], depth, position: 0, of: 0);
            }

            if (AllDistinct(labels))
            {
                return labels;
            }
        }

        var numbered = new string[sides.Count];
        for (var i = 0; i < sides.Count; i++)
        {
            numbered[i] = Label(sides[i], depth: 3, position: i + 1, of: sides.Count);
        }

        return numbered;
    }

    private static string Label(MergeSideViewModel side, int depth, int position, int of)
    {
        var qualifiers = new List<string>(4);

        if (depth >= 1 && side.HasStores)
        {
            qualifiers.Add(side.StoreNames);
        }

        if (depth >= 2 && side.Year is not null)
        {
            qualifiers.Add(side.YearText);
        }

        if (depth >= 3 && side.Publisher is { Length: > 0 } publisher)
        {
            qualifiers.Add(publisher);
        }

        if (position > 0)
        {
            qualifiers.Add(string.Format(
                CultureInfo.CurrentCulture,
                MergeCopy.MemberPositionFormat,
                position.ToString("N0", CultureInfo.CurrentCulture),
                of.ToString("N0", CultureInfo.CurrentCulture)));
        }

        if (qualifiers.Count == 0)
        {
            return side.Title;
        }

        var joined = new StringBuilder();
        for (var i = 0; i < qualifiers.Count; i++)
        {
            if (i > 0)
            {
                joined.Append(MergeCopy.MemberQualifierSeparator);
            }

            joined.Append(qualifiers[i]);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            MergeCopy.MemberLabelFormat,
            side.Title,
            joined.ToString());
    }

    private static bool AllDistinct(string[] labels)
    {
        var seen = new HashSet<string>(labels.Length, StringComparer.CurrentCulture);
        foreach (var label in labels)
        {
            if (!seen.Add(label))
            {
                return false;
            }
        }

        return true;
    }
}
