using System.Globalization;
using Hoard.Core.Domain;
using Hoard.Core.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoard.Ingest.Gog;

/// <summary>
/// Composes GOG's local sources into the normalised
/// <see cref="CandidateOwnership"/> feed (§5.1: ingest emits candidates only and
/// never writes works/releases — that is Resolve's job). Strictly read-only
/// against every GOG file, and a machine without GOG simply yields an empty list.
///
/// <para><b>Two sources, in priority order</b>
/// (docs/spikes/epic-gog-local-files.md sections 10, 13, 16):</para>
/// <list type="number">
/// <item><b>Galaxy's client database</b> — authoritative wherever it exists. The
/// owned library including uninstalled titles, canonical titles, real playtime,
/// real last-played, purchase dates and install paths. Read through
/// <see cref="GalaxyDatabaseSnapshot"/>, which copies before it opens; the live
/// file is never opened, for any reason, including <c>mode=ro</c>.</item>
/// <item><b>GOG's install registry plus <c>goggame-&lt;id&gt;.info</c></b> — the
/// path for users who never install Galaxy, which is a first-class way to own GOG
/// games. It also fills in games installed standalone that Galaxy has not
/// noticed, so it runs even when the database is present.</item>
/// </list>
///
/// <para><b>GOG really does have playtime, unlike Epic.</b>
/// <c>GameTimes.minutesInGame</c> is minutes and <c>LastPlayedDates</c> is UTC,
/// and both survive uninstall — The Witcher 3 reports 50 minutes and a 2018
/// last-played while not installed, so playtime is never gated on install state.
/// A <c>GameTimes</c> row of <c>0</c> is a real answer and is emitted as <c>0</c>;
/// no row at all is emitted as null, because those are different statements.
/// Games discovered only through the registry carry null playtime for the same
/// reason: that path has no playtime source whatsoever, and a zero there would be
/// a claim rather than an observation. Galaxy only accrues time for sessions it
/// launched, exactly as §4.1's caveat says of Steam's local config.</para>
///
/// <para><b>Titles.</b> Galaxy's <c>title</c> GamePiece is canonical and is
/// preferred always. Every install-side source carries the <i>installer-locale</i>
/// title instead — GWENT's registry name and <c>.info</c> name are Polish on an
/// English install — so those are used only where Galaxy is absent. GOG's product
/// id hard-joins to IGDB directly, so a GOG title never has to carry identity and
/// a local one can never reach the fuzzy matcher.</para>
/// </summary>
public sealed class GogLibrarySource
{
    /// <summary><see cref="CandidateOwnership.Source"/> value for this reader.</summary>
    public const string SourceName = "gog_local";

    private readonly GalaxyLibraryReader _galaxyReader;
    private readonly GogGameInfoReader _gameInfoReader;
    private readonly IGogInstalledGameRegistry _registry;
    private readonly ILogger<GogLibrarySource> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string? _galaxyRoot;

    /// <param name="galaxyReader">Runs the ownership query over a database snapshot.</param>
    /// <param name="gameInfoReader">Reads <c>goggame-&lt;id&gt;.info</c>.</param>
    /// <param name="registry">Enumerates GOG's per-game install registry.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock stamping <see cref="CandidateOwnership.ObservedAt"/>.</param>
    /// <param name="galaxyRoot">
    /// Fixed Galaxy root (the directory holding <c>config.json</c>) for the
    /// argument-less <see cref="Scan()"/>. Null — the default — means locate it per
    /// <see cref="GogPaths.FindGalaxyRoot"/>.
    /// </param>
    public GogLibrarySource(
        GalaxyLibraryReader? galaxyReader = null,
        GogGameInfoReader? gameInfoReader = null,
        IGogInstalledGameRegistry? registry = null,
        ILogger<GogLibrarySource>? logger = null,
        TimeProvider? timeProvider = null,
        string? galaxyRoot = null)
    {
        _galaxyReader = galaxyReader ?? new GalaxyLibraryReader();
        _gameInfoReader = gameInfoReader ?? new GogGameInfoReader();
        _registry = registry ?? new WindowsGogInstalledGameRegistry();
        _logger = logger ?? NullLogger<GogLibrarySource>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _galaxyRoot = galaxyRoot;
    }

