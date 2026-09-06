using System.Globalization;
using Winnow.Core.Queries;

namespace Winnow.App.ViewModels;

/// <summary>
/// User-facing copy for the rating-cap slider in the Display preferences
/// popover. The cap hides works whose highest stored maturity evidence
/// exceeds the chosen tier. A work with no rating evidence is never hidden;
/// <see cref="MaturityTiers.IsWithinCap"/> treats Unrated as within every
/// cap, and these strings must not imply otherwise.
/// </summary>
public static class MaturityCapCopy
{
    /// <summary>Label beside the slider. Sentence-case body text, matching the
    /// other Display preference labels.</summary>
    public const string Heading = "Rating cap";

    /// <summary>One line under the slider. States what the cap does and that
    /// unrated games are unaffected.</summary>
    public const string Explanation = "Hides games rated above this level. Unrated games always stay.";

    /// <summary>
    /// Shown when the cap includes adults-only content but the Settings ›
    /// Library 18+ toggle is off, so that toggle is still hiding it. Names
    /// which control is responsible so the user can tell where a game went.
    /// </summary>
    public const string ClampedNote =
        "Adults-only content is still hidden by the toggle in Settings › Library.";

    /// <summary>Status line when the cap is not hiding any titles.</summary>
    public const string NothingHidden = "No titles hidden.";

    /// <summary>Status line when the cap is hiding exactly one title.</summary>
    public const string HiddenOne = "Hiding 1 title.";

    /// <summary>Status line when the cap is hiding more than one title.
    /// <c>{0}</c> is the formatted count.</summary>
    public const string HiddenManyFormat = "Hiding {0} titles.";

    /// <summary>
    /// The label shown beside the slider for the given cap position. Each
    /// label names the tier in ordinary language without reference to any
    /// specific rating board, because the scale spans ESRB, PEGI, USK, CERO,
    /// GRAC, ClassInd and ACB.
    /// </summary>
    public static string LabelFor(MaturityTier tier) => tier switch
    {
        MaturityTier.Everyone => "All ages",
        MaturityTier.Preteen => "Preteen",
        MaturityTier.Teen => "Teen",
        MaturityTier.Mature => "Mature",
        MaturityTier.Restricted18 => "18+",
        _ => "Adults only",
    };

    /// <summary>
    /// Returns the status line for the number of titles the cap is currently
    /// hiding. The count excludes titles already hidden by the 18+ toggle.
    /// </summary>
    public static string HiddenText(int count) => count switch
    {
        <= 0 => NothingHidden,
        1 => HiddenOne,
        _ => string.Format(CultureInfo.CurrentCulture, HiddenManyFormat, count.ToString("N0", CultureInfo.CurrentCulture)),
    };
}
