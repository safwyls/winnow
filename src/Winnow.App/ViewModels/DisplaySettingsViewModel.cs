using CommunityToolkit.Mvvm.ComponentModel;
using Winnow.App.Services;
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

    /// <summary>Guards against write-back during initial load.</summary>
    private bool _loading;

    /// <param name="reloadLibrary">Re-runs the library query when ShowNonGameEntries changes.</param>
    public DisplaySettingsViewModel(
        DormancyRamp ramp,
        ISettingsRepository? settings = null,
        Func<Task>? reloadLibrary = null,
        SessionJournalService? journal = null)
    {
        _ramp = ramp;
        _settings = settings;
        _reloadLibrary = reloadLibrary;
        _journal = journal;
        DimDormantCovers = ramp.DimsDormantCovers;
    }

    /// <summary>Whether idle game covers are visually dimmed (§8).</summary>
    [ObservableProperty]
    public partial bool DimDormantCovers { get; set; } = true;

    /// <summary>Show non-game entries (tools, soundtracks, etc.). Hidden by default.</summary>
    [ObservableProperty]
    public partial bool ShowNonGameEntries { get; set; }

    /// <summary>Post-play journal prompt. Off by default (§9 pitfall 7).</summary>
    [ObservableProperty]
    public partial bool PromptAfterPlay { get; set; }

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
        }
        finally
        {
            _loading = false;
        }
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
