using Winnow.Api.Contracts.Preferences;
using Winnow.Core.Repositories;

namespace Winnow.Api.Client;

/// <summary>Adapts existing UI preference readers to the closed, nonsecret API contract.</summary>
public sealed class ApiPresentationSettings(WinnowApiClient api) : ISettingsRepository
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<PresentationPreferenceValue>>? _snapshot;
    public void Invalidate() { lock (_gate) _snapshot = null; }
    public static PresentationPreference PreferenceFor(string key) => key switch
    {
        "appearance.theme" => PresentationPreference.Theme,
        "application.updates.automatic" => PresentationPreference.AutomaticUpdates,
        "application.updates.include_beta" => PresentationPreference.IncludeBetaUpdates,
        "enrichment.artwork_source_order" => PresentationPreference.ArtworkSourceOrder,
        "appearance.typography" => PresentationPreference.Typography,
        "appearance.transparency" => PresentationPreference.Transparency,
        "appearance.backdrop" => PresentationPreference.Backdrop,
        "appearance.wall" => PresentationPreference.TranslucentWall,
        "appearance.layout" => PresentationPreference.Layout,
        "application.minimize_to_tray" => PresentationPreference.MinimizeToTray,
        "application.close_to_tray" => PresentationPreference.CloseToTray,
        "application.start_in_fullscreen" => PresentationPreference.StartInFullscreen,
        "application.link_destination" => PresentationPreference.LinkDestination,
        "display.cover_art_mode" => PresentationPreference.CoverArtMode,
        "display.dim_dormant_covers" => PresentationPreference.DimDormantCovers,
        "library.default-sort" => PresentationPreference.DefaultSort,
        "merges.preferred_platform" => PresentationPreference.PreferredMergePlatform,
        "library.group_expansions" => PresentationPreference.GroupExpansions,
        "library.show_non_game_entries" => PresentationPreference.ShowNonGameEntries,
        "library.show_explicit_content" => PresentationPreference.ShowExplicitContent,
        "library.maturity_cap" => PresentationPreference.MaturityCap,
        "fullscreen.text-scale" => PresentationPreference.FullscreenTextScale,
        "fullscreen.ui-scale-v2" => PresentationPreference.FullscreenInterfaceScale,
        "fullscreen.safe-margin" => PresentationPreference.FullscreenSafeMargin,
        "fullscreen.reduced-motion" => PresentationPreference.FullscreenReducedMotion,
        "fullscreen.fit-ultrawide" => PresentationPreference.FullscreenFitUltrawide,
        _ => throw new ArgumentException("This setting is not a frontend preference.", nameof(key))
    };

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var preference = PreferenceFor(key);
        Task<IReadOnlyList<PresentationPreferenceValue>> snapshot;
        lock (_gate)
            snapshot = _snapshot ??= api.GetAsync<IReadOnlyList<PresentationPreferenceValue>>("preferences/presentation");
        IReadOnlyList<PresentationPreferenceValue> values;
        try { values = await snapshot.WaitAsync(ct); }
        catch
        {
            lock (_gate) if (_snapshot == snapshot && snapshot.IsCompleted) _snapshot = null;
            throw;
        }
        return values.Single(value => value.Preference == preference).Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await api.SendAsync(HttpMethod.Put, "preferences/presentation/" + PreferenceFor(key), new SetPresentationPreference(value), ct);
        Invalidate();
    }
}
