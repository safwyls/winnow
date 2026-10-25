using System.Text.Json.Serialization;

namespace Winnow.Api.Contracts.Preferences;

/// <summary>Closed set of nonsecret presentation preferences. Account and provider storage is separate.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationPreference>))]
public enum PresentationPreference
{
    Theme, Typography, Transparency, Backdrop, TranslucentWall, Layout,
    MinimizeToTray, CloseToTray, StartInFullscreen, LinkDestination,
    CoverArtMode, DimDormantCovers, DefaultSort, PreferredMergePlatform,
    GroupExpansions, ShowNonGameEntries, ShowExplicitContent, MaturityCap,
    FullscreenTextScale, FullscreenInterfaceScale, FullscreenSafeMargin,
    FullscreenReducedMotion, FullscreenFitUltrawide, ArtworkSourceOrder, AutomaticUpdates, IncludeBetaUpdates
}

public sealed record PresentationPreferenceValue(PresentationPreference Preference, string? Value);
public sealed record SetPresentationPreference(string Value);
public sealed record SetupProgress(int? Step, string? Problem);
public sealed record SetSetupProgress(int? Step);
