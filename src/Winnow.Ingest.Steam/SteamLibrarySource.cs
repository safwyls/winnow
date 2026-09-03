using System.Globalization;
using Winnow.Core.Domain;
using Winnow.Core.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Winnow.Ingest.Steam;

/// <summary>
/// Emits <see cref="CandidateOwnership"/> candidates from Steam's local files:
/// union of installed appmanifests and per-account playtime from localconfig.vdf.
/// Read-only; yields an empty list when no Steam install is found. Multi-account
/// playtime ties are broken by highest total minutes, then most recent last-played.
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

    /// <summary>Path equality for deduplicating library roots (case-insensitive on Windows/macOS, ordinal on Linux).</summary>
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
    private readonly string? _steamRoot;

    /// <param name="steamRoot">Fixed install root; null (default) uses <see cref="SteamPaths.FindSteamRoot"/>.</param>
    public SteamLibrarySource(
        LibraryFoldersReader? libraryFoldersReader = null,
        AppManifestReader? appManifestReader = null,
        LocalConfigReader? localConfigReader = null,
        SteamAccountEnumerator? accountEnumerator = null,
        ILogger<SteamLibrarySource>? logger = null,
        TimeProvider? timeProvider = null,
        string? steamRoot = null)
    {
        _steamRoot = steamRoot;
        _libraryFoldersReader = libraryFoldersReader ?? new LibraryFoldersReader();
        _appManifestReader = appManifestReader ?? new AppManifestReader();
        _localConfigReader = localConfigReader ?? new LocalConfigReader();
        _accountEnumerator = accountEnumerator ?? new SteamAccountEnumerator();
        _logger = logger ?? NullLogger<SteamLibrarySource>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Scans the Steam install and returns one candidate per appid. Never throws for a missing install.</summary>
    public IReadOnlyList<CandidateOwnership> Scan(string? steamRoot = null)
    {
        steamRoot ??= _steamRoot ?? SteamPaths.FindSteamRoot();
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

            // ...and the un-collapsed answer beside it. The line above can name
            // only one account, which is right for a playtime figure and wrong
            // for the question "who has this": a game two people on this PC have
            // played reports whichever of them played it more, and the other
            // becomes invisible. Collected once here for both candidate shapes
            // below.
            var accountEntries = CollectAccounts(appId, playtimeByAccount, accounts);

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
                    // Never null from this source: the manifest is the answer.
                    Installed: manifest.IsFullyInstalled,
                    PlaytimeMinutes: winner?.PlaytimeMinutes,
                    LastPlayedAt: lastPlayedAt,
                    AcquiredAt: null,
                    Source: SourceName,
                    ObservedAt: observedAt)
                {
                    // Carried even when the manifest date is unattributed: the
                    // date belongs to the machine, but whichever accounts
                    // localconfig.vdf named still hold the game.
                    Accounts = accountEntries,
                });
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
                    // A real observation, not a shrug: this scan read every
                    // library root and found no manifest, so the game is gone
                    // from disk and the stored flag must clear.
                    Installed: false,
                    PlaytimeMinutes: winner?.PlaytimeMinutes,
                    LastPlayedAt: winner?.LastPlayedUtc,
                    AcquiredAt: null,
                    Source: SourceName,
                    ObservedAt: observedAt)
                {
                    Accounts = accountEntries,
                });
            }
        }

        _logger.LogInformation(
            "Steam scan: {Candidates} candidates ({Installed} with an appmanifest, "
            + "{PlaytimeOnly} played-but-uninstalled) from {Accounts} account(s) under {Root}",
            candidates.Count, installedCount, candidates.Count - installedCount, accounts.Count, steamRoot);
        return candidates;
    }

    /// <summary>
    /// Every account on this PC that this appid can be attributed to, each with
    /// its OWN figures rather than the machine's collapsed answer.
    ///
    /// <para>The ordinary case is one entry per account whose
    /// <c>localconfig.vdf</c> names the appid. That file records games the
    /// account has PLAYED, so it is evidence of holding as well as of playing —
    /// including Family Sharing, where a game played under this login on
    /// somebody else's licence is still this account's play. The visibility
    /// filter is about whose account, not whose purchase, so no special case is
    /// wanted here and none is made.</para>
    ///
    /// <para>The exception is the sole-account machine. An appid with a manifest
    /// on disk and no playtime record anywhere is a game the one account here
    /// owns and has never launched — the largest population in most libraries,
    /// and one that would otherwise carry no account evidence at all. It gets an
    /// entry with null figures: this account holds it, and nobody measured a
    /// session because there was none. With two or more accounts signed in the
    /// same appid names nobody, because the manifest cannot say which of them
    /// installed it, and guessing would be the single-winner mistake again.</para>
    /// </summary>
    private static IReadOnlyList<CandidateAccount> CollectAccounts(
        string appId,
        List<(SteamAccount Account, IReadOnlyDictionary<string, SteamAppPlaytime> Apps)> playtimeByAccount,
        IReadOnlyList<SteamAccount> accounts)
    {
        List<CandidateAccount>? entries = null;

        foreach (var (account, apps) in playtimeByAccount)
        {
            if (!apps.TryGetValue(appId, out var playtime))
            {
                continue;
            }

            entries ??= new List<CandidateAccount>(playtimeByAccount.Count);
            entries.Add(new CandidateAccount(
                account.Steam3Id, playtime.PlaytimeMinutes, playtime.LastPlayedUtc));
        }

        if (entries is not null)
        {
            return entries;
        }

        return accounts.Count == 1
            ? [new CandidateAccount(accounts[0].Steam3Id, PlaytimeMinutes: null, LastPlayedAt: null)]
            : [];
    }

    /// <summary>Picks the account with the highest playtime for this appid, ties broken by most recent last-played.</summary>
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
