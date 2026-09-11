using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Epic;

/// <summary>
/// Composes the Epic launcher's local files (catalog, manifests, third-party apps)
/// into the normalised <see cref="CandidateOwnership"/> feed. Read-only; yields
/// nothing on machines without the launcher.
/// </summary>
public sealed class EpicLibrarySource
{
    /// <summary><see cref="CandidateOwnership.Source"/> value for this reader.</summary>
    public const string SourceName = "epic_local";

    private readonly EpicManifestReader _manifestReader;
    private readonly EpicCatalogReader _catalogReader;
    private readonly EpicThirdPartyAppReader _thirdPartyReader;
    private readonly IEpicThirdPartyInstallProbe _installProbe;
    private readonly ILogger<EpicLibrarySource> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string? _dataRoot;

    /// <param name="manifestReader">Reader for <c>Manifests\*.item</c>.</param>
    /// <param name="catalogReader">Reader for <c>Catalog\catcache.bin</c>.</param>
    /// <param name="thirdPartyReader">Reader for <c>ThirPartyManagedApps\*.json</c>.</param>
    /// <param name="installProbe">Resolves install state for third-party-managed titles.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock stamping <see cref="CandidateOwnership.ObservedAt"/>.</param>
    /// <param name="dataRoot">
    /// Fixed launcher <c>Data</c> root for the argument-less <see cref="Scan()"/>.
    /// Null — the default — means locate it per <see cref="EpicPaths.FindDataRoot"/>.
    /// Set it in tests to drive the scan over a fixture tree instead of whatever
    /// Epic install the test machine happens to have.
    /// </param>
    public EpicLibrarySource(
        EpicManifestReader? manifestReader = null,
        EpicCatalogReader? catalogReader = null,
        EpicThirdPartyAppReader? thirdPartyReader = null,
        IEpicThirdPartyInstallProbe? installProbe = null,
        ILogger<EpicLibrarySource>? logger = null,
        TimeProvider? timeProvider = null,
        string? dataRoot = null)
    {
        _manifestReader = manifestReader ?? new EpicManifestReader();
        _catalogReader = catalogReader ?? new EpicCatalogReader();
        _thirdPartyReader = thirdPartyReader ?? new EpicThirdPartyAppReader();
        _installProbe = installProbe ?? new WindowsEpicThirdPartyInstallProbe();
        _logger = logger ?? NullLogger<EpicLibrarySource>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _dataRoot = dataRoot;
    }

    /// <summary>
    /// Scans the Epic install and returns one candidate per owned base game.
    /// Never throws for a missing launcher.
    /// </summary>
    /// <param name="dataRoot">
    /// Launcher <c>Data</c> root to scan. Null falls back to the root this
    /// instance was constructed with, then to <see cref="EpicPaths.FindDataRoot"/>.
    /// </param>
    public IReadOnlyList<CandidateOwnership> Scan(string? dataRoot = null)
        => ScanLibrary(dataRoot).Candidates;

    /// <summary>
    /// The whole result of a scan: candidates and launch triples in one pass.
    /// <see cref="Scan"/> is the candidates-only overload, kept because most
    /// callers want exactly that and changing its signature would touch every one
    /// of them.
    /// </summary>
    /// <param name="dataRoot">
    /// Launcher <c>Data</c> root to scan. Null falls back to the root this
    /// instance was constructed with, then to <see cref="EpicPaths.FindDataRoot"/>.
    /// </param>
    public EpicScanResult ScanLibrary(string? dataRoot = null)
    {
        dataRoot ??= _dataRoot ?? EpicPaths.FindDataRoot();
        if (dataRoot is null || !Directory.Exists(dataRoot))
        {
            _logger.LogInformation("No Epic Games Launcher installation found; Epic ingest yields nothing");
            return new EpicScanResult([], []);
        }

        var manifestsDirectory = EpicPaths.ManifestsDirectory(dataRoot);

        var manifestScan = _manifestReader.ScanDirectory(manifestsDirectory);
        var manifests = manifestScan.Manifests;
        var catalog = _catalogReader.Read(EpicPaths.CatalogCachePath(dataRoot));
        var thirdParty = _thirdPartyReader.ReadDirectory(
            EpicPaths.ThirdPartyManagedAppsDirectory(dataRoot));

        var manifestsById = new Dictionary<string, EpicManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            if (!manifestsById.TryAdd(manifest.CatalogItemId, manifest))
            {
                _logger.LogWarning(
                    "Epic catalog item {CatalogItemId} has more than one manifest; keeping {Kept}",
                    manifest.CatalogItemId, manifestsById[manifest.CatalogItemId].InstallationGuid);
            }
        }

