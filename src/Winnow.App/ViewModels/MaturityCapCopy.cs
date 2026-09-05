using System.Globalization;
using Winnow.Core.Queries;

namespace Winnow.App.ViewModels;

public static class MaturityCapCopy
{
    public const string Heading = "PLACEHOLDER_HEADING";

    public const string Explanation = "PLACEHOLDER_EXPLANATION";

    public const string ClampedNote = "PLACEHOLDER_CLAMPED_NOTE";

    public const string NothingHidden = "PLACEHOLDER_NOTHING_HIDDEN";

    public const string HiddenOne = "PLACEHOLDER_HIDDEN_ONE";

    public const string HiddenManyFormat = "PLACEHOLDER_HIDDEN_MANY {0}";

    public static string LabelFor(MaturityTier tier) => tier switch
    {
        MaturityTier.Everyone => "PLACEHOLDER_EVERYONE",
        MaturityTier.Preteen => "PLACEHOLDER_PRETEEN",
        MaturityTier.Teen => "PLACEHOLDER_TEEN",
        MaturityTier.Mature => "PLACEHOLDER_MATURE",
        MaturityTier.Restricted18 => "PLACEHOLDER_RESTRICTED18",
        _ => "PLACEHOLDER_ADULTS_ONLY",
    };

    public static string HiddenText(int count) => count switch
    {
        <= 0 => NothingHidden,
        1 => HiddenOne,
        _ => string.Format(CultureInfo.CurrentCulture, HiddenManyFormat, count.ToString("N0", CultureInfo.CurrentCulture)),
    };
}
