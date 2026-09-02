using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winnow.Core.Queries;

/// <summary>
/// A library filter stored in <c>lists.filter_json</c>. Within each field, values
/// are OR'd ("any of"); across fields, they are AND'd ("all of").
/// An empty filter matches everything. Applied in memory, not pushed into SQL.
/// </summary>
public sealed record LibraryFilter
{
    // Backing fields ensure collections are never null. The source-generated
    // JSON deserializer defaults IReadOnlyList<T> to null (not the initializer),
    // so without this, a stored filter missing a field would throw on .Count.

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

    /// <summary><see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Genre"/> (Winnow surrogate ids, not IGDB's).</summary>
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

    /// <summary><see cref="Facet.Id"/> values of kind <see cref="FacetKinds.Feature"/> (Steam storefront categories).</summary>
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

    /// <summary>Game-mode slugs from <see cref="Queries.GameModes"/>. Matched by slug, not id.</summary>
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

    /// <summary>Earliest first-release year, inclusive. Unknown years do not match any bounded range.</summary>
    public int? YearFrom { get; init; }

    /// <summary>Latest first-release year, inclusive. Same treatment of an unknown year.</summary>
    public int? YearTo { get; init; }

    /// <summary>Case-insensitive substring of the title. Whitespace-only is no filter at all.</summary>
    public string? Search { get; init; }

    /// <summary>True when nothing is selected (every row matches).</summary>
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

    /// <summary>Returns the list or empty if null (guards against deserializer nulls).</summary>
    private static IReadOnlyList<T> OrEmpty<T>(IReadOnlyList<T>? values) => values ?? [];

    /// <summary>Serialises for storage in <c>lists.filter_json</c>.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, LibraryFilterJson.Default.LibraryFilter);

    /// <summary>Deserialises a stored filter. Never throws; returns <see cref="Empty"/> on any parse failure.</summary>
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

    /// <summary>Set-based structural equality (order-independent, since each collection means "any of").</summary>
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

    /// <summary>Order-independent hash to agree with <see cref="Equals(LibraryFilter)"/>.</summary>
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

    /// <summary>Whether one row satisfies this filter. Prefer <see cref="Apply"/> for collections.</summary>
    public bool Matches(FilterableRow row) => new LibraryFilterMatcher(this).Matches(row);

    /// <summary>Returns the rows satisfying this filter, preserving input order.</summary>
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
/// One library row's filterable attributes, assembled from bucket query + ownership + facets.
/// </summary>
/// <param name="ReleaseId">The release this row is about.</param>
/// <param name="OwnershipId">The primary ownership of this game. Since TASK-70.6 the grid is one tile per game, so a game owned on two stores is one row here and names its primary entry; the stores it is owned on are in <paramref name="Stores"/>.</param>
/// <param name="Bucket">Its derived bucket (<see cref="LibraryBuckets"/>), from the bucket query.</param>
/// <param name="Stores">Every store this game is owned on. A store cut matches when any of them matches, so a twice-owned game is kept by either store's option and counts under both (§11.2).</param>
/// <param name="Title">The display title, for <see cref="LibraryFilter.Search"/>.</param>
/// <param name="Installed">Whether it is installed locally.</param>
/// <param name="HasUnread">Whether an update landed after the last session.</param>
/// <param name="FirstReleaseYear">Year of first release, or null when unknown.</param>
/// <param name="FacetIds">Every facet id true of this release, across every kind.</param>
/// <param name="GameModes">Game-mode slugs true of this release.</param>
public sealed record FilterableRow(
    long ReleaseId,
    long OwnershipId,
    string Bucket,
    IReadOnlyList<string> Stores,
    string Title,
    bool Installed,
    bool HasUnread,
    int? FirstReleaseYear,
    IReadOnlyList<long> FacetIds,
    IReadOnlyList<string> GameModes);

/// <summary>Pre-built lookup sets for a <see cref="LibraryFilter"/>, so filtering is one pass.</summary>
internal sealed class LibraryFilterMatcher
{
    private readonly LibraryFilter _filter;
    private readonly HashSet<string>? _buckets;
    private readonly HashSet<string>? _stores;
    private readonly HashSet<string>? _gameModes;

    // Facet-kind sets are kept separate so genre AND tag means both are required.
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

        // Any store, not one. A game owned on Steam and Epic is one tile
        // with two chips; asking for one store keeps it, and the option's
        // count includes it. Otherwise the Platforms screen and the panel
        // would report different numbers for the same relation (§11.2).
        if (_stores is not null && !row.Stores.Any(_stores.Contains))
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

/// <summary>Source-generated serialization context for <see cref="LibraryFilter"/>.</summary>
[JsonSourceGenerationOptions(
    // Matches the snake_case the rest of Winnow's stored and wire JSON uses, and
    // keeps the stored value readable to anyone opening the database by hand.
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    // Null and empty collections are omitted; see LibraryFilter.ToJson.
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LibraryFilter))]
internal sealed partial class LibraryFilterJson : JsonSerializerContext;
