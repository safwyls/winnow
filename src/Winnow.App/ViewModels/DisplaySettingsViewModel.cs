using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
using Winnow.Core.Identity;
using Winnow.Core.Queries;
using Winnow.Core.Repositories;

namespace Winnow.App.ViewModels;

/// <summary>
/// Display popover: persisted preferences (dimming, non-game visibility, journal
/// prompt). Store is optional — without it the toggles still work for the session.
/// </summary>
public partial class DisplaySettingsViewModel : ObservableObject
{
    private readonly DormancyRamp _ramp;
    private readonly ISettingsRepository? _settings;
    private readonly Func<Task>? _reloadLibrary;
    private readonly SessionJournalService? _journal;
    private readonly ILibraryQueryRepository? _libraryQueries;

    /// <summary>Guards against write-back during initial load.</summary>
    private bool _loading;

    /// <param name="reloadLibrary">Re-runs the library query when ShowNonGameEntries changes.</param>
    public DisplaySettingsViewModel(
        DormancyRamp ramp,
        ISettingsRepository? settings = null,
        Func<Task>? reloadLibrary = null,
        SessionJournalService? journal = null,
        ILibraryQueryRepository? libraryQueries = null)
    {
        _ramp = ramp;
        _settings = settings;
        _reloadLibrary = reloadLibrary;
        _journal = journal;
        _libraryQueries = libraryQueries;
        DimDormantCovers = ramp.DimsDormantCovers;
    }

    /// <summary>The cap steps, lowest first. The slider indexes this list.</summary>
    public static IReadOnlyList<MaturityTier> CapSteps { get; } = MaturityTiers.Ordered;

    public static double MaximumCapIndex => CapSteps.Count - 1;

    /// <summary>Whether idle game covers are visually dimmed (§8).</summary>
    [ObservableProperty]
    public partial bool DimDormantCovers { get; set; } = true;

    /// <summary>Show non-game entries (tools, soundtracks, etc.). Hidden by default.</summary>
    [ObservableProperty]
    public partial bool ShowNonGameEntries { get; set; }

    /// <summary>
    /// Group expansions under their base game in the library grid. Off by
    /// default, which is the state in which an expansion link changes nothing
    /// anywhere (TASK-70.5 AC6).
    /// </summary>
    [ObservableProperty]
    public partial bool GroupExpansions { get; set; }