    /// <summary>
    /// Scans GOG's local data and returns one candidate per owned base game.
    /// Never throws for a missing Galaxy install or a missing registry key.
    /// </summary>
    /// <param name="galaxyRoot">
    /// Galaxy root to scan. Null falls back to the root this instance was
    /// constructed with, then to <see cref="GogPaths.FindGalaxyRoot"/>. A null
    /// result there is not the end of the scan: the registry path still runs, and
    /// it is the whole point of the Galaxy-less case.
    /// </param>
    public IReadOnlyList<CandidateOwnership> Scan(string? galaxyRoot = null)
    {
        galaxyRoot ??= _galaxyRoot ?? GogPaths.FindGalaxyRoot();

        var galaxyEntries = ReadGalaxy(galaxyRoot);
        var registryGames = ReadRegistry();

        if (galaxyEntries.Count == 0 && registryGames.Count == 0)
        {
            _logger.LogInformation("No GOG installation found; GOG ingest yields nothing");
            return [];
        }

        var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = new List<(string Title, string ProductId, CandidateOwnership Candidate)>();

        // Galaxy's answer first, and it owns the title, playtime and dates.
        var fromGalaxy = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in galaxyEntries.GroupBy(e => e.ProductId, StringComparer.Ordinal))
        {
            var winner = ResolvePlaytimeWinner(group);
            if (winner.IsDlc)
            {
                _logger.LogDebug("Skipping GOG DLC {ReleaseKey} ({Title})", winner.ReleaseKey, winner.Title);
                continue;
            }

            if (!winner.IsVisibleInLibrary)
            {
                _logger.LogDebug(
                    "Skipping GOG release {ReleaseKey} hidden from the library", winner.ReleaseKey);
                continue;
            }

            fromGalaxy.Add(winner.ProductId);
            registryGames.TryGetValue(winner.ProductId, out var registryGame);

            // Both records look at the same disk. Galaxy is usually right and is
            // taken first; the registry can still be ahead of it for a game
            // installed by a standalone installer Galaxy has not indexed yet.
            var installPath = winner.InstallationPath ?? registryGame?.InstallPath;

            candidates.Add((
                winner.Title ?? string.Empty,
                winner.ProductId,
                new CandidateOwnership(
                    Provider: ExternalIdProviders.Gog,
                    ProviderId: winner.ProductId,
                    Title: FirstReal(winner.Title, LocalTitleFor(registryGame)),
                    AccountRef: winner.UserId.ToString(CultureInfo.InvariantCulture),
                    InstallPath: installPath,
                    // A real observation: Galaxy tracks installs and this scan
                    // read InstalledBaseProducts, so false is what makes an
                    // uninstall visible rather than a shrug.
                    Installed: !string.IsNullOrWhiteSpace(installPath),
                    // 0 when Galaxy has a row saying zero; null when it has no
                    // row at all. Not the same statement.
                    PlaytimeMinutes: winner.PlaytimeMinutes,
                    LastPlayedAt: winner.LastPlayedUtc,
                    // purchaseDate is the real acquisition date. addedDate is a
                    // Galaxy backfill artefact — identical across most of the
                    // library — and is deliberately not used.
                    AcquiredAt: winner.PurchasedAtUtc,
                    Source: SourceName,
                    ObservedAt: observedAt)));
        }

