using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Steam;

/// <summary>
/// Install metadata parsed from one <c>appmanifest_&lt;appid&gt;.acf</c>.
/// </summary>
/// <param name="AppId">Steam appid as a string.</param>
/// <param name="Name">
/// Display name at install time (may go stale vs store renames). <b>Null when
/// the manifest carries no usable name</b> — absent, or present but blank
/// (<c>"name" ""</c>, which Steam does write). Blank is "unnamed", not a name:
/// downstream, a candidate with no title becomes a flagged provisional work
/// that enrichment can repair, whereas an empty string would become a
/// permanently blank work that nothing can.
/// </param>
/// <param name="InstallDir">Folder name under <c>steamapps\common\</c> — NOT a full path. Empty when absent.</param>
/// <param name="BuildId">Currently installed build id.</param>
/// <param name="StateFlags">Raw bitfield; bit 4 = fully installed.</param>
/// <param name="LastUpdatedUtc">Last content update (on-disk key is all-lowercase <c>lastupdated</c>).</param>
/// <param name="LastPlayedUtc">
/// Per-machine last-launch time; null when never launched (<c>"0"</c>) or below
/// the sanity floor. Matches localconfig's LastPlayed ±1 s — useful fallback only.
/// </param>
public sealed record AppManifest(
    string AppId,
    string? Name,
    string InstallDir,
    string? BuildId,
    long StateFlags,
    DateTime? LastUpdatedUtc,
    DateTime? LastPlayedUtc)
{
    /// <summary>StateFlags bit meaning "fully installed".</summary>
    public const long StateFlagFullyInstalled = 4;

    public bool IsFullyInstalled => (StateFlags & StateFlagFullyInstalled) != 0;
}

/// <summary>
/// Reads a single <c>appmanifest_&lt;appid&gt;.acf</c> (root key
/// <c>AppState</c>). Keys are matched case-insensitively — casing is
/// inconsistent within a single manifest (§4.1 spike). Read-only; a missing
/// or malformed file yields null.
/// </summary>
public sealed class AppManifestReader
{
    private readonly ILogger<AppManifestReader> _logger;

    public AppManifestReader(ILogger<AppManifestReader>? logger = null)
        => _logger = logger ?? NullLogger<AppManifestReader>.Instance;

    public AppManifest? Read(string manifestPath)
    {
        var doc = KeyValues1.TryLoad(manifestPath, _logger);
        if (doc is null)
        {
            return null;
        }

        var root = doc.Root;
        var appId = KeyValues1.GetString(root, "appid");
        if (string.IsNullOrWhiteSpace(appId))
        {
            _logger.LogWarning("App manifest {Path} has no appid; skipping", manifestPath);
            return null;
        }

        // A blank name is treated exactly like an absent one (null). See the
        // AppManifest.Name docs: an empty string here becomes an unrepairable
        // blank work three layers downstream.
        var name = KeyValues1.GetString(root, "name");

        return new AppManifest(
            AppId: appId,
            Name: string.IsNullOrWhiteSpace(name) ? null : name,
            InstallDir: KeyValues1.GetString(root, "installdir") ?? string.Empty,
            BuildId: KeyValues1.GetString(root, "buildid"),
            StateFlags: KeyValues1.GetLong(root, "StateFlags") ?? 0,
            LastUpdatedUtc: KeyValues1.GetEpochUtc(root, "lastupdated"),
            LastPlayedUtc: KeyValues1.GetEpochUtc(root, "LastPlayed"));
    }
}
