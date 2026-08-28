using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winnow.Core.Queries;

/// <summary>
/// A library filter: what the filter panel has selected, and what a live list
/// stores in <c>lists.filter_json</c>.
///
/// <para><b>Every field is an "any of"; the fields are "all of".</b> Selecting
/// two genres widens the result to games in either; selecting a genre and a store
/// narrows it to games that are both. That is what the reference storefront does
/// and what a checkbox column means, and it is the only combination rule here —
/// there is no expression tree, no nesting and no negation. If a filter ever
/// needs those, it needs a new version of this type, not a clever
/// reinterpretation of this one.</para>
///
/// <para><b>An empty filter matches everything.</b> <see cref="IsEmpty"/> is the
/// difference between "the user selected nothing" and "the user selected
/// something that nothing satisfies", and the two must never be confused: a live
/// list whose filter failed to parse degrades to the whole library, which is
/// visibly wrong and recoverable, rather than to an empty list, which looks like
/// data loss.</para>
///
/// <para><b>Applied in memory, never pushed into SQL.</b> <c>LibraryViewModel</c>
/// filters the whole library in memory because it is a few hundred kilobytes of
/// projection and re-querying SQLite per keystroke buys nothing; live-list
/// membership is the same question over the same rows and is answered the same
/// way, through <see cref="Apply"/>. One implementation, so a saved list and the
/// panel that saved it can never disagree about what the filter meant.</para>
/// </summary>
public sealed record LibraryFilter
{
    // Every collection below is stored in a backing field and normalised on the
    // way in, so NONE of them can ever be null however the object was made.
    //
    // That is not defensive habit; it is required by the one thing this type
    // promises. `System.Text.Json`'s SOURCE-GENERATED deserializer assigns every
    // property it knows about, using the default for the ones the document does
    // not carry — and for `IReadOnlyList<T>` that default is null, not the
    // property initializer. So a stored filter written before a field existed
    // (or written by a build that simply had nothing selected for it, or hand-
    // edited in a database browser) comes back with a null collection, and the
    // very next `Buckets.Count` throws.
    //
    // That is precisely the case `FromJson` exists to survive. A filter that
    // parses without error and then explodes on first use is not tolerant; it
    // has just moved the failure somewhere harder to find. Measured, not
    // assumed: `FromJson("""{"search":"portal"}""")` returned an object with
    // five null lists before this was here.

    private readonly IReadOnlyList<string> _buckets = [];
    private readonly IReadOnlyList<string> _stores = [];
    private readonly IReadOnlyList<long> _genreIds = [];
    private readonly IReadOnlyList<long> _themeIds = [];
    private readonly IReadOnlyList<long> _tagIds = [];
    private readonly IReadOnlyList<long> _featureIds = [];
    private readonly IReadOnlyList<long> _controllerIds = [];
    private readonly IReadOnlyList<string> _gameModes = [];

    /// <summary>Derived bucket keys (<see cref="LibraryBuckets"/>).</summary>
    public IReadOnlyList<string> Buckets
    {
        get => _buckets;
        init => _buckets = OrEmpty(value);
    }

    /// <summary>Store names as ownership records them ("steam", "gog", "epic").</summary>
    public IReadOnlyList<string> Stores
    {
        get => _stores;
        init => _stores = OrEmpty(value);
    }

    /// <summary>
    /// <see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Genre"/>.
    ///
    /// <para>Winnow's own surrogate ids (migration 0007), not IGDB's — the cached
    /// IGDB payloads carry genre NAMES only, the ids having been dropped at
    /// projection time long before this feature existed. The migration explains
    /// why re-fetching to recover them would have been the more expensive and
    /// more fragile choice.</para>
    /// </summary>
    public IReadOnlyList<long> GenreIds
    {
        get => _genreIds;
        init => _genreIds = OrEmpty(value);
    }

    /// <summary><see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Theme"/>.</summary>
    public IReadOnlyList<long> ThemeIds
    {
        get => _themeIds;
        init => _themeIds = OrEmpty(value);
    }