        // Whatever the registry knows that Galaxy did not report. On a
        // Galaxy-less machine this is the entire library.
        var registryOnly = 0;
        foreach (var game in registryGames.Values)
        {
            if (fromGalaxy.Contains(game.GameId))
            {
                continue;
            }

            var info = game.InstallPath is not null
                ? _gameInfoReader.ReadForGame(game.InstallPath, game.GameId)
                : null;

            if (info?.IsDlc == true)
            {
                _logger.LogDebug(
                    "Skipping GOG DLC {GameId} (rootGameId {RootGameId})", info.GameId, info.RootGameId);
                continue;
            }

            // Installer-locale, diacritic-stripped in the registry's case. It is
            // the only title this path has; the product id carries identity, so
            // the title never has to.
            var title = FirstReal(info?.Name, game.GameName);
            registryOnly++;

            candidates.Add((
                title ?? string.Empty,
                game.GameId,
                new CandidateOwnership(
                    Provider: ExternalIdProviders.Gog,
                    ProviderId: game.GameId,
                    Title: title,
                    // GOG's install registry is machine-wide; it names no account.
                    AccountRef: null,
                    InstallPath: game.InstallPath,
                    // The registry key exists because a GOG installer put the game
                    // on this disk. That is an observation.
                    Installed: true,
                    // This path has no playtime source at all. Null means "cannot
                    // know" — a zero here would be a claim that the user has never
                    // played it, which nothing on disk supports.
                    PlaytimeMinutes: null,
                    LastPlayedAt: null,
                    // INSTALLDATE exists but is LOCAL time while every Galaxy date
                    // is UTC, and it is an install date rather than a purchase
                    // date either way. Not carried.
                    AcquiredAt: null,
                    Source: SourceName,
                    ObservedAt: observedAt)));
        }

        var ordered = candidates
            .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ProductId, StringComparer.Ordinal)
            .Select(c => c.Candidate)
            .ToList();

        _logger.LogInformation(
            "GOG scan: {Candidates} candidates ({Galaxy} from the Galaxy database, "
            + "{RegistryOnly} from the install registry only, {Installed} installed)",
            ordered.Count, fromGalaxy.Count, registryOnly,
            ordered.Count(c => c.Installed == true));

        return ordered;
    }

    private IReadOnlyList<GogLibraryEntry> ReadGalaxy(string? galaxyRoot)
    {
        if (galaxyRoot is null || !Directory.Exists(galaxyRoot))
        {
            _logger.LogDebug("No GOG Galaxy root found; falling back to the install registry");
            return [];
        }

        var databasePath = GogPaths.FindClientDatabase(galaxyRoot);
        if (databasePath is null)
        {
            _logger.LogDebug(
                "GOG Galaxy is present at {Root} but has no client database; "
                + "falling back to the install registry", galaxyRoot);
            return [];
        }

        // Copy first, always. Opening the live file — even read-only — creates
        // -wal/-shm in GOG's own directory (section 11).
        using var snapshot = GalaxyDatabaseSnapshot.Take(databasePath, _logger);
        if (snapshot is null)
        {
            return [];
        }

        // Galaxy migrates this schema. A jump from the verified 40 is the early
        // warning that the ownership query needs re-verifying against a real DB.
        _logger.LogDebug(
            "GOG Galaxy client schema user_version = {UserVersion}", snapshot.ReadUserVersion());

        return _galaxyReader.Read(snapshot);
    }

    private Dictionary<string, GogRegistryGame> ReadRegistry()
    {
        var games = new Dictionary<string, GogRegistryGame>(StringComparer.Ordinal);
        foreach (var game in _registry.Enumerate())
        {
            games.TryAdd(game.GameId, game);
        }

        return games;
    }

    /// <summary>
    /// Galaxy keys everything by <c>userId</c> and a machine can have several. One
    /// account's record wins the whole row — minutes, last-played and attribution
    /// move together — so the numbers stay coherent, exactly as
    /// <c>SteamLibrarySource</c> does across <c>userdata</c> folders. Highest
    /// playtime wins; ties break on the most recent last-played.
    /// </summary>
    private static GogLibraryEntry ResolvePlaytimeWinner(IEnumerable<GogLibraryEntry> entries)
    {
        GogLibraryEntry? winner = null;
        foreach (var entry in entries)
        {
            if (winner is null
                || (entry.PlaytimeMinutes ?? -1) > (winner.PlaytimeMinutes ?? -1)
                || ((entry.PlaytimeMinutes ?? -1) == (winner.PlaytimeMinutes ?? -1)
                    && Nullable.Compare(entry.LastPlayedUtc, winner.LastPlayedUtc) > 0))
            {
                winner = entry;
            }
        }

        return winner!;
    }

    private static string? LocalTitleFor(GogRegistryGame? game)
        => game?.GameName;

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
