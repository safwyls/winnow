namespace Winnow.App.ViewModels;

/// <summary>
/// Confidence as a word, never a score. Declared strongest first so the
/// enum's own order is the "strongest match" sort.
/// </summary>
public enum MergeConfidence
{
    /// <summary>
    /// A same-game group whose strongest proposal sits in the matcher's top
    /// band with identical normalised titles, or an expansion group every
    /// member of which a storefront declared. The only tier the bulk accept
    /// path takes, and then only across stores.
    /// </summary>
    Exact,

    /// <summary>The matcher's top band, or a corroborated title heuristic.</summary>
    Likely,

    /// <summary>Everything below the band. Amber, because it needs reading.</summary>
    WorthALook,
}
