namespace Winnow.App.ViewModels;

/// <summary>
/// The five kinds of proposal the Merges screen groups into sections, in the
/// order the sections are drawn. A same-game proposal is ACROSS STORES when
/// its members are owned on more than one store and EDITIONS otherwise; an
/// expansion proposal is TEST BUILDS when its relation is <c>variant_of</c>,
/// PARTS when the storefront's word for it is an episode or a season, and
/// EXPANSIONS for everything else.
/// </summary>
public enum MergeSectionKind
{
    /// <summary>The same game bought more than once.</summary>
    Stores,

    /// <summary>Remasters and re-releases, owned on one store.</summary>
    Editions,

    /// <summary>Content that needs the base game to run.</summary>
    Expansions,

    /// <summary>Entries the store lists separately but ships as one release.</summary>
    Parts,

    /// <summary>Demos, betas and playtests that shipped as their own entry.</summary>
    Tests,
}
