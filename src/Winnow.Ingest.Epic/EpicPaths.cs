using Microsoft.Win32;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Locates the Epic Games Launcher's local data tree. Discovery via registry,
/// falling back to <c>%PROGRAMDATA%</c>. Read-only.
/// </summary>
public static class EpicPaths
{
    /// <summary>Registry key (HKCU) under which the launcher publishes its metadata directory.</summary>
    public const string EosRegistryKey = @"SOFTWARE\Epic Games\EOS";

    /// <summary>Registry value naming the manifests directory. Observed with forward slashes.</summary>
    public const string ModSdkMetadataDirValue = "ModSdkMetadataDir";

    /// <summary>Subdirectory of the data root holding one <c>.item</c> per installation.</summary>
    public const string ManifestsDirectoryName = "Manifests";

    /// <summary>Subdirectory of the data root holding <see cref="CatalogCacheFileName"/>.</summary>
    public const string CatalogDirectoryName = "Catalog";

    /// <summary>The entitlement catalog: base64 of plain JSON, not gzip, not encrypted.</summary>
    public const string CatalogCacheFileName = "catcache.bin";

    /// <summary>Epic's misspelling, preserved verbatim — the real directory name on disk.</summary>
    public const string ThirdPartyManagedAppsDirectoryName = "ThirPartyManagedApps";

    /// <summary>Well-known fallback data root, relative to <c>%PROGRAMDATA%</c>.</summary>
    public const string ProgramDataRelativeDataRoot = @"Epic\EpicGamesLauncher\Data";

    /// <summary>
    /// Finds the launcher's <c>Data</c> root, or null when Epic is not installed.
    /// Registry first (the parent of <see cref="ModSdkMetadataDirValue"/>), then
    /// <c>%PROGRAMDATA%\Epic\EpicGamesLauncher\Data</c>.
    /// </summary>
    public static string? FindDataRoot()
    {
        var manifests = FindManifestsDirectoryFromRegistry();
        if (manifests is not null)
        {
            var parent = Path.GetDirectoryName(manifests.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                return parent;
            }
        }

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(programData))
        {
            var fallback = Path.Combine(programData, ProgramDataRelativeDataRoot);
            if (Directory.Exists(fallback))
            {
                return fallback;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads <c>HKCU\SOFTWARE\Epic Games\EOS\ModSdkMetadataDir</c>, normalising the
    /// forward slashes the launcher writes. Returns null off Windows, when the key
    /// is absent, or when the directory it names does not exist.
    /// </summary>
    public static string? FindManifestsDirectoryFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(EosRegistryKey);
            if (key?.GetValue(ModSdkMetadataDirValue) is not string value
                || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = Path.GetFullPath(
                value.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(normalized) ? normalized : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            // A locked-down or malformed registry is a machine without Epic as
            // far as this reader is concerned — never an ingest failure.
            return null;
        }
    }

    /// <summary>The <c>Manifests</c> directory beneath a data root.</summary>
    public static string ManifestsDirectory(string dataRoot)
        => Path.Combine(dataRoot, ManifestsDirectoryName);

    /// <summary>The <c>Catalog\catcache.bin</c> file beneath a data root.</summary>
    public static string CatalogCachePath(string dataRoot)
        => Path.Combine(dataRoot, CatalogDirectoryName, CatalogCacheFileName);

    /// <summary>The <c>ThirPartyManagedApps</c> directory beneath a data root (sic).</summary>
    public static string ThirdPartyManagedAppsDirectory(string dataRoot)
        => Path.Combine(dataRoot, ThirdPartyManagedAppsDirectoryName);
}