    /// <summary><see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Tag"/> (Steam store tags).</summary>
    public IReadOnlyList<long> TagIds
    {
        get => _tagIds;
        init => _tagIds = OrEmpty(value);
    }

    /// <summary>
    /// <see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Feature"/> —
    /// Steam's storefront categories: achievements, cloud saves, workshop,
    /// family sharing.
    ///
    /// <para>Ids rather than slugs, unlike <see cref="GameModes"/>, because only
    /// one provider names these. The moment a second one does, this becomes the
    /// same string-keyed problem game modes already are and gets the same
    /// answer — a vocabulary Winnow owns.</para>
    /// </summary>
    public IReadOnlyList<long> FeatureIds
    {
        get => _featureIds;
        init => _featureIds = OrEmpty(value);
    }

    /// <summary>
    /// <see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Controller"/>
    /// — full and partial controller support.
    /// </summary>
    public IReadOnlyList<long> ControllerIds
    {
        get => _controllerIds;
        init => _controllerIds = OrEmpty(value);
    }

    /// <summary>
    /// Game-mode slugs from <see cref="Queries.GameModes"/> — the one facet
    /// matched by name rather than by id, because two providers write it and
    /// neither one's ids could serve as the key.
    /// </summary>
    public IReadOnlyList<string> GameModes
    {
        get => _gameModes;
        init => _gameModes = OrEmpty(value);
    }

    /// <summary>Installed locally. Null means "don't care", which is not the same as false.</summary>
    public bool? Installed { get; init; }

    /// <summary>
    /// Patched since last played — the Flare badge, and membership of
    /// <see cref="LibraryBuckets.StaleButPatched"/>. Null means "don't care".
    /// </summary>
    public bool? HasUnread { get; init; }

    /// <summary>
    /// Earliest first-release year, inclusive. A game whose year is unknown does
    /// NOT match any bounded range: "we have no idea when this came out" is not
    /// evidence that it came out in the window, and quietly including it would
    /// make the count wrong in the direction nobody checks.
    /// </summary>
    public int? YearFrom { get; init; }

    /// <summary>Latest first-release year, inclusive. Same treatment of an unknown year.</summary>
    public int? YearTo { get; init; }

    /// <summary>Case-insensitive substring of the title. Whitespace-only is no filter at all.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Nothing selected, so every row matches. Computed rather than stored so it
    /// cannot drift from the field values, and ignored by the serializer for the
    /// same reason.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty
        => Buckets.Count == 0
           && Stores.Count == 0
           && GenreIds.Count == 0
           && ThemeIds.Count == 0
           && TagIds.Count == 0
           && FeatureIds.Count == 0
           && ControllerIds.Count == 0
           && GameModes.Count == 0
           && Installed is null
           && HasUnread is null
           && YearFrom is null
           && YearTo is null
           && string.IsNullOrWhiteSpace(Search);

    /// <summary>The filter that selects nothing and therefore matches everything.</summary>
    public static LibraryFilter Empty { get; } = new();

    /// <summary>
    /// The normaliser the init accessors run values through. The parameter is
    /// nullable so that passing a value the compiler believes cannot be null is
    /// not flagged as a redundant check — the deserializer disagrees with the
    /// compiler here, and the deserializer is the one holding the file.
    /// </summary>
    private static IReadOnlyList<T> OrEmpty<T>(IReadOnlyList<T>? values) => values ?? [];