    /// <summary>Post-play journal prompt. Off by default (§9 pitfall 7).</summary>
    [ObservableProperty]
    public partial bool PromptAfterPlay { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaturityCapIndex), nameof(MaturityCapLabel), nameof(IsCapClamped))]
    public partial MaturityTier MaturityCap { get; set; } = BucketThresholds.NoMaturityCap;

    /// <summary>
    /// Mirrors the 18+ setting so the popover can say why the top step is
    /// unavailable. Explanatory only — the clamp itself is applied by
    /// <see cref="BucketThresholds.EffectiveMaturityCap"/> in the query.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCapClamped))]
    public partial bool AdultContentAllowed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapHiddenText))]
    public partial int CapHiddenCount { get; set; }

    public double MaturityCapIndex
    {
        get
        {
            for (var i = 0; i < CapSteps.Count; i++)
            {
                if (CapSteps[i] == MaturityCap)
                {
                    return i;
                }
            }

            return MaximumCapIndex;
        }

        set
        {
            var index = (int)Math.Round(value, MidpointRounding.AwayFromZero);
            index = Math.Clamp(index, 0, CapSteps.Count - 1);
            MaturityCap = CapSteps[index];
        }
    }

    public string MaturityCapLabel => MaturityCapCopy.LabelFor(MaturityCap);

    public bool IsCapClamped =>
        BucketThresholds.IsCapClampedByAdultSetting(MaturityCap, AdultContentAllowed);

    public string CapHiddenText => MaturityCapCopy.HiddenText(CapHiddenCount);

    /// <summary>In-flight save; exposed for tests. The UI never awaits it.</summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    /// <summary>Reads stored preferences; unparseable values leave defaults in place.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return;
        }

        var storedDim = await _settings.GetAsync(DormancyRamp.DimCoversSettingKey, ct);
        var storedNonGame = await _settings.GetAsync(
            BucketThresholds.ShowNonGameEntriesSettingKey, ct);
        var storedGrouping = await _settings.GetAsync(
            ExpansionGroupingPreference.SettingKey, ct);
        var storedCap = await _settings.GetAsync(
            BucketThresholds.MaturityCapSettingKey, ct);
        var storedExplicit = await _settings.GetAsync(
            BucketThresholds.ShowExplicitContentSettingKey, ct);

        if (_journal is not null)
        {
            await _journal.LoadAsync(ct);
        }

        _loading = true;
        try
        {
            PromptAfterPlay = _journal?.PromptEnabled ?? false;
            if (bool.TryParse(storedDim, out var dim))
            {
                DimDormantCovers = dim;
            }

            ShowNonGameEntries = BucketThresholds.ParseShowNonGameEntries(storedNonGame);
            GroupExpansions = ExpansionGroupingPreference.Parse(storedGrouping);
            AdultContentAllowed = BucketThresholds.ParseShowExplicitContent(storedExplicit);
            MaturityCap = BucketThresholds.ParseMaturityCap(storedCap);
        }
        finally
        {
            _loading = false;
        }

        await RefreshCapCountAsync(ct);
    }

    /// <summary>
    /// Re-reads how many games the cap alone is hiding. Costs two bucket
    /// queries, so it runs on load and after a cap change, never per keystroke.
    /// </summary>
    public async Task RefreshCapCountAsync(CancellationToken ct = default)
    {
        if (_libraryQueries is null)
        {
            return;
        }

        CapHiddenCount = await _libraryQueries.CountHiddenByRatingCapAsync(
            BucketThresholds.Default with
            {
                ShowNonGameEntries = ShowNonGameEntries,
                ShowExplicitContent = AdultContentAllowed,
                MaturityCap = MaturityCap,
            },
            ct);
    }

    partial void OnDimDormantCoversChanged(bool value)
    {
        _ramp.DimsDormantCovers = value;

        if (_loading || _settings is null)
        {
            return;
        }

        PendingSave = _settings.SetAsync(
            DormancyRamp.DimCoversSettingKey,
            value ? "true" : "false");
    }

    partial void OnPromptAfterPlayChanged(bool value)
    {
        if (_loading || _journal is null)
        {
            return;
        }

        PendingSave = _journal.SetPromptEnabledAsync(value);
    }

    partial void OnGroupExpansionsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Reloads for the same reason the non-game toggle does: this decides
        // which tiles the grid draws, and every rail count is computed from
        // that set, so the counts and the grid would otherwise disagree.
        var reload = _reloadLibrary?.Invoke() ?? Task.CompletedTask;

        PendingSave = _settings is null
            ? reload
            : Task.WhenAll(
                reload,
                _settings.SetAsync(
                    ExpansionGroupingPreference.SettingKey,
                    ExpansionGroupingPreference.Format(value)));
    }

    partial void OnMaturityCapChanged(MaturityTier value)
    {
        if (_loading)
        {
            return;
        }

        // Reloads for the same reason the non-game toggle does: the cap decides
        // which rows the bucket query returns, and every rail count is computed
        // from that set.
        var reload = _reloadLibrary?.Invoke() ?? Task.CompletedTask;

        PendingSave = Task.WhenAll(
            reload,
            _settings is null
                ? Task.CompletedTask
                : _settings.SetAsync(
                    BucketThresholds.MaturityCapSettingKey,
                    BucketThresholds.FormatMaturityCap(value)),
            RefreshCapCountAsync());
    }

    partial void OnShowNonGameEntriesChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Triggers a library reload since this changes which rows are returned.
        var reload = _reloadLibrary?.Invoke() ?? Task.CompletedTask;

        PendingSave = _settings is null
            ? reload
            : Task.WhenAll(
                reload,
                _settings.SetAsync(
                    BucketThresholds.ShowNonGameEntriesSettingKey,
                    BucketThresholds.FormatShowNonGameEntries(value)));
    }
}
