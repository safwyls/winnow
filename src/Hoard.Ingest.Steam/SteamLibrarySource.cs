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
/// <para><b>Candidate set is the union of two sources:</b> (a) appids with an
/// <c>appmanifest_*.acf</c> in any library root — the installed games; and (b)
/// appids with a <c>Playtime</c> record in any account's
/// <c>localconfig.vdf</c> — everything ever launched on this machine, whether
/// or not it is still installed. Manifest data is authoritative where a
/// manifest exists (title, install dir, install state). A playtime-only appid
/// has no local title source at all, so it is emitted with
/// <c>Title: null</c>, <c>Installed: false</c>, <c>InstallPath: null</c> — a
/// provisional candidate the resolver names as a placeholder pending
/// enrichment. The uninstalled-but-played pile is the whole point: on a real
/// install it outnumbers the installed games by an order of magnitude.</para>
///
/// <para><b>Multi-account playtime strategy:</b> installed apps come from the
/// machine-wide appmanifests, but playtime is per-account. When more than one
/// <c>userdata/&lt;steam3id&gt;</c> account has playtime for the same appid,
/// the account with the highest total <c>Playtime</c> wins the whole record —
/// minutes, last-played, and <see cref="CandidateOwnership.AccountRef"/>
/// attribution move together (ties broken by most recent last-played).
/// Fields are never mixed across accounts, so the numbers stay coherent. The
/// same rule decides playtime-only candidates, which additionally take their
/// account attribution from the winner.</para>
///
/// <para><b>The machine-level manifest date.</b> An <c>appmanifest</c> also
/// records a <c>LastPlayed</c>, but it belongs to the machine, not to any
/// account. It is used only when NO account has a playtime record for the appid
/// — a machine whose <c>userdata/</c> is missing or unreadable, where it is the
/// only evidence of play that exists. Such a candidate is emitted with no
/// <see cref="CandidateOwnership.AccountRef"/> at all, because attributing it
/// would be a guess. It is never used to fill a gap in a winning account's own
/// record: a winner whose date is the <c>86400</c> "before Steam tracked this"
/// sentinel keeps its null, since borrowing another account's session to stand
/// in for it can make a genuinely dormant title look recently played — the exact
/// mistake the staleness buckets exist to avoid.</para>
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

    /// <summary>
    /// Path equality for deduplicating library roots. Windows and macOS
    /// filesystems are case-insensitive by default; Linux ones are not, and
    /// <c>/games/Steam</c> and <c>/games/steam</c> there are two different
    /// libraries. Folding them would silently drop one whole root's games.
    /// </summary>
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

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
    /// appid in the union of installed appmanifests across all library folders
    /// and played apps across every account's localconfig.vdf. Never throws for
    /// a missing install.
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

        var accounts = _accountEnumerator.Enumerate(steamRoot);
        var playtimeByAccount = new List<(SteamAccount Account, IReadOnlyDictionary<string, SteamAppPlaytime> Apps)>(accounts.Count);
        foreach (var account in accounts)
        {
            playtimeByAccount.Add((account, _localConfigReader.Read(account.LocalConfigPath)));
        }

        // Union of both sources, deduplicated by appid, deterministically
        // ordered. The deny-list applies to localconfig too: tool appids carry
        // playtime records of their own (Proton, the Linux runtimes).
        var appIds = new HashSet<string>(manifests.Keys, StringComparer.Ordinal);
        foreach (var (_, apps) in playtimeByAccount)
        {
            foreach (var appId in apps.Keys)
            {
                if (ToolAppIds.Contains(appId))
                {
                    _logger.LogDebug("Skipping Steam tooling app {AppId} from localconfig playtime", appId);
                    continue;
                }

                appIds.Add(appId);
            }
        }

        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = new List<CandidateOwnership>(appIds.Count);
        var installedCount = 0;

        foreach (var appId in appIds
                     .OrderBy(ParseAppIdForOrdering)
                     .ThenBy(static id => id, StringComparer.Ordinal))
        {
            var (winner, winnerAccount) = ResolvePlaytimeWinner(appId, playtimeByAccount);

            // Attribution: the winning account; for never-played games, the
            // sole account when there is exactly one, else unknown.
            var accountRef = winnerAccount?.Steam3Id
                ?? (accounts.Count == 1 ? accounts[0].Steam3Id : null);

            if (manifests.TryGetValue(appId, out var entry))
            {
                // Manifest present: it is authoritative for title and install state.
                var (manifest, libraryRoot) = entry;
                var installPath = manifest.InstallDir.Length > 0
                    ? Path.Combine(libraryRoot, "steamapps", "common", manifest.InstallDir)
                    : null;

                // With a winner, minutes/date/attribution all come from that one
                // account and nothing else touches them. Without one, the
                // manifest date is the only play evidence on this machine — keep
                // it, but unattributed, since it belongs to the machine.
                var hasWinner = winner is not null;
                var lastPlayedAt = hasWinner ? winner!.LastPlayedUtc : manifest.LastPlayedUtc;
                var manifestOnlyDate = !hasWinner && lastPlayedAt is not null;

                candidates.Add(new CandidateOwnership(
                    Provider: ExternalIdProviders.Steam,
                    ProviderId: appId,
                    Title: manifest.Name,
                    AccountRef: manifestOnlyDate ? null : accountRef,
                    InstallPath: installPath,
                    Installed: manifest.IsFullyInstalled,
                    PlaytimeMinutes: winner?.PlaytimeMinutes,
                    LastPlayedAt: lastPlayedAt,
                    AcquiredAt: null,
                    Source: SourceName,
                    ObservedAt: observedAt));
                installedCount++;
            }
            else
            {
                // Played but not installed: localconfig knows the appid and the
                // minutes, nothing else. No local title source exists — the
                // resolver names it provisionally until enrichment runs.
                candidates.Add(new CandidateOwnership(
                    Provider: ExternalIdProviders.Steam,
                    ProviderId: appId,
                    Title: null,
                    AccountRef: accountRef,
                    InstallPath: null,
                    Installed: false,
                    PlaytimeMinutes: winner?.PlaytimeMinutes,
                    LastPlayedAt: winner?.LastPlayedUtc,
                    AcquiredAt: null,
                    Source: SourceName,
                    ObservedAt: observedAt));
            }
        }

        _logger.LogInformation(
            "Steam scan: {Candidates} candidates ({Installed} with an appmanifest, "
            + "{PlaytimeOnly} played-but-uninstalled) from {Accounts} account(s) under {Root}",
            candidates.Count, installedCount, candidates.Count - installedCount, accounts.Count, steamRoot);
        return candidates;
    }

    /// <summary>
    /// Picks the single account whose record owns this appid: highest total
    /// playtime, ties broken by the most recent last-played. Returns
    /// <c>(null, null)</c> when no account has a playtime record for the appid.
    /// </summary>
    private static (SteamAppPlaytime? Winner, SteamAccount? Account) ResolvePlaytimeWinner(
        string appId,
        List<(SteamAccount Account, IReadOnlyDictionary<string, SteamAppPlaytime> Apps)> playtimeByAccount)
    {
        SteamAccount? winnerAccount = null;
        SteamAppPlaytime? winner = null;

        foreach (var (account, apps) in playtimeByAccount)
        {
            if (!apps.TryGetValue(appId, out var playtime))
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

        return (winner, winnerAccount);
    }

    private Dictionary<string, (AppManifest Manifest, string LibraryRoot)> CollectManifests(string steamRoot)
    {
        var libraryRoots = new List<string>();
        var seenRoots = new HashSet<string>(PathComparer);

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
                // On Windows a three-character extension in a glob also matches
                // longer ones (the 8.3 short-name rule): "*.acf" happily returns
                // "appmanifest_1.acfx" or a "…acf.bak"-style leftover. Steam's
                // own backup/temp files land in this directory, so check the
                // suffix explicitly rather than trusting the pattern.
                if (!manifestPath.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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
