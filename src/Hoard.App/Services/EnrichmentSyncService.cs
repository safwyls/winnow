using System.Diagnostics;
using Hoard.Core.Domain;
using Hoard.Core.Queries;
using Hoard.Core.Repositories;
using Hoard.Enrich.Igdb;
using Hoard.Enrich.Igdb.Model;
using Hoard.Enrich.Steam;
using Microsoft.Extensions.Logging;

namespace Hoard.App.Services;

/// <summary>
/// Fills in what the local Steam files could not know: real titles, and the
/// metadata §6 gives <c>works</c> columns for — <c>igdb_id</c>,
/// <c>first_release_year</c>, <c>summary</c>, <c>cover_url</c> and (migration
/// 0005) <c>publisher</c>.
///
/// <para><b>Two sources, deliberately ordered.</b> IGDB is the designed
/// metadata backbone (§4.4) and wins any disagreement — it carries the
/// canonical title plus the year, summary, cover and publisher. Steam's keyless
/// store endpoint fills whatever IGDB does not answer for, and covers the case
/// where no IGDB credentials are configured at all, so the library is never
/// stuck showing appids because of a missing API key. The Steam endpoint is
/// asked about <b>titles only</b>: it is undocumented, it is strictly a
/// fallback, and it has nothing to say about the columns the matcher needs.</para>
///
/// <para><b>Two IGDB calls, not one.</b> <c>external_games</c> is the
/// high-precision Steam-appid join (§4.4) and its expanded <c>game.*</c> fields
/// carry name, year, summary and cover — but not publisher, which lives on
/// <c>involved_companies</c> and is only reachable through <c>/games</c>. Both
/// batch 400 ids per request, so a 616-game library costs about four requests
/// on a cold cache and none on a warm one.</para>
///
/// <para><b>Why metadata matters beyond display.</b> §5.3 scores a soft match on
/// title, release year, publisher and cover hash. Until this pass stored the
/// year and the publisher, two of those four signals could never fire on a
/// library-internal pair, and every candidate in the merge queue was scored on
/// title similarity alone — the one thing §5.3 says must never be trusted by
/// itself. Persisting the metadata IGDB already returns is therefore a precision
/// change, not a cosmetic one.</para>
///
/// <para>§5.1: this composes ingest-adjacent sources and the repositories, and
/// the UI never calls it — Program sequences it, the view models read the
/// database afterwards.</para>
/// </summary>
public sealed class EnrichmentSyncService
{
    private readonly IWorkRepository _works;
    private readonly IReleaseRepository _releases;
    private readonly IIgdbClient _igdb;
    private readonly ISteamStoreClient _steamStore;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ILogger<EnrichmentSyncService> _logger;