    /// <summary>
    /// Serialises for storage in <c>lists.filter_json</c>.
    ///
    /// <para>Unset scalars are omitted rather than written as null, so a stored
    /// filter reads as what the user actually chose. Empty collections are
    /// written as <c>[]</c>: the serializer has no "omit if empty" for them, and
    /// inventing one through a converter would buy a few bytes at the cost of the
    /// one thing this method must be, which is boring.</para>
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, LibraryFilterJson.Default.LibraryFilter);

    /// <summary>
    /// Reads a stored filter. <b>Total: never throws, for any input.</b>
    ///
    /// <para>Null, empty, malformed JSON, JSON of the wrong shape, and JSON
    /// written by a future version carrying fields this build does not know all
    /// return a usable filter. Unknown fields are ignored; absent fields take
    /// their defaults.</para>
    ///
    /// <para><b>Why totality is the requirement and not merely nice.</b> This
    /// value comes off disk, and the row it comes from is a list the user made.
    /// A parse failure that throws is a list that vanishes from the sidebar and
    /// takes its name and description with it; a parse failure that returns
    /// <see cref="Empty"/> is a list that shows the whole library until it is
    /// re-saved. One of those is a bug and the other is data loss.</para>
    /// </summary>
    public static LibraryFilter FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize(json, LibraryFilterJson.Default.LibraryFilter) ?? Empty;
        }
        catch (JsonException)
        {
            // Malformed, truncated, or simply not JSON. See the remarks: the
            // caller gets the whole library, never an exception.
            return Empty;
        }
        catch (NotSupportedException)
        {
            // Valid JSON of a shape no converter can read — e.g. a field this
            // build declares as a number arriving as an object because a later
            // version changed its meaning. Same treatment, same reason.
            return Empty;
        }
    }

    /// <summary>
    /// Structural equality, because the compiler-generated version is not.
    ///
    /// <para>A record compares its fields with <c>EqualityComparer&lt;T&gt;</c>,
    /// which for <c>IReadOnlyList&lt;T&gt;</c> means REFERENCE equality — so two
    /// filters carrying the same three genre ids in two different arrays would
    /// compare unequal. The one thing callers most want to ask a filter is
    /// "is this the same as the one I already applied", and a record that
    /// answers that with "no, always" is a record that repaints the library on
    /// every keystroke and re-saves a list that did not change.</para>
    ///
    /// <para><b>Set comparison, not sequence comparison.</b> Every collection
    /// here means "any of", so order carries no meaning and neither do
    /// duplicates: <c>[3, 9]</c>, <c>[9, 3]</c> and <c>[3, 9, 3]</c> select
    /// exactly the same games and are the same filter.</para>
    /// </summary>
    public bool Equals(LibraryFilter? other)
        => other is not null
           && SameSet(Buckets, other.Buckets, StringComparer.Ordinal)
           && SameSet(Stores, other.Stores, StringComparer.OrdinalIgnoreCase)
           && SameSet(GenreIds, other.GenreIds, EqualityComparer<long>.Default)
           && SameSet(ThemeIds, other.ThemeIds, EqualityComparer<long>.Default)
           && SameSet(TagIds, other.TagIds, EqualityComparer<long>.Default)
           && SameSet(FeatureIds, other.FeatureIds, EqualityComparer<long>.Default)
           && SameSet(ControllerIds, other.ControllerIds, EqualityComparer<long>.Default)
           && SameSet(GameModes, other.GameModes, StringComparer.Ordinal)
           && Installed == other.Installed
           && HasUnread == other.HasUnread
           && YearFrom == other.YearFrom
           && YearTo == other.YearTo
           && string.Equals(Search?.Trim(), other.Search?.Trim(), StringComparison.Ordinal);

    /// <summary>
    /// Order-independent, to agree with <see cref="Equals(LibraryFilter)"/>:
    /// the collections are combined by XOR, which is commutative, rather than by
    /// the usual order-sensitive rolling hash.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SetHash(Buckets, StringComparer.Ordinal));
        hash.Add(SetHash(Stores, StringComparer.OrdinalIgnoreCase));
        hash.Add(SetHash(GenreIds, EqualityComparer<long>.Default));
        hash.Add(SetHash(ThemeIds, EqualityComparer<long>.Default));
        hash.Add(SetHash(TagIds, EqualityComparer<long>.Default));
        hash.Add(SetHash(FeatureIds, EqualityComparer<long>.Default));
        hash.Add(SetHash(ControllerIds, EqualityComparer<long>.Default));
        hash.Add(SetHash(GameModes, StringComparer.Ordinal));
        hash.Add(Installed);
        hash.Add(HasUnread);
        hash.Add(YearFrom);
        hash.Add(YearTo);
        hash.Add(Search?.Trim(), StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static bool SameSet<T>(
        IReadOnlyList<T> left, IReadOnlyList<T> right, IEqualityComparer<T> comparer)
        => ReferenceEquals(left, right)
           || new HashSet<T>(left, comparer).SetEquals(right);

    private static int SetHash<T>(IReadOnlyList<T> values, IEqualityComparer<T> comparer)
    {
        var hash = 0;
        foreach (var value in new HashSet<T>(values, comparer))
        {
            hash ^= value is null ? 0 : comparer.GetHashCode(value);
        }

        return hash;
    }

    /// <summary>
    /// Whether one row satisfies this filter.
    ///
    /// <para>Convenience for a single row; <see cref="Apply"/> is the form to use
    /// over a collection, because it builds the lookup sets once instead of once
    /// per row.</para>
    /// </summary>
    public bool Matches(FilterableRow row) => new LibraryFilterMatcher(this).Matches(row);

    /// <summary>
    /// The rows that satisfy this filter, in the order given.
    ///
    /// <para>This is live-list membership. A live list stores this filter and no
    /// <c>list_items</c>, so its contents are whatever this returns at the moment
    /// it is read — which is the entire point: a list defined as "RPGs I have
    /// never played" gains and loses members as playtime and metadata change, and
    /// materialising it into rows would freeze it into a manual list wearing a
    /// live list's name.</para>
    /// </summary>
    public IReadOnlyList<FilterableRow> Apply(IEnumerable<FilterableRow> rows)
    {
        if (IsEmpty)
        {
            return rows as IReadOnlyList<FilterableRow> ?? rows.ToArray();
        }

        var matcher = new LibraryFilterMatcher(this);
        return rows.Where(matcher.Matches).ToArray();
    }
}

