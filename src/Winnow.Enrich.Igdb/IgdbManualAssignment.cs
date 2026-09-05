using Winnow.Core.Domain;
using Winnow.Core.Repositories;
using Winnow.Enrich.Igdb.Model;
using Microsoft.Extensions.Logging;

namespace Winnow.Enrich.Igdb;

/// <summary>
/// The non-UI half of manual IGDB assignment: search for a game by title,
/// assign the user's choice to a work, clear that choice, and read back
/// the current pin. The view names the buttons; this class does the work.
///
/// <para>Modelled on <see cref="IgdbMaturitySync"/>: same layer, same
/// soft-fail discipline, same use of <c>Winnow.Core</c> repository
/// interfaces. Search and assign catch and log rather than throw, so a
/// failed search degrades into an empty candidate list and a failed
/// assignment degrades into <see cref="Model.IgdbAssignmentStatus.Failed"/>
/// rather than an exception reaching the UI.
/// <see cref="OperationCanceledException"/> on the caller's own token is
/// rethrown, never swallowed.</para>
///
/// <para><see cref="AssignAsync"/> refuses to pin when IGDB cannot supply
/// the chosen game's metadata (<see cref="Model.IgdbAssignmentStatus.MetadataUnavailable"/>):
/// pinning without metadata would leave the wrong metadata in place and
/// block the automatic pass from ever correcting it. Clearing a pin
/// returns the work to automatic resolution; the metadata the pin wrote
/// stays in place, and the next automatic pass fills what is empty.</para>
/// </summary>
public sealed class IgdbManualAssignment
{
    private readonly IIgdbClient _igdb;
    private readonly IWorkIgdbPinRepository _pins;
    private readonly ILogger<IgdbManualAssignment> _log;

    public IgdbManualAssignment(
        IIgdbClient igdb,
        IWorkIgdbPinRepository pins,
        ILogger<IgdbManualAssignment> log)
    {
        _igdb = igdb;
        _pins = pins;
        _log = log;
    }

    /// <summary>
    /// Searches IGDB by title and returns candidates for a picker. A
    /// failed search returns an empty list rather than throwing.
    /// </summary>
    public async Task<IReadOnlyList<IgdbSearchResult>> SearchAsync(
        string title, int limit = 0, CancellationToken ct = default)
    {
        try
        {
            return await _igdb.SearchGamesAsync(title, limit, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "IGDB title search failed; no candidates returned.");
            return [];
        }
    }

    /// <summary>
    /// Fetches the chosen game's metadata from IGDB, pins the work and
    /// rewrites its metadata. Refuses when IGDB has no record for the
    /// game, when the work does not exist, or when another work already
    /// holds that <c>igdb_id</c>.
    /// </summary>
    public async Task<IgdbAssignmentResult> AssignAsync(
        long workId, long igdbId, CancellationToken ct = default)
    {
        if (workId <= 0 || igdbId <= 0)
        {
            return new IgdbAssignmentResult(IgdbAssignmentStatus.WorkNotFound, null);
        }

        try
        {
            var games = await _igdb.GetGamesAsync([igdbId], ct: ct);
            var game = games.FirstOrDefault(g => g.IgdbId == igdbId);
            if (game is null)
            {
                _log.LogWarning(
                    "IGDB returned no record for game {IgdbId}; work {WorkId} was left unpinned.",
                    igdbId, workId);
                return new IgdbAssignmentResult(IgdbAssignmentStatus.MetadataUnavailable, null);
            }

            var outcome = await _pins.PinAsync(
                new WorkIgdbPinAssignment
                {
                    WorkId = workId,
                    IgdbId = game.IgdbId,
                    Name = game.Name,
                    FirstReleaseYear = game.FirstReleaseYear,
                    Summary = game.Summary,
                    CoverUrl = game.CoverUrl,
                    Publisher = game.Publishers.Count > 0 ? game.Publishers[0] : null,
                    IgdbGameType = game.GameType,
                    IgdbParentId = game.ParentGameId,
                    IgdbVersionParentId = game.VersionParentId,
                },
                ct);

            return outcome switch
            {
                WorkIgdbPinOutcome.Pinned => new IgdbAssignmentResult(IgdbAssignmentStatus.Assigned, game),
                WorkIgdbPinOutcome.IgdbIdClaimedByAnotherWork =>
                    new IgdbAssignmentResult(IgdbAssignmentStatus.IgdbIdClaimedByAnotherWork, game),
                _ => new IgdbAssignmentResult(IgdbAssignmentStatus.WorkNotFound, game),
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(
                ex, "Assigning IGDB game {IgdbId} to work {WorkId} failed.", igdbId, workId);
            return new IgdbAssignmentResult(IgdbAssignmentStatus.Failed, null);
        }
    }

    /// <summary>
    /// Clears the pin and returns the work to automatic resolution.
    /// </summary>
    public Task<bool> ClearAsync(long workId, CancellationToken ct = default)
        => _pins.ClearAsync(workId, ct);

    /// <summary>
    /// Returns the live pin for a work, or null when none is active.
    /// </summary>
    public Task<WorkIgdbPin?> GetPinAsync(long workId, CancellationToken ct = default)
        => _pins.GetAsync(workId, ct);
}