    public EnrichmentSyncService(
        IWorkRepository works,
        IReleaseRepository releases,
        IIgdbClient igdb,
        ISteamStoreClient steamStore,
        IUnitOfWorkFactory unitOfWork,
        ILogger<EnrichmentSyncService> logger)
    {
        _works = works;
        _releases = releases;
        _igdb = igdb;
        _steamStore = steamStore;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Names every work still carrying a placeholder, and back-fills the
    /// metadata columns of every work missing any of them.
    ///
    /// <para><b>Why the target set is wider than "provisional".</b> On the
    /// author's real library 616 works already have real names and not one has a
    /// year, summary, cover or publisher — they were named by a build that threw
    /// the rest of the IGDB answer away. Keyed on <c>name_is_provisional</c>
    /// alone this pass would look at nothing and back-fill nothing, forever. So
    /// the query asks the wider question: which works are missing <i>anything</i>
    /// (<see cref="IWorkRepository.GetEnrichmentTargetsAsync"/>).</para>
    ///
    /// <para><b>Idempotent, and free once warm.</b> Three separate mechanisms,
    /// each covering a different cost:</para>
    /// <list type="bullet">
    ///   <item><b>No re-fetch.</b> Both IGDB calls read <c>metadata_cache</c>
    ///     first, with a 30-day TTL and cached misses recorded as such, so a
    ///     second launch spends no requests — not even on the appids IGDB has
    ///     never heard of. This, not a stored watermark, is what keeps a
    ///     re-run off the network; a watermark would also permanently suppress
    ///     works IGDB only learns about later.</item>
    ///   <item><b>No re-write.</b> A work whose every column is filled is not
    ///     returned by the query at all, and a target the sources say nothing
    ///     new about never opens a transaction.</item>
    ///   <item><b>No fallback flood.</b> The Steam store is asked only about
    ///     works that still need a <i>name</i>. Without that split, a
    ///     credential-free machine would hit an undocumented endpoint 616 times
    ///     on every launch to re-learn titles it already has.</item>
    /// </list>
    /// </summary>
    public async Task<EnrichmentReport> EnrichAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var targets = await _works.GetEnrichmentTargetsAsync(ExternalIdProviders.Steam, ct);
        var outstandingNames = targets.Count(t => t.NameIsProvisional);
        if (targets.Count == 0)
        {
            _logger.LogInformation(
                "Every work is named and fully enriched; enrichment has nothing to do.");
            return new EnrichmentReport(0, 0, 0, stopwatch.Elapsed);
        }

        var appIds = targets.Select(t => t.ProviderId).Distinct(StringComparer.Ordinal).ToArray();

        // 1. IGDB first — the backbone, and the only source that carries the
        //    year/summary/cover/publisher the works columns and the soft matcher
        //    both want.
        var matches = new Dictionary<string, IgdbSteamMatch>(StringComparer.Ordinal);
        var games = new Dictionary<long, IgdbGame>();
        if (await _igdb.IsConfiguredAsync(ct))
        {
            // IsConfiguredAsync only proves credentials EXIST — it reads the
            // credential store, not the network. Minting can still fail (Twitch
            // down, credentials revoked, machine offline), and that must not
            // take the credential-free fallback down with it: the whole point
            // of step 3 is that it needs nothing from IGDB.
            try
            {
                foreach (var (appId, match) in await _igdb.ResolveBySteamAppIdsAsync(appIds, ct: ct))
                {
                    matches[appId] = match;
                }

                // 2. Second batched call for the publisher. external_games
                //    expands game.name/summary/first_release_date/cover but
                //    cannot reach involved_companies, and publisher is one of
                //    §5.3's four soft-match signals — the one that has never
                //    once fired because nothing ever fetched or stored it.
                //    Batched and cached exactly like step 1, so this is roughly
                //    two more requests for a 616-game library and zero
                //    thereafter.
                var igdbIds = matches.Values
                    .Select(m => m.IgdbId)
                    .Where(id => id > 0)
                    .Distinct()
                    .ToArray();

                if (igdbIds.Length > 0)
                {
                    foreach (var game in await _igdb.GetGamesAsync(igdbIds, ct: ct))
                    {
                        games[game.IgdbId] = game;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex, "IGDB lookup failed; continuing with the Steam store fallback.");
            }
        }
        else
        {
            _logger.LogInformation(
                "IGDB is not configured; falling back to the Steam store for titles. "
                + "Set Igdb__ClientId / Igdb__ClientSecret to enable the metadata backbone.");
        }

        var fromIgdb = 0;
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (appId, match) in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.Name))
            {
                titles[appId] = match.Name;
            }
        }

        // 3. Steam store for the remaining NAMES only. Undocumented endpoint,
        //    soft-fails, and carries nothing the metadata columns want — so a
        //    work that has a title and only needs a year never reaches it.
        var unnamed = targets
            .Where(t => t.NameIsProvisional && !titles.ContainsKey(t.ProviderId))
            .Select(t => t.ProviderId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var fromSteam = 0;
        if (unnamed.Length > 0)
        {
            foreach (var (appId, item) in await _steamStore.GetItemsAsync(unnamed, ct: ct))
            {
                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    titles[appId] = item.Name;
                }
            }
        }

        // 4. Write. Work and release move together, in ONE transaction each:
        //    clearing name_is_provisional is what removes the work from the
        //    name half of this query, so a crash between the two writes would
        //    strand a work named "Portal 2" beside a release still named
        //    "App 620" that no future run would ever revisit.
        var promoted = 0;
        var enriched = 0;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();

            var patch = BuildPatch(target, titles, matches, games);
            if (patch.IsEmpty)
            {
                continue;
            }

            bool namePromoted;
            using (var scope = _unitOfWork.Begin())
            {
                namePromoted = await _works.ApplyEnrichmentAsync(patch, ct);
                if (namePromoted)
                {
                    await _releases.UpdateNameAsync(target.ReleaseId, patch.Name!, ct);
                }

                scope.Commit();
            }

            if (namePromoted)
            {
                promoted++;
                if (matches.ContainsKey(target.ProviderId))
                {
                    fromIgdb++;
                }
                else
                {
                    fromSteam++;
                }
            }

            enriched++;
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Enrichment: {Promoted} of {Outstanding} names promoted "
            + "({Igdb} from IGDB, {Steam} from the Steam store); "
            + "{Enriched} of {Targets} works had metadata filled in, in {Elapsed:n1}s.",
            promoted, outstandingNames, fromIgdb, fromSteam, enriched, targets.Count,
            stopwatch.Elapsed.TotalSeconds);

        return new EnrichmentReport(outstandingNames, promoted, fromIgdb, stopwatch.Elapsed, enriched);
    }

    /// <summary>
    /// What this run is entitled to write for one work: everything a source
    /// supplied that the database does not already hold.
    ///
    /// <para>Columns already filled are left out of the patch entirely rather
    /// than re-sent. The repository would refuse to clobber them anyway (its
    /// COALESCE guards are the real safety net), but omitting them is what makes
    /// <see cref="WorkEnrichment.IsEmpty"/> answer "there is nothing to do here"
    /// truthfully — and an empty patch is a transaction not opened.</para>
    ///
    /// <para>Fields are taken from the <c>external_games</c> match first because
    /// that is the row IGDB published against this exact appid; <c>/games</c>
    /// fills whatever it did not carry, and is the only source of the
    /// publisher.</para>
    /// </summary>
    private static WorkEnrichment BuildPatch(
        EnrichmentTarget target,
        IReadOnlyDictionary<string, string> titles,
        IReadOnlyDictionary<string, IgdbSteamMatch> matches,
        IReadOnlyDictionary<long, IgdbGame> games)
    {
        var match = matches.GetValueOrDefault(target.ProviderId);
        var game = match is not null ? games.GetValueOrDefault(match.IgdbId) : null;

        // A title is only ever offered to a work still holding a placeholder.
        // A real title — from an earlier run, from the store, or edited by the
        // user — is never overwritten, which is the failure that would rename a
        // library back to appids.
        var name = target.NameIsProvisional ? titles.GetValueOrDefault(target.ProviderId) : null;

        return new WorkEnrichment(
            target.WorkId,
            Name: name,
            IgdbId: target.HasIgdbId ? null : match?.IgdbId,
            FirstReleaseYear: target.HasFirstReleaseYear
                ? null
                : match?.FirstReleaseYear ?? game?.FirstReleaseYear,
            Summary: target.HasSummary ? null : Prefer(match?.Summary, game?.Summary),
            CoverUrl: target.HasCoverUrl ? null : Prefer(match?.CoverUrl, game?.CoverUrl),
            Publisher: target.HasPublisher ? null : PrimaryPublisher(game));
    }

    private static string? Prefer(string? first, string? second)
        => string.IsNullOrWhiteSpace(first) ? second : first;

    /// <summary>
    /// Reduces IGDB's publisher list to the one name migration 0005 stores.
    ///
    /// <para><b>Ordinal-first, not "whatever IGDB listed first".</b> The pair
    /// this signal exists to score is two library rows for the SAME game under
    /// different store ids. Both resolve to the same IGDB game and therefore see
    /// the same set of publishers, so any order-independent choice makes the two
    /// sides agree and the signal fire. IGDB's own row order is not guaranteed
    /// stable between fetches, so picking the first element would let a
    /// re-fetch months apart write two different names for one game and turn a
    /// corroborating signal into a -0.15 mismatch penalty. Multi-publisher games
    /// are the norm rather than the exception — The Witcher 3 lists four, in
    /// regional order — so this is not a hypothetical.</para>
    ///
    /// <para>Comparison is ordinal and case-insensitive so the pick does not
    /// drift with the machine's culture; the stored string keeps IGDB's own
    /// casing.</para>
    /// </summary>
    internal static string? PrimaryPublisher(IgdbGame? game)
    {
        if (game is null)
        {
            return null;
        }

        string? best = null;
        foreach (var publisher in game.Publishers)
        {
            if (string.IsNullOrWhiteSpace(publisher))
            {
                continue;
            }

            var candidate = publisher.Trim();
            if (best is null || string.Compare(candidate, best, StringComparison.OrdinalIgnoreCase) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }
}

/// <param name="Outstanding">Works carrying a placeholder name when the run began.</param>
/// <param name="Promoted">Works given a real title this run.</param>
/// <param name="FromIgdb">How many of the promotions came from IGDB rather than the fallback.</param>
/// <param name="Elapsed">Wall-clock time for the whole pass.</param>
/// <param name="MetadataFilled">
/// Works that had at least one column written — the back-fill count. Larger than
/// <paramref name="Promoted"/> on any library whose titles were already real,
/// which after the first run is every library.
/// </param>
public sealed record EnrichmentReport(
    int Outstanding,
    int Promoted,
    int FromIgdb,
    TimeSpan Elapsed,
    int MetadataFilled = 0);
