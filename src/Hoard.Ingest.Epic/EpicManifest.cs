namespace Hoard.Ingest.Epic;

/// <summary>
/// One <c>Data\Manifests\&lt;InstallationGuid&gt;.item</c> file — the launcher's
/// record of a single installation
/// (docs/spikes/epic-gog-local-files.md section 2). 61 keys were observed; these
/// are the ones an ingest needs.
/// </summary>
/// <param name="CatalogItemId">
/// 32-hex catalog item id. <b>The stable identity</b>, and the value emitted as
/// <c>CandidateOwnership.ProviderId</c>: unique across all 297 catalog entries on
/// a real account, and the id Epic's own composite key is built from.
/// </param>
/// <param name="CatalogNamespace">
/// Catalog namespace — 32-hex, or a short word such as <c>fn</c> or <c>catnip</c>.
/// <b>Not unique on its own</b> (<c>fn</c> covers Fortnite plus three children),
/// so it qualifies the id rather than replacing it. Kept because Epic's composite
/// key everywhere in the launcher is <c>namespace:catalogItemId:appName</c>.
/// </param>
/// <param name="AppName">
/// Epic's per-artifact release id. <b>A codename, never a title</b> — Fez's is
/// <c>"Bluebird"</c>, and 58 of 73 observed values are words like <c>Ginger</c>,
/// <c>Emu</c>, <c>Sage</c>. Rendering it ships gibberish. It is kept for one
/// reason: it is the exact key GOG's public gamesdb identity graph accepts for
/// Epic (<c>/platforms/epic/external_releases/Bluebird</c>), which is the route
/// to cross-store dedup — out of scope here, but the id must survive to reach it.
/// </param>
/// <param name="DisplayName">The human title. The only title field on a manifest.</param>
/// <param name="InstallLocation">
/// <b>Absolute</b> install path — unlike Steam's <c>installdir</c>, which is a
/// bare folder name needing a library root to resolve.
/// </param>
/// <param name="LaunchExecutable">
/// Executable path <b>relative to <paramref name="InstallLocation"/></b>
/// (e.g. <c>FEZ.exe</c>). Empty for non-launchable items. Together with the
/// install location this is exact raw material for §5.2's process monitor, which
/// is the only mechanism that can ever give an Epic title a real last-played date.
/// </param>
/// <param name="AppVersionString">Installed version string.</param>
/// <param name="InstallSize">Bytes. A JSON <i>number</i>, not a string.</param>
/// <param name="IsIncompleteInstall">
/// The launcher's own "this install finished" bit, inverted. <b>The
/// <c>.item</c> file is written when the install is queued, not when it
/// completes</b>, so a manifests-only reader that ignores this flag reports a
/// half-downloaded game as installed. Treat <c>true</c> as not installed.
/// </param>
/// <param name="MainGameCatalogItemId">
/// Parent catalog item id when this is DLC, and an <b>empty string</b> — not a
/// missing key — when it is a base game. See <see cref="EpicGameFilter.IsDlc"/>.
/// </param>
/// <param name="MainGameAppName">Parent <c>AppName</c>, same empty-string convention.</param>
/// <param name="AppCategories">
/// Classification vocabulary; see <see cref="EpicGameFilter.IsGame"/>.
/// (<c>TechnicalType</c> is the same list comma-joined and is redundant.)
/// </param>
/// <param name="InstallationGuid">32 uppercase hex; matches the file's own stem.</param>
/// <param name="ManifestPath">Absolute path of the file this was read from.</param>
public sealed record EpicManifest(
    string CatalogItemId,
    string CatalogNamespace,
    string AppName,
    string DisplayName,
    string InstallLocation,
    string LaunchExecutable,
    string AppVersionString,
    long InstallSize,
    bool IsIncompleteInstall,
    string MainGameCatalogItemId,
    string MainGameAppName,
    IReadOnlyList<string> AppCategories,
    string InstallationGuid,
    string ManifestPath)
{
    /// <summary>True when <see cref="EpicGameFilter.IsGame"/> admits this manifest's categories.</summary>
    public bool IsGame => EpicGameFilter.IsGame(AppCategories);

    /// <summary>True when this manifest describes DLC rather than a base game.</summary>
    public bool IsDlc => EpicGameFilter.IsDlc(MainGameCatalogItemId);

    /// <summary>
    /// Whether the game is actually on disk and runnable. False while a download
    /// is in flight, even though the manifest already exists.
    /// </summary>
    public bool IsFullyInstalled => !IsIncompleteInstall;

    /// <summary>
    /// Absolute path of the launch executable, or null when the manifest names
    /// none. For §5.2's executable-to-release mapping.
    /// </summary>
    public string? LaunchExecutablePath
        => LaunchExecutable.Length > 0 && InstallLocation.Length > 0
            ? Path.Combine(InstallLocation, LaunchExecutable)
            : null;
}
