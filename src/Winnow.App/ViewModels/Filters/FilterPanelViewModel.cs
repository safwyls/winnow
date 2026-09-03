using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Winnow.Core.Queries;

namespace Winnow.App.ViewModels.Filters;

/// <summary>
/// The filter panel: a column of checkable groups with residual counts.
/// Holds only the axes the rail does not (no bucket or unread group).
/// Every group here maps to a <see cref="LibraryFilter"/> field so live lists can persist it.
/// </summary>
public partial class FilterPanelViewModel : ObservableObject
{
    public const string GenreKey = "genre";
    public const string ThemeKey = "theme";
    public const string TagKey = "tag";
    public const string FeatureKey = "feature";
    public const string ControllerKey = "controller";
    public const string ModeKey = "mode";
    public const string StoreKey = "store";
    public const string InstalledKey = "installed";

    /// <summary>Option keys of the two-state on-disk group.</summary>
    internal const string OnDisk = "on_disk";
    internal const string NotOnDisk = "not_on_disk";

    private readonly Action _onChanged;
    private readonly List<GroupSpec> _specs = [];

    /// <summary>Suppresses per-option change callbacks during batch application.</summary>
    private bool _applying;

    public FilterPanelViewModel(Action onChanged)
    {
        _onChanged = onChanged;

        void Changed()
        {
            if (!_applying)
            {
                onChanged();
            }
        }

        // Order: genre > theme > tag (descending curation confidence), then modes,
        // features, controller, store, installed.
        Add(new FilterGroupViewModel(GenreKey, "GENRE", Changed, sortByCount: true),
            t => Ids(t.Facets.GenreIds));
        Add(new FilterGroupViewModel(ThemeKey, "THEME", Changed, sortByCount: true),
            t => Ids(t.Facets.ThemeIds));
        Add(new FilterGroupViewModel(ModeKey, "GAME MODE", Changed),
            t => t.Facets.Modes);
        Add(new FilterGroupViewModel(TagKey, "STORE TAG", Changed, sortByCount: true, findWatermark: "Find a tag…"),
            t => Ids(t.Facets.TagIds));

        Add(new FilterGroupViewModel(FeatureKey, "FEATURES", Changed, sortByCount: true, findWatermark: "Find a feature…"),
            t => Ids(t.Facets.FeatureIds));
        Add(new FilterGroupViewModel(ControllerKey, "CONTROLLER", Changed, sortByCount: true),
            t => Ids(t.Facets.ControllerIds));
        // Every store the tile is owned on, so a game bought twice is
        // counted under both options and kept by either — the same relation
        // the Platforms screen counts, which stops the two from printing
        // different numbers for one question (§11.2).
        Add(new FilterGroupViewModel(StoreKey, "PLATFORM", Changed),
            t => t.Stores);
        // Two-way cut: unknown install state groups with "not on disk".
        Add(new FilterGroupViewModel(InstalledKey, "ON DISK", Changed),
            t => [t.IsOnDisk ? OnDisk : NotOnDisk]);

        Groups = [.. _specs.Select(s => s.Group)];
    }

    public IReadOnlyList<FilterGroupViewModel> Groups { get; }

    /// <summary>Groups with data that can actually filter (excludes empty and single-value-covers-all groups).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups), nameof(HasDescriptorGroups))]
    public partial IReadOnlyList<FilterGroupViewModel> VisibleGroups { get; set; } = [];

    public bool HasGroups => VisibleGroups.Count > 0;

    /// <summary>True when any metadata-derived group (genre, theme, tag, mode, feature, controller) is visible.</summary>
    public bool HasDescriptorGroups => VisibleGroups.Any(g =>
        g.Key is GenreKey or ThemeKey or TagKey or ModeKey or FeatureKey or ControllerKey);

    /// <summary>Panel open state (not persisted across launches).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>Release-year lower bound, as typed text.</summary>
    [ObservableProperty]
    public partial string YearFromText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YearToText { get; set; } = string.Empty;

    /// <summary>Oldest year in the library (watermark for input field).</summary>
    [ObservableProperty]
    public partial string EarliestYearText { get; set; } = "—";

