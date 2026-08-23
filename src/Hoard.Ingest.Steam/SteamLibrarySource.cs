using System.Globalization;
using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Steam;

/// <summary>
/// Composes the Steam local-file readers into the normalised
/// <see cref="CandidateOwnership"/> feed for the whole install (§5.1: ingest
/// emits candidates only; it never writes works/releases — that is Resolve's
/// job). Strictly read-only against every Steam file, and a machine without
/// Steam simply yields an empty list.
///
/// <para><b>Multi-account playtime strategy:</b> installed apps come from the
/// machine-wide appmanifests, but playtime is per-account. When more than one
/// <c>userdata/&lt;steam3id&gt;</c> account has playtime for the same appid,
/// the account with the highest total <c>Playtime</c> wins the whole record —
/// minutes, last-played, and <see cref="CandidateOwnership.AccountRef"/>
/// attribution move together (ties broken by most recent last-played).
/// Fields are never mixed across accounts, so the numbers stay coherent.</para>
/// </summary>
public sealed class SteamLibrarySource
{
    /// <summary><see cref="CandidateOwnership.Source"/> value for this reader.</summary>
    public const string SourceName = "steam_local";

    /// <summary>
    /// Steam tooling appids that are installed apps but not games — never emit
    /// candidates for these. Not exhaustive; extend as new runtimes appear.
    /// </summary>
    public static readonly IReadOnlySet<string> ToolAppIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "228980",  // Steamworks Common Redistributables
        "1070560", // Steam Linux Runtime 1.0 (scout)
        "1391110", // Steam Linux Runtime 2.0 (soldier)
        "1628350", // Steam Linux Runtime 3.0 (sniper)
        "1493710", // Proton Experimental
        "2180100", // Proton Hotfix
    };

    private readonly LibraryFoldersReader _libraryFoldersReader;
    private readonly AppManifestReader _appManifestReader;
    private readonly LocalConfigReader _localConfigReader;
    private readonly SteamAccountEnumerator _accountEnumerator;
    private readonly ILogger<SteamLibrarySource> _logger;
    private readonly TimeProvider _timeProvider;

    public SteamLibrarySource(
        LibraryFoldersReader? libraryFoldersReader = null,
        AppManifestReader? appManifestReader = null,
        LocalConfigReader? localConfigReader = null,
        SteamAccountEnumerator? accountEnumerator = null,
        ILogger<SteamLibrarySource>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _libraryFoldersReader = libraryFoldersReader ?? new LibraryFoldersReader();
        _appManifestReader = appManifestReader ?? new AppManifestReader();
        _localConfigReader = localConfigReader ?? new LocalConfigReader();
        _accountEnumerator = accountEnumerator ?? new SteamAccountEnumerator();
        _logger = logger ?? NullLogger<SteamLibrarySource>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Scans the Steam install (auto-located via <see cref="SteamPaths"/> when
    /// <paramref name="steamRoot"/> is null) and returns one candidate per
    /// installed game across all library folders, with playtime joined from
    /// every account's localconfig.vdf. Never throws for a missing install.
    /// </summary>
    public IReadOnlyList<CandidateOwnership> Scan(string? steamRoot = null)
    {
        steamRoot ??= SteamPaths.FindSteamRoot();
        if (steamRoot is null || !Directory.Exists(steamRoot))
        {
            _logger.LogInformation("No Steam installation found; Steam ingest yields nothing");
            return [];
        }

        var manifests = CollectManifests(steamRoot);
        if (manifests.Count == 0)
        {
            return [];
        }

        var accounts = _accountEnumerator.Enumerate(steamRoot);
        var playtimeByAccount = new List<(SteamAccount Account, IReadOnlyDictionary<string, SteamAppPlaytime> Apps)>(accounts.Count);
        foreach (var account in accounts)
        {
            playtimeByAccount.Add((account, _localConfigReader.Read(account.LocalConfigPath)));
        }

        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = new List<CandidateOwnership>(manifests.Count);

        foreach (var (manifest, libraryRoot) in manifests
                     .Values
                     .OrderBy(static entry => ParseAppIdForOrdering(entry.Manifest.AppId)))
        {
            SteamAccount? winnerAccount = null;
            SteamAppPlaytime? winner = null;
            foreach (var (account, apps) in playtimeByAccount)
            {
                if (!apps.TryGetValue(manifest.AppId, out var playtime))
                {
                    continue;
                }

                if (winner is null
                    || playtime.PlaytimeMinutes > winner.PlaytimeMinutes
                    || (playtime.PlaytimeMinutes == winner.PlaytimeMinutes
                        && Nullable.Compare(playtime.LastPlayedUtc, winner.LastPlayedUtc) > 0))
                {
                    winner = playtime;
                    winnerAccount = account;
                }
            }

            // Attribution: the winning account; for never-played games, the
            // sole account when there is exactly one, else unknown.
            var accountRef = winnerAccount?.Steam3Id
                ?? (accounts.Count == 1 ? accounts[0].Steam3Id : null);

            var installPath = manifest.InstallDir.Length > 0
                ? Path.Combine(libraryRoot, "steamapps", "common", manifest.InstallDir)
                : null;

            candidates.Add(new CandidateOwnership(
                Provider: ExternalIdProviders.Steam,
                ProviderId: manifest.AppId,
                Title: manifest.Name,
                AccountRef: accountRef,
                InstallPath: installPath,
                Installed: manifest.IsFullyInstalled,
                PlaytimeMinutes: winner?.PlaytimeMinutes,
                LastPlayedAt: winner?.LastPlayedUtc ?? manifest.LastPlayedUtc,
                AcquiredAt: null,
                Source: SourceName,
                ObservedAt: observedAt));
        }

        _logger.LogInformation(
            "Steam scan: {Candidates} candidates from {Accounts} account(s) under {Root}",
            candidates.Count, accounts.Count, steamRoot);
        return candidates;
    }

    private Dictionary<string, (AppManifest Manifest, string LibraryRoot)> CollectManifests(string steamRoot)
    {
        var libraryRoots = new List<string>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in _libraryFoldersReader.Read(
                     Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf")))
        {
            if (!Directory.Exists(folder.Path))
            {
                _logger.LogDebug(
                    "Steam library root {Path} does not exist (offline drive?); skipping",
                    folder.Path);
                continue;
            }

            if (seenRoots.Add(Path.GetFullPath(folder.Path)))
            {
                libraryRoots.Add(folder.Path);
            }
        }

        // Defensive fallback: no readable libraryfolders.vdf but a steamapps
        // directory exists — treat the install root as the sole library.
        if (libraryRoots.Count == 0 && Directory.Exists(Path.Combine(steamRoot, "steamapps")))
        {
            libraryRoots.Add(steamRoot);
        }

        var manifests = new Dictionary<string, (AppManifest Manifest, string LibraryRoot)>(StringComparer.Ordinal);
        foreach (var libraryRoot in libraryRoots)
        {
            var steamApps = Path.Combine(libraryRoot, "steamapps");
            if (!Directory.Exists(steamApps))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                var manifest = _appManifestReader.Read(manifestPath);
                if (manifest is null)
                {
                    continue;
                }

                if (ToolAppIds.Contains(manifest.AppId))
                {
                    _logger.LogDebug(
                        "Skipping Steam tooling app {AppId} ({Name})", manifest.AppId, manifest.Name);
                    continue;
                }

                if (!manifests.TryAdd(manifest.AppId, (manifest, libraryRoot)))
                {
                    _logger.LogWarning(
                        "App {AppId} has manifests in multiple library roots; keeping {Kept}",
                        manifest.AppId, manifests[manifest.AppId].LibraryRoot);
                }
            }
        }

        return manifests;
    }

    private static long ParseAppIdForOrdering(string appId)
        => long.TryParse(appId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : long.MaxValue;
}
