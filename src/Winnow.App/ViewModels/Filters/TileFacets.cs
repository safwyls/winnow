using Winnow.Core.Queries;

namespace Winnow.App.ViewModels.Filters;

/// <summary>
/// One tile's descriptors, split by kind.
///
/// <para><see cref="ReleaseFacets"/> arrives as one flat id set, which is
/// exactly right for <see cref="LibraryFilter"/> — the matcher asks "does this
/// row carry any of these ids" and never needs to know what kind an id is. The
/// panel does: it draws one block per kind and counts each block separately, and
/// resolving a kind through <see cref="FacetSnapshot.ById"/> once per id per
/// option per recount would be a dictionary lookup per checkbox per tile per
/// keystroke. Split once at load instead.</para>
/// </summary>
public sealed record TileFacets
{
    public static TileFacets None { get; } = new();

    public IReadOnlyList<long> GenreIds { get; init; } = [];

    public IReadOnlyList<long> ThemeIds { get; init; } = [];

    public IReadOnlyList<long> TagIds { get; init; } = [];

    /// <summary>Steam storefront categories — achievements, cloud, workshop.</summary>
    public IReadOnlyList<long> FeatureIds { get; init; } = [];

    /// <summary>Full or partial controller support.</summary>
    public IReadOnlyList<long> ControllerIds { get; init; } = [];

    /// <summary>Slugs, not ids — the one kind <see cref="GameModes"/> keys by name.</summary>
    public IReadOnlyList<string> Modes { get; init; } = [];

    /// <summary>
    /// Splits a release's flat facet set by kind. Ids whose kind the panel does
    /// not draw are dropped here rather than carried and ignored later: they are
    /// still in <see cref="FilterableRow.FacetIds"/>, which is what the filter
    /// itself matches on, so nothing is lost.
    /// </summary>
    public static TileFacets From(ReleaseFacets facets, IReadOnlyDictionary<long, Facet> byId)
    {
        List<long>? genres = null;
        List<long>? themes = null;
        List<long>? tags = null;
        List<long>? features = null;
        List<long>? controllers = null;

        foreach (var id in facets.FacetIds)
        {
            if (!byId.TryGetValue(id, out var facet))
            {
                continue;
            }

            switch (facet.Kind)
            {
                case FacetKinds.Genre:
                    (genres ??= []).Add(id);
                    break;
                case FacetKinds.Theme:
                    (themes ??= []).Add(id);
                    break;
                case FacetKinds.Tag:
                    (tags ??= []).Add(id);
                    break;
                case FacetKinds.Feature:
                    (features ??= []).Add(id);
                    break;
                case FacetKinds.Controller:
                    (controllers ??= []).Add(id);
                    break;
            }
        }

        return new TileFacets
        {
            GenreIds = genres ?? (IReadOnlyList<long>)[],
            ThemeIds = themes ?? (IReadOnlyList<long>)[],
            TagIds = tags ?? (IReadOnlyList<long>)[],
            FeatureIds = features ?? (IReadOnlyList<long>)[],
            ControllerIds = controllers ?? (IReadOnlyList<long>)[],
            Modes = facets.GameModes,
        };
    }
}