    [ObservableProperty]
    public partial string LatestYearText { get; set; } = "—";

    [ObservableProperty]
    public partial bool HasYearData { get; set; }

    /// <summary>How many rules are in force — the number on the Filters button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ActiveCountText))]
    public partial int ActiveCount { get; set; }

    public bool HasSelection => ActiveCount > 0;

    public string ActiveCountText => ActiveCount.ToString("N0");

    public int? YearFrom => ParseYear(YearFromText);

    public int? YearTo => ParseYear(YearToText);

    /// <summary>Rebuilds every group's options from the current tiles. Selections survive by key.</summary>
    public void Rebuild(IReadOnlyList<GameTileViewModel> tiles, FacetSnapshot snapshot)
    {
        var names = snapshot.ById;

        SetOptions(GenreKey, Labelled(tiles, t => t.Facets.GenreIds, names));
        SetOptions(ThemeKey, Labelled(tiles, t => t.Facets.ThemeIds, names));
        SetOptions(TagKey, Labelled(tiles, t => t.Facets.TagIds, names));
        SetOptions(FeatureKey, Labelled(tiles, t => t.Facets.FeatureIds, names));
        SetOptions(ControllerKey, Labelled(tiles, t => t.Facets.ControllerIds, names));

        SetOptions(ModeKey, tiles
            .SelectMany(t => t.Facets.Modes)
            .Distinct(StringComparer.Ordinal)
            .Select(m => (m, ModeLabel(m, snapshot))));

        SetOptions(StoreKey, tiles
            .SelectMany(t => t.Stores)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => (s, StoreLabel(s))));

        SetOptions(InstalledKey, [(OnDisk, "Installed"), (NotOnDisk, "Not installed")]);

        var years = tiles.Select(t => t.ReleaseYear).Where(y => y is > 0).Select(y => y!.Value).ToList();
        HasYearData = years.Count > 0;
        EarliestYearText = HasYearData ? years.Min().ToString(CultureInfo.InvariantCulture) : "—";
        LatestYearText = HasYearData ? years.Max().ToString(CultureInfo.InvariantCulture) : "—";

