using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoard.Core.Queries;

namespace Hoard.App.ViewModels.Filters;

/// <summary>
/// The filter panel: one column of labelled groups, every option carrying the
/// number of titles it would leave you with.
///
/// <para><b>It does not repeat the rail.</b> The rail's buckets are the filter's
/// bucket dimension — selecting <c>Bounced off</c> there is the same act as
/// ticking it here would be, and two controls writing one axis is how a panel
/// starts disagreeing with the screen behind it. So the panel sits directly
/// beside the rail on the same <c>Surface</c>, holds only the axes the rail does
/// not, and the two meet in the cut bar above the grid, where the bucket appears
/// as the first chip and can be dropped like any other.</para>
///
/// <para><b>There is deliberately no "has updates" group.</b>
/// <see cref="LibraryFilter.HasUnread"/> selects exactly the rail's
/// <c>Patched since</c> bucket, and design-system §5.2 gives the unread signal
/// one home. A second door onto it would need a second marker, and the only
/// marker for unread is Flare.</para>
///
/// <para><b>Every group here is a group a live list can store.</b> That is the
/// rule that decides which kinds are drawn. <see cref="FacetKinds"/> also holds
/// player perspective, which <see cref="LibraryFilter"/> has no field for — so
/// it is not drawn, because a rule that vanishes the moment you save it is
/// worse than a rule you never had. Features and controller support ARE drawn,
/// because the filter record gained
/// <see cref="LibraryFilter.FeatureIds"/> and
/// <see cref="LibraryFilter.ControllerIds"/> for exactly this reason.</para>
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

    /// <summary>True while a saved filter is being poured in, so one recompute covers the lot.</summary>
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

        // Order is the order they are read in: what the game IS, then what it is
        // to you as a purchase. Genre before theme before tag is descending
        // confidence — IGDB's genres are curated, its themes looser, and store
        // tags are whatever the crowd typed.
        Add(new FilterGroupViewModel(GenreKey, "GENRE", Changed, sortByCount: true),
            t => Ids(t.Facets.GenreIds));
        Add(new FilterGroupViewModel(ThemeKey, "THEME", Changed, sortByCount: true),
            t => Ids(t.Facets.ThemeIds));
        Add(new FilterGroupViewModel(ModeKey, "GAME MODE", Changed),
            t => t.Facets.Modes);
        Add(new FilterGroupViewModel(TagKey, "STORE TAG", Changed, sortByCount: true, findWatermark: "Find a tag…"),
            t => Ids(t.Facets.TagIds));

        // The reference storefront's FEATURES and HARDWARE SUPPORT columns, and
        // the two named for what they answer rather than for the table they come
        // out of: "Steam features" is a heading about Valve, and the user is
        // asking whether the game has achievements.
        Add(new FilterGroupViewModel(FeatureKey, "FEATURES", Changed, sortByCount: true, findWatermark: "Find a feature…"),
            t => Ids(t.Facets.FeatureIds));
        Add(new FilterGroupViewModel(ControllerKey, "CONTROLLER", Changed, sortByCount: true),
            t => Ids(t.Facets.ControllerIds));
        Add(new FilterGroupViewModel(StoreKey, "STORE", Changed),
            t => [t.Store]);
        Add(new FilterGroupViewModel(InstalledKey, "ON DISK", Changed),
            t => [t.Installed ? OnDisk : NotOnDisk]);

        Groups = [.. _specs.Select(s => s.Group)];
    }

    public IReadOnlyList<FilterGroupViewModel> Groups { get; }

    /// <summary>
    /// The groups worth drawing. Two are left out, for two different reasons.
    ///
    /// <para>A dimension with no data at all draws nothing — four columns of
    /// greyed checkboxes is the wall this panel is deliberately not.</para>
    ///
    /// <para>And a dimension whose one option is true of every title draws
    /// nothing either. "STORE · Steam 926" on a Steam-only library is not a
    /// filter, it is a fact about the library restated as a control that cannot
    /// change anything. It reappears by itself the day a second store lands,
    /// which is the day it starts meaning something.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGroups), nameof(HasDescriptorGroups))]
    public partial IReadOnlyList<FilterGroupViewModel> VisibleGroups { get; set; } = [];

    public bool HasGroups => VisibleGroups.Count > 0;

    /// <summary>
    /// Whether anything the enrichment backfill supplies is on screen. Store and
    /// on-disk come free with an ownership row, so their presence says nothing
    /// about whether metadata has arrived — and a panel showing only those two
    /// needs to say why rather than look finished.
    /// </summary>
    public bool HasDescriptorGroups => VisibleGroups.Any(g =>
        g.Key is GenreKey or ThemeKey or TagKey or ModeKey or FeatureKey or ControllerKey);

    /// <summary>
    /// Panel open state. Not persisted: a filter panel that is up when you launch
    /// is a library you did not ask to have cut down.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// Release-year bounds, as typed. Text rather than a slider or two spinners:
    /// a year is four characters the user already knows, and a range you set by
    /// dragging is a range you cannot state exactly.
    /// </summary>
    [ObservableProperty]
    public partial string YearFromText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string YearToText { get; set; } = string.Empty;

    /// <summary>Watermarks: the oldest and newest year the library actually holds.</summary>
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

    /// <summary>
    /// Rebuilds every group's options from the library as it now stands.
    /// Selections survive by key, so a reload after an enrichment pass does not
    /// silently drop a rule the user set five minutes ago.
    /// </summary>
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
            .Select(t => t.Store)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => (s, StoreLabel(s))));

        // Both states always, even when the library is entirely one of them: the
        // pair is what makes the group legible as a question rather than as a
        // lone switch, and the residual count says which half is empty.
        SetOptions(InstalledKey, [(OnDisk, "Installed"), (NotOnDisk, "Not installed")]);

        var years = tiles.Select(t => t.ReleaseYear).Where(y => y is > 0).Select(y => y!.Value).ToList();
        HasYearData = years.Count > 0;
        EarliestYearText = HasYearData ? years.Min().ToString(CultureInfo.InvariantCulture) : "—";
        LatestYearText = HasYearData ? years.Max().ToString(CultureInfo.InvariantCulture) : "—";

        VisibleGroups = [.. _specs
            .Where(s => s.Group.HasOptions && !CannotCut(s, tiles))
            .Select(s => s.Group)];
    }

    /// <summary>
    /// True when the group has one option and every title carries it — a control
    /// whose only setting selects the whole library.
    /// </summary>
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

    /// <summary>
    /// The panel's half of a live list's rules. The rail supplies the bucket and
    /// the command bar supplies the search text; <c>LibraryViewModel</c> puts the
    /// three together.
    /// </summary>
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

    /// <summary>
    /// The same filter with one group's selections lifted — the rule set a
    /// residual count is taken under.
    ///
    /// <para>Lifting the group's own selections is the whole trick. Without it,
    /// ticking one genre drops every other genre to zero and the panel becomes a
    /// dead end — which is wrong twice over, because options inside a group widen
    /// the result rather than narrow it.</para>
    /// </summary>
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

    /// <summary>
    /// Writes every option's residual count.
    ///
    /// <para><paramref name="matching"/> answers "which of the tiles currently in
    /// play satisfy this filter" — the caller owns that, because the baseline is
    /// the library after the rail's bucket, any open list and the search box, and
    /// those are the other AND terms that make each count true.</para>
    /// </summary>
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

    /// <summary>Every rule in force, as dismissable chips for the cut bar.</summary>
    public IEnumerable<FilterChipViewModel> BuildChips()
    {
        foreach (var spec in _specs)
        {
            foreach (var option in spec.Group.Checked.ToList())
            {
                var captured = option;
                yield return new FilterChipViewModel(
                    captured.Label,
                    spec.Group.Header,
                    () => captured.IsChecked = false);
            }
        }

        if (HasYearRange)
        {
            yield return new FilterChipViewModel(YearRangeText, "RELEASE YEAR", ClearYears);
        }
    }

    /// <summary>Pours a saved live list's rules in, then asks for exactly one recompute.</summary>
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

    /// <summary>
    /// A game mode's display name. Migration 0007 seeded the vocabulary with
    /// real names, so the snapshot is asked first; the fallback exists because a
    /// mode can be true of a release before its vocabulary row has been read.
    /// </summary>
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
    /// "steam" is how it is stored; "Steam" is how it is read. The three known
    /// stores are named rather than title-cased, because GOG is an acronym and
    /// "Gog" is a misspelling of a brand on the user's own screen. Anything else
    /// falls back to title case, which is right far more often than it is wrong.
    /// </summary>
    private static string StoreLabel(string store) => store.ToLowerInvariant() switch
    {
        "" => store,
        "steam" => "Steam",
        "gog" => "GOG",
        "epic" => "Epic",
        _ => string.Concat(char.ToUpper(store[0], CultureInfo.CurrentCulture), store[1..]),
    };

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