/// <summary>
/// One library row as the filter sees it: the projection
/// <see cref="LibraryFilter"/> asks questions of, and nothing more.
///
/// <para>Assembled by the caller from the derived-bucket query, the ownership
/// row and the facet snapshot. Deliberately a flat record of answers rather than
/// a reference to a view model, so live-list membership can be computed —
/// and tested — without a UI.</para>
/// </summary>
/// <param name="ReleaseId">The release this row is about.</param>
/// <param name="OwnershipId">The ownership this row is about; a release owned on two stores is two rows.</param>
/// <param name="Bucket">Its derived bucket (<see cref="LibraryBuckets"/>), from the bucket query.</param>
/// <param name="Store">The store that sold it.</param>
/// <param name="Title">The display title, for <see cref="LibraryFilter.Search"/>.</param>
/// <param name="Installed">Whether it is installed locally.</param>
/// <param name="HasUnread">
/// Whether an update landed after the last session — the same fact as membership
/// of <see cref="LibraryBuckets.StaleButPatched"/> (design-system §5.2), passed
/// in rather than re-derived so the badge and the filter cannot disagree.
/// </param>
/// <param name="FirstReleaseYear">Year of first release, or null when unknown.</param>
/// <param name="FacetIds">Every facet id true of this release, across every kind.</param>
/// <param name="GameModes">Game-mode slugs true of this release.</param>
public sealed record FilterableRow(
    long ReleaseId,
    long OwnershipId,
    string Bucket,
    string Store,
    string Title,
    bool Installed,
    bool HasUnread,
    int? FirstReleaseYear,
    IReadOnlyList<long> FacetIds,
    IReadOnlyList<string> GameModes);

/// <summary>
/// A <see cref="LibraryFilter"/> with its lookup sets built once, so filtering a
/// library is one pass rather than one set-construction per row.
/// </summary>
internal sealed class LibraryFilterMatcher
{
    private readonly LibraryFilter _filter;
    private readonly HashSet<string>? _buckets;
    private readonly HashSet<string>? _stores;
    private readonly HashSet<string>? _gameModes;