        var thirdPartyById = new Dictionary<string, EpicThirdPartyApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in thirdParty)
        {
            thirdPartyById.TryAdd(app.CatalogItemId, app);
        }

        var catalogById = new Dictionary<string, EpicCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
        {
            catalogById.TryAdd(entry.CatalogItemId, entry);
        }

        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = new List<CandidateOwnership>();
        var launchTriples = new List<EpicLaunchTriple>();
        var installedCount = 0;
        var thirdPartyCount = 0;

        foreach (var catalogItemId in OwnedGameIds(catalogById, manifestsById, thirdPartyById))
        {
            catalogById.TryGetValue(catalogItemId, out var entry);
            manifestsById.TryGetValue(catalogItemId, out var manifest);
            thirdPartyById.TryGetValue(catalogItemId, out var app);

            var install = ResolveInstallState(manifest, entry, app, manifestScan.IsComplete);
            if (install.Installed == true)
            {
                installedCount++;
            }

            if (app is not null || entry?.IsThirdPartyManaged == true)
            {
                thirdPartyCount++;
            }

            if (LaunchTripleFor(catalogItemId, manifest, entry, app) is { } triple)
            {
                launchTriples.Add(triple);
            }

            candidates.Add(new CandidateOwnership(
                Provider: ExternalIdProviders.Epic,
                ProviderId: catalogItemId,
                // The manifest's DisplayName is the installed artifact's own name
                // and is the better-punctuated of the two where they differ; the
                // catalog title covers everything not installed. Never AppName.
                Title: FirstReal(manifest?.DisplayName, entry?.Title, app?.Title),
                // Manifests live in %PROGRAMDATA%, not per-user: one machine-wide
                // set shared across Windows accounts. Epic account attribution
                // exists only in files this reader deliberately does not open.
                AccountRef: null,
                InstallPath: install.InstallPath,
                Installed: install.Installed,
                // Not zero. Epic writes no per-game playtime and no last-played
                // date to disk at all, so both stay null: "this source cannot
                // know". See the type doc.
                PlaytimeMinutes: null,
                LastPlayedAt: null,
                // releaseInfo[0].dateAdded is the STORE RELEASE date, not the
                // acquisition date. Nothing on disk records when the user claimed
                // a title, so this stays null rather than carrying a plausible
                // wrong answer.
                AcquiredAt: null,
                Source: SourceName,
                ObservedAt: observedAt));
        }

        _logger.LogInformation(
            "Epic scan: {Candidates} candidates ({Installed} installed, {Catalog} catalog entries, "
            + "{Manifests} manifests, {ThirdParty} delivered by another launcher, "
            + "{LaunchTriples} with a complete launch triple) under {Root}",
            candidates.Count, installedCount, catalog.Count, manifests.Count, thirdPartyCount,
            launchTriples.Count, dataRoot);

        return new EpicScanResult(candidates, launchTriples) { ManifestScanComplete = manifestScan.IsComplete };
    }

    /// <summary>
    /// Assembles the triple from whichever source has each field, strongest
    /// evidence first: manifest (authoritative for an installed title), then
    /// catalog entry (authoritative for the owned library), then third-party
    /// record (the only source for a title delivered by another launcher that
    /// the catalog has not caught up with). Returns null unless both the
    /// namespace and the AppName were found: a partial triple builds a URL the
    /// launcher cannot resolve, and a URL that cannot resolve is worse than no
    /// button.
    /// </summary>
    private static EpicLaunchTriple? LaunchTripleFor(
        string catalogItemId,
        EpicManifest? manifest,
        EpicCatalogEntry? entry,
        EpicThirdPartyApp? app)
    {
        var catalogNamespace = FirstReal(
            manifest?.CatalogNamespace, entry?.CatalogNamespace, app?.CatalogNamespace);
        var appName = FirstReal(manifest?.AppName, entry?.AppName, app?.AppName);

        return catalogNamespace is null || appName is null
            ? null
            : new EpicLaunchTriple(catalogItemId, catalogNamespace, appName);
    }

    /// <summary>
    /// The owned base games, deduplicated by catalog item id and deterministically
    /// ordered by title. Union of the three sources so that a manifest or a
    /// third-party record the catalog has not caught up with is still ingested —
    /// the catalog is only rewritten when the launcher starts and logs in.
    /// </summary>
    private IEnumerable<string> OwnedGameIds(
        Dictionary<string, EpicCatalogEntry> catalog,
        Dictionary<string, EpicManifest> manifests,
        Dictionary<string, EpicThirdPartyApp> thirdParty)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in catalog.Values)
        {
            if (entry.IsGame && !entry.IsDlc)
            {
                ids[entry.CatalogItemId] = entry.Title;
            }
        }

        foreach (var manifest in manifests.Values)
        {
            if (!manifest.IsGame || manifest.IsDlc)
            {
                _logger.LogDebug(
                    "Skipping Epic manifest {CatalogItemId} ({Title}): {Reason}",
                    manifest.CatalogItemId,
                    manifest.DisplayName,
                    manifest.IsDlc ? "DLC of " + manifest.MainGameCatalogItemId : "not a game");
                continue;
            }

            ids[manifest.CatalogItemId] = manifest.DisplayName;
        }

        foreach (var app in thirdParty.Values)
        {
            // These are Epic-owned games by construction, but classify them
            // through the catalog when it has an opinion, so a non-game managed
            // entry cannot slip past the category filter.
            if (catalog.TryGetValue(app.CatalogItemId, out var entry) && (!entry.IsGame || entry.IsDlc))
            {
                continue;
            }

            ids[app.CatalogItemId] = app.Title;
        }

        return ids
            .OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key);
    }

    /// <summary>
    /// The three-valued install answer, in the order the evidence is strongest.
    ///
    /// <list type="number">
    /// <item><b>A manifest exists</b> — authoritative. Its
    /// <c>bIsIncompleteInstall</c> decides: the file is written when the download
    /// is <i>queued</i>, so an in-flight install has a manifest and is not
    /// installed.</item>
    /// <item><b>Delivered by another launcher</b> — the manifests directory says
    /// nothing about these, so it must not be used to conclude anything. Probe the
    /// delivering launcher's own install record instead; that probe returns
    /// unknown when it cannot look.</item>
    /// <item><b>Neither</b> — the catalog knows the title and the manifests
    /// directory has no record of it, so it is owned and not installed. That
    /// <c>false</c> is an observation, and it is what makes an uninstall show. It
    /// downgrades to null when any part of the manifests scan is incomplete.</item>
    /// </list>
    /// </summary>
    private EpicInstallState ResolveInstallState(
        EpicManifest? manifest,
        EpicCatalogEntry? entry,
        EpicThirdPartyApp? app,
        bool manifestsComplete)
    {
        if (manifest is not null)
        {
            if (!manifest.HasInstallState) return EpicInstallState.Unknown;
            if (!manifest.IsFullyInstalled)
            {
                _logger.LogDebug(
                    "Epic {Title} has a manifest but bIsIncompleteInstall is set; reporting not installed",
                    manifest.DisplayName);
                return EpicInstallState.NotInstalled;
            }

            return manifest.InstallLocation.Length > 0
                ? EpicInstallState.At(manifest.InstallLocation)
                : new EpicInstallState(true, null);
        }

        var registryPath = app?.RegistryPath ?? entry?.RegistryPath ?? string.Empty;
        var registryKey = app?.RegistryKey ?? entry?.RegistryKey ?? string.Empty;
        var thirdPartyManaged = app is not null || entry?.IsThirdPartyManaged == true;

        if (thirdPartyManaged)
        {
            return _installProbe.Probe(registryPath, registryKey);
        }

        return manifestsComplete ? EpicInstallState.NotInstalled : EpicInstallState.Unknown;
    }

    /// <summary>Blank is never an answer — matches <c>CandidateOwnership.Title</c>'s contract.</summary>
    private static string? FirstReal(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
