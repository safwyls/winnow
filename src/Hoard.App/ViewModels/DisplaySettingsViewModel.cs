using CommunityToolkit.Mvvm.ComponentModel;
using Hoard.App.Services;
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

    /// <summary>True while <see cref="LoadAsync"/> is seeding the property, so
    /// reading the stored value does not immediately write it back.</summary>
    private bool _loading;

    public DisplaySettingsViewModel(DormancyRamp ramp, ISettingsRepository? settings = null)
    {
        _ramp = ramp;
        _settings = settings;
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

        var stored = await _settings.GetAsync(DormancyRamp.DimCoversSettingKey, ct);
        if (!bool.TryParse(stored, out var dim))
        {
            return;
        }

        _loading = true;
        try
        {
            DimDormantCovers = dim;
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
}