    // The three id fields are kept apart rather than merged into one set even
    // though facet ids share a namespace: a filter naming a genre AND a tag must
    // require both, and a single merged set would silently turn that "and" into
    // an "or" — the one mistake in this file that would produce plausible,
    // wrong results instead of visibly broken ones.
    private readonly HashSet<long>? _genreIds;
    private readonly HashSet<long>? _themeIds;
    private readonly HashSet<long>? _tagIds;
    private readonly HashSet<long>? _featureIds;
    private readonly HashSet<long>? _controllerIds;

    internal LibraryFilterMatcher(LibraryFilter filter)
    {
        _filter = filter;
        _buckets = Set(filter.Buckets, StringComparer.Ordinal);
        _stores = Set(filter.Stores, StringComparer.OrdinalIgnoreCase);
        _gameModes = Set(filter.GameModes, StringComparer.Ordinal);
        _genreIds = Set(filter.GenreIds);
        _themeIds = Set(filter.ThemeIds);
        _tagIds = Set(filter.TagIds);
        _featureIds = Set(filter.FeatureIds);
        _controllerIds = Set(filter.ControllerIds);
    }

    internal bool Matches(FilterableRow row)
    {
        if (_buckets is not null && !_buckets.Contains(row.Bucket))
        {
            return false;
        }

        if (_stores is not null && !_stores.Contains(row.Store))
        {
            return false;
        }

        if (_filter.Installed is { } installed && row.Installed != installed)
        {
            return false;
        }

        if (_filter.HasUnread is { } unread && row.HasUnread != unread)
        {
            return false;
        }

        if (_filter.YearFrom is { } from && (row.FirstReleaseYear is null || row.FirstReleaseYear < from))
        {
            return false;
        }

        if (_filter.YearTo is { } to && (row.FirstReleaseYear is null || row.FirstReleaseYear > to))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_filter.Search)
            && !row.Title.Contains(_filter.Search.Trim(), StringComparison.CurrentCultureIgnoreCase))
        {
            return false;
        }

        if (_gameModes is not null && !row.GameModes.Any(_gameModes.Contains))
        {
            return false;
        }

        return MatchesFacets(_genreIds, row)
               && MatchesFacets(_themeIds, row)
               && MatchesFacets(_tagIds, row)
               && MatchesFacets(_featureIds, row)
               && MatchesFacets(_controllerIds, row);
    }

    private static bool MatchesFacets(HashSet<long>? selected, FilterableRow row)
    {
        if (selected is null)
        {
            return true;
        }

        foreach (var facetId in row.FacetIds)
        {
            if (selected.Contains(facetId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Null for "no selection", which is the cheap test for "no constraint".</summary>
    private static HashSet<string>? Set(IReadOnlyList<string> values, StringComparer comparer)
    {
        var set = new HashSet<string>(values.Where(v => !string.IsNullOrWhiteSpace(v)), comparer);
        return set.Count == 0 ? null : set;
    }

    private static HashSet<long>? Set(IReadOnlyList<long> values)
    {
        var set = new HashSet<long>(values);
        return set.Count == 0 ? null : set;
    }
}

/// <summary>
/// Source-generated serialization context for <see cref="LibraryFilter"/>.
///
/// <para>Source-generated rather than reflection-based because §3.1 keeps
/// NativeAOT viable, and reflection-based <c>System.Text.Json</c> is one of the
/// things that quietly makes it not.</para>
/// </summary>
[JsonSourceGenerationOptions(
    // Matches the snake_case the rest of Winnow's stored and wire JSON uses, and
    // keeps the stored value readable to anyone opening the database by hand.
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    // Null and empty collections are omitted; see LibraryFilter.ToJson.
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryFilter))]
internal sealed partial class LibraryFilterJson : JsonSerializerContext;
