using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.App.Services;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;

namespace Hoard.App.ViewModels;

/// <summary>
/// The command bar's Display popover: preferences that outlive the session, as
/// against the view mode, the sort and the density slider beside it, which §4
/// remembers only for as long as the process lives.
///
/// <para>That difference is why this is not another control in the command bar's
/// row. A checkbox sitting between the density slider and the sort menu would
/// read as one more thing you set for the next five minutes; it is in fact the
/// §8 accessibility preference, written to disk and true on every launch until
/// it is changed back. It also needs a sentence of explanation, which nothing in
/// that row has anywhere to put.</para>
///
/// <para>The store is optional so the app still composes — and the toggle still
/// works for the session — when the host has not registered an
/// <see cref="ISettingsRepository"/>.</para>
/// </summary>
public partial class DisplaySettingsViewModel : ObservableObject
{
    private readonly DormancyRamp _ramp;
    private readonly ISettingsRepository? _settings;
    private readonly Func<Task>? _reloadLibrary;
    private readonly SessionJournalService? _journal;

    /// <summary>True while <see cref="LoadAsync"/> is seeding the property, so
    /// reading the stored value does not immediately write it back.</summary>
    private bool _loading;

    /// <param name="reloadLibrary">
    /// Re-runs the library query. Needed by
    /// <see cref="ShowNonGameEntries"/> and not by
    /// <see cref="DimDormantCovers"/>: dimming repaints the same tiles, but
    /// hiding non-game entries changes which rows the query returns — and the
    /// rail's counts are computed from those rows, so without a reload the
    /// counts and the grid would disagree.
    /// </param>
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

    /// <summary>
    /// §8's toggle, named for what the user controls rather than for how it is
    /// built (§7): covers fade with idle time, or they don't. It is not "the
    /// dormancy ramp" and it is not a saturation number.
    /// </summary>
    [ObservableProperty]
    public partial bool DimDormantCovers { get; set; } = true;

    /// <summary>
    /// Steam carries tools, dedicated servers, soundtracks and videos alongside
    /// games; this application is about games, so they are hidden by default and
    /// this is the way back. Never applies to an entry whose type Valve has not
    /// told us — unknown is not the same fact as "not a game", and reading it
    /// that way would empty the library.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowNonGameEntries { get; set; }

    /// <summary>
    /// M3b / §5.2's journal prompt, and the toggle §9 pitfall 7 requires to
    /// exist: <b>false unless the user comes here and turns it on</b>. There is
    /// no first-run offer and no "would you like to enable this?" — an
    /// onboarding question about an interruption is the same interruption,
    /// moved earlier.
    ///
    /// <para>It sits in the Display popover rather than in a settings screen of
    /// its own, and that is a compromise rather than a claim: this is the app's
    /// only surface for preferences that persist, and inventing a fourth rail
    /// row to hold one checkbox would cost more than the mild category stretch
    /// of calling an after-play prompt a display preference. If a real Settings
    /// screen ever lands, this moves.</para>
    /// </summary>
    [ObservableProperty]
    public partial bool PromptAfterPlay { get; set; }

    /// <summary>
    /// The in-flight write, exposed so a caller — or a test — can wait for the
    /// preference to reach disk. The control itself never waits: a checkbox that
    /// blocks on IO is a checkbox that stutters.
    /// </summary>
    public Task PendingSave { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Reads the stored preference. Anything unparseable is treated as unset and
    /// leaves the default standing — the store returns exactly what was written
    /// and takes no position on bad text, so this one does.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_settings is null)
        {
            return;
        }

        var storedDim = await _settings.GetAsync(DormancyRamp.DimCoversSettingKey, ct);
        var storedNonGame = await _settings.GetAsync(
            BucketThresholds.ShowNonGameEntriesSettingKey, ct);

        // The service owns the key and the "absent means off" reading, so the
        // checkbox and the thing that decides whether to prompt cannot end up
        // with two opinions about what an unset row means.
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

            // Unparseable reads as hidden, which is also the default — the one
            // authoritative reading of the stored text lives on BucketThresholds
            // so the query and the toggle can never disagree about it.
            ShowNonGameEntries = BucketThresholds.ParseShowNonGameEntries(storedNonGame);
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnDimDormantCoversChanged(bool value)
    {
        // The ramp is the single source the tiles read; writing it here is what
        // repaints the wall. Nothing about the cover cache is invalidated — the
        // floor variants stay loaded under the vivid layer either way.
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

        // Takes effect immediately rather than on restart: the service is the
        // one thing that decides whether to raise the event, and it is the same
        // instance the prompt is listening to.
        PendingSave = _journal.SetPromptEnabledAsync(value);
    }

    partial void OnShowNonGameEntriesChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        // Reload first: this changes what the query RETURNS, so the tiles and
        // every rail count have to be recomputed. The write can land after.
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