        VisibleGroups = [.. _specs
            .Where(s => s.Group.HasOptions && !CannotCut(s, tiles))
            .Select(s => s.Group)];
    }

    /// <summary>True when the group has one option that every title carries (cannot filter).</summary>
    private static bool CannotCut(GroupSpec spec, IReadOnlyList<GameTileViewModel> tiles)
    {
        if (spec.Group.AllOptions.Count != 1 || tiles.Count == 0)
        {
            return false;
        }

        var only = spec.Group.AllOptions[0].Key;
        foreach (var tile in tiles)
        {
            var keys = spec.Keys(tile);
            var carries = false;
            for (var i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], only, StringComparison.Ordinal))
                {
                    carries = true;
                    break;
                }
            }

            if (!carries)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Builds a <see cref="LibraryFilter"/> from the panel's current selections.</summary>
    public LibraryFilter ToFilter() => new()
    {
        GenreIds = LongKeys(GenreKey),
        ThemeIds = LongKeys(ThemeKey),
        TagIds = LongKeys(TagKey),
        FeatureIds = LongKeys(FeatureKey),
        ControllerIds = LongKeys(ControllerKey),
        GameModes = [.. Group(ModeKey).Checked.Select(o => o.Key)],
        Stores = [.. Group(StoreKey).Checked.Select(o => o.Key)],
        Installed = InstalledSelection(),
        YearFrom = YearFrom,
        YearTo = YearTo,
    };

    /// <summary>Returns the current filter with <paramref name="groupKey"/>'s selections cleared (for residual counts).</summary>
    public LibraryFilter FilterWithout(string groupKey)
    {
        var filter = ToFilter();
        return groupKey switch
        {
            GenreKey => filter with { GenreIds = [] },
            ThemeKey => filter with { ThemeIds = [] },
            TagKey => filter with { TagIds = [] },
            FeatureKey => filter with { FeatureIds = [] },
            ControllerKey => filter with { ControllerIds = [] },
            ModeKey => filter with { GameModes = [] },
            StoreKey => filter with { Stores = [] },
            InstalledKey => filter with { Installed = null },
            _ => filter,
        };
    }

    /// <summary>Recomputes residual counts for every group using <paramref name="matching"/> to evaluate filters.</summary>
    public void Recount(Func<LibraryFilter, IReadOnlyList<GameTileViewModel>> matching)
    {
        foreach (var spec in _specs)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var tile in matching(FilterWithout(spec.Group.Key)))
            {
                foreach (var key in spec.Keys(tile))
                {
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                }
            }

            spec.Group.SetCounts(counts);
        }

        ActiveCount = _specs.Sum(s => s.Group.Checked.Count()) + (HasYearRange ? 1 : 0);
    }

    /// <summary>Yields dismissable chips for the cut bar. <paramref name="context"/> distinguishes list-contributed vs. user-added rules.</summary>
    public IEnumerable<FilterChipViewModel> BuildChips(LibraryFilter? context = null)
    {
        foreach (var spec in _specs)
        {
            foreach (var option in spec.Group.Checked.ToList())
            {
                var captured = option;
                yield return new FilterChipViewModel(
                    captured.Label,
                    spec.Group.Header,
                    () => captured.IsChecked = false,
                    Origin(context, InContext(spec.Group.Key, captured.Key, context)));
            }
        }

        if (HasYearRange)
        {
            var saved = context is not null
                && context.YearFrom == YearFrom
                && context.YearTo == YearTo;

            yield return new FilterChipViewModel(
                YearRangeText, "RELEASE YEAR", ClearYears, Origin(context, saved));
        }
    }

    private static FilterChipOrigin Origin(LibraryFilter? context, bool fromContext) => context switch
    {
        null => FilterChipOrigin.User,
        _ when fromContext => FilterChipOrigin.List,
        _ => FilterChipOrigin.Unsaved,
    };

    /// <summary>Whether the given option is part of the open live list's saved rules.</summary>
    private static bool InContext(string groupKey, string optionKey, LibraryFilter? context)
    {
        if (context is null)
        {
            return false;
        }

        return groupKey switch
        {
            GenreKey => HasId(context.GenreIds, optionKey),
            ThemeKey => HasId(context.ThemeIds, optionKey),
            TagKey => HasId(context.TagIds, optionKey),
            FeatureKey => HasId(context.FeatureIds, optionKey),
            ControllerKey => HasId(context.ControllerIds, optionKey),
            ModeKey => context.GameModes.Contains(optionKey, StringComparer.Ordinal),
            StoreKey => context.Stores.Contains(optionKey, StringComparer.OrdinalIgnoreCase),
            InstalledKey => context.Installed == string.Equals(optionKey, OnDisk, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool HasId(IReadOnlyList<long> ids, string optionKey)
        => long.TryParse(optionKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && ids.Contains(id);

    /// <summary>Applies a saved filter to all groups, then triggers one recompute.</summary>
    public void Apply(LibraryFilter filter)
    {
        _applying = true;
        try
        {
            Group(GenreKey).ApplySelection(filter.GenreIds.Select(Text), silent: true);
            Group(ThemeKey).ApplySelection(filter.ThemeIds.Select(Text), silent: true);
            Group(TagKey).ApplySelection(filter.TagIds.Select(Text), silent: true);
            Group(FeatureKey).ApplySelection(filter.FeatureIds.Select(Text), silent: true);
            Group(ControllerKey).ApplySelection(filter.ControllerIds.Select(Text), silent: true);
            Group(ModeKey).ApplySelection(filter.GameModes, silent: true);
            Group(StoreKey).ApplySelection(filter.Stores, silent: true);
            Group(InstalledKey).ApplySelection(
                filter.Installed switch
                {
                    true => [OnDisk],
                    false => [NotOnDisk],
                    null => Array.Empty<string>(),
                },
                silent: true);

            YearFromText = filter.YearFrom?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            YearToText = filter.YearTo?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        finally
        {
            _applying = false;
        }

        _onChanged();
    }

    [RelayCommand]
    public void Clear()
    {
        _applying = true;
        try
        {
            foreach (var spec in _specs)
            {
                spec.Group.ClearSelection(silent: true);
                spec.Group.FindText = string.Empty;
                spec.Group.IsExpanded = false;
            }

            YearFromText = string.Empty;
            YearToText = string.Empty;
        }
        finally
        {
            _applying = false;
        }

        _onChanged();
    }

    [RelayCommand]
    private void Toggle() => IsOpen = !IsOpen;

    internal FilterGroupViewModel Group(string key)
        => _specs.Find(s => s.Group.Key == key)!.Group;

    private bool HasYearRange => YearFrom is not null || YearTo is not null;

    private string YearRangeText => (YearFrom, YearTo) switch
    {
        ({ } from, { } to) when from == to => from.ToString(CultureInfo.InvariantCulture),
        ({ } from, { } to) => $"{from}–{to}",
        ({ } from, null) => $"{from} onwards",
        (null, { } to) => $"up to {to}",
        _ => string.Empty,
    };

    private bool? InstalledSelection()
        => Group(InstalledKey).Checked.Select(o => o.Key).ToList() switch
        {
            [OnDisk] => true,
            [NotOnDisk] => false,
            _ => null,
        };

    private void ClearYears()
    {
        _applying = true;
        try
        {
            YearFromText = string.Empty;
            YearToText = string.Empty;
        }
        finally
        {
            _applying = false;
        }

        _onChanged();
    }

    private void Add(FilterGroupViewModel group, Func<GameTileViewModel, IReadOnlyList<string>> keys)
        => _specs.Add(new GroupSpec(group, keys));

    private void SetOptions(string key, IEnumerable<(string Key, string Label)> options)
        => Group(key).SetOptions(options);

    private IReadOnlyList<long> LongKeys(string key)
        => [.. Group(key).Checked
            .Select(o => long.TryParse(o.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id != 0)];

    private static IReadOnlyList<string> Ids(IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var text = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            text[i] = Text(ids[i]);
        }

        return text;
    }

    private static string Text(long id) => id.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<(string Key, string Label)> Labelled(
        IReadOnlyList<GameTileViewModel> tiles,
        Func<GameTileViewModel, IReadOnlyList<long>> select,
        IReadOnlyDictionary<long, Facet> names)
        => tiles
            .SelectMany(select)
            .Distinct()
            .Where(names.ContainsKey)
            .Select(id => (Text(id), names[id].Name));

    /// <summary>Display name for a game mode slug; looks up the snapshot first, falls back to hardcoded names.</summary>
    private static string ModeLabel(string slug, FacetSnapshot snapshot)
    {
        foreach (var facet in snapshot.Facets)
        {
            if (facet.Kind == FacetKinds.GameMode && facet.Slug == slug)
            {
                return facet.Name;
            }
        }

        return slug switch
        {
            GameModes.SinglePlayer => "Single player",
            GameModes.Multiplayer => "Multiplayer",
            GameModes.CoOperative => "Co-operative",
            GameModes.SplitScreen => "Split screen",
            GameModes.Mmo => "MMO",
            GameModes.BattleRoyale => "Battle royale",
            _ => slug.Replace('_', ' '),
        };
    }

    /// <summary>
    /// Display name for a store key, from the one vocabulary the chips, the
    /// list column and the launch messages also read.
    /// </summary>
    private static string StoreLabel(string store) => StoreNaming.Label(store);

    private static int? ParseYear(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 4
            && int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && year is >= 1000 and <= 9999
                ? year
                : null;
    }

    partial void OnYearFromTextChanged(string value)
    {
        _ = value;
        if (!_applying)
        {
            _onChanged();
        }
    }

    partial void OnYearToTextChanged(string value)
    {
        _ = value;
        if (!_applying)
        {
            _onChanged();
        }
    }

    private sealed record GroupSpec(
        FilterGroupViewModel Group,
        Func<GameTileViewModel, IReadOnlyList<string>> Keys);
}
