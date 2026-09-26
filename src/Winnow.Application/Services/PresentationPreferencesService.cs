using Winnow.Api.Contracts.Preferences;
using Winnow.Core.Repositories;

namespace Winnow.Application;

public sealed class PresentationPreferencesService(ISettingsRepository settings, IApplicationChangePublisher changes)
{
    public static string StorageKey(PresentationPreference preference) => preference switch
    {
        PresentationPreference.AutomaticUpdates => "application.updates.automatic",
        PresentationPreference.IncludeBetaUpdates => "application.updates.include_beta",
        PresentationPreference.Theme => "appearance.theme",
        PresentationPreference.ArtworkSourceOrder => "enrichment.artwork_source_order",
        PresentationPreference.Typography => "appearance.typography",
        PresentationPreference.Transparency => "appearance.transparency",
        PresentationPreference.Backdrop => "appearance.backdrop",
        PresentationPreference.TranslucentWall => "appearance.wall",
        PresentationPreference.Layout => "appearance.layout",
        PresentationPreference.MinimizeToTray => "application.minimize_to_tray",
        PresentationPreference.CloseToTray => "application.close_to_tray",
        PresentationPreference.StartInFullscreen => "application.start_in_fullscreen",
        PresentationPreference.LinkDestination => "application.link_destination",
        PresentationPreference.CoverArtMode => "display.cover_art_mode",
        PresentationPreference.DimDormantCovers => "display.dim_dormant_covers",
        PresentationPreference.DefaultSort => "library.default-sort",
        PresentationPreference.PreferredMergePlatform => "merges.preferred_platform",
        PresentationPreference.GroupExpansions => "library.group_expansions",
        PresentationPreference.ShowNonGameEntries => "library.show_non_game_entries",
        PresentationPreference.ShowExplicitContent => "library.show_explicit_content",
        PresentationPreference.MaturityCap => "library.maturity_cap",
        PresentationPreference.FullscreenTextScale => "fullscreen.text-scale",
        PresentationPreference.FullscreenInterfaceScale => "fullscreen.ui-scale-v2",
        PresentationPreference.FullscreenSafeMargin => "fullscreen.safe-margin",
        PresentationPreference.FullscreenReducedMotion => "fullscreen.reduced-motion",
        PresentationPreference.FullscreenFitUltrawide => "fullscreen.fit-ultrawide",
        _ => throw new ArgumentOutOfRangeException(nameof(preference))
    };

    public async Task<IReadOnlyList<PresentationPreferenceValue>> ReadAsync(CancellationToken ct)
    {
        var result = new List<PresentationPreferenceValue>();
        foreach (var preference in Enum.GetValues<PresentationPreference>())
            result.Add(new(preference, await settings.GetAsync(StorageKey(preference), ct)));
        return result;
    }

    public async Task SetAsync(PresentationPreference preference, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 16384 || value.Contains('\0')) throw new ArgumentException("Preference value is invalid.");
        await settings.SetAsync(StorageKey(preference), value, ct);
        changes.Publish("preferences.changed", preference.ToString());
        if (preference is PresentationPreference.ShowNonGameEntries or PresentationPreference.ShowExplicitContent
            or PresentationPreference.MaturityCap or PresentationPreference.GroupExpansions)
            changes.Publish("library.changed");
    }
}
